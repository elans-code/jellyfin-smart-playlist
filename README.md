# Smart Spotify Playlists for Jellyfin

A Jellyfin plugin that creates AI-powered mood/vibe playlists by analyzing your music library. Uses OpenAI to intelligently cluster your tracks into cohesive playlists based on genre, mood, energy, and musical characteristics.

## Features

- **AI-Powered Clustering**: Uses OpenAI (GPT-4o-mini recommended) to analyze your music and create themed playlists
- **Spotify Integration**: Optionally uses your Spotify Liked Songs to understand your music preferences
- **Local Audio Analysis**: Optional Essentia integration for extracting audio features (energy, tempo, danceability) directly from your files
- **Genre Enrichment**: Fetches genre data from Last.fm when your local files lack metadata
- **Lyrics Analysis**: Optional lyric-based mood detection
- **Smart Caching**: Caches all metadata lookups to speed up subsequent syncs

## Requirements

- Jellyfin Server 10.9+
- .NET 8.0 or later
- OpenAI API key
- (Optional) Spotify Developer credentials
- (Optional) Last.fm API key
- (Optional) Essentia for local audio analysis

## Installation

### From Release

1. Download the latest release from the [Releases](../../releases) page
2. Extract the ZIP file
3. Copy the contents to your Jellyfin plugins folder:
   - **Linux**: `/var/lib/jellyfin/plugins/SmartSpotifyPlaylists/`
   - **Windows**: `C:\ProgramData\Jellyfin\Server\plugins\SmartSpotifyPlaylists\`
   - **Docker**: `/config/plugins/SmartSpotifyPlaylists/`
4. Restart Jellyfin

### From Source

```bash
git clone https://github.com/YOUR_USERNAME/jellyfin-smart-playlists.git
cd jellyfin-smart-playlists
dotnet build -c Release
```

Copy the contents of `bin/Release/net9.0/` to your Jellyfin plugins folder.

## Configuration

1. Go to Jellyfin Dashboard → Plugins → Smart Spotify Playlists
2. Configure the following:

### Required Settings

- **OpenAI API Key**: Get one from [platform.openai.com](https://platform.openai.com/api-keys)
- **Jellyfin User**: Select the user who will own the playlists

### Optional Settings

- **Spotify Credentials**: Enable to use your Liked Songs as preference data
  - Create an app at [developer.spotify.com](https://developer.spotify.com/dashboard)
  - You'll need Client ID, Client Secret, and a Refresh Token

- **Last.fm API Key**: Improves genre detection for tracks without metadata
  - Get a free key at [last.fm/api](https://www.last.fm/api/account/create)

- **Essentia Audio Analysis**: Analyzes your actual audio files for energy, tempo, etc.
  - See [Essentia Setup](#essentia-setup) below

## Essentia Setup (Optional)

Essentia provides local audio analysis as a replacement for Spotify's deprecated Audio Features API.

### Docker (linuxserver/jellyfin)

Create `config/custom-cont-init.d/install-essentia.sh`:

```bash
#!/bin/bash

if ! python3 -c "import essentia" 2>/dev/null; then
    echo "Installing Essentia..."
    apt-get update
    apt-get install -y python3-pip
    pip3 uninstall numpy -y --break-system-packages 2>/dev/null
    pip3 install numpy==1.26.4 --break-system-packages
    pip3 install essentia --break-system-packages
fi

if [ ! -f /usr/local/bin/essentia_streaming_extractor_music ]; then
    cat > /usr/local/bin/essentia_streaming_extractor_music << 'SCRIPT'
#!/usr/bin/env python3
import sys
import json
from essentia.standard import MusicExtractor

if len(sys.argv) < 3:
    print("Usage: essentia_streaming_extractor_music <input_audio> <output_json>")
    sys.exit(1)

try:
    extractor = MusicExtractor()
    features, _ = extractor(sys.argv[1])
    result = {}
    for name in features.descriptorNames():
        value = features[name]
        if hasattr(value, "tolist"):
            value = value.tolist()
        result[name] = value
    with open(sys.argv[2], "w") as f:
        json.dump(result, f, indent=2)
    print("Analysis saved to " + sys.argv[2])
except Exception as e:
    print("Error: " + str(e), file=sys.stderr)
    sys.exit(1)
SCRIPT
    chmod +x /usr/local/bin/essentia_streaming_extractor_music
fi
```

Make it executable and restart the container.

### Linux (Native)

```bash
pip3 install essentia
# Create the wrapper script as shown above
```

### macOS

```bash
brew install essentia
```

## Usage

1. Configure the plugin in Dashboard → Plugins → Smart Spotify Playlists
2. Click "Run Sync Now" or wait for the scheduled task (default: Sunday 3 AM)
3. The plugin will:
   - Fetch your Spotify preferences (if configured)
   - Load all tracks from your Jellyfin library
   - Enrich tracks with genre/mood data
   - Analyze audio with Essentia (if enabled)
   - Use OpenAI to cluster tracks into themed playlists
   - Create "AI Mix: [Theme Name]" playlists

## Cost Estimation

The plugin uses OpenAI API which has usage-based pricing:

| Model | ~500 tracks | ~2000 tracks |
|-------|-------------|--------------|
| gpt-4o-mini | ~$0.01 | ~$0.03 |
| gpt-4o | ~$0.10 | ~$0.30 |

The config page shows estimated and actual costs.

## Troubleshooting

### Debug Logs

Check `/tmp/SmartSpotifyPlaylists_debug.log` for detailed logs.

### Common Issues

- **"Run Sync Now" doesn't work**: Make sure you've saved the configuration first
- **No playlists created**: Check that you have tracks in your library and OpenAI key is valid
- **Essentia not working**: Verify the binary is executable and in PATH

## License

MIT License - see [LICENSE](LICENSE) file.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Acknowledgments

- [Jellyfin](https://jellyfin.org/) - The free software media system
- [OpenAI](https://openai.com/) - AI models for clustering
- [Essentia](https://essentia.upf.edu/) - Audio analysis library
- [Last.fm](https://www.last.fm/) - Music metadata
- [SpotifyAPI-NET](https://github.com/JohnnyCrazy/SpotifyAPI-NET) - Spotify API wrapper
