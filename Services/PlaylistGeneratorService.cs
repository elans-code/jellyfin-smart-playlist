using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Configuration;
using Jellyfin.Plugin.SmartSpotifyPlaylists.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Services;

/// <summary>
/// Service for creating and managing Jellyfin playlists.
/// </summary>
public class PlaylistGeneratorService : IPlaylistGeneratorService
{
    private readonly IPlaylistManager _playlistManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<PlaylistGeneratorService> _logger;
    private const string PlaylistPrefix = "AI Mix: ";

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistGeneratorService"/> class.
    /// </summary>
    /// <param name="playlistManager">The Jellyfin playlist manager.</param>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="logger">The logger.</param>
    public PlaylistGeneratorService(
        IPlaylistManager playlistManager,
        ILibraryManager libraryManager,
        ILogger<PlaylistGeneratorService> logger)
    {
        _playlistManager = playlistManager;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    private static PluginConfiguration Configuration => Plugin.Instance?.Configuration
        ?? throw new InvalidOperationException("Plugin not initialized");

    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText("/tmp/SmartSpotifyPlaylists_debug.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [Playlist] {message}\n");
        }
        catch
        {
            // Ignore
        }
    }

    /// <inheritdoc />
    public async Task CreatePlaylistsAsync(
        List<VibeCluster> clusters,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        DebugLog($"Creating playlists for user {userId}");
        DebugLog($"Received {clusters.Count} clusters to create");

        if (Configuration.ReplaceExistingPlaylists)
        {
            DebugLog("Deleting existing AI Mix playlists...");
            await DeleteExistingAIMixPlaylistsAsync(userId, cancellationToken).ConfigureAwait(false);
        }

        var maxTracks = Configuration.MaxTracksPerPlaylist;
        var minTracks = Configuration.MinTracksPerPlaylist;
        if (minTracks <= 0)
        {
            minTracks = 10;
        }

        DebugLog($"Min tracks per playlist: {minTracks}, Max tracks per playlist: {maxTracks}");

        foreach (var cluster in clusters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DebugLog($"Processing cluster: {cluster.Name} with {cluster.TrackIdentifiers.Count} tracks");

            if (cluster.TrackIdentifiers.Count == 0)
            {
                DebugLog($"Skipping empty cluster: {cluster.Name}");
                continue;
            }

            if (cluster.TrackIdentifiers.Count < minTracks)
            {
                DebugLog($"WARNING: Cluster '{cluster.Name}' has only {cluster.TrackIdentifiers.Count} tracks (minimum: {minTracks}) - creating anyway");
            }

            var playlistName = $"{PlaylistPrefix}{cluster.Name}";
            var itemIds = cluster.TrackIdentifiers
                .Take(maxTracks)
                .Select(id => Guid.Parse(id))
                .ToArray();

            DebugLog($"Creating playlist '{playlistName}' with {itemIds.Length} tracks");

            try
            {
                var request = new PlaylistCreationRequest
                {
                    Name = playlistName,
                    UserId = userId,
                    MediaType = Jellyfin.Data.Enums.MediaType.Audio,
                    ItemIdList = itemIds
                };

                var result = await _playlistManager.CreatePlaylist(request).ConfigureAwait(false);

                DebugLog($"SUCCESS: Created playlist '{playlistName}' with ID: {result.Id}");
            }
            catch (Exception ex)
            {
                DebugLog($"ERROR creating playlist '{playlistName}': {ex.GetType().Name}: {ex.Message}");
                DebugLog($"Stack: {ex.StackTrace}");
            }
        }

        DebugLog("Finished creating playlists");
    }

    private async Task DeleteExistingAIMixPlaylistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existingPlaylists = _playlistManager.GetPlaylists(userId);

        foreach (var playlist in existingPlaylists)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (playlist.Name?.StartsWith(PlaylistPrefix, StringComparison.OrdinalIgnoreCase) == true)
            {
                try
                {
                    _libraryManager.DeleteItem(
                        playlist,
                        new DeleteOptions { DeleteFileLocation = false });

                    _logger.LogInformation("Deleted existing AI Mix playlist: {PlaylistName}", playlist.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete playlist '{PlaylistName}'", playlist.Name);
                }
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
