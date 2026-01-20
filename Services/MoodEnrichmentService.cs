using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for enriching tracks with mood/vibe data from multiple sources.
/// </summary>
public class MoodEnrichmentService
{
    private readonly ILogger<MoodEnrichmentService> _logger;
    private readonly Lazy<LastFmService> _lastFmService;
    private readonly Lazy<AudioDbService> _audioDbService;
    private readonly Lazy<MusicBrainzService> _musicBrainzService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MoodEnrichmentService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MoodEnrichmentService(ILogger<MoodEnrichmentService> logger)
    {
        _logger = logger;
        _lastFmService = new Lazy<LastFmService>(() =>
            new LastFmService(Microsoft.Extensions.Logging.Abstractions.NullLogger<LastFmService>.Instance));
        _audioDbService = new Lazy<AudioDbService>(() =>
            new AudioDbService(Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioDbService>.Instance));
        _musicBrainzService = new Lazy<MusicBrainzService>(() =>
            new MusicBrainzService(Microsoft.Extensions.Logging.Abstractions.NullLogger<MusicBrainzService>.Instance));
    }

    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText("/tmp/SmartSpotifyPlaylists_debug.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [MoodEnrich] {message}\n");
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Enriches tracks with mood data from Last.fm, TheAudioDB, and MusicBrainz.
    /// </summary>
    /// <param name="tracks">The tracks to enrich.</param>
    /// <param name="cache">The metadata cache service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    public async Task EnrichWithMoodDataAsync(
        List<MatchedTrack> tracks,
        MetadataCacheService cache,
        CancellationToken cancellationToken = default)
    {
        var lastFmService = _lastFmService.Value;
        var audioDbService = _audioDbService.Value;
        var musicBrainzService = _musicBrainzService.Value;

        var useLastFm = lastFmService.IsConfigured;
        var enrichedCount = 0;
        var totalTracks = tracks.Count;

        DebugLog($"Starting mood enrichment for {totalTracks} tracks (Last.fm configured: {useLastFm})");

        // Process in smaller batches to avoid overwhelming APIs
        var batchSize = 50;
        var batchNumber = 0;

        for (var i = 0; i < tracks.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batchNumber++;

            var batch = tracks.Skip(i).Take(batchSize).ToList();
            DebugLog($"Processing mood batch {batchNumber}: tracks {i + 1} to {Math.Min(i + batchSize, totalTracks)}");

            foreach (var track in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var artist = track.JellyfinArtist;
                var title = track.JellyfinTitle;

                if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title))
                {
                    continue;
                }

                var allMoodTags = new List<string>();

                // 1. Last.fm Track Tags (if configured)
                if (useLastFm)
                {
                    try
                    {
                        var lastFmTags = await lastFmService.GetTrackTagsAsync(artist, title, cache, 5, cancellationToken).ConfigureAwait(false);
                        if (lastFmTags.Count > 0)
                        {
                            allMoodTags.AddRange(lastFmTags);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Last.fm track tags failed for {Artist} - {Title}", artist, title);
                    }
                }

                // 2. TheAudioDB Mood Data (always try - free API)
                try
                {
                    var moodData = await audioDbService.GetTrackMoodAsync(artist, title, cache, cancellationToken).ConfigureAwait(false);
                    if (moodData != null)
                    {
                        if (!string.IsNullOrEmpty(moodData.Mood))
                        {
                            track.Mood = moodData.Mood;
                            allMoodTags.Add(moodData.Mood);
                        }

                        if (!string.IsNullOrEmpty(moodData.Theme))
                        {
                            track.Theme = moodData.Theme;
                            allMoodTags.Add(moodData.Theme);
                        }

                        if (!string.IsNullOrEmpty(moodData.Speed))
                        {
                            allMoodTags.Add(moodData.Speed);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "AudioDB mood failed for {Artist} - {Title}", artist, title);
                }

                // 3. MusicBrainz Tags (rate limited - only for tracks without other mood data)
                // Skip MusicBrainz for now to avoid rate limit issues - can be enabled later
                // MusicBrainz has a 1 request/second limit which would be too slow for large libraries

                // Combine and dedupe mood tags
                if (allMoodTags.Count > 0)
                {
                    var uniqueTags = allMoodTags
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(6)
                        .ToArray();
                    track.MoodTags = uniqueTags;
                    enrichedCount++;
                }

                // Small delay to respect rate limits
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }

            // Save cache periodically
            if (batchNumber % 5 == 0)
            {
                cache.Save();
            }
        }

        // Final cache save
        cache.Save();

        _logger.LogInformation("Mood enrichment complete: {Enriched}/{Total} tracks enriched", enrichedCount, totalTracks);
        DebugLog($"Mood enrichment complete: {enrichedCount}/{totalTracks} tracks enriched");
    }

    /// <summary>
    /// Filter mood tags to only include vibe-related tags (not genre tags which we get from other sources).
    /// </summary>
    private static bool IsMoodTag(string tag)
    {
        var moodKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Energy/Tempo
            "chill", "relaxed", "calm", "mellow", "peaceful", "serene", "soothing",
            "energetic", "upbeat", "fast", "uptempo", "driving", "intense", "powerful",
            "slow", "downtempo", "ambient",

            // Emotional valence
            "happy", "joyful", "cheerful", "fun", "party", "celebratory",
            "sad", "melancholic", "melancholy", "bittersweet", "emotional", "heartbreak",
            "angry", "aggressive", "dark", "brooding", "moody",
            "romantic", "sensual", "love", "passionate",

            // Atmosphere
            "atmospheric", "dreamy", "ethereal", "hypnotic", "psychedelic",
            "epic", "anthemic", "triumphant", "uplifting", "inspirational",
            "groovy", "funky", "bouncy", "catchy",

            // Activity
            "workout", "running", "dance", "clubbing", "study", "focus", "sleep",
            "road trip", "summer", "night"
        };

        var tagLower = tag.ToLowerInvariant();
        return moodKeywords.Any(k => tagLower.Contains(k));
    }
}
