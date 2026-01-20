using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Fastenshtein;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Configuration;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for matching Spotify tracks against the local Jellyfin library.
/// </summary>
public class LibraryMatcherService : ILibraryMatcherService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryMatcherService> _logger;
    private readonly Lazy<LastFmService> _lastFmService;
    private readonly Lazy<LyricsService> _lyricsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryMatcherService"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="logger">The logger.</param>
    public LibraryMatcherService(
        ILibraryManager libraryManager,
        ILogger<LibraryMatcherService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _lastFmService = new Lazy<LastFmService>(() =>
            new LastFmService(Microsoft.Extensions.Logging.Abstractions.NullLogger<LastFmService>.Instance));
        _lyricsService = new Lazy<LyricsService>(() =>
            new LyricsService(Microsoft.Extensions.Logging.Abstractions.NullLogger<LyricsService>.Instance));
    }

    private static PluginConfiguration Configuration => Plugin.Instance?.Configuration
        ?? throw new InvalidOperationException("Plugin not initialized");

    /// <inheritdoc />
    public async Task<List<MatchedTrack>> MatchTracksAsync(
        List<SpotifyTrackInfo> spotifyTracks,
        MetadataCacheService? cache,
        CancellationToken cancellationToken = default)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Audio },
            Recursive = true
        };

        var libraryItems = _libraryManager.GetItemList(query)
            .OfType<Audio>()
            .ToList();

        _logger.LogInformation("Found {Count} audio items in Jellyfin library", libraryItems.Count);

        var matchedTracks = new List<MatchedTrack>();
        var minimumScore = Configuration.MinimumMatchScore;

        foreach (var spotifyTrack in spotifyTracks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bestMatch = await FindBestMatchAsync(spotifyTrack, libraryItems, cache, cancellationToken).ConfigureAwait(false);

            if (bestMatch != null && bestMatch.MatchScore >= minimumScore)
            {
                // Copy Spotify metadata to MatchedTrack
                bestMatch.Energy = spotifyTrack.Energy;
                bestMatch.Valence = spotifyTrack.Valence;
                bestMatch.Danceability = spotifyTrack.Danceability;
                bestMatch.Acousticness = spotifyTrack.Acousticness;
                bestMatch.Tempo = spotifyTrack.Tempo;
                bestMatch.Popularity = spotifyTrack.Popularity;
                bestMatch.ReleaseYear = spotifyTrack.ReleaseYear;
                bestMatch.IsExplicit = spotifyTrack.IsExplicit;
                bestMatch.SpotifyGenres = spotifyTrack.SpotifyGenres;

                matchedTracks.Add(bestMatch);
                _logger.LogDebug(
                    "Matched '{SpotifyTrack}' to '{JellyfinTrack}' (score: {Score})",
                    spotifyTrack.DisplayName,
                    bestMatch.DisplayName,
                    bestMatch.MatchScore);
            }
        }

        _logger.LogInformation(
            "Matched {Matched} of {Total} Spotify tracks to local library",
            matchedTracks.Count,
            spotifyTracks.Count);

        return matchedTracks;
    }

    /// <inheritdoc />
    public async Task<List<MatchedTrack>> GetAllLibraryTracksAsync(MetadataCacheService? cache, CancellationToken cancellationToken = default)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Audio },
            Recursive = true
        };

        var libraryItems = _libraryManager.GetItemList(query)
            .OfType<Audio>()
            .ToList();

        _logger.LogInformation("Found {Count} audio items in Jellyfin library", libraryItems.Count);

        var tracks = new List<MatchedTrack>();

        var enableLyricAnalysis = Configuration.EnableLyricAnalysis;
        var lastFmService = _lastFmService.Value;
        var useLastFm = lastFmService.IsConfigured;

        // Track stats for logging
        var lastFmLookups = 0;
        var lastFmHits = 0;

        foreach (var item in libraryItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var artistName = item.Artists?.FirstOrDefault() ?? string.Empty;

            // Get genres - first try the track, then fall back to the album
            var genres = item.Genres ?? Array.Empty<string>();
            if (genres.Length == 0)
            {
                // Try to get genres from the parent album
                var albumItem = item.AlbumEntity;
                if (albumItem != null && albumItem.Genres != null && albumItem.Genres.Length > 0)
                {
                    genres = albumItem.Genres;
                }
            }

            // If we still don't have genres and Last.fm is configured, fetch from Last.fm
            if (genres.Length == 0 && useLastFm && !string.IsNullOrEmpty(artistName))
            {
                lastFmLookups++;
                var lastFmTags = await lastFmService.GetArtistTagsAsync(artistName, cache, 3, cancellationToken).ConfigureAwait(false);
                if (lastFmTags.Count > 0)
                {
                    genres = lastFmTags.ToArray();
                    lastFmHits++;
                }
            }

            // Always try to get lyrics if lyric analysis is enabled (helps with vibe detection)
            string? lyricsSnippet = null;
            if (enableLyricAnalysis)
            {
                // First try local lyric files
                lyricsSnippet = ExtractLyricsSnippet(item.Path);

                // If no local lyrics, try fetching from API
                if (string.IsNullOrEmpty(lyricsSnippet) && !string.IsNullOrEmpty(artistName))
                {
                    lyricsSnippet = await _lyricsService.Value.GetLyricsSnippetAsync(
                        artistName, item.Name, cache, 200, cancellationToken).ConfigureAwait(false);
                }
            }

            tracks.Add(new MatchedTrack
            {
                SpotifyTrack = new SpotifyTrackInfo
                {
                    SpotifyId = string.Empty, // No Spotify ID for library-only tracks
                    Title = item.Name,
                    Artist = artistName,
                    Album = item.Album ?? string.Empty
                },
                JellyfinItemId = item.Id,
                JellyfinTitle = item.Name,
                JellyfinArtist = artistName,
                Album = item.Album ?? string.Empty,
                Genres = genres,
                LyricsSnippet = lyricsSnippet,
                MatchScore = 100 // Perfect match since it's from the library itself
            });
        }

        if (useLastFm && lastFmLookups > 0)
        {
            _logger.LogInformation("Last.fm genre lookups: {Hits}/{Total} artists found", lastFmHits, lastFmLookups);
        }

        _logger.LogInformation("Loaded {Count} tracks from Jellyfin library", tracks.Count);

        return tracks;
    }

    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText("/tmp/SmartSpotifyPlaylists_debug.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [LibMatcher] {message}\n");
        }
        catch
        {
            // Ignore
        }
    }

    /// <inheritdoc />
    public async Task<(List<MatchedTrack> Tracks, Dictionary<Guid, string> FilePaths)> GetAllLibraryTracksWithPathsAsync(
        MetadataCacheService? cache,
        CancellationToken cancellationToken = default)
    {
        DebugLog("GetAllLibraryTracksWithPathsAsync starting...");

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Audio },
            Recursive = true
        };

        DebugLog("Querying library manager...");
        var libraryItems = _libraryManager.GetItemList(query)
            .OfType<Audio>()
            .ToList();

        DebugLog($"Found {libraryItems.Count} audio items in Jellyfin library");
        _logger.LogInformation("Found {Count} audio items in Jellyfin library", libraryItems.Count);

        var tracks = new List<MatchedTrack>();
        var filePaths = new Dictionary<Guid, string>();

        var enableLyricAnalysis = Configuration.EnableLyricAnalysis;
        var lastFmService = _lastFmService.Value;
        var useLastFm = lastFmService.IsConfigured;

        DebugLog($"Config: EnableLyricAnalysis={enableLyricAnalysis}, UseLastFm={useLastFm}");

        // Track stats for logging
        var lastFmLookups = 0;
        var lastFmHits = 0;
        var processedCount = 0;

        foreach (var item in libraryItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            processedCount++;
            if (processedCount % 100 == 0)
            {
                DebugLog($"Processing track {processedCount}/{libraryItems.Count}...");
            }

            var artistName = item.Artists?.FirstOrDefault() ?? string.Empty;

            // Store file path for Essentia analysis
            if (!string.IsNullOrEmpty(item.Path))
            {
                filePaths[item.Id] = item.Path;
            }

            // Get genres - first try the track, then fall back to the album
            var genres = item.Genres ?? Array.Empty<string>();
            if (genres.Length == 0)
            {
                // Try to get genres from the parent album
                var albumItem = item.AlbumEntity;
                if (albumItem != null && albumItem.Genres != null && albumItem.Genres.Length > 0)
                {
                    genres = albumItem.Genres;
                }
            }

            // If we still don't have genres and Last.fm is configured, fetch from Last.fm
            if (genres.Length == 0 && useLastFm && !string.IsNullOrEmpty(artistName))
            {
                lastFmLookups++;
                try
                {
                    var lastFmTags = await lastFmService.GetArtistTagsAsync(artistName, cache, 3, cancellationToken).ConfigureAwait(false);
                    if (lastFmTags.Count > 0)
                    {
                        genres = lastFmTags.ToArray();
                        lastFmHits++;
                    }
                }
                catch (OperationCanceledException)
                {
                    DebugLog($"Last.fm lookup cancelled at track {processedCount}");
                    throw;
                }
                catch (Exception ex)
                {
                    DebugLog($"Last.fm lookup failed for '{artistName}': {ex.Message}");
                }
            }

            // Always try to get lyrics if lyric analysis is enabled (helps with vibe detection)
            string? lyricsSnippet = null;
            if (enableLyricAnalysis)
            {
                // First try local lyric files
                lyricsSnippet = ExtractLyricsSnippet(item.Path);

                // If no local lyrics, try fetching from API
                if (string.IsNullOrEmpty(lyricsSnippet) && !string.IsNullOrEmpty(artistName))
                {
                    try
                    {
                        lyricsSnippet = await _lyricsService.Value.GetLyricsSnippetAsync(
                            artistName, item.Name, cache, 200, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        DebugLog($"Lyrics lookup cancelled at track {processedCount}");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"Lyrics lookup failed for '{artistName} - {item.Name}': {ex.Message}");
                    }
                }
            }

            tracks.Add(new MatchedTrack
            {
                SpotifyTrack = new SpotifyTrackInfo
                {
                    SpotifyId = string.Empty, // No Spotify ID for library-only tracks
                    Title = item.Name,
                    Artist = artistName,
                    Album = item.Album ?? string.Empty
                },
                JellyfinItemId = item.Id,
                JellyfinTitle = item.Name,
                JellyfinArtist = artistName,
                Album = item.Album ?? string.Empty,
                Genres = genres,
                LyricsSnippet = lyricsSnippet,
                MatchScore = 100 // Perfect match since it's from the library itself
            });
        }

        DebugLog($"Finished processing {processedCount} tracks, LastFm lookups: {lastFmHits}/{lastFmLookups}");

        if (useLastFm && lastFmLookups > 0)
        {
            _logger.LogInformation("Last.fm genre lookups: {Hits}/{Total} artists found", lastFmHits, lastFmLookups);
        }

        _logger.LogInformation("Loaded {Count} tracks with {Paths} file paths from Jellyfin library", tracks.Count, filePaths.Count);

        return (tracks, filePaths);
    }

    private async Task<MatchedTrack?> FindBestMatchAsync(
        SpotifyTrackInfo spotifyTrack,
        List<Audio> libraryItems,
        MetadataCacheService? cache,
        CancellationToken cancellationToken)
    {
        MatchedTrack? bestMatch = null;
        var spotifyNormalized = NormalizeString($"{spotifyTrack.Artist} - {spotifyTrack.Title}");
        var enableLyricAnalysis = Configuration.EnableLyricAnalysis;
        var lastFmService = _lastFmService.Value;
        var useLastFm = lastFmService.IsConfigured;

        foreach (var item in libraryItems)
        {
            var artistName = item.Artists?.FirstOrDefault() ?? string.Empty;
            var libraryNormalized = NormalizeString($"{artistName} - {item.Name}");

            var score = CalculateSimilarityScore(spotifyNormalized, libraryNormalized);

            if (bestMatch == null || score > bestMatch.MatchScore)
            {
                // Get genres - first try the track, then fall back to the album
                var genres = item.Genres ?? Array.Empty<string>();
                if (genres.Length == 0)
                {
                    var albumItem = item.AlbumEntity;
                    if (albumItem != null && albumItem.Genres != null && albumItem.Genres.Length > 0)
                    {
                        genres = albumItem.Genres;
                    }
                }

                // If we still don't have genres and Last.fm is configured, fetch from Last.fm
                if (genres.Length == 0 && useLastFm && !string.IsNullOrEmpty(artistName))
                {
                    var lastFmTags = await lastFmService.GetArtistTagsAsync(artistName, cache, 3, cancellationToken).ConfigureAwait(false);
                    if (lastFmTags.Count > 0)
                    {
                        genres = lastFmTags.ToArray();
                    }
                }

                // If lyrics analysis is enabled and we still don't have genres, try to get lyrics
                string? lyricsSnippet = null;
                if (enableLyricAnalysis && genres.Length == 0)
                {
                    lyricsSnippet = ExtractLyricsSnippet(item.Path);
                }

                bestMatch = new MatchedTrack
                {
                    SpotifyTrack = spotifyTrack,
                    JellyfinItemId = item.Id,
                    JellyfinTitle = item.Name,
                    JellyfinArtist = artistName,
                    Album = item.Album ?? string.Empty,
                    Genres = genres,
                    LyricsSnippet = lyricsSnippet,
                    MatchScore = score
                };
            }
        }

        return bestMatch;
    }

    private static int CalculateSimilarityScore(string source, string target)
    {
        // Quick exact match check
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        // Contains check (good for partial matches)
        if (source.Contains(target, StringComparison.OrdinalIgnoreCase) ||
            target.Contains(source, StringComparison.OrdinalIgnoreCase))
        {
            return 85;
        }

        // Levenshtein distance for fuzzy matching
        var maxLength = Math.Max(source.Length, target.Length);
        if (maxLength == 0)
        {
            return 100;
        }

        var distance = Levenshtein.Distance(source.ToLowerInvariant(), target.ToLowerInvariant());
        var similarity = (1.0 - ((double)distance / maxLength)) * 100;

        return (int)Math.Round(similarity);
    }

    private static string NormalizeString(string input)
    {
        return input
            .ToLowerInvariant()
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .Replace("[", string.Empty)
            .Replace("]", string.Empty)
            .Replace("feat.", string.Empty)
            .Replace("ft.", string.Empty)
            .Replace("featuring", string.Empty)
            .Replace("  ", " ")
            .Trim();
    }

    /// <summary>
    /// Attempts to extract a lyrics snippet from sidecar files (.lrc, .txt) for a given audio item.
    /// </summary>
    /// <param name="audioPath">The path to the audio file.</param>
    /// <param name="maxLength">Maximum length of the snippet to return.</param>
    /// <returns>A lyrics snippet or null if not found.</returns>
    private string? ExtractLyricsSnippet(string? audioPath, int maxLength = 150)
    {
        if (string.IsNullOrEmpty(audioPath))
        {
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(audioPath);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(audioPath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileNameWithoutExt))
            {
                return null;
            }

            // Check for common lyric file extensions
            var lyricExtensions = new[] { ".lrc", ".txt", ".lyrics" };

            foreach (var ext in lyricExtensions)
            {
                var lyricPath = Path.Combine(directory, fileNameWithoutExt + ext);
                if (File.Exists(lyricPath))
                {
                    var content = File.ReadAllText(lyricPath);
                    var cleanedLyrics = CleanLyrics(content);

                    if (!string.IsNullOrWhiteSpace(cleanedLyrics))
                    {
                        // Return a snippet, truncated if necessary
                        if (cleanedLyrics.Length > maxLength)
                        {
                            return cleanedLyrics.Substring(0, maxLength) + "...";
                        }

                        return cleanedLyrics;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract lyrics for {Path}", audioPath);
        }

        return null;
    }

    /// <summary>
    /// Cleans LRC format lyrics by removing timestamps and metadata.
    /// </summary>
    /// <param name="lyrics">Raw lyrics content.</param>
    /// <returns>Cleaned lyrics text.</returns>
    private static string CleanLyrics(string lyrics)
    {
        // Remove LRC timestamps like [00:12.34]
        var cleaned = Regex.Replace(lyrics, @"\[\d{2}:\d{2}(\.\d{2,3})?\]", string.Empty);

        // Remove LRC metadata like [ar:Artist] [ti:Title] [al:Album]
        cleaned = Regex.Replace(cleaned, @"\[[a-z]{2}:[^\]]*\]", string.Empty, RegexOptions.IgnoreCase);

        // Remove empty lines and normalize whitespace
        var lines = cleaned.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l));

        return string.Join(" ", lines);
    }
}
