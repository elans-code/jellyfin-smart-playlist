using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for fetching track tags from MusicBrainz API.
/// </summary>
public class MusicBrainzService
{
    private readonly ILogger<MusicBrainzService> _logger;
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://musicbrainz.org/ws/2";

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicBrainzService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MusicBrainzService(ILogger<MusicBrainzService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        // MusicBrainz requires a User-Agent with contact info
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "JellyfinSmartPlaylists/1.0 (github.com/jellyfin)");
    }

    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText("/tmp/SmartSpotifyPlaylists_debug.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [MusicBrainz] {message}\n");
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Gets tags for a track from MusicBrainz.
    /// </summary>
    /// <param name="artistName">The artist name.</param>
    /// <param name="trackName">The track name.</param>
    /// <param name="cache">The metadata cache service.</param>
    /// <param name="maxTags">Maximum number of tags to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of tags or empty if not found.</returns>
    public async Task<List<string>> GetTrackTagsAsync(
        string artistName,
        string trackName,
        MetadataCacheService? cache,
        int maxTags = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(trackName))
        {
            return new List<string>();
        }

        // Check if we already have a cached MusicBrainz ID
        string? recordingId = null;
        if (cache != null)
        {
            if (cache.HasMusicBrainzId(artistName, trackName))
            {
                recordingId = cache.GetMusicBrainzId(artistName, trackName);
                if (string.IsNullOrEmpty(recordingId))
                {
                    // Cached as "not found"
                    return new List<string>();
                }

                // Check if we have cached tags for this recording
                var cachedTags = cache.GetMusicBrainzTags(recordingId);
                if (cachedTags != null)
                {
                    return cachedTags;
                }
            }
        }

        try
        {
            // Step 1: Search for the recording to get the MusicBrainz ID
            if (string.IsNullOrEmpty(recordingId))
            {
                recordingId = await SearchRecordingAsync(artistName, trackName, cache, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(recordingId))
                {
                    return new List<string>();
                }
            }

            // Step 2: Fetch tags for the recording
            // MusicBrainz requires rate limiting (1 request per second)
            await Task.Delay(1100, cancellationToken).ConfigureAwait(false);

            var url = $"{BaseUrl}/recording/{recordingId}?inc=tags&fmt=json";
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                cache?.SetMusicBrainzTags(recordingId, new List<string>());
                return new List<string>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<MusicBrainzRecordingResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var tags = result?.Tags?
                .OrderByDescending(t => t.Count)
                .Take(maxTags)
                .Select(t => NormalizeTag(t.Name))
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList() ?? new List<string>();

            // Cache the tags
            cache?.SetMusicBrainzTags(recordingId, tags);

            if (tags.Count > 0)
            {
                DebugLog($"MusicBrainz tags for '{artistName} - {trackName}': [{string.Join(", ", tags)}]");
            }

            return tags;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch MusicBrainz tags for: {Artist} - {Track}", artistName, trackName);
            return new List<string>();
        }
    }

    private async Task<string?> SearchRecordingAsync(
        string artistName,
        string trackName,
        MetadataCacheService? cache,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = Uri.EscapeDataString($"recording:\"{trackName}\" AND artist:\"{artistName}\"");
            var url = $"{BaseUrl}/recording?query={query}&limit=1&fmt=json";

            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                cache?.SetMusicBrainzId(artistName, trackName, string.Empty);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<MusicBrainzSearchResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var recording = result?.Recordings?.FirstOrDefault();
            if (recording == null || string.IsNullOrEmpty(recording.Id))
            {
                cache?.SetMusicBrainzId(artistName, trackName, string.Empty);
                return null;
            }

            // Cache the recording ID
            cache?.SetMusicBrainzId(artistName, trackName, recording.Id);
            DebugLog($"Found MusicBrainz ID for '{artistName} - {trackName}': {recording.Id}");

            return recording.Id;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to search MusicBrainz for: {Artist} - {Track}", artistName, trackName);
            cache?.SetMusicBrainzId(artistName, trackName, string.Empty);
            return null;
        }
    }

    private static string NormalizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        // Capitalize first letter of each word
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(tag.Trim().ToLowerInvariant());
    }

    // JSON response models for MusicBrainz API
    private class MusicBrainzSearchResponse
    {
        public List<MusicBrainzRecording>? Recordings { get; set; }
    }

    private class MusicBrainzRecording
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
    }

    private class MusicBrainzRecordingResponse
    {
        public string? Id { get; set; }
        public List<MusicBrainzTag>? Tags { get; set; }
    }

    private class MusicBrainzTag
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }
}
