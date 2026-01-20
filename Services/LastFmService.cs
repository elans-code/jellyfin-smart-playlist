using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for fetching artist genre tags from Last.fm API.
/// </summary>
public class LastFmService : ILastFmService
{
    private readonly ILogger<LastFmService> _logger;
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://ws.audioscrobbler.com/2.0/";

    /// <summary>
    /// Initializes a new instance of the <see cref="LastFmService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public LastFmService(ILogger<LastFmService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    private static PluginConfiguration Configuration => Plugin.Instance?.Configuration
        ?? throw new InvalidOperationException("Plugin not initialized");

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Configuration.LastFmApiKey);

    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText("/tmp/SmartSpotifyPlaylists_debug.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [LastFm] {message}\n");
        }
        catch
        {
            // Ignore
        }
    }

    /// <inheritdoc />
    public async Task<List<string>> GetArtistTagsAsync(
        string artistName,
        MetadataCacheService? cache,
        int maxTags = 3,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistName))
        {
            return new List<string>();
        }

        if (!IsConfigured)
        {
            return new List<string>();
        }

        // Check persistent cache first
        if (cache != null && cache.HasArtistGenres(artistName))
        {
            var cached = cache.GetArtistGenres(artistName);
            return cached ?? new List<string>();
        }

        try
        {
            var apiKey = Configuration.LastFmApiKey;
            var encodedArtist = Uri.EscapeDataString(artistName);
            var url = $"{BaseUrl}?method=artist.gettoptags&artist={encodedArtist}&api_key={apiKey}&format=json";

            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                DebugLog($"Last.fm API error for '{artistName}': {response.StatusCode}");
                cache?.SetArtistGenres(artistName, new List<string>());
                return new List<string>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<LastFmTopTagsResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var tags = result?.Toptags?.Tag?
                .Where(t => t.Count > 0)
                .OrderByDescending(t => t.Count)
                .Take(maxTags)
                .Select(t => NormalizeTag(t.Name))
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList() ?? new List<string>();

            // Cache the result
            cache?.SetArtistGenres(artistName, tags);

            if (tags.Count > 0)
            {
                DebugLog($"Last.fm tags for '{artistName}': [{string.Join(", ", tags)}]");
            }

            return tags;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch Last.fm tags for artist: {Artist}", artistName);
            DebugLog($"Last.fm error for '{artistName}': {ex.Message}");
            cache?.SetArtistGenres(artistName, new List<string>());
            return new List<string>();
        }
    }

    /// <summary>
    /// Gets the top tags for a specific track from Last.fm.
    /// </summary>
    /// <param name="artistName">The artist name.</param>
    /// <param name="trackName">The track name.</param>
    /// <param name="cache">The metadata cache service.</param>
    /// <param name="maxTags">Maximum number of tags to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of tags including mood descriptors.</returns>
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

        if (!IsConfigured)
        {
            return new List<string>();
        }

        // Check persistent cache first
        if (cache != null && cache.HasTrackTags(artistName, trackName))
        {
            var cached = cache.GetTrackTags(artistName, trackName);
            return cached ?? new List<string>();
        }

        try
        {
            var apiKey = Configuration.LastFmApiKey;
            var encodedArtist = Uri.EscapeDataString(artistName);
            var encodedTrack = Uri.EscapeDataString(trackName);
            var url = $"{BaseUrl}?method=track.gettoptags&artist={encodedArtist}&track={encodedTrack}&api_key={apiKey}&format=json";

            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                cache?.SetTrackTags(artistName, trackName, new List<string>());
                return new List<string>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<LastFmTopTagsResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var tags = result?.Toptags?.Tag?
                .Where(t => t.Count > 0)
                .OrderByDescending(t => t.Count)
                .Take(maxTags)
                .Select(t => NormalizeTag(t.Name))
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList() ?? new List<string>();

            // Cache the result
            cache?.SetTrackTags(artistName, trackName, tags);

            if (tags.Count > 0)
            {
                DebugLog($"Last.fm track tags for '{artistName} - {trackName}': [{string.Join(", ", tags)}]");
            }

            return tags;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch Last.fm track tags for: {Artist} - {Track}", artistName, trackName);
            cache?.SetTrackTags(artistName, trackName, new List<string>());
            return new List<string>();
        }
    }

    /// <summary>
    /// Normalizes a Last.fm tag to a cleaner genre name.
    /// </summary>
    private static string NormalizeTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        // Last.fm tags can be messy, clean them up
        var normalized = tag.Trim();

        // Skip tags that are too generic or not useful
        var skipTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "seen live", "favorites", "favourite", "favorite", "my favorite",
            "amazing", "awesome", "love", "loved", "beautiful", "cool",
            "albums i own", "check out", "spotify", "good", "best"
        };

        if (skipTags.Contains(normalized))
        {
            return string.Empty;
        }

        // Capitalize first letter of each word
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    // JSON response models for Last.fm API
    private class LastFmTopTagsResponse
    {
        public TopTagsContainer? Toptags { get; set; }
    }

    private class TopTagsContainer
    {
        public List<LastFmTag>? Tag { get; set; }
    }

    private class LastFmTag
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
