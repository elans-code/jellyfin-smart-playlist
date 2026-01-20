using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Models;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for interacting with the Spotify API.
/// </summary>
public interface ISpotifyService
{
    /// <summary>
    /// Gets tracks from the user's Liked Songs or specified playlist.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of Spotify tracks.</returns>
    Task<List<SpotifyTrackInfo>> GetSourceTracksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the current Spotify configuration and credentials.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if credentials are valid, false otherwise.</returns>
    Task<bool> ValidateCredentialsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enriches tracks with audio features from Spotify API, using cache when available.
    /// Note: This API was deprecated by Spotify in Nov 2024 for new apps.
    /// </summary>
    /// <param name="tracks">The tracks to enrich.</param>
    /// <param name="cache">The metadata cache service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    Task EnrichWithAudioFeaturesAsync(
        List<SpotifyTrackInfo> tracks,
        MetadataCacheService cache,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enriches tracks with artist genres from Spotify API, using cache when available.
    /// </summary>
    /// <param name="tracks">The tracks to enrich.</param>
    /// <param name="cache">The metadata cache service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    Task EnrichWithArtistGenresAsync(
        List<SpotifyTrackInfo> tracks,
        MetadataCacheService cache,
        CancellationToken cancellationToken = default);
}
