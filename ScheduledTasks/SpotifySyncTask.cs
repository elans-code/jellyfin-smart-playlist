using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Configuration;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Helpers;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.ScheduledTasks;

/// <summary>
/// Scheduled task that syncs Spotify tracks and generates AI playlists.
/// </summary>
public class SpotifySyncTask : IScheduledTask
{
    private ISpotifyService _spotifyService = null!;
    private ILibraryMatcherService _libraryMatcherService = null!;
    private IOpenAIService _openAIService = null!;
    private IPlaylistGeneratorService _playlistGeneratorService = null!;
    private MetadataCacheService _metadataCache = null!;
    private MoodEnrichmentService _moodEnrichmentService = null!;
    private EssentiaService _essentiaService = null!;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifySyncTask"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="playlistManager">The Jellyfin playlist manager.</param>
    public SpotifySyncTask(
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager)
    {
        _logger = NullLogger.Instance;
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;

        // Defer service creation until execution to avoid constructor issues
        _spotifyService = null!;
        _libraryMatcherService = null!;
        _openAIService = null!;
        _playlistGeneratorService = null!;
    }

    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;

    private void EnsureServicesInitialized()
    {
        if (_spotifyService == null)
        {
            _spotifyService = new SpotifyService(NullLogger<SpotifyService>.Instance);
        }

        if (_libraryMatcherService == null)
        {
            _libraryMatcherService = new LibraryMatcherService(_libraryManager, NullLogger<LibraryMatcherService>.Instance);
        }

        if (_openAIService == null)
        {
            _openAIService = new OpenAIService(NullLogger<OpenAIService>.Instance);
        }

        if (_playlistGeneratorService == null)
        {
            _playlistGeneratorService = new PlaylistGeneratorService(_playlistManager, _libraryManager, NullLogger<PlaylistGeneratorService>.Instance);
        }

        if (_metadataCache == null)
        {
            _metadataCache = new MetadataCacheService(NullLogger<MetadataCacheService>.Instance);
        }

        if (_moodEnrichmentService == null)
        {
            _moodEnrichmentService = new MoodEnrichmentService(NullLogger<MoodEnrichmentService>.Instance);
        }

        if (_essentiaService == null)
        {
            _essentiaService = new EssentiaService(NullLogger<EssentiaService>.Instance);
        }
    }

    /// <inheritdoc />
    public string Name => "Sync Spotify AI Playlists";

    /// <inheritdoc />
    public string Key => "SmartSpotifyPlaylistsSync";

    /// <inheritdoc />
    public string Description =>
        "Fetches Spotify Liked Songs, matches to local library, and generates AI mood playlists.";

    /// <inheritdoc />
    public string Category => "Smart Spotify Playlists";

