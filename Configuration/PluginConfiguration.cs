using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Configuration;

/// <summary>
/// Configuration settings for the SmartSpotifyPlaylists plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Spotify Client ID.
    /// </summary>
    public string SpotifyClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Spotify Client Secret.
    /// </summary>
    public string SpotifyClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Spotify Refresh Token (obtained after OAuth flow).
    /// </summary>
    public string SpotifyRefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OpenAI API Key.
    /// </summary>
    public string OpenAIApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OpenAI model to use for analysis.
    /// </summary>
    public string OpenAIModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Gets or sets the maximum number of tracks to send to OpenAI for analysis.
    /// </summary>
    public int MaxTracksForAnalysis { get; set; } = 500;

    /// <summary>
    /// Gets or sets the target Spotify Playlist ID.
    /// If empty, defaults to user's Liked Songs.
    /// </summary>
    public string TargetSpotifyPlaylistId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether to use Spotify as a preference guide for clustering the entire Jellyfin library.
    /// When true: Analyzes Spotify liked songs to understand preferences, then creates playlists from ALL Jellyfin tracks.
    /// When false: Only creates playlists from tracks that match between Spotify and Jellyfin.
    /// </summary>
    public bool UseSpotifyAsPreference { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to analyze song lyrics to determine vibe when genre metadata is missing.
    /// This increases OpenAI token usage but improves categorization accuracy for songs without genre tags.
    /// </summary>
    public bool EnableLyricAnalysis { get; set; } = false;

    /// <summary>
    /// Gets or sets the Last.fm API key for fetching artist genre tags.
    /// Get a free key at https://www.last.fm/api/account/create
    /// </summary>
    public string LastFmApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyfin User ID to create playlists for.
    /// </summary>
    public string JellyfinUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of vibe clusters to generate (default: 5).
    /// </summary>
    public int NumberOfClusters { get; set; } = 5;

    /// <summary>
    /// Gets or sets the minimum fuzzy match score (0-100) to consider a match.
    /// </summary>
    public int MinimumMatchScore { get; set; } = 80;

    /// <summary>
    /// Gets or sets whether to delete old AI Mix playlists before creating new ones.
    /// </summary>
    public bool ReplaceExistingPlaylists { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum tracks per playlist.
    /// </summary>
    public int MaxTracksPerPlaylist { get; set; } = 50;

    /// <summary>
    /// Gets or sets the minimum tracks per playlist. Clusters with fewer tracks will be skipped.
    /// </summary>
    public int MinTracksPerPlaylist { get; set; } = 10;

    /// <summary>
    /// Gets or sets the total tokens used across all syncs.
    /// </summary>
    public int TotalTokensUsed { get; set; }

    /// <summary>
    /// Gets or sets the cumulative cost in USD across all syncs.
    /// </summary>
    public decimal TotalCostUsd { get; set; }

    /// <summary>
    /// Gets or sets the cost in USD of the last sync operation.
    /// </summary>
    public decimal LastSyncCostUsd { get; set; }

    // ========== Essentia Audio Analysis ==========

    /// <summary>
    /// Gets or sets whether to enable local audio analysis using Essentia.
    /// When enabled, analyzes local audio files to extract audio features (energy, tempo, etc.)
    /// as a replacement for Spotify's deprecated audio features API.
    /// </summary>
    public bool EnableEssentiaAnalysis { get; set; } = false;

    /// <summary>
    /// Gets or sets the path to the Essentia streaming extractor binary.
    /// Example: "/usr/bin/essentia_streaming_extractor_music" or "C:\essentia\essentia_streaming_extractor_music.exe"
    /// </summary>
    public string EssentiaBinaryPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of concurrent Essentia analysis processes.
    /// Higher values speed up analysis but use more CPU/memory. Default: 2.
    /// </summary>
    public int EssentiaMaxConcurrency { get; set; } = 2;

    /// <summary>
    /// Gets or sets the timeout in seconds for Essentia analysis per track.
    /// Longer tracks may need higher values. Default: 120 seconds.
    /// </summary>
    public int EssentiaTimeoutSeconds { get; set; } = 120;
}
