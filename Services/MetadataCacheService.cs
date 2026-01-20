using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for persistent JSON-based caching of track metadata.
/// </summary>
public class MetadataCacheService
{
    private readonly ILogger<MetadataCacheService> _logger;
    private readonly string _cachePath;
    private TrackMetadataCache _cache;
    private bool _isDirty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataCacheService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MetadataCacheService(ILogger<MetadataCacheService> logger)
    {
        _logger = logger;
        _cachePath = GetCachePath();
        _cache = new TrackMetadataCache();
    }

    private static string GetCachePath()
    {
        var dataPath = Plugin.Instance?.DataFolderPath
            ?? Path.Combine(Path.GetTempPath(), "SmartSpotifyPlaylists");

        return Path.Combine(dataPath, "metadata_cache.json");
    }

    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText("/tmp/SmartSpotifyPlaylists_debug.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [Cache] {message}\n");
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Loads the cache from disk.
    /// </summary>
    public void Load()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                var json = File.ReadAllText(_cachePath);
                _cache = JsonSerializer.Deserialize<TrackMetadataCache>(json, JsonOptions) ?? new TrackMetadataCache();

                DebugLog($"Loaded cache from {_cachePath}: {_cache.AudioFeatures.Count} audio features, {_cache.Lyrics.Count} lyrics, {_cache.ArtistGenres.Count} artist genres");
                _logger.LogInformation(
                    "Loaded metadata cache: {AudioFeatures} audio features, {Lyrics} lyrics, {Genres} artist genres",
                    _cache.AudioFeatures.Count,
                    _cache.Lyrics.Count,
                    _cache.ArtistGenres.Count);
            }
            else
            {
                DebugLog($"No cache file found at {_cachePath}, starting fresh");
                _cache = new TrackMetadataCache();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load metadata cache, starting fresh");
            DebugLog($"Failed to load cache: {ex.Message}");
            _cache = new TrackMetadataCache();
        }

        _isDirty = false;
    }

    /// <summary>
    /// Saves the cache to disk if there are changes.
    /// </summary>
    public void Save()
    {
        if (!_isDirty)
        {
            DebugLog("Cache not dirty, skipping save");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_cache, JsonOptions);
            File.WriteAllText(_cachePath, json);

            DebugLog($"Saved cache to {_cachePath}: {_cache.AudioFeatures.Count} audio features, {_cache.Lyrics.Count} lyrics, {_cache.ArtistGenres.Count} artist genres");
            _logger.LogInformation(
                "Saved metadata cache: {AudioFeatures} audio features, {Lyrics} lyrics, {Genres} artist genres",
                _cache.AudioFeatures.Count,
                _cache.Lyrics.Count,
                _cache.ArtistGenres.Count);

            _isDirty = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save metadata cache");
            DebugLog($"Failed to save cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets audio features for a track by Spotify ID.
    /// </summary>
    /// <param name="spotifyId">The Spotify track ID.</param>
    /// <returns>The audio features or null if not cached.</returns>
    public SpotifyAudioFeatures? GetAudioFeatures(string spotifyId)
    {
        if (string.IsNullOrEmpty(spotifyId))
        {
            return null;
        }

        return _cache.AudioFeatures.TryGetValue(spotifyId, out var features) ? features : null;
    }

    /// <summary>
    /// Sets audio features for a track.
    /// </summary>
    /// <param name="spotifyId">The Spotify track ID.</param>
    /// <param name="features">The audio features.</param>
    public void SetAudioFeatures(string spotifyId, SpotifyAudioFeatures features)
    {
        if (string.IsNullOrEmpty(spotifyId))
        {
            return;
        }

        _cache.AudioFeatures[spotifyId] = features;
        _isDirty = true;
    }

    /// <summary>
    /// Gets lyrics for a song.
    /// </summary>
    /// <param name="artist">The artist name.</param>
    /// <param name="title">The song title.</param>
    /// <returns>The lyrics snippet or null if not cached.</returns>
    public string? GetLyrics(string artist, string title)
    {
        var key = GetLyricsKey(artist, title);
        return _cache.Lyrics.TryGetValue(key, out var lyrics) ? lyrics : null;
    }

    /// <summary>
    /// Sets lyrics for a song. Use empty string to cache a "not found" result.
    /// </summary>
    /// <param name="artist">The artist name.</param>
    /// <param name="title">The song title.</param>
    /// <param name="snippet">The lyrics snippet (empty string for not found).</param>
    public void SetLyrics(string artist, string title, string snippet)
    {
        var key = GetLyricsKey(artist, title);
        _cache.Lyrics[key] = snippet;
        _isDirty = true;
    }

    /// <summary>
    /// Checks if lyrics are cached for a song (including "not found" results).
    /// </summary>
    /// <param name="artist">The artist name.</param>
    /// <param name="title">The song title.</param>
    /// <returns>True if lyrics are cached.</returns>
    public bool HasLyrics(string artist, string title)
    {
        var key = GetLyricsKey(artist, title);
        return _cache.Lyrics.ContainsKey(key);
    }

    /// <summary>
    /// Gets genre tags for an artist.
    /// </summary>
    /// <param name="artist">The artist name.</param>
    /// <returns>The genre tags or null if not cached.</returns>
    public List<string>? GetArtistGenres(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return null;
        }

        var key = artist.Trim().ToLowerInvariant();
        return _cache.ArtistGenres.TryGetValue(key, out var genres) ? genres : null;
    }

    /// <summary>
    /// Sets genre tags for an artist.
    /// </summary>
    /// <param name="artist">The artist name.</param>
    /// <param name="genres">The genre tags.</param>
    public void SetArtistGenres(string artist, List<string> genres)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return;
        }

        var key = artist.Trim().ToLowerInvariant();
        _cache.ArtistGenres[key] = genres;
        _isDirty = true;
    }

    /// <summary>
    /// Checks if artist genres are cached (including empty results).
    /// </summary>
    /// <param name="artist">The artist name.</param>
    /// <returns>True if genres are cached.</returns>
    public bool HasArtistGenres(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return false;
        }

        var key = artist.Trim().ToLowerInvariant();
        return _cache.ArtistGenres.ContainsKey(key);
    }

    /// <summary>
    /// Gets Spotify artist genres by artist ID.
    /// </summary>
    /// <param name="artistId">The Spotify artist ID.</param>
    /// <returns>The genre tags or null if not cached.</returns>
    public List<string>? GetSpotifyArtistGenres(string artistId)
    {
        if (string.IsNullOrEmpty(artistId))
        {
            return null;
        }

        return _cache.SpotifyArtistGenres.TryGetValue(artistId, out var genres) ? genres : null;
    }

    /// <summary>
    /// Sets Spotify artist genres by artist ID.
    /// </summary>
    /// <param name="artistId">The Spotify artist ID.</param>
    /// <param name="genres">The genre tags.</param>
    public void SetSpotifyArtistGenres(string artistId, List<string> genres)
    {
        if (string.IsNullOrEmpty(artistId))
        {
            return;
        }

        _cache.SpotifyArtistGenres[artistId] = genres;
        _isDirty = true;
    }

    /// <summary>
    /// Checks if Spotify artist genres are cached.
    /// </summary>
    /// <param name="artistId">The Spotify artist ID.</param>
    /// <returns>True if genres are cached.</returns>
    public bool HasSpotifyArtistGenres(string artistId)
    {
        if (string.IsNullOrEmpty(artistId))
        {
            return false;
        }

        return _cache.SpotifyArtistGenres.ContainsKey(artistId);
    }

    // ========== Track Tags (Last.fm) ==========

    /// <summary>
    /// Gets track tags by artist and title.
    /// </summary>
    public List<string>? GetTrackTags(string artist, string title)
    {
        var key = GetTrackKey(artist, title);
        return _cache.TrackTags.TryGetValue(key, out var tags) ? tags : null;
    }

    /// <summary>
    /// Sets track tags by artist and title.
    /// </summary>
    public void SetTrackTags(string artist, string title, List<string> tags)
    {
        var key = GetTrackKey(artist, title);
        _cache.TrackTags[key] = tags;
        _isDirty = true;
    }

    /// <summary>
    /// Checks if track tags are cached.
    /// </summary>
    public bool HasTrackTags(string artist, string title)
    {
        var key = GetTrackKey(artist, title);
        return _cache.TrackTags.ContainsKey(key);
    }

    // ========== AudioDB Mood Data ==========

    /// <summary>
    /// Gets AudioDB mood data by artist and title.
    /// </summary>
    public Models.TrackMoodData? GetAudioDbMood(string artist, string title)
    {
        var key = GetTrackKey(artist, title);
        return _cache.AudioDbMoods.TryGetValue(key, out var mood) ? mood : null;
    }

    /// <summary>
    /// Sets AudioDB mood data by artist and title.
    /// </summary>
    public void SetAudioDbMood(string artist, string title, Models.TrackMoodData mood)
    {
        var key = GetTrackKey(artist, title);
        _cache.AudioDbMoods[key] = mood;
        _isDirty = true;
    }

    /// <summary>
    /// Checks if AudioDB mood data is cached.
    /// </summary>
    public bool HasAudioDbMood(string artist, string title)
    {
        var key = GetTrackKey(artist, title);
        return _cache.AudioDbMoods.ContainsKey(key);
    }

    // ========== MusicBrainz ==========

    /// <summary>
    /// Gets MusicBrainz recording ID by artist and title.
    /// </summary>
    public string? GetMusicBrainzId(string artist, string title)
    {
        var key = GetTrackKey(artist, title);
        return _cache.MusicBrainzIds.TryGetValue(key, out var id) ? id : null;
    }

    /// <summary>
    /// Sets MusicBrainz recording ID by artist and title.
    /// </summary>
    public void SetMusicBrainzId(string artist, string title, string recordingId)
    {
        var key = GetTrackKey(artist, title);
        _cache.MusicBrainzIds[key] = recordingId;
        _isDirty = true;
    }

    /// <summary>
    /// Checks if MusicBrainz ID is cached.
    /// </summary>
    public bool HasMusicBrainzId(string artist, string title)
    {
        var key = GetTrackKey(artist, title);
        return _cache.MusicBrainzIds.ContainsKey(key);
    }

    /// <summary>
    /// Gets MusicBrainz tags by recording ID.
    /// </summary>
    public List<string>? GetMusicBrainzTags(string recordingId)
    {
        if (string.IsNullOrEmpty(recordingId))
        {
            return null;
        }

        return _cache.MusicBrainzTags.TryGetValue(recordingId, out var tags) ? tags : null;
    }

    /// <summary>
    /// Sets MusicBrainz tags by recording ID.
    /// </summary>
    public void SetMusicBrainzTags(string recordingId, List<string> tags)
    {
        if (string.IsNullOrEmpty(recordingId))
        {
            return;
        }

        _cache.MusicBrainzTags[recordingId] = tags;
        _isDirty = true;
    }

    // ========== Essentia Audio Features ==========

    /// <summary>
    /// Gets Essentia audio features by file hash.
    /// </summary>
    /// <param name="fileHash">The file hash (path + size + mtime).</param>
    /// <returns>The audio features or null if not cached.</returns>
    public EssentiaAudioFeatures? GetEssentiaFeatures(string fileHash)
    {
        if (string.IsNullOrEmpty(fileHash))
        {
            return null;
        }

        return _cache.EssentiaFeatures.TryGetValue(fileHash, out var features) ? features : null;
    }

    /// <summary>
    /// Sets Essentia audio features by file hash.
    /// </summary>
    /// <param name="fileHash">The file hash (path + size + mtime).</param>
    /// <param name="features">The audio features.</param>
    public void SetEssentiaFeatures(string fileHash, EssentiaAudioFeatures features)
    {
        if (string.IsNullOrEmpty(fileHash))
        {
            return;
        }

        _cache.EssentiaFeatures[fileHash] = features;
        _isDirty = true;
    }

    /// <summary>
    /// Checks if Essentia audio features are cached for a file.
    /// </summary>
    /// <param name="fileHash">The file hash (path + size + mtime).</param>
    /// <returns>True if features are cached.</returns>
    public bool HasEssentiaFeatures(string fileHash)
    {
        if (string.IsNullOrEmpty(fileHash))
        {
            return false;
        }

        return _cache.EssentiaFeatures.ContainsKey(fileHash);
    }

    /// <summary>
    /// Gets the number of cached Essentia analysis results.
    /// </summary>
    public int EssentiaCacheCount => _cache.EssentiaFeatures.Count;

    // ========== Helper Methods ==========

    private static string GetTrackKey(string artist, string title)
    {
        var cleanArtist = CleanForKey(artist);
        var cleanTitle = CleanForKey(title);
        return $"{cleanArtist}|{cleanTitle}";
    }

    private static string GetLyricsKey(string artist, string title)
    {
        var cleanArtist = CleanForKey(artist);
        var cleanTitle = CleanForKey(title);
        return $"{cleanArtist}|{cleanTitle}";
    }

    private static string CleanForKey(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return input.Trim().ToLowerInvariant();
    }
}
