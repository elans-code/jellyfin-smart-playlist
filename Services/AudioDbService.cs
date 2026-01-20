using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for fetching track mood data from TheAudioDB API.
/// </summary>
public class AudioDbService
{
    private readonly ILogger<AudioDbService> _logger;
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://theaudiodb.com/api/v1/json/2"; // Free API key

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioDbService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public AudioDbService(ILogger<AudioDbService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "JellyfinSmartPlaylists/1.0");
    }

    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText("/tmp/SmartSpotifyPlaylists_debug.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [AudioDB] {message}\n");
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Gets mood data for a track from TheAudioDB.
    /// </summary>
    /// <param name="artistName">The artist name.</param>
    /// <param name="trackName">The track name.</param>
    /// <param name="cache">The metadata cache service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Mood data or null if not found.</returns>
    public async Task<TrackMoodData?> GetTrackMoodAsync(
        string artistName,
        string trackName,
        MetadataCacheService? cache,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(trackName))
        {
            return null;
        }

        // Check persistent cache first
        if (cache != null && cache.HasAudioDbMood(artistName, trackName))
        {
            return cache.GetAudioDbMood(artistName, trackName);
        }

        try
        {
            var encodedArtist = Uri.EscapeDataString(artistName);
            var encodedTrack = Uri.EscapeDataString(trackName);
            var url = $"{BaseUrl}/searchtrack.php?s={encodedArtist}&t={encodedTrack}";

            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                cache?.SetAudioDbMood(artistName, trackName, new TrackMoodData());
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<AudioDbSearchResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Track == null || result.Track.Count == 0)
            {
                cache?.SetAudioDbMood(artistName, trackName, new TrackMoodData());
                return null;
            }

            var track = result.Track[0];
            var moodData = new TrackMoodData
            {
                Mood = track.StrMood,
                Theme = track.StrTheme,
                Speed = track.StrSpeed
            };

            // Cache the result
            cache?.SetAudioDbMood(artistName, trackName, moodData);

            if (!string.IsNullOrEmpty(moodData.Mood) || !string.IsNullOrEmpty(moodData.Theme))
            {
                DebugLog($"AudioDB mood for '{artistName} - {trackName}': Mood={moodData.Mood}, Theme={moodData.Theme}, Speed={moodData.Speed}");
            }

            return moodData;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch AudioDB mood for: {Artist} - {Track}", artistName, trackName);
            cache?.SetAudioDbMood(artistName, trackName, new TrackMoodData());
            return null;
        }
    }

    // JSON response models for TheAudioDB API
    private class AudioDbSearchResponse
    {
        public System.Collections.Generic.List<AudioDbTrack>? Track { get; set; }
    }

    private class AudioDbTrack
    {
        public string? StrMood { get; set; }
        public string? StrTheme { get; set; }
        public string? StrSpeed { get; set; }
        public string? StrGenre { get; set; }
        public string? StrStyle { get; set; }
    }
}
