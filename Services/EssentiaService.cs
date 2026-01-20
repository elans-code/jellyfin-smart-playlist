using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Configuration;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for analyzing audio files using Essentia CLI tool.
/// </summary>
public class EssentiaService
{
    private readonly ILogger<EssentiaService> _logger;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private bool? _isAvailable;
    private string? _resolvedBinaryPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="EssentiaService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public EssentiaService(ILogger<EssentiaService> logger)
    {
        _logger = logger;
        var maxConcurrency = Configuration.EssentiaMaxConcurrency;
        if (maxConcurrency <= 0)
        {
            maxConcurrency = 2;
        }

        _concurrencySemaphore = new SemaphoreSlim(maxConcurrency);
    }

    private static PluginConfiguration Configuration => Plugin.Instance?.Configuration
        ?? throw new InvalidOperationException("Plugin not initialized");

    /// <summary>
    /// Gets the path to the Essentia binary, either from configuration or bundled with the plugin.
    /// </summary>
    private string GetEssentiaBinaryPath()
    {
        // If already resolved, return cached path
        if (!string.IsNullOrEmpty(_resolvedBinaryPath) && File.Exists(_resolvedBinaryPath))
        {
            return _resolvedBinaryPath;
        }

        // First, check if user specified a custom path
        var configuredPath = Configuration.EssentiaBinaryPath;
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            _resolvedBinaryPath = configuredPath;
            DebugLog($"Using configured Essentia binary: {_resolvedBinaryPath}");
            return _resolvedBinaryPath;
        }

        // Try to use bundled binary
        var bundledPath = ExtractBundledBinary();
        if (!string.IsNullOrEmpty(bundledPath) && File.Exists(bundledPath))
        {
            _resolvedBinaryPath = bundledPath;
            DebugLog($"Using bundled Essentia binary: {_resolvedBinaryPath}");
            return _resolvedBinaryPath;
        }

        // Fall back to system PATH
        var systemBinary = FindInSystemPath("essentia_streaming_extractor_music");
        if (!string.IsNullOrEmpty(systemBinary))
        {
            _resolvedBinaryPath = systemBinary;
            DebugLog($"Using system Essentia binary: {_resolvedBinaryPath}");
            return _resolvedBinaryPath;
        }

