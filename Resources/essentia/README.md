# Essentia Binaries

Place the `essentia_streaming_extractor_music` binary for each platform in the corresponding folder:

## Folder Structure

```
essentia/
├── linux-x64/
│   └── essentia_streaming_extractor_music
├── linux-arm64/
│   └── essentia_streaming_extractor_music
├── win-x64/
│   └── essentia_streaming_extractor_music.exe
├── osx-x64/
│   └── essentia_streaming_extractor_music
└── osx-arm64/
    └── essentia_streaming_extractor_music
```

## How to Get the Binary

### Option 1: pip (Recommended)

```bash
pip install essentia

# The streaming extractor may be included, check:
which essentia_streaming_extractor_music
# Or on Windows: where essentia_streaming_extractor_music
```

If installed via pip, the binary location depends on your Python environment:
- **Linux/macOS**: `~/.local/bin/` or within virtualenv `bin/`
- **Windows**: `Scripts/` folder in your Python installation

### Option 2: System Package Manager

**Linux (Debian/Ubuntu):**
```bash
apt-get install essentia-extractors
# Binary at: /usr/bin/essentia_streaming_extractor_music
```

**macOS (Homebrew):**
```bash
brew install essentia
# Binary at: /usr/local/bin/essentia_streaming_extractor_music (Intel)
# Or: /opt/homebrew/bin/essentia_streaming_extractor_music (Apple Silicon)
```

### Option 3: Docker

```dockerfile
RUN pip install essentia
# Or: RUN apt-get install -y essentia-extractors
```

### Option 4: Build from Source

Download from Essentia releases: https://github.com/MTG/essentia/releases

## Notes

- Only include platforms you want to support
- The plugin will auto-detect and extract the appropriate binary
- If no binary is bundled, users can install system-wide or specify a path in settings
- Each binary is ~30-50MB, so consider which platforms to include
- The plugin searches in this order:
  1. User-configured path (in plugin settings)
  2. Bundled binary (extracted from plugin)
  3. System PATH
