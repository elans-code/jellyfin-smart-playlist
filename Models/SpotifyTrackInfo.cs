using System;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Models;

/// <summary>
/// Represents a track from Spotify.
/// </summary>
public class SpotifyTrackInfo
{
    /// <summary>
    /// Gets or sets the Spotify track ID.
    /// </summary>
    public string SpotifyId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the primary artist's Spotify ID (for fetching artist genres).
    /// </summary>
    public string ArtistId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the track title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the artist name(s).
    /// </summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the album name.
    /// </summary>
    public string Album { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the track popularity (0-100). Higher = more popular/mainstream.
    /// </summary>
    public int Popularity { get; set; }

    /// <summary>
    /// Gets or sets the release year of the track.
    /// </summary>
    public int? ReleaseYear { get; set; }

    /// <summary>
    /// Gets or sets whether the track has explicit content.
    /// </summary>
    public bool IsExplicit { get; set; }

    /// <summary>
    /// Gets or sets the artist genres from Spotify (e.g., "pop", "hip hop", "indie rock").
    /// </summary>
    public string[] SpotifyGenres { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the energy level (0-1). Higher = more intense/energetic.
    /// </summary>
    public float? Energy { get; set; }

    /// <summary>
    /// Gets or sets the valence (0-1). Higher = more positive/happy sounding.
    /// </summary>
    public float? Valence { get; set; }

    /// <summary>
    /// Gets or sets the danceability (0-1). Higher = more suitable for dancing.
    /// </summary>
    public float? Danceability { get; set; }

    /// <summary>
    /// Gets or sets the acousticness (0-1). Higher = more acoustic.
    /// </summary>
    public float? Acousticness { get; set; }

    /// <summary>
    /// Gets or sets the tempo in BPM.
    /// </summary>
    public float? Tempo { get; set; }

    /// <summary>
    /// Gets the display name in "Artist - Title" format.
    /// </summary>
    public string DisplayName => $"{Artist} - {Title}";
}
