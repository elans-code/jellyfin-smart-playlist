using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Configuration;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Helpers;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Models;
using Microsoft.Extensions.Logging;
using Betalgo.Ranul.OpenAI;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using OpenAIClient = Betalgo.Ranul.OpenAI.Managers.OpenAIService;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for interacting with OpenAI to generate vibe clusters.
/// </summary>
public class OpenAIService : IOpenAIService
{
    private readonly ILogger<OpenAIService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public OpenAIService(ILogger<OpenAIService> logger)
    {
        _logger = logger;
    }

    private static PluginConfiguration Configuration => Plugin.Instance?.Configuration
        ?? throw new InvalidOperationException("Plugin not initialized");

    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText("/tmp/SmartSpotifyPlaylists_debug.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [OpenAI] {message}\n");
        }
        catch
        {
            // Ignore
        }
    }

    /// <inheritdoc />
    public async Task<VibeClusterResult> GenerateVibeClustersAsync(
        List<MatchedTrack> tracks,
        int numberOfClusters,
        CancellationToken cancellationToken = default)
    {
        var openAiClient = new OpenAIClient(new OpenAIOptions
        {
            ApiKey = Configuration.OpenAIApiKey
        });

        var model = Configuration.OpenAIModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            model = "gpt-4o-mini";
        }

        var totalUsage = new OpenAIUsageResult { ModelUsed = model };

        // Step 1: Generate cluster names and descriptions
        var trackList = string.Join("\n", tracks.Select(t => t.SpotifyTrack.DisplayName));

        var clusterPrompt = $$"""
            Analyze these songs and create exactly {{numberOfClusters}} mood/genre clusters.

            Songs:
            {{trackList}}

            For each cluster, provide:
            1. A creative, descriptive name (e.g., "Late Night Chill", "Upbeat Energy", "Melancholic Ballads")
            2. A brief description of the mood/vibe

            Respond in JSON format:
            {
                "clusters": [
                    {"name": "Cluster Name", "description": "Brief description"}
                ]
            }
            """;

        _logger.LogInformation("Requesting cluster generation from OpenAI using model {Model} for {Count} tracks", model, tracks.Count);

        var clusterResponse = await openAiClient.ChatCompletion.CreateCompletion(
            new ChatCompletionCreateRequest
            {
                Messages = new List<ChatMessage>
                {
                    ChatMessage.FromSystem("You are a music curator expert. Analyze songs and group them by mood, genre, and vibe. Always respond with valid JSON."),
                    ChatMessage.FromUser(clusterPrompt)
                },
                Model = model,
                ResponseFormat = new ResponseFormat { Type = StaticValues.CompletionStatics.ResponseFormat.Json }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!clusterResponse.Successful)
        {
            _logger.LogError("OpenAI cluster generation failed: {Error}", clusterResponse.Error?.Message);
            throw new InvalidOperationException($"OpenAI API error: {clusterResponse.Error?.Message}");
        }

        // Track usage from first call
        var usage1 = clusterResponse.Usage;
        if (usage1 != null)
        {
            totalUsage.InputTokens += usage1.PromptTokens;
            totalUsage.OutputTokens += usage1.CompletionTokens ?? 0;
            _logger.LogDebug("Cluster generation used {Input} input tokens, {Output} output tokens",
                usage1.PromptTokens, usage1.CompletionTokens);
        }

        var clusterJson = clusterResponse.Choices.First().Message.Content;
        _logger.LogDebug("Cluster generation response: {Json}", clusterJson);

        var clusterResult = JsonSerializer.Deserialize<ClusterGenerationResponse>(clusterJson ?? "{}", JsonOptions);

        if (clusterResult?.Clusters == null || clusterResult.Clusters.Count == 0)
        {
            throw new InvalidOperationException("Failed to parse cluster response from OpenAI");
        }

        _logger.LogInformation("Generated {Count} clusters: {Names}",
            clusterResult.Clusters.Count,
            string.Join(", ", clusterResult.Clusters.Select(c => c.Name)));

        // Step 2: Assign tracks to clusters (using genre info when available)
        var clusterList = string.Join("\n", clusterResult.Clusters.Select((c, i) => $"{i}. {c.Name}: {c.Description}"));
        var songList = string.Join("\n", tracks.Select((t, i) => $"{i}. {t.DisplayNameWithGenre}"));

        var minTracksPerPlaylist = Configuration.MinTracksPerPlaylist;
        if (minTracksPerPlaylist <= 0)
        {
            minTracksPerPlaylist = 10;
        }

        var assignmentPrompt = $$"""
            Given these mood/genre clusters:
            {{clusterList}}

            And these songs with metadata:
            Songs indexed from 0 to {{tracks.Count - 1}}:
            {{songList}}

            METADATA GUIDE:
            - Genre: Actual genre tags from Spotify/music files (most reliable for categorization)
            - Year: Release year (useful for era-based playlists: 80s, 90s, 2000s, modern)
            - Popularity: "Popular" (mainstream hits), "Moderate" (known tracks), "Niche" (deep cuts)
            - Mood: Direct mood descriptor from databases (e.g., "Happy", "Sad", "Energetic", "Romantic")
            - Theme: Thematic content (e.g., "Love", "Party", "Rebellion", "Heartbreak")
            - Vibe: Crowdsourced tags describing the feel (e.g., "chill", "uplifting", "dark", "groovy")
            - Energy/Valence/Tempo: Audio analysis (when available)
            - Lyrics: Snippet for mood analysis

            MOOD/VIBE-BASED MATCHING (HIGHLY RELIABLE):
            - Mood "Happy" + Theme "Party" → Upbeat/Party playlists
            - Mood "Sad" or "Melancholic" → Melancholic/Emotional playlists
            - Mood "Romantic" + Theme "Love" → Love songs/Romantic playlists
            - Vibe tags like "chill", "relaxed", "calm" → Chill/Ambient playlists
            - Vibe tags like "energetic", "workout", "pump up" → Workout/Energy playlists
            - Vibe tags like "dark", "brooding", "intense" → Dark/Moody playlists

            GENRE-BASED MATCHING (PRIMARY METHOD):
            - Pop, Dance Pop, Synth Pop → Pop/Dance playlists
            - Hip Hop, Rap, Trap, Drill → Hip-Hop/Rap playlists
            - Rock, Alternative, Indie Rock, Punk → Rock playlists
            - R&B, Soul, Neo Soul → R&B/Soul playlists
            - Electronic, House, EDM, Techno → Electronic/Dance playlists
            - Country, Americana → Country playlists
            - Jazz, Blues → Jazz/Blues playlists
            - Classical, Orchestral → Classical/Ambient playlists
            - Metal, Hard Rock → Metal/Heavy playlists
            - Indie, Alternative → Indie playlists
            - Latin, Reggaeton → Latin playlists

            AUDIO FEATURES (when available):
            - Energy 0.7+ + Valence 0.7+ → Upbeat/Party
            - Energy 0.3- + Valence 0.3- → Melancholic/Chill
            - Tempo 140+ → Fast/Dance

            CRITICAL RULES:
            1. PRIORITIZE GENRE TAGS - they're the most accurate signal
            2. Use your knowledge of artists (Drake=Hip-Hop, Taylor Swift=Pop, etc.)
            3. Analyze LYRICS to determine mood when available
            4. Use release YEAR for era-based clusters if applicable
            5. Assign EVERY song to exactly one cluster
            6. Each cluster MUST have at least {{minTracksPerPlaylist}} songs

            Respond in JSON format:
            {
                "assignments": [
                    {"cluster_index": 0, "song_indices": [0, 3, 7, ...]},
                    {"cluster_index": 1, "song_indices": [1, 2, 5, ...]},
                    ...
                ]
            }
            """;

        _logger.LogInformation("Requesting track assignments from OpenAI");

        var assignmentResponse = await openAiClient.ChatCompletion.CreateCompletion(
            new ChatCompletionCreateRequest
            {
                Messages = new List<ChatMessage>
                {
                    ChatMessage.FromSystem("You are a music curator expert. Match songs to the most appropriate mood clusters. Always respond with valid JSON."),
                    ChatMessage.FromUser(assignmentPrompt)
                },
                Model = model,
                ResponseFormat = new ResponseFormat { Type = StaticValues.CompletionStatics.ResponseFormat.Json }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!assignmentResponse.Successful)
        {
            _logger.LogError("OpenAI assignment generation failed: {Error}", assignmentResponse.Error?.Message);
            throw new InvalidOperationException($"OpenAI API error: {assignmentResponse.Error?.Message}");
        }

        // Track usage from second call
        var usage2 = assignmentResponse.Usage;
        if (usage2 != null)
        {
            totalUsage.InputTokens += usage2.PromptTokens;
            totalUsage.OutputTokens += usage2.CompletionTokens ?? 0;
            _logger.LogDebug("Assignment generation used {Input} input tokens, {Output} output tokens",
                usage2.PromptTokens, usage2.CompletionTokens);
        }

        var assignmentJson = assignmentResponse.Choices.First().Message.Content;
        DebugLog($"Assignment JSON response: {assignmentJson}");

        var assignmentResult = JsonSerializer.Deserialize<AssignmentResponse>(assignmentJson ?? "{}", JsonOptions);

        DebugLog($"Parsed {assignmentResult?.Assignments?.Count ?? 0} assignments");

        // Build final clusters with track identifiers
        if (assignmentResult?.Assignments != null)
        {
            foreach (var assignment in assignmentResult.Assignments)
            {
                DebugLog($"Assignment: cluster_index={assignment.ClusterIndex}, song_indices count={assignment.SongIndices?.Count ?? 0}");
                if (assignment.ClusterIndex >= 0 && assignment.ClusterIndex < clusterResult.Clusters.Count)
                {
                    var cluster = clusterResult.Clusters[assignment.ClusterIndex];
                    foreach (var songIndex in assignment.SongIndices ?? new List<int>())
                    {
                        if (songIndex >= 0 && songIndex < tracks.Count)
                        {
                            cluster.TrackIdentifiers.Add(tracks[songIndex].JellyfinItemId.ToString());
                        }
                    }
                    DebugLog($"Cluster '{cluster.Name}' now has {cluster.TrackIdentifiers.Count} tracks");
                }
            }
        }
        else
        {
            DebugLog("WARNING: No assignments parsed from OpenAI response!");
        }

        // Calculate the actual cost
        totalUsage.EstimatedCostUsd = OpenAICostCalculator.CalculateActualCost(
            totalUsage.InputTokens,
            totalUsage.OutputTokens,
            model);

        _logger.LogInformation(
            "Generated {Count} vibe clusters. Token usage: {Input} input, {Output} output, {Total} total. Cost: {Cost}",
            clusterResult.Clusters.Count,
            totalUsage.InputTokens,
            totalUsage.OutputTokens,
            totalUsage.TotalTokens,
            OpenAICostCalculator.FormatCost(totalUsage.EstimatedCostUsd));

        return new VibeClusterResult
        {
            Clusters = clusterResult.Clusters,
            Usage = totalUsage
        };
    }

    /// <inheritdoc />
    public async Task<VibeClusterResult> GenerateClustersFromPreferencesAsync(
        List<SpotifyTrackInfo> spotifyTracks,
        List<MatchedTrack> jellyfinTracks,
        int numberOfClusters,
        CancellationToken cancellationToken = default)
    {
        var openAiClient = new OpenAIClient(new OpenAIOptions
        {
            ApiKey = Configuration.OpenAIApiKey
        });

        var model = Configuration.OpenAIModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            model = "gpt-4o-mini";
        }

        var totalUsage = new OpenAIUsageResult { ModelUsed = model };

        // Limit Spotify tracks for preference analysis (to manage token usage)
        var spotifyForAnalysis = spotifyTracks.Take(200).ToList();
        var spotifyList = string.Join("\n", spotifyForAnalysis.Select(t => t.DisplayName));

        DebugLog($"Analyzing {spotifyForAnalysis.Count} Spotify tracks to understand preferences...");

        // Step 1: Analyze Spotify preferences and create cluster definitions
        var preferencePrompt = $$"""
            Analyze these songs from the user's Spotify library to understand their music taste:

            {{spotifyList}}

            Based on their preferences, create exactly {{numberOfClusters}} mood/genre playlist categories that would appeal to this user.
            These categories will be used to organize their entire music library, so make them broad enough to include various songs
            but specific enough to create cohesive playlists.

            For each category, provide:
            1. A creative, descriptive name (e.g., "Late Night Chill", "Workout Energy", "Melancholic Vibes")
            2. A detailed description of the mood, energy level, and types of songs that fit

            Respond in JSON format:
            {
                "clusters": [
                    {"name": "Category Name", "description": "Detailed description of mood, energy, and song types"}
                ]
            }
            """;

        var clusterResponse = await openAiClient.ChatCompletion.CreateCompletion(
            new ChatCompletionCreateRequest
            {
                Messages = new List<ChatMessage>
                {
                    ChatMessage.FromSystem("You are a music curator expert. Analyze the user's music taste and create personalized playlist categories. Always respond with valid JSON."),
                    ChatMessage.FromUser(preferencePrompt)
                },
                Model = model,
                ResponseFormat = new ResponseFormat { Type = StaticValues.CompletionStatics.ResponseFormat.Json }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!clusterResponse.Successful)
        {
            throw new InvalidOperationException($"OpenAI API error: {clusterResponse.Error?.Message}");
        }

        var usage1 = clusterResponse.Usage;
        if (usage1 != null)
        {
            totalUsage.InputTokens += usage1.PromptTokens;
            totalUsage.OutputTokens += usage1.CompletionTokens ?? 0;
        }

        var clusterJson = clusterResponse.Choices.First().Message.Content;
        DebugLog($"Preference-based clusters: {clusterJson}");

        var clusterResult = JsonSerializer.Deserialize<ClusterGenerationResponse>(clusterJson ?? "{}", JsonOptions);

        if (clusterResult?.Clusters == null || clusterResult.Clusters.Count == 0)
        {
            throw new InvalidOperationException("Failed to parse cluster response from OpenAI");
        }

        DebugLog($"Created {clusterResult.Clusters.Count} preference-based clusters: {string.Join(", ", clusterResult.Clusters.Select(c => c.Name))}");

        // Step 2: Assign ALL Jellyfin library tracks to these clusters
        var clusterList = string.Join("\n", clusterResult.Clusters.Select((c, i) => $"{i}. {c.Name}: {c.Description}"));

        // Include genre information in the song list for accurate matching
        var jellyfinList = string.Join("\n", jellyfinTracks.Select((t, i) => $"{i}. {t.DisplayNameWithGenre}"));

        // Log how many tracks have various metadata
        var tracksWithGenres = jellyfinTracks.Count(t => t.Genres.Length > 0 || t.SpotifyGenres.Length > 0);
        var tracksWithLyrics = jellyfinTracks.Count(t => !string.IsNullOrEmpty(t.LyricsSnippet));
        var tracksWithMood = jellyfinTracks.Count(t => !string.IsNullOrEmpty(t.Mood));
        var tracksWithTheme = jellyfinTracks.Count(t => !string.IsNullOrEmpty(t.Theme));
        var tracksWithVibeTags = jellyfinTracks.Count(t => t.MoodTags.Length > 0);
        var tracksWithAnyMoodData = jellyfinTracks.Count(t => !string.IsNullOrEmpty(t.Mood) || !string.IsNullOrEmpty(t.Theme) || t.MoodTags.Length > 0);
        DebugLog($"Tracks with genre metadata: {tracksWithGenres}/{jellyfinTracks.Count}");
        DebugLog($"Tracks with mood descriptor: {tracksWithMood}/{jellyfinTracks.Count}");
        DebugLog($"Tracks with theme descriptor: {tracksWithTheme}/{jellyfinTracks.Count}");
        DebugLog($"Tracks with vibe tags: {tracksWithVibeTags}/{jellyfinTracks.Count}");
        DebugLog($"Tracks with ANY mood/vibe data: {tracksWithAnyMoodData}/{jellyfinTracks.Count}");
        DebugLog($"Tracks with lyrics: {tracksWithLyrics}/{jellyfinTracks.Count}");

        // Log sample of what's being sent to OpenAI
        DebugLog("=== SAMPLE TRACKS BEING SENT TO OPENAI ===");
        var sampleTracks = jellyfinTracks.Take(20).ToList();
        foreach (var track in sampleTracks)
        {
            DebugLog($"  {track.DisplayNameWithGenre}");
        }
        if (jellyfinTracks.Count > 20)
        {
            DebugLog($"  ... and {jellyfinTracks.Count - 20} more tracks");
        }
        DebugLog("=== END SAMPLE ===");

        var minTracksPerPlaylist = Configuration.MinTracksPerPlaylist;
        if (minTracksPerPlaylist <= 0)
        {
            minTracksPerPlaylist = 10;
        }

        DebugLog($"Assigning {jellyfinTracks.Count} Jellyfin tracks to clusters...");

        // Process tracks in batches to avoid token limits
        const int batchSize = 250;
        var totalBatches = (int)Math.Ceiling((double)jellyfinTracks.Count / batchSize);
        DebugLog($"Processing {totalBatches} batches of {batchSize} tracks each");

        for (var batchIndex = 0; batchIndex < totalBatches; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchTracks = jellyfinTracks.Skip(batchIndex * batchSize).Take(batchSize).ToList();
            var batchStartIndex = batchIndex * batchSize;

            DebugLog($"=== BATCH {batchIndex + 1}/{totalBatches}: Tracks {batchStartIndex} to {batchStartIndex + batchTracks.Count - 1} ===");

            var batchList = string.Join("\n", batchTracks.Select((t, i) => $"{batchStartIndex + i}. {t.DisplayNameWithGenre}"));

            var assignmentPrompt = $$"""
                You are an expert music curator. Your job is to create PERFECT playlists where every song genuinely fits the vibe.

                PLAYLISTS TO FILL:
                {{clusterList}}

                SONGS TO ANALYZE (with metadata):
                {{batchList}}

                METADATA GUIDE:
                - Genre: From Spotify artist data and local files (MOST RELIABLE)
                - Year: Release year (for era-based playlists: 80s, 90s, 2000s, modern)
                - Popularity: "Popular" = mainstream hit, "Moderate" = known, "Niche" = deep cut
                - Mood: Direct mood descriptor (e.g., "Happy", "Sad", "Energetic", "Romantic")
                - Theme: Thematic content (e.g., "Love", "Party", "Rebellion", "Heartbreak")
                - Vibe: Crowdsourced tags describing feel (e.g., "chill", "uplifting", "dark", "groovy")
                - Energy/Valence/Tempo: Audio analysis (when available)
                - Lyrics: For mood analysis

                MOOD/VIBE-BASED MATCHING (HIGHLY RELIABLE - USE WITH GENRE):
                When Mood/Theme/Vibe tags are present, they're very accurate for playlist matching:
                - Mood "Happy" + Theme "Party" → Upbeat/Party playlists
                - Mood "Sad"/"Melancholic" → Emotional/Melancholic playlists
                - Mood "Romantic" + Theme "Love" → Love songs/Romantic playlists
                - Vibe: "chill", "relaxed" → Chill/Ambient playlists
                - Vibe: "energetic", "workout" → Workout/Energy playlists
                - Vibe: "dark", "brooding" → Dark/Moody playlists

                GENRE-BASED MATCHING (PRIMARY METHOD - USE THIS FIRST):
                - pop, dance pop, synth pop → Pop/Mainstream playlists
                - hip hop, rap, trap, drill → Hip-Hop/Rap playlists
                - rock, alternative, indie rock, punk → Rock playlists
                - r&b, soul, neo soul → R&B/Soul playlists
                - electronic, house, edm, techno → Electronic/Dance playlists
                - country, americana → Country playlists
                - jazz, blues → Jazz/Blues playlists
                - metal, hard rock, heavy metal → Metal playlists
                - indie, alternative → Indie playlists
                - latin, reggaeton → Latin playlists
                - classical, orchestral → Classical/Ambient playlists

                ADDITIONAL SIGNALS:
                1. MOOD/THEME/VIBE TAGS - From music databases (Last.fm, TheAudioDB) - VERY RELIABLE
                2. LYRICS - Analyze for mood (love=romantic, party=upbeat, pain=melancholic)
                3. ARTIST KNOWLEDGE - You know genres: Drake=rap, Taylor Swift=pop, Metallica=metal, etc.
                4. AUDIO FEATURES (if available) - Energy+Valence reveal vibe
                5. RELEASE YEAR - Group by era if creating decade-themed playlists

                STRICT RULES:
                ❌ DO NOT mix incompatible genres (no metal in pop playlist)
                ❌ DO NOT ignore genre tags when available
                ❌ DO NOT assign songs you're uncertain about
                ✓ PRIORITIZE genre over other signals
                ✓ SKIP songs that don't clearly match any playlist
                ✓ Use artist knowledge when metadata is sparse

                OUTPUT (only include songs that TRULY fit):
                {"assignments":[{"cluster_index":0,"song_indices":[...]},{"cluster_index":1,"song_indices":[...]}, ...]}
                """;

            var assignmentResponse = await openAiClient.ChatCompletion.CreateCompletion(
                new ChatCompletionCreateRequest
                {
                    Messages = new List<ChatMessage>
                    {
                        ChatMessage.FromSystem("You are an elite music curator. You ONLY place songs in playlists where they PERFECTLY match the vibe. You analyze lyrics, genre, and artist to ensure accuracy. You would rather skip a song than put it in the wrong playlist. You output valid JSON."),
                        ChatMessage.FromUser(assignmentPrompt)
                    },
                    Model = model,
                    MaxTokens = 4000,
                    ResponseFormat = new ResponseFormat { Type = StaticValues.CompletionStatics.ResponseFormat.Json }
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!assignmentResponse.Successful)
            {
                DebugLog($"ERROR in batch {batchIndex + 1}: {assignmentResponse.Error?.Message}");
                continue;
            }

            var usage2 = assignmentResponse.Usage;
            if (usage2 != null)
            {
                totalUsage.InputTokens += usage2.PromptTokens;
                totalUsage.OutputTokens += usage2.CompletionTokens ?? 0;
            }

            var assignmentJson = assignmentResponse.Choices.First().Message.Content;
            var assignmentResult = JsonSerializer.Deserialize<AssignmentResponse>(assignmentJson ?? "{}", JsonOptions);

            var tracksAssignedInBatch = 0;
            var assignedIndices = new HashSet<int>();

            if (assignmentResult?.Assignments != null)
            {
                foreach (var assignment in assignmentResult.Assignments)
                {
                    if (assignment.ClusterIndex >= 0 && assignment.ClusterIndex < clusterResult.Clusters.Count)
                    {
                        var cluster = clusterResult.Clusters[assignment.ClusterIndex];
                        foreach (var songIndex in assignment.SongIndices ?? new List<int>())
                        {
                            if (songIndex >= 0 && songIndex < jellyfinTracks.Count && !assignedIndices.Contains(songIndex))
                            {
                                cluster.TrackIdentifiers.Add(jellyfinTracks[songIndex].JellyfinItemId.ToString());
                                assignedIndices.Add(songIndex);
                                tracksAssignedInBatch++;
                            }
                        }
                    }
                }
            }

            DebugLog($"Batch {batchIndex + 1} complete: {tracksAssignedInBatch} tracks matched to playlists");
        }

        // Log final cluster results
        DebugLog("=== FINAL CLUSTER ASSIGNMENTS ===");
        foreach (var cluster in clusterResult.Clusters)
        {
            DebugLog($"  {cluster.Name}: {cluster.TrackIdentifiers.Count} tracks");
        }

        var totalAssigned = clusterResult.Clusters.Sum(c => c.TrackIdentifiers.Count);
        DebugLog($"Total tracks assigned: {totalAssigned}/{jellyfinTracks.Count}");

        // Calculate the actual cost
        totalUsage.EstimatedCostUsd = OpenAICostCalculator.CalculateActualCost(
            totalUsage.InputTokens,
            totalUsage.OutputTokens,
            model);

        DebugLog($"Generated {clusterResult.Clusters.Count} preference-based clusters. Cost: {OpenAICostCalculator.FormatCost(totalUsage.EstimatedCostUsd)}");

        return new VibeClusterResult
        {
            Clusters = clusterResult.Clusters,
            Usage = totalUsage
        };
    }
}
