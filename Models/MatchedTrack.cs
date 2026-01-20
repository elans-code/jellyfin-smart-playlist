using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Models;

/// <summary>
/// Represents a Spotify track matched to a local Jellyfin library item.
/// </summary>
public class MatchedTrack
{
    /// <summary>
    /// Gets or sets the original Spotify track information.
    /// </summary>
    public SpotifyTrackInfo SpotifyTrack { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Jellyfin item ID.
    /// </summary>
    public Guid JellyfinItemId { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin track title.
    /// </summary>
    public string JellyfinTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyfin artist name.
    /// </summary>
    public string JellyfinArtist { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the genres from Jellyfin metadata.
    /// </summary>
    public string[] Genres { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the album name from Jellyfin.
    /// </summary>
    public string Album { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a snippet of lyrics for vibe analysis (when enabled and genre is missing).
    /// </summary>
    public string? LyricsSnippet { get; set; }

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
    /// Gets or sets the Spotify artist genres (e.g., "pop", "hip hop", "indie rock").
    /// </summary>
    public string[] SpotifyGenres { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets mood/vibe tags from various sources (Last.fm, MusicBrainz, etc.).
    /// </summary>
    public string[] MoodTags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the mood descriptor (e.g., "Happy", "Sad", "Energetic").
    /// </summary>
    public string? Mood { get; set; }

    /// <summary>
    /// Gets or sets the theme descriptor (e.g., "Love", "Party", "Rebellion").
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Gets or sets the fuzzy match score (0-100).
    /// </summary>
    public int MatchScore { get; set; }

    /// <summary>
    /// Gets the full display name with all available metadata for OpenAI analysis.
    /// </summary>
    public string DisplayNameWithGenre
    {
        get
        {
            var parts = new List<string> { $"{JellyfinArtist} - {JellyfinTitle}" };

            if (!string.IsNullOrEmpty(Album))
            {
                parts.Add($"Album: {Album}");
            }

            // Combine genres from Jellyfin and Spotify
            var allGenres = new List<string>();
            if (Genres.Length > 0)
            {
                allGenres.AddRange(Genres);
            }

            if (SpotifyGenres.Length > 0)
            {
                foreach (var g in SpotifyGenres)
                {
                    if (!allGenres.Contains(g, StringComparer.OrdinalIgnoreCase))
                    {
                        allGenres.Add(g);
                    }
                }
            }

            if (allGenres.Count > 0)
            {
                parts.Add($"Genre: {string.Join(", ", allGenres.Take(5))}"); // Limit to 5 genres
            }

            // Add release year if available
            if (ReleaseYear.HasValue)
            {
                parts.Add($"Year: {ReleaseYear.Value}");
            }

            // Add popularity indicator
            if (Popularity > 0)
            {
                var popLabel = Popularity >= 70 ? "Popular" : Popularity >= 40 ? "Moderate" : "Niche";
                parts.Add($"Popularity: {popLabel}");
            }

            // Add audio features if available
            if (Energy.HasValue)
            {
                parts.Add($"Energy: {Energy.Value:F2}");
            }

            if (Valence.HasValue)
            {
                parts.Add($"Valence: {Valence.Value:F2}");
            }

            if (Tempo.HasValue)
            {
                parts.Add($"Tempo: {Tempo.Value:F0}");
            }

            // Add mood/vibe data
            if (!string.IsNullOrEmpty(Mood))
            {
                parts.Add($"Mood: {Mood}");
            }

            if (!string.IsNullOrEmpty(Theme))
            {
                parts.Add($"Theme: {Theme}");
            }

            if (MoodTags.Length > 0)
            {
                parts.Add($"Vibe: {string.Join(", ", MoodTags.Take(3))}");
            }

            if (!string.IsNullOrEmpty(LyricsSnippet))
            {
                parts.Add($"Lyrics: \"{LyricsSnippet}\"");
            }

            return string.Join(" | ", parts);
        }
    }

    /// <summary>
    /// Gets the display name in "Artist - Title" format.
    /// </summary>
    public string DisplayName => $"{JellyfinArtist} - {JellyfinTitle}";
}
