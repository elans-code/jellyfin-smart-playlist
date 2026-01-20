using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Models;

/// <summary>
/// Cache data model for persistent storage of track metadata.
/// </summary>
public class TrackMetadataCache
{
    /// <summary>
    /// Gets or sets the audio features cache. Key: SpotifyId, Value: audio features.
    /// </summary>
    public Dictionary<string, SpotifyAudioFeatures> AudioFeatures { get; set; } = new();

    /// <summary>
    /// Gets or sets the lyrics cache. Key: "artist|title", Value: lyrics snippet.
    /// </summary>
    public Dictionary<string, string> Lyrics { get; set; } = new();

    /// <summary>
    /// Gets or sets the artist genres cache from Last.fm. Key: artist name (lowercase), Value: genre tags.
    /// </summary>
    public Dictionary<string, List<string>> ArtistGenres { get; set; } = new();

    /// <summary>
    /// Gets or sets the Spotify artist genres cache. Key: Spotify artist ID, Value: genre tags.
    /// </summary>
    public Dictionary<string, List<string>> SpotifyArtistGenres { get; set; } = new();

    /// <summary>
    /// Gets or sets the Last.fm track tags cache. Key: "artist|title", Value: mood/genre tags.
    /// </summary>
    public Dictionary<string, List<string>> TrackTags { get; set; } = new();

    /// <summary>
    /// Gets or sets TheAudioDB mood cache. Key: "artist|title", Value: mood data.
    /// </summary>
    public Dictionary<string, TrackMoodData> AudioDbMoods { get; set; } = new();

    /// <summary>
    /// Gets or sets the MusicBrainz tags cache. Key: MusicBrainz recording ID, Value: tags.
    /// </summary>
    public Dictionary<string, List<string>> MusicBrainzTags { get; set; } = new();

    /// <summary>
    /// Gets or sets the MusicBrainz ID lookup cache. Key: "artist|title", Value: MusicBrainz recording ID.
    /// </summary>
    public Dictionary<string, string> MusicBrainzIds { get; set; } = new();

    /// <summary>
    /// Gets or sets the Essentia audio analysis cache. Key: file hash (path+size+mtime), Value: audio features.
    /// </summary>
    public Dictionary<string, EssentiaAudioFeatures> EssentiaFeatures { get; set; } = new();
}

/// <summary>
/// Mood data from TheAudioDB.
/// </summary>
public class TrackMoodData
{
    /// <summary>
    /// Gets or sets the mood (e.g., "Happy", "Sad", "Energetic").
    /// </summary>
    public string? Mood { get; set; }

    /// <summary>
    /// Gets or sets the theme (e.g., "Love", "Party", "Rebellion").
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Gets or sets the speed/tempo descriptor (e.g., "Fast", "Medium", "Slow").
    /// </summary>
    public string? Speed { get; set; }
}

/// <summary>
/// Spotify audio features for a track.
/// </summary>
public class SpotifyAudioFeatures
{
    /// <summary>
    /// Gets or sets the energy level (0-1). Higher = more intense/energetic.
    /// </summary>
    public float Energy { get; set; }

    /// <summary>
    /// Gets or sets the valence (0-1). Higher = more positive/happy sounding.
    /// </summary>
    public float Valence { get; set; }

    /// <summary>
    /// Gets or sets the danceability (0-1). Higher = more suitable for dancing.
    /// </summary>
    public float Danceability { get; set; }

    /// <summary>
    /// Gets or sets the acousticness (0-1). Higher = more acoustic.
    /// </summary>
    public float Acousticness { get; set; }

    /// <summary>
    /// Gets or sets the tempo in BPM.
    /// </summary>
    public float Tempo { get; set; }
}

/// <summary>
/// Audio features extracted from local files via Essentia.
/// </summary>
public class EssentiaAudioFeatures
{
    /// <summary>
    /// Gets or sets the file hash used for cache invalidation.
    /// Computed from: file path + file size + last modified time.
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the analysis timestamp (UTC).
    /// </summary>
    public DateTime AnalyzedAt { get; set; }

    /// <summary>
    /// Gets or sets the energy level (0-1). Derived from loudness and spectral energy.
    /// </summary>
    public float Energy { get; set; }

    /// <summary>
    /// Gets or sets the valence/mood (0-1). Derived from spectral features and key analysis.
    /// </summary>
    public float Valence { get; set; }

    /// <summary>
    /// Gets or sets the danceability (0-1). From Essentia's rhythm analysis.
    /// </summary>
    public float Danceability { get; set; }

    /// <summary>
    /// Gets or sets the acousticness (0-1). Derived from spectral characteristics.
    /// </summary>
    public float Acousticness { get; set; }

    /// <summary>
    /// Gets or sets the tempo in BPM. From Essentia's rhythm.bpm.
    /// </summary>
    public float Tempo { get; set; }

    /// <summary>
    /// Gets or sets the musical key (e.g., "C major", "A minor").
    /// </summary>
    public string? Key { get; set; }
}