        DebugLog("No Essentia binary found");
        return string.Empty;
    }

    /// <summary>
    /// Extracts the bundled Essentia binary to the plugin data folder.
    /// </summary>
    private string? ExtractBundledBinary()
    {
        try
        {
            var pluginDataPath = Plugin.Instance?.DataFolderPath;
            if (string.IsNullOrEmpty(pluginDataPath))
            {
                return null;
            }

            // Determine platform
            string platformFolder;
            string binaryName;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                platformFolder = RuntimeInformation.OSArchitecture == Architecture.Arm64
                    ? "linux-arm64"
                    : "linux-x64";
                binaryName = "essentia_streaming_extractor_music";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                platformFolder = "win-x64";
                binaryName = "essentia_streaming_extractor_music.exe";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                platformFolder = RuntimeInformation.OSArchitecture == Architecture.Arm64
                    ? "osx-arm64"
                    : "osx-x64";
                binaryName = "essentia_streaming_extractor_music";
            }
            else
            {
                DebugLog($"Unsupported platform: {RuntimeInformation.OSDescription}");
                return null;
            }

            var targetDir = Path.Combine(pluginDataPath, "essentia", platformFolder);
            var targetPath = Path.Combine(targetDir, binaryName);

            // Check if already extracted
            if (File.Exists(targetPath))
            {
                return targetPath;
            }

            // Try to extract from embedded resource
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"Jellyfin.Plugin.SmartSpotifyPlaylists.Resources.essentia.{platformFolder}.{binaryName}";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                // Resource not found - this is expected if binary not bundled
                DebugLog($"Bundled Essentia binary not found for platform: {platformFolder}");
                return null;
            }

            // Create directory and extract
            Directory.CreateDirectory(targetDir);

            using (var fileStream = File.Create(targetPath))
            {
                stream.CopyTo(fileStream);
            }

            DebugLog($"Extracted bundled Essentia binary to: {targetPath}");

            // Set executable permissions on Unix
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    // Use chmod to make executable
                    var chmod = Process.Start(new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{targetPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    chmod?.WaitForExit(5000);
                    DebugLog("Set executable permissions on Essentia binary");
                }
                catch (Exception ex)
                {
                    DebugLog($"Failed to set executable permissions: {ex.Message}");
                }
            }

            return targetPath;
        }
        catch (Exception ex)
        {
            DebugLog($"Failed to extract bundled Essentia binary: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Searches for a binary in the system PATH.
    /// </summary>
    private static string? FindInSystemPath(string binaryName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
        {
            return null;
        }

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var paths = pathVar.Split(separator);

        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, binaryName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            // On Windows, try with .exe extension
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var exePath = fullPath + ".exe";
                if (File.Exists(exePath))
                {
                    return exePath;
                }
            }
        }

        return null;
    }

    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText("/tmp/SmartSpotifyPlaylists_debug.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [Essentia] {message}\n");
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Gets whether Essentia analysis is enabled and the binary is available.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            if (!Configuration.EnableEssentiaAnalysis)
            {
                return false;
            }

            // Return the validation result - the binary could be configured, bundled, or in system PATH
            return _isAvailable ?? false;
        }
    }

    /// <summary>
    /// Validates that the Essentia binary exists and is executable.
    /// </summary>
    /// <returns>True if Essentia is properly configured and available.</returns>
    public async Task<bool> ValidateConfigurationAsync(CancellationToken cancellationToken = default)
    {
        if (!Configuration.EnableEssentiaAnalysis)
        {
            DebugLog("Essentia analysis is disabled in configuration");
            _isAvailable = false;
            return false;
        }

        // Try to resolve the binary path (configured, bundled, or system)
        var binaryPath = GetEssentiaBinaryPath();
        if (string.IsNullOrWhiteSpace(binaryPath))
        {
            DebugLog("No Essentia binary found (not configured, not bundled, not in PATH)");
            _logger.LogWarning("No Essentia binary found. Either configure the path, install essentia-extractors, or use a plugin version with bundled binary.");
            _isAvailable = false;
            return false;
        }

        if (!File.Exists(binaryPath))
        {
            DebugLog($"Essentia binary not found at resolved path: {binaryPath}");
            _logger.LogWarning("Essentia binary not found at: {Path}", binaryPath);
            _isAvailable = false;
            return false;
        }

        // Try running with --help to verify it works
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = "--help",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Ignore
                }

                DebugLog("Essentia binary validation timed out");
                _isAvailable = false;
                return false;
            }

            // Essentia returns 0 on --help, some versions return 1
            _isAvailable = process.ExitCode == 0 || process.ExitCode == 1;

            if (_isAvailable == true)
            {
                DebugLog($"Essentia binary validated successfully at: {binaryPath}");
                _logger.LogInformation("Essentia binary validated successfully at: {Path}", binaryPath);
            }
            else
            {
                DebugLog($"Essentia binary validation failed with exit code: {process.ExitCode}");
                _logger.LogWarning("Essentia binary validation failed with exit code: {Code}", process.ExitCode);
            }

            return _isAvailable.Value;
        }
        catch (Exception ex)
        {
            DebugLog($"Failed to validate Essentia binary: {ex.Message}");
            _logger.LogError(ex, "Failed to validate Essentia binary at: {Path}", binaryPath);
            _isAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Analyzes a single audio file and returns extracted features.
    /// </summary>
    /// <param name="filePath">The path to the audio file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The extracted audio features or null if analysis fails.</returns>
    public async Task<EssentiaAudioFeatures?> AnalyzeFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Audio file not found: {Path}", filePath);
            return null;
        }

        var tempOutputPath = Path.Combine(Path.GetTempPath(), $"essentia_{Guid.NewGuid()}.json");

        try
        {
            var timeoutSeconds = Configuration.EssentiaTimeoutSeconds;
            if (timeoutSeconds <= 0)
            {
                timeoutSeconds = 120;
            }

            var binaryPath = GetEssentiaBinaryPath();
            if (string.IsNullOrEmpty(binaryPath))
            {
                _logger.LogWarning("Essentia binary path not available");
                return null;
            }

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = $"\"{filePath}\" \"{tempOutputPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            DebugLog($"Analyzing: {Path.GetFileName(filePath)}");

            process.Start();

            // Wait with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout occurred
                DebugLog($"Essentia analysis timed out after {timeoutSeconds}s for: {Path.GetFileName(filePath)}");
                _logger.LogWarning("Essentia analysis timed out after {Seconds}s for: {Path}",
                    timeoutSeconds, filePath);
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Ignore
                }

                return null;
            }

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                DebugLog($"Essentia analysis failed for {Path.GetFileName(filePath)}. Exit code: {process.ExitCode}");
                _logger.LogWarning("Essentia analysis failed for {Path}. Exit code: {Code}, Error: {Error}",
                    filePath, process.ExitCode, stderr);
                return null;
            }

            // Parse the JSON output
            if (!File.Exists(tempOutputPath))
            {
                DebugLog($"Essentia output file not created for: {Path.GetFileName(filePath)}");
                _logger.LogWarning("Essentia output file not created for: {Path}", filePath);
                return null;
            }

            var jsonContent = await File.ReadAllTextAsync(tempOutputPath, cancellationToken).ConfigureAwait(false);
            return ParseEssentiaOutput(jsonContent, filePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog($"Essentia analysis exception for {Path.GetFileName(filePath)}: {ex.Message}");
            _logger.LogError(ex, "Essentia analysis failed for: {Path}", filePath);
            return null;
        }
        finally
        {
            // Clean up temp file
            try
            {
                if (File.Exists(tempOutputPath))
                {
                    File.Delete(tempOutputPath);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Analyzes multiple tracks in parallel, using caching to avoid re-analysis.
    /// </summary>
    /// <param name="tracks">The tracks to analyze.</param>
    /// <param name="trackFilePaths">Mapping of track IDs to file paths.</param>
    /// <param name="cache">The metadata cache service.</param>
    /// <param name="progress">Optional progress reporter (0-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of tracks successfully analyzed.</returns>
    public async Task<int> EnrichTracksWithAudioFeaturesAsync(
        List<MatchedTrack> tracks,
        Dictionary<Guid, string> trackFilePaths,
        MetadataCacheService cache,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            DebugLog("Essentia is not available, skipping audio analysis");
            _logger.LogDebug("Essentia is not available, skipping audio analysis");
            return 0;
        }

        var tracksToAnalyze = new List<(MatchedTrack Track, string FilePath, string FileHash)>();
        var cachedCount = 0;

        // Check cache and identify tracks needing analysis
        foreach (var track in tracks)
        {
            if (!trackFilePaths.TryGetValue(track.JellyfinItemId, out var filePath) ||
                string.IsNullOrEmpty(filePath))
            {
                continue;
            }

            var fileHash = ComputeFileHash(filePath);
            var cached = cache.GetEssentiaFeatures(fileHash);

            if (cached != null)
            {
                // Apply cached features
                ApplyFeaturesToTrack(track, cached);
                cachedCount++;
            }
            else
            {
                tracksToAnalyze.Add((track, filePath, fileHash));
            }
        }

        DebugLog($"Essentia analysis: {cachedCount} from cache, {tracksToAnalyze.Count} to analyze");
        _logger.LogInformation("Essentia analysis: {Cached} from cache, {ToAnalyze} to analyze",
            cachedCount, tracksToAnalyze.Count);

        if (tracksToAnalyze.Count == 0)
        {
            progress?.Report(100);
            return cachedCount;
        }

        // Analyze tracks in parallel with concurrency limit
        var analyzed = 0;
        var failed = 0;
        var completed = 0;
        var maxConcurrency = Configuration.EssentiaMaxConcurrency;
        if (maxConcurrency <= 0)
        {
            maxConcurrency = 2;
        }

        await Parallel.ForEachAsync(
            tracksToAnalyze,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = cancellationToken
            },
            async (item, ct) =>
            {
                await _concurrencySemaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var features = await AnalyzeFileAsync(item.FilePath, ct).ConfigureAwait(false);

                    if (features != null)
                    {
                        ApplyFeaturesToTrack(item.Track, features);
                        cache.SetEssentiaFeatures(item.FileHash, features);
                        Interlocked.Increment(ref analyzed);
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                    }

                    var current = Interlocked.Increment(ref completed);
                    var progressValue = (double)current / tracksToAnalyze.Count * 100;
                    progress?.Report(progressValue);

                    // Log progress every 50 tracks
                    if (current % 50 == 0)
                    {
                        DebugLog($"Essentia progress: {current}/{tracksToAnalyze.Count} ({progressValue:F1}%)");
                    }
                }
                finally
                {
                    _concurrencySemaphore.Release();
                }
            }).ConfigureAwait(false);

        // Save cache after analysis
        cache.Save();

        DebugLog($"Essentia analysis complete: {analyzed} analyzed, {failed} failed, {cachedCount} from cache");
        _logger.LogInformation("Essentia analysis complete: {Analyzed} analyzed, {Failed} failed, {Cached} from cache",
            analyzed, failed, cachedCount);

        return analyzed + cachedCount;
    }

    private static void ApplyFeaturesToTrack(MatchedTrack track, EssentiaAudioFeatures features)
    {
        track.Energy = features.Energy;
        track.Valence = features.Valence;
        track.Danceability = features.Danceability;
        track.Acousticness = features.Acousticness;
        track.Tempo = features.Tempo;
    }

    private EssentiaAudioFeatures? ParseEssentiaOutput(string json, string filePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var features = new EssentiaAudioFeatures
            {
                FileHash = ComputeFileHash(filePath),
                AnalyzedAt = DateTime.UtcNow
            };

            // Python MusicExtractor outputs FLAT keys like "lowlevel.average_loudness" and "rhythm.bpm"
            // instead of nested objects. Handle both formats.

            // Try flat format first (Python MusicExtractor)
            var avgLoudness = GetFlatFloat(root, "lowlevel.average_loudness", 0);
            var dynamicComplexity = GetFlatFloat(root, "lowlevel.dynamic_complexity", 0);
            var spectralCentroidMean = GetFlatFloat(root, "lowlevel.spectral_centroid.mean", 0);
            var spectralEnergyMean = GetFlatFloat(root, "lowlevel.spectral_energy.mean", 0);
            var dissonanceMean = GetFlatFloat(root, "lowlevel.dissonance.mean", 0);
            var zcrMean = GetFlatFloat(root, "lowlevel.zerocrossingrate.mean", 0);

            features.Tempo = GetFlatFloat(root, "rhythm.bpm", 0);
            features.Danceability = GetFlatFloat(root, "rhythm.danceability", 0);

            // Try to get key info
            var keyKey = GetFlatString(root, "tonal.key_krumhansl.key");
            var keyScale = GetFlatString(root, "tonal.key_krumhansl.scale");
            if (string.IsNullOrEmpty(keyKey))
            {
                keyKey = GetFlatString(root, "tonal.key_edma.key");
                keyScale = GetFlatString(root, "tonal.key_edma.scale");
            }
            if (string.IsNullOrEmpty(keyKey))
            {
                keyKey = GetFlatString(root, "tonal.chords_key");
                keyScale = GetFlatString(root, "tonal.chords_scale");
            }

            // If flat format didn't work, try nested format (CLI binary)
            if (avgLoudness == 0 && features.Tempo == 0)
            {
                if (root.TryGetProperty("rhythm", out var rhythm))
                {
                    features.Tempo = GetFloatValue(rhythm, "bpm", 0);
                    features.Danceability = GetFloatValue(rhythm, "danceability", 0);
                }

                if (root.TryGetProperty("lowlevel", out var lowlevel))
                {
                    avgLoudness = GetFloatValue(lowlevel, "average_loudness", 0);
                    dynamicComplexity = GetFloatValue(lowlevel, "dynamic_complexity", 0);
                    spectralCentroidMean = GetMeanValue(lowlevel, "spectral_centroid");
                    spectralEnergyMean = GetMeanValue(lowlevel, "spectral_energy");
                    dissonanceMean = GetMeanValue(lowlevel, "dissonance");
                    zcrMean = GetMeanValue(lowlevel, "zerocrossingrate");
                }

                if (root.TryGetProperty("tonal", out var tonal))
                {
                    keyKey = GetStringValue(tonal, "key_krumhansl", "key") ?? GetStringValue(tonal, "key_edma", "key");
                    keyScale = GetStringValue(tonal, "key_krumhansl", "scale") ?? GetStringValue(tonal, "key_edma", "scale");
                }
            }

            // Normalize danceability if it's not already 0-1
            if (features.Danceability > 1)
            {
                features.Danceability = Math.Clamp(features.Danceability / 3.0f, 0, 1);
            }

            // Map to normalized 0-1 values (Spotify-like)
            features.Energy = MapEnergy(avgLoudness, dynamicComplexity, spectralEnergyMean);
            features.Acousticness = MapAcousticness(spectralCentroidMean, zcrMean, dissonanceMean);

            if (!string.IsNullOrEmpty(keyKey))
            {
                features.Key = $"{keyKey} {keyScale}".Trim();
            }

            // Valence - use combination of key (major=happy) and spectral
            features.Valence = MapValence(keyScale, spectralCentroidMean);

            DebugLog($"Parsed: {Path.GetFileName(filePath)} -> Energy={features.Energy:F2}, Valence={features.Valence:F2}, Dance={features.Danceability:F2}, Tempo={features.Tempo:F0}");

            return features;
        }
        catch (Exception ex)
        {
            DebugLog($"Failed to parse Essentia JSON output: {ex.Message}");
            _logger.LogError(ex, "Failed to parse Essentia JSON output");
            return null;
        }
    }

    /// <summary>
    /// Gets a float value from a flat key like "lowlevel.average_loudness".
    /// </summary>
    private static float GetFlatFloat(JsonElement root, string flatKey, float defaultValue)
    {
        if (root.TryGetProperty(flatKey, out var prop))
        {
            if (prop.TryGetSingle(out var value))
            {
                return value;
            }

            if (prop.TryGetDouble(out var dValue))
            {
                return (float)dValue;
            }

            // Handle arrays - take first element or mean
            if (prop.ValueKind == JsonValueKind.Array && prop.GetArrayLength() > 0)
            {
                var first = prop[0];
                if (first.TryGetSingle(out var arrValue))
                {
                    return arrValue;
                }
            }
        }

        return defaultValue;
    }

    /// <summary>
    /// Gets a string value from a flat key.
    /// </summary>
    private static string? GetFlatString(JsonElement root, string flatKey)
    {
        if (root.TryGetProperty(flatKey, out var prop))
        {
            return prop.GetString();
        }

        return null;
    }

    private static float GetFloatValue(JsonElement element, string propertyName, float defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.TryGetSingle(out var value))
            {
                return value;
            }

            if (prop.TryGetDouble(out var dValue))
            {
                return (float)dValue;
            }
        }

        return defaultValue;
    }

    private static float GetMeanValue(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            // Try getting the mean value first
            if (prop.TryGetProperty("mean", out var mean))
            {
                if (mean.TryGetSingle(out var value))
                {
                    return value;
                }

                if (mean.TryGetDouble(out var dValue))
                {
                    return (float)dValue;
                }
            }

            // Try direct value
            if (prop.TryGetSingle(out var directValue))
            {
                return directValue;
            }

            if (prop.TryGetDouble(out var directDValue))
            {
                return (float)directDValue;
            }
        }

        return 0;
    }

    private static string? GetStringValue(JsonElement element, string propertyName, string subProperty)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.TryGetProperty(subProperty, out var sub))
            {
                return sub.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Maps Essentia features to Spotify-like Energy (0-1).
    /// Energy represents intensity and activity.
    /// </summary>
    private static float MapEnergy(float avgLoudness, float dynamicComplexity, float spectralEnergy)
    {
        // avgLoudness is typically 0-1 already
        // dynamicComplexity is typically 0-10
        // Combine them with weights
        var loudnessComponent = Math.Clamp(avgLoudness, 0, 1);
        var dynamicComponent = Math.Clamp(dynamicComplexity / 10f, 0, 1);
        var energyComponent = spectralEnergy > 0 ? Math.Clamp((float)Math.Log10(spectralEnergy + 1) / 10f, 0, 1) : 0;

        return Math.Clamp(loudnessComponent * 0.4f + dynamicComponent * 0.3f + energyComponent * 0.3f, 0, 1);
    }

    /// <summary>
    /// Maps Essentia features to Spotify-like Acousticness (0-1).
    /// Higher = more acoustic (less electronic/distorted).
    /// </summary>
    private static float MapAcousticness(float spectralCentroid, float zcr, float dissonance)
    {
        // Lower spectral centroid, lower ZCR, and lower dissonance = more acoustic
        // Normalize spectral centroid (typically 500-4000 Hz for music)
        var centroidNorm = 1 - Math.Clamp((spectralCentroid - 500) / 3500f, 0, 1);
        var zcrNorm = 1 - Math.Clamp(zcr * 10, 0, 1);
        var dissonanceNorm = 1 - Math.Clamp(dissonance, 0, 1);

        return Math.Clamp(centroidNorm * 0.4f + zcrNorm * 0.3f + dissonanceNorm * 0.3f, 0, 1);
    }

    /// <summary>
    /// Maps Essentia features to Spotify-like Valence (0-1).
    /// Higher = more positive/happy, Lower = more negative/sad.
    /// </summary>
    private static float MapValence(string? scale, float spectralCentroid)
    {
        var baseValence = 0.5f;

        // Major keys tend to sound happier
        if (!string.IsNullOrEmpty(scale))
        {
            if (scale.Equals("major", StringComparison.OrdinalIgnoreCase))
            {
                baseValence += 0.15f;
            }
            else if (scale.Equals("minor", StringComparison.OrdinalIgnoreCase))
            {
                baseValence -= 0.15f;
            }
        }

        // Higher spectral centroid correlates with brighter, happier sounds
        if (spectralCentroid > 0)
        {
            var brightnessBonus = Math.Clamp((spectralCentroid - 1500) / 3000f, -0.15f, 0.15f);
            baseValence += brightnessBonus;
        }

        return Math.Clamp(baseValence, 0, 1);
    }

    /// <summary>
    /// Computes a hash for cache invalidation based on file path, size, and modification time.
    /// </summary>
    private static string ComputeFileHash(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var hashInput = $"{filePath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
            return Convert.ToHexString(hashBytes)[..32]; // First 32 chars
        }
        catch
        {
            // If we can't get file info, just hash the path
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(filePath));
            return Convert.ToHexString(hashBytes)[..32];
        }
    }
}
