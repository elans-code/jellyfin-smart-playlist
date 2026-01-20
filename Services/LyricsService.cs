using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for fetching song lyrics from external APIs.
/// </summary>
public class LyricsService
{
    private readonly ILogger<LyricsService> _logger;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="LyricsService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public LyricsService(ILogger<LyricsService> logger)
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
            File.AppendAllText("/tmp/SmartSpotifyPlaylists_debug.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [Lyrics] {message}\n");
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Gets lyrics for a song, returning a snippet for analysis.
    /// </summary>
    /// <param name="artist">The artist name.</param>
    /// <param name="title">The song title.</param>
    /// <param name="cache">The metadata cache service for persistent caching.</param>
    /// <param name="maxLength">Maximum snippet length.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A lyrics snippet or null if not found.</returns>
    public async Task<string?> GetLyricsSnippetAsync(
        string artist,
        string title,
        MetadataCacheService? cache,
        int maxLength = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        // Clean up artist and title for better matching
        var cleanArtist = CleanForSearch(artist);
        var cleanTitle = CleanForSearch(title);

        // Check persistent cache first
        if (cache != null && cache.HasLyrics(cleanArtist, cleanTitle))
        {
            var cached = cache.GetLyrics(cleanArtist, cleanTitle);
            // Return null for empty string (cached "not found")
            return string.IsNullOrEmpty(cached) ? null : cached;
        }

        try
        {
            // Try lyrics.ovh (free, no API key)
            var lyrics = await FetchFromLyricsOvh(cleanArtist, cleanTitle, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(lyrics))
            {
                var snippet = ExtractSnippet(lyrics, maxLength);
                cache?.SetLyrics(cleanArtist, cleanTitle, snippet);
                DebugLog($"Found lyrics for '{artist} - {title}'");
                return snippet;
            }
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch lyrics for {Artist} - {Title}", artist, title);
        }

        // Cache the "not found" result
        cache?.SetLyrics(cleanArtist, cleanTitle, string.Empty);
        return null;
    }

    private async Task<string?> FetchFromLyricsOvh(string artist, string title, CancellationToken cancellationToken)
    {
        var url = $"https://api.lyrics.ovh/v1/{Uri.EscapeDataString(artist)}/{Uri.EscapeDataString(title)}";

        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<LyricsOvhResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result?.Lyrics;
    }

    private static string CleanForSearch(string input)
    {
        // Remove common suffixes and clean up for better API matching
        var cleaned = input
            .Replace("(feat.", "(")
            .Replace("(Feat.", "(")
            .Replace("(ft.", "(")
            .Replace("(Ft.", "(");

        // Remove anything in parentheses
        var parenIndex = cleaned.IndexOf('(');
        if (parenIndex > 0)
        {
            cleaned = cleaned.Substring(0, parenIndex);
        }

        return cleaned.Trim();
    }

    private static string ExtractSnippet(string lyrics, int maxLength)
    {
        // Clean up lyrics
        var lines = lyrics.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var meaningfulLines = new System.Collections.Generic.List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Skip empty lines, section markers like [Chorus], and very short lines
            if (string.IsNullOrWhiteSpace(trimmed) ||
                (trimmed.StartsWith("[") && trimmed.EndsWith("]")) ||
                trimmed.Length < 10)
            {
                continue;
            }

            meaningfulLines.Add(trimmed);

            // Get a few lines for context
            if (meaningfulLines.Count >= 4)
            {
                break;
            }
        }

        var snippet = string.Join(" / ", meaningfulLines);

        if (snippet.Length > maxLength)
        {
            snippet = snippet.Substring(0, maxLength) + "...";
        }

        return snippet;
    }

    private class LyricsOvhResponse
    {
        public string? Lyrics { get; set; }
    }
}
