using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Configuration;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Models;
using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for interacting with the Spotify API.
/// </summary>
public class SpotifyService : ISpotifyService
{
    private readonly ILogger<SpotifyService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public SpotifyService(ILogger<SpotifyService> logger)
    {
        _logger = logger;
    }

    private static PluginConfiguration Configuration => Plugin.Instance?.Configuration
        ?? throw new InvalidOperationException("Plugin not initialized");

    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText("/tmp/SmartSpotifyPlaylists_debug.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [Spotify] {message}\n");
        }
        catch
        {
            // Ignore
        }
    }

    /// <inheritdoc />
    public async Task<List<SpotifyTrackInfo>> GetSourceTracksAsync(CancellationToken cancellationToken = default)
    {
        var spotify = await GetAuthenticatedClientAsync().ConfigureAwait(false);
        var tracks = new List<SpotifyTrackInfo>();

        if (string.IsNullOrEmpty(Configuration.TargetSpotifyPlaylistId))
        {
            _logger.LogInformation("Fetching user's Liked Songs from Spotify");
            await foreach (var item in FetchAllSavedTracksAsync(spotify, cancellationToken).ConfigureAwait(false))
            {
                if (item.Track is FullTrack track)
                {
                    tracks.Add(MapToTrackInfo(track));
                }
            }
        }
        else
        {
            _logger.LogInformation("Fetching playlist {PlaylistId} from Spotify", Configuration.TargetSpotifyPlaylistId);
            await foreach (var item in FetchPlaylistTracksAsync(spotify, Configuration.TargetSpotifyPlaylistId, cancellationToken).ConfigureAwait(false))
            {
                if (item.Track is FullTrack track)
                {
                    tracks.Add(MapToTrackInfo(track));
                }
            }
        }

        _logger.LogInformation("Retrieved {Count} tracks from Spotify", tracks.Count);
        return tracks;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateCredentialsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            DebugLog("Attempting to get authenticated Spotify client...");
            var spotify = await GetAuthenticatedClientAsync().ConfigureAwait(false);
            DebugLog("Got Spotify client, fetching user profile...");
            var profile = await spotify.UserProfile.Current(cancellationToken).ConfigureAwait(false);
            DebugLog($"Spotify credentials valid for user: {profile.DisplayName}");
            return true;
        }
        catch (APIException apiEx)
        {
            DebugLog($"Spotify API Error: {apiEx.Message}");
            DebugLog($"Response: {apiEx.Response?.Body}");
            return false;
        }
        catch (Exception ex)
        {
            DebugLog($"Spotify validation error: {ex.GetType().Name}: {ex.Message}");
            DebugLog($"Stack: {ex.StackTrace}");
            return false;
        }
    }

    private static async Task<SpotifyClient> GetAuthenticatedClientAsync()
    {
        var response = await new OAuthClient().RequestToken(
            new AuthorizationCodeRefreshRequest(
                Configuration.SpotifyClientId,
                Configuration.SpotifyClientSecret,
                Configuration.SpotifyRefreshToken)).ConfigureAwait(false);

        var config = SpotifyClientConfig
            .CreateDefault()
            .WithToken(response.AccessToken);

        return new SpotifyClient(config);
    }

    private static async IAsyncEnumerable<SavedTrack> FetchAllSavedTracksAsync(
        SpotifyClient spotify,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new LibraryTracksRequest { Limit = 50 };
        var page = await spotify.Library.GetTracks(request, cancellationToken).ConfigureAwait(false);

        while (page != null)
        {
            foreach (var item in page.Items ?? Enumerable.Empty<SavedTrack>())
            {
                yield return item;
            }

            if (page.Next == null)
            {
                break;
            }

            page = await spotify.NextPage(page).ConfigureAwait(false);
        }
    }

    private static async IAsyncEnumerable<PlaylistTrack<IPlayableItem>> FetchPlaylistTracksAsync(
        SpotifyClient spotify,
        string playlistId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new PlaylistGetItemsRequest { Limit = 50 };
        var page = await spotify.Playlists.GetItems(playlistId, request, cancellationToken).ConfigureAwait(false);

        while (page != null)
        {
            foreach (var item in page.Items ?? Enumerable.Empty<PlaylistTrack<IPlayableItem>>())
            {
                yield return item;
            }

            if (page.Next == null)
            {
                break;
            }

            page = await spotify.NextPage(page).ConfigureAwait(false);
        }
    }

    private static SpotifyTrackInfo MapToTrackInfo(FullTrack track)
    {
        // Parse release year from album release date
        int? releaseYear = null;
        if (!string.IsNullOrEmpty(track.Album?.ReleaseDate))
        {
            var releaseDateStr = track.Album.ReleaseDate;
            // Format can be "YYYY", "YYYY-MM", or "YYYY-MM-DD"
            if (releaseDateStr.Length >= 4 && int.TryParse(releaseDateStr.Substring(0, 4), out var year))
            {
                releaseYear = year;
            }
        }

        return new SpotifyTrackInfo
        {
            SpotifyId = track.Id,
            ArtistId = track.Artists.FirstOrDefault()?.Id ?? string.Empty,
            Title = track.Name,
            Artist = string.Join(", ", track.Artists.Select(a => a.Name)),
            Album = track.Album?.Name ?? string.Empty,
            Popularity = track.Popularity,
            ReleaseYear = releaseYear,
            IsExplicit = track.Explicit
        };
    }

    /// <inheritdoc />
    public async Task EnrichWithAudioFeaturesAsync(
        List<SpotifyTrackInfo> tracks,
        MetadataCacheService cache,
        CancellationToken cancellationToken = default)
    {
        // Find tracks that need audio features fetched (not in cache)
        var tracksToFetch = new List<SpotifyTrackInfo>();
        var cachedCount = 0;

        foreach (var track in tracks)
        {
            if (string.IsNullOrEmpty(track.SpotifyId))
            {
                continue;
            }

            var cached = cache.GetAudioFeatures(track.SpotifyId);
            if (cached != null)
            {
                // Apply cached features
                track.Energy = cached.Energy;
                track.Valence = cached.Valence;
                track.Danceability = cached.Danceability;
                track.Acousticness = cached.Acousticness;
                track.Tempo = cached.Tempo;
                cachedCount++;
            }
            else
            {
                tracksToFetch.Add(track);
            }
        }

        DebugLog($"Audio features: {cachedCount} from cache, {tracksToFetch.Count} to fetch from Spotify API");

        if (tracksToFetch.Count == 0)
        {
            _logger.LogInformation("All {Count} tracks have cached audio features", tracks.Count);
            return;
        }

        _logger.LogInformation("Fetching audio features for {Count} tracks (out of {Total})", tracksToFetch.Count, tracks.Count);

        var spotify = await GetAuthenticatedClientAsync().ConfigureAwait(false);

        // Spotify API allows batches of up to 100 tracks
        const int batchSize = 100;
        var fetched = 0;
        var consecutiveErrors = 0;
        const int maxConsecutiveErrors = 3;

        for (var i = 0; i < tracksToFetch.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = tracksToFetch.Skip(i).Take(batchSize).ToList();
            var trackIds = batch.Select(t => t.SpotifyId).ToList();

            try
            {
                var request = new TracksAudioFeaturesRequest(trackIds);
                var response = await spotify.Tracks.GetSeveralAudioFeatures(request, cancellationToken).ConfigureAwait(false);

                if (response?.AudioFeatures != null)
                {
                    for (var j = 0; j < response.AudioFeatures.Count && j < batch.Count; j++)
                    {
                        var features = response.AudioFeatures[j];
                        var track = batch[j];

                        if (features != null)
                        {
                            track.Energy = features.Energy;
                            track.Valence = features.Valence;
                            track.Danceability = features.Danceability;
                            track.Acousticness = features.Acousticness;
                            track.Tempo = features.Tempo;

                            // Store in cache
                            cache.SetAudioFeatures(track.SpotifyId, new Models.SpotifyAudioFeatures
                            {
                                Energy = features.Energy,
                                Valence = features.Valence,
                                Danceability = features.Danceability,
                                Acousticness = features.Acousticness,
                                Tempo = features.Tempo
                            });

                            fetched++;
                        }
                    }
                }

                DebugLog($"Fetched audio features batch {i / batchSize + 1}: {batch.Count} tracks");
                consecutiveErrors = 0; // Reset on success
            }
            catch (APIException apiEx)
            {
                consecutiveErrors++;
                _logger.LogWarning(apiEx, "Spotify API error fetching audio features for batch at index {Index}", i);
                DebugLog($"Error fetching audio features batch: {apiEx.Message}");
                DebugLog($"API Response: {apiEx.Response?.Body}");
                DebugLog($"Status Code: {apiEx.Response?.StatusCode}");

                // If we get a 403 or 401, the token might not have the right scopes
                // Audio features API was deprecated by Spotify in Nov 2024 for new apps
                if (apiEx.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                    apiEx.Response?.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    DebugLog("WARNING: Audio features API returned 403/401 - this API may be deprecated for your app type");
                    DebugLog("The sync will continue without audio features. Playlists will still be created based on genre, lyrics, and artist knowledge.");
                    break; // Stop trying if we're getting auth errors
                }

                if (consecutiveErrors >= maxConsecutiveErrors)
                {
                    DebugLog($"WARNING: {maxConsecutiveErrors} consecutive errors - stopping audio features fetch");
                    DebugLog("The sync will continue without audio features.");
                    break;
                }
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                _logger.LogWarning(ex, "Failed to fetch audio features for batch starting at index {Index}", i);
                DebugLog($"Error fetching audio features batch: {ex.GetType().Name}: {ex.Message}");

                if (consecutiveErrors >= maxConsecutiveErrors)
                {
                    DebugLog($"WARNING: {maxConsecutiveErrors} consecutive errors - stopping audio features fetch");
                    break;
                }
            }

            // Small delay to respect rate limits
            if (i + batchSize < tracksToFetch.Count)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }

        if (fetched == 0 && tracksToFetch.Count > 0)
        {
            _logger.LogWarning("Audio features API unavailable - continuing without audio features");
            DebugLog("NOTE: Spotify deprecated the Audio Features API in Nov 2024 for new apps.");
            DebugLog("If your Spotify app was created after this date, audio features won't be available.");
            DebugLog("The plugin will still work using genre, lyrics, and artist knowledge for clustering.");
        }
        else
        {
            _logger.LogInformation("Fetched audio features for {Fetched} tracks", fetched);
        }

        DebugLog($"Audio features enrichment complete: {fetched} fetched from API");
    }

    /// <inheritdoc />
    public async Task EnrichWithArtistGenresAsync(
        List<SpotifyTrackInfo> tracks,
        MetadataCacheService cache,
        CancellationToken cancellationToken = default)
    {
        // Get unique artist IDs that need genre fetching
        var artistIdsToFetch = new HashSet<string>();
        var cachedCount = 0;

        foreach (var track in tracks)
        {
            if (string.IsNullOrEmpty(track.ArtistId))
            {
                continue;
            }

            if (cache.HasSpotifyArtistGenres(track.ArtistId))
            {
                var cached = cache.GetSpotifyArtistGenres(track.ArtistId);
                if (cached != null)
                {
                    track.SpotifyGenres = cached.ToArray();
                }

                cachedCount++;
            }
            else
            {
                artistIdsToFetch.Add(track.ArtistId);
            }
        }

        DebugLog($"Artist genres: {cachedCount} artists from cache, {artistIdsToFetch.Count} unique artists to fetch from Spotify API");

        if (artistIdsToFetch.Count == 0)
        {
            _logger.LogInformation("All artists have cached genres");
            return;
        }

        _logger.LogInformation("Fetching genres for {Count} unique artists from Spotify API", artistIdsToFetch.Count);

        var spotify = await GetAuthenticatedClientAsync().ConfigureAwait(false);

        // Spotify API allows batches of up to 50 artists
        const int batchSize = 50;
        var artistIdList = artistIdsToFetch.ToList();
        var artistGenreMap = new Dictionary<string, List<string>>();
        var fetched = 0;

        for (var i = 0; i < artistIdList.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = artistIdList.Skip(i).Take(batchSize).ToList();

            try
            {
                var request = new ArtistsRequest(batch);
                var response = await spotify.Artists.GetSeveral(request, cancellationToken).ConfigureAwait(false);

                if (response?.Artists != null)
                {
                    foreach (var artist in response.Artists)
                    {
                        if (artist != null)
                        {
                            var genres = artist.Genres ?? new List<string>();
                            artistGenreMap[artist.Id] = genres;
                            cache.SetSpotifyArtistGenres(artist.Id, genres);
                            fetched++;
                        }
                    }
                }

                DebugLog($"Fetched artist genres batch {i / batchSize + 1}: {batch.Count} artists");
            }
            catch (APIException apiEx)
            {
                _logger.LogWarning(apiEx, "Spotify API error fetching artist genres for batch at index {Index}", i);
                DebugLog($"Error fetching artist genres batch: {apiEx.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch artist genres for batch starting at index {Index}", i);
                DebugLog($"Error fetching artist genres batch: {ex.GetType().Name}: {ex.Message}");
            }

            // Small delay to respect rate limits
            if (i + batchSize < artistIdList.Count)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        // Apply fetched genres to tracks
        foreach (var track in tracks)
        {
            if (!string.IsNullOrEmpty(track.ArtistId) && artistGenreMap.TryGetValue(track.ArtistId, out var genres))
            {
                track.SpotifyGenres = genres.ToArray();
            }
        }

        _logger.LogInformation("Fetched genres for {Fetched} artists", fetched);
        DebugLog($"Artist genres enrichment complete: {fetched} artists fetched from API");
    }
}