    private static string DebugLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "jellyfin",
        "plugins",
        "SmartSpotifyPlaylists_debug.log");

    private static void DebugLog(string message)
    {
        try
        {
            var logDir = Path.GetDirectoryName(DebugLogPath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            File.AppendAllText(DebugLogPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch
        {
            // If we can't write to the log, try /tmp as fallback
            try
            {
                File.AppendAllText("/tmp/SmartSpotifyPlaylists_debug.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch
            {
                // Ignore if we can't log
            }
        }
    }

    private static PluginConfiguration Configuration => Plugin.Instance?.Configuration
        ?? throw new InvalidOperationException("Plugin not initialized");

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.WeeklyTrigger,
                DayOfWeek = DayOfWeek.Sunday,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
            }
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        DebugLog("=== Starting Spotify AI Playlist sync ===");

        try
        {
            // Check if Plugin.Instance is available
            if (Plugin.Instance == null)
            {
                DebugLog("ERROR: Plugin.Instance is null!");
                return;
            }

            DebugLog("Plugin.Instance is available");

            // Initialize services lazily
            EnsureServicesInitialized();
            DebugLog("Services initialized");

            // Load metadata cache from disk
            _metadataCache.Load();
            DebugLog("Metadata cache loaded");

            // Validate configuration
            if (!ValidateConfiguration())
            {
                DebugLog("Configuration validation failed - see details above");
                return;
            }

            DebugLog("Configuration validated successfully");

            Services.VibeClusterResult clusterResult;

            // Check which mode to use
            if (Configuration.UseSpotifyAsPreference)
            {
                // NEW MODE: Use Spotify as preference guide, cluster ALL Jellyfin tracks
                progress.Report(5);
                DebugLog("Mode: Using Spotify preferences to cluster ALL Jellyfin library tracks");

                // Step 1: Validate Spotify credentials
                DebugLog("Step 1: Validating Spotify credentials...");
                if (!await _spotifyService.ValidateCredentialsAsync(cancellationToken).ConfigureAwait(false))
                {
                    DebugLog("ERROR: Spotify credentials are invalid");
                    return;
                }
                DebugLog("Spotify credentials validated");

                // Step 2: Fetch Spotify tracks (for preference analysis)
                progress.Report(10);
                DebugLog("Step 2: Fetching Spotify liked songs for preference analysis...");
                var spotifyTracks = await _spotifyService.GetSourceTracksAsync(cancellationToken).ConfigureAwait(false);
                DebugLog($"Fetched {spotifyTracks.Count} Spotify tracks for preference analysis");

                if (spotifyTracks.Count == 0)
                {
                    DebugLog("WARNING: No Spotify tracks found for preference analysis");
                    return;
                }

                // Step 2b: Enrich Spotify tracks with metadata
                progress.Report(12);
                DebugLog("Step 2b: Enriching Spotify tracks with artist genres...");
                await _spotifyService.EnrichWithArtistGenresAsync(spotifyTracks, _metadataCache, cancellationToken).ConfigureAwait(false);
                DebugLog("Artist genres enrichment complete");

                progress.Report(18);
                DebugLog("Step 2c: Attempting to fetch audio features (may be unavailable)...");
                await _spotifyService.EnrichWithAudioFeaturesAsync(spotifyTracks, _metadataCache, cancellationToken).ConfigureAwait(false);
                DebugLog("Audio features enrichment complete");

                // Step 3: Get ALL Jellyfin library tracks
                progress.Report(25);
                DebugLog("Step 3: Loading ALL tracks from Jellyfin library...");

                // Use the method that also returns file paths for Essentia analysis
                var (jellyfinTracks, filePaths) = await _libraryMatcherService.GetAllLibraryTracksWithPathsAsync(_metadataCache, cancellationToken).ConfigureAwait(false);
                DebugLog($"Loaded {jellyfinTracks.Count} tracks from Jellyfin library ({filePaths.Count} with file paths)");

                if (jellyfinTracks.Count == 0)
                {
                    DebugLog("WARNING: No tracks found in Jellyfin library");
                    return;
                }

                // Step 3b: Enrich tracks with mood data from Last.fm and TheAudioDB
                progress.Report(28);
                DebugLog("Step 3b: Enriching tracks with mood/vibe data...");
                await _moodEnrichmentService.EnrichWithMoodDataAsync(jellyfinTracks, _metadataCache, cancellationToken).ConfigureAwait(false);
                DebugLog("Mood enrichment complete");

                // Step 3c: Analyze tracks with Essentia (if enabled)
                if (Configuration.EnableEssentiaAnalysis)
                {
                    progress.Report(30);
                    DebugLog("Step 3c: Running Essentia audio analysis...");

                    if (await _essentiaService.ValidateConfigurationAsync(cancellationToken).ConfigureAwait(false))
                    {
                        // Essentia gets 30-70% of progress (40% total) since it takes the longest
                        var essentiaProgress = new Progress<double>(p =>
                            progress.Report(30 + (p * 0.40))); // 30-70%

                        var analyzed = await _essentiaService.EnrichTracksWithAudioFeaturesAsync(
                            jellyfinTracks,
                            filePaths,
                            _metadataCache,
                            essentiaProgress,
                            cancellationToken).ConfigureAwait(false);

                        DebugLog($"Essentia analysis complete: {analyzed} tracks enriched with audio features");
                    }
                    else
                    {
                        DebugLog("WARNING: Essentia is enabled but not properly configured - skipping audio analysis");
                    }
                }

                // Save cache after enrichment
                progress.Report(Configuration.EnableEssentiaAnalysis ? 70 : 38);
                _metadataCache.Save();
                DebugLog("Metadata cache saved");

                // Apply track limit for OpenAI analysis
                var maxTracks = Configuration.MaxTracksForAnalysis;
                if (maxTracks <= 0)
                {
                    maxTracks = 500;
                }

                var tracksForAnalysis = jellyfinTracks;
                if (jellyfinTracks.Count > maxTracks)
                {
                    DebugLog($"Limiting Jellyfin tracks from {jellyfinTracks.Count} to {maxTracks} for OpenAI analysis");
                    tracksForAnalysis = jellyfinTracks.Take(maxTracks).ToList();
                }

                // Progress depends on whether Essentia was run
                progress.Report(Configuration.EnableEssentiaAnalysis ? 72 : 40);

                // Step 4: Generate clusters based on Spotify preferences, assign Jellyfin tracks
                DebugLog($"Step 4: Analyzing preferences and clustering {tracksForAnalysis.Count} Jellyfin tracks...");
                clusterResult = await _openAIService.GenerateClustersFromPreferencesAsync(
                    spotifyTracks,
                    tracksForAnalysis,
                    Configuration.NumberOfClusters,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // LEGACY MODE: Only use tracks that match between Spotify and Jellyfin
                progress.Report(5);
                DebugLog("Mode: Using Spotify-matched tracks only (legacy mode)");
                DebugLog("Step 1: Validating Spotify credentials...");

                if (!await _spotifyService.ValidateCredentialsAsync(cancellationToken).ConfigureAwait(false))
                {
                    DebugLog("ERROR: Spotify credentials are invalid");
                    return;
                }

                DebugLog("Spotify credentials validated");

                progress.Report(10);
                DebugLog("Step 2: Fetching tracks from Spotify...");

                var spotifyTracks = await _spotifyService.GetSourceTracksAsync(cancellationToken).ConfigureAwait(false);

                DebugLog($"Fetched {spotifyTracks.Count} tracks from Spotify");

                if (spotifyTracks.Count == 0)
                {
                    DebugLog("WARNING: No tracks found in Spotify source");
                    return;
                }

                // Step 2b: Enrich Spotify tracks with metadata
                progress.Report(12);
                DebugLog("Step 2b: Enriching Spotify tracks with artist genres...");
                await _spotifyService.EnrichWithArtistGenresAsync(spotifyTracks, _metadataCache, cancellationToken).ConfigureAwait(false);
                DebugLog("Artist genres enrichment complete");

                progress.Report(18);
                DebugLog("Step 2c: Attempting to fetch audio features (may be unavailable)...");
                await _spotifyService.EnrichWithAudioFeaturesAsync(spotifyTracks, _metadataCache, cancellationToken).ConfigureAwait(false);
                DebugLog("Audio features enrichment complete");

                progress.Report(25);

                DebugLog($"Step 3: Matching {spotifyTracks.Count} tracks against local library...");

                var matchedTracks = await _libraryMatcherService.MatchTracksAsync(spotifyTracks, _metadataCache, cancellationToken).ConfigureAwait(false);

                DebugLog($"Matched {matchedTracks.Count} tracks to local library");

                // Save cache after enrichment
                _metadataCache.Save();
                DebugLog("Metadata cache saved");

                if (matchedTracks.Count == 0)
                {
                    DebugLog("WARNING: No tracks matched to local library");
                    return;
                }

                // Step 3b: Enrich matched tracks with mood data
                progress.Report(35);
                DebugLog("Step 3b: Enriching matched tracks with mood/vibe data...");
                await _moodEnrichmentService.EnrichWithMoodDataAsync(matchedTracks, _metadataCache, cancellationToken).ConfigureAwait(false);
                DebugLog("Mood enrichment complete");

                // Step 3c: Analyze tracks with Essentia (if enabled)
                if (Configuration.EnableEssentiaAnalysis)
                {
                    progress.Report(38);
                    DebugLog("Step 3c: Running Essentia audio analysis...");

                    if (await _essentiaService.ValidateConfigurationAsync(cancellationToken).ConfigureAwait(false))
                    {
                        // Get file paths for matched tracks
                        var (_, filePaths) = await _libraryMatcherService.GetAllLibraryTracksWithPathsAsync(_metadataCache, cancellationToken).ConfigureAwait(false);
                        DebugLog($"Retrieved {filePaths.Count} file paths for Essentia analysis");

                        // Essentia gets 38-70% of progress (32% total) since it takes the longest
                        var essentiaProgress = new Progress<double>(p =>
                            progress.Report(38 + (p * 0.32))); // 38-70%

                        var analyzed = await _essentiaService.EnrichTracksWithAudioFeaturesAsync(
                            matchedTracks,
                            filePaths,
                            _metadataCache,
                            essentiaProgress,
                            cancellationToken).ConfigureAwait(false);

                        DebugLog($"Essentia analysis complete: {analyzed} tracks enriched with audio features");
                    }
                    else
                    {
                        DebugLog("WARNING: Essentia is enabled but not properly configured - skipping audio analysis");
                    }
                }

                // Save cache after enrichment
                _metadataCache.Save();
                DebugLog("Metadata cache saved after enrichment");

                progress.Report(Configuration.EnableEssentiaAnalysis ? 72 : 50);

                // Apply track limit for OpenAI analysis
                var maxTracks = Configuration.MaxTracksForAnalysis;
                if (maxTracks <= 0)
                {
                    maxTracks = 500;
                }

                var tracksForAnalysis = matchedTracks;
                if (matchedTracks.Count > maxTracks)
                {
                    DebugLog($"Limiting tracks from {matchedTracks.Count} to {maxTracks}");
                    tracksForAnalysis = matchedTracks.Take(maxTracks).ToList();
                }

                DebugLog($"Step 4: Generating vibe clusters via OpenAI for {tracksForAnalysis.Count} tracks...");

                clusterResult = await _openAIService.GenerateVibeClustersAsync(
                    tracksForAnalysis,
                    Configuration.NumberOfClusters,
                    cancellationToken).ConfigureAwait(false);
            }

            DebugLog($"Generated {clusterResult.Clusters.Count} clusters");

            // Update usage statistics
            UpdateUsageStatistics(clusterResult.Usage);

            progress.Report(80);

            // Step 5: Create playlists (80-100%)
            DebugLog($"Step 5: Creating {clusterResult.Clusters.Count} playlists...");

            var userId = Guid.Parse(Configuration.JellyfinUserId);
            await _playlistGeneratorService.CreatePlaylistsAsync(clusterResult.Clusters, userId, cancellationToken).ConfigureAwait(false);

            progress.Report(100);
            DebugLog($"=== Sync completed successfully! Cost: {OpenAICostCalculator.FormatCost(clusterResult.Usage.EstimatedCostUsd)} ===");
        }
        catch (OperationCanceledException)
        {
            DebugLog("Task was cancelled");
            _logger.LogInformation("Spotify AI Playlist sync was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            DebugLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            DebugLog($"Stack trace: {ex.StackTrace}");
            _logger.LogError(ex, "Spotify AI Playlist sync failed");
            throw;
        }
    }

    private void UpdateUsageStatistics(Models.OpenAIUsageResult usage)
    {
        try
        {
            var config = Configuration;
            config.LastSyncCostUsd = usage.EstimatedCostUsd;
            config.TotalTokensUsed += usage.TotalTokens;
            config.TotalCostUsd += usage.EstimatedCostUsd;

            Plugin.Instance?.SaveConfiguration(config);

            _logger.LogInformation(
                "Updated usage statistics: Last sync cost: {LastCost}, Total tokens: {TotalTokens}, Cumulative cost: {TotalCost}",
                OpenAICostCalculator.FormatCost(config.LastSyncCostUsd),
                config.TotalTokensUsed,
                OpenAICostCalculator.FormatCost(config.TotalCostUsd));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save usage statistics to configuration");
        }
    }

    private bool ValidateConfiguration()
    {
        DebugLog("Validating configuration...");

        var config = Configuration;

        var issues = new List<string>();

        DebugLog($"UseSpotifyAsPreference: {config.UseSpotifyAsPreference}");

        // Spotify credentials are required for both modes (preference-based or matched-only)
        DebugLog($"SpotifyClientId: {(string.IsNullOrWhiteSpace(config.SpotifyClientId) ? "EMPTY" : "SET")}");
        if (string.IsNullOrWhiteSpace(config.SpotifyClientId))
        {
            issues.Add("Spotify Client ID is not configured");
        }

        DebugLog($"SpotifyClientSecret: {(string.IsNullOrWhiteSpace(config.SpotifyClientSecret) ? "EMPTY" : "SET")}");
        if (string.IsNullOrWhiteSpace(config.SpotifyClientSecret))
        {
            issues.Add("Spotify Client Secret is not configured");
        }

        DebugLog($"SpotifyRefreshToken: {(string.IsNullOrWhiteSpace(config.SpotifyRefreshToken) ? "EMPTY" : "SET")}");
        if (string.IsNullOrWhiteSpace(config.SpotifyRefreshToken))
        {
            issues.Add("Spotify Refresh Token is not configured (OAuth required)");
        }

        DebugLog($"OpenAIApiKey: {(string.IsNullOrWhiteSpace(config.OpenAIApiKey) ? "EMPTY" : "SET")}");
        if (string.IsNullOrWhiteSpace(config.OpenAIApiKey))
        {
            issues.Add("OpenAI API Key is not configured");
        }

        DebugLog($"JellyfinUserId: {(string.IsNullOrWhiteSpace(config.JellyfinUserId) ? "EMPTY" : config.JellyfinUserId)}");
        if (string.IsNullOrWhiteSpace(config.JellyfinUserId))
        {
            issues.Add("Jellyfin User ID is not configured");
        }

        if (issues.Count > 0)
        {
            foreach (var issue in issues)
            {
                DebugLog($"CONFIG ISSUE: {issue}");
                _logger.LogWarning("Configuration issue: {Issue}", issue);
            }

            return false;
        }

        return true;
    }
}
