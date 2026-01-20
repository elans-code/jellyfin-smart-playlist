using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Models;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for matching Spotify tracks against the local Jellyfin library.
/// </summary>
public interface ILibraryMatcherService
{
    /// <summary>
    /// Matches Spotify tracks against local Jellyfin library items.
    /// </summary>
    /// <param name="spotifyTracks">The list of Spotify tracks to match.</param>
    /// <param name="cache">The metadata cache service for persistent caching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of matched tracks with their Jellyfin item IDs.</returns>
    Task<List<MatchedTrack>> MatchTracksAsync(
        List<SpotifyTrackInfo> spotifyTracks,
        MetadataCacheService? cache,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all audio tracks from the Jellyfin library.
    /// </summary>
    /// <param name="cache">The metadata cache service for persistent caching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of all tracks in the library as MatchedTrack objects.</returns>
    Task<List<MatchedTrack>> GetAllLibraryTracksAsync(MetadataCacheService? cache, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all audio tracks from the Jellyfin library along with their file paths.
    /// </summary>
    /// <param name="cache">The metadata cache service for persistent caching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing tracks and a dictionary mapping track IDs to file paths.</returns>
    Task<(List<MatchedTrack> Tracks, Dictionary<Guid, string> FilePaths)> GetAllLibraryTracksWithPathsAsync(
        MetadataCacheService? cache,
        CancellationToken cancellationToken = default);
}
