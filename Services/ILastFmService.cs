using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for fetching artist genre tags from Last.fm.
/// </summary>
public interface ILastFmService
{
    /// <summary>
    /// Gets the top genre tags for an artist from Last.fm.
    /// </summary>
    /// <param name="artistName">The artist name to look up.</param>
    /// <param name="cache">The metadata cache service for persistent caching.</param>
    /// <param name="maxTags">Maximum number of tags to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of genre tags, or empty if not found.</returns>
    Task<List<string>> GetArtistTagsAsync(
        string artistName,
        MetadataCacheService? cache,
        int maxTags = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the Last.fm service is configured with an API key.
    /// </summary>
    bool IsConfigured { get; }
}
