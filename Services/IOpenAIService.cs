using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Models;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Result from OpenAI vibe cluster generation including usage statistics.
/// </summary>
public class VibeClusterResult
{
    /// <summary>
    /// Gets or sets the generated vibe clusters.
    /// </summary>
    public List<VibeCluster> Clusters { get; set; } = new();

    /// <summary>
    /// Gets or sets the token usage and cost information.
    /// </summary>
    public OpenAIUsageResult Usage { get; set; } = new();
}

/// <summary>
/// Service for interacting with OpenAI to generate vibe clusters.
/// </summary>
public interface IOpenAIService
{
    /// <summary>
    /// Analyzes tracks and generates mood/genre clusters.
    /// </summary>
    /// <param name="tracks">The matched tracks to analyze.</param>
    /// <param name="numberOfClusters">The number of clusters to generate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing vibe clusters and usage statistics.</returns>
    Task<VibeClusterResult> GenerateVibeClustersAsync(
        List<MatchedTrack> tracks,
        int numberOfClusters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes Spotify preferences and clusters Jellyfin library tracks based on those preferences.
    /// </summary>
    /// <param name="spotifyTracks">The Spotify tracks representing user preferences.</param>
    /// <param name="jellyfinTracks">All Jellyfin library tracks to cluster.</param>
    /// <param name="numberOfClusters">The number of clusters to generate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing vibe clusters and usage statistics.</returns>
    Task<VibeClusterResult> GenerateClustersFromPreferencesAsync(
        List<SpotifyTrackInfo> spotifyTracks,
        List<MatchedTrack> jellyfinTracks,
        int numberOfClusters,
        CancellationToken cancellationToken = default);
}
