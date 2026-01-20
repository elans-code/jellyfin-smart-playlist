using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Models;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for creating and managing Jellyfin playlists.
/// </summary>
public interface IPlaylistGeneratorService
{
    /// <summary>
    /// Creates or updates Jellyfin playlists for the given vibe clusters.
    /// </summary>
    /// <param name="clusters">The vibe clusters with assigned track IDs.</param>
    /// <param name="userId">The Jellyfin user ID to create playlists for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CreatePlaylistsAsync(
        List<VibeCluster> clusters,
        Guid userId,
        CancellationToken cancellationToken = default);
}
