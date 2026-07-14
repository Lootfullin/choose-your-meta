# Russian Metadata for Jellyfin

![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11+-00A4DC?style=flat-square&logo=jellyfin&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)
![Build](https://img.shields.io/badge/Build-passing-brightgreen?style=flat-square)

A Jellyfin metadata plugin that automatically replaces English movie and TV series titles and overviews with Russian translations using **TMDB** (primary) and **Wikidata** (fallback).

---

## Features

- **🇷🇺 Russian titles & overviews** — movies and TV series get localized names and descriptions
- **Dual-source fallback** — TMDB for rich metadata (via proxy support), Wikidata (SPARQL) as automatic fallback
- **Configurable** — enable/disable title and overview replacement independently via Jellyfin Dashboard
- **Proxy support** — optional HTTP proxy for TMDB API access (useful in regions where TMDB is blocked)
- **Smart cascading** — tries TMDB first (richest data), falls back to Wikidata if unavailable
- **Logging** — detailed diagnostic logs for debugging metadata resolution
- **IMDb ID extraction** — automatically extracts IMDb IDs from file/folder names and provider keys

## Requirements

| Requirement | Version |
|-------------|---------|
| Jellyfin    | 10.11.x |
| .NET Runtime | 9.0+ (bundled with Jellyfin 10.11) |
| TMDB API key | [Get one free](https://www.themoviedb.org/settings/api) (optional, Wikidata fallback works without) |

## Installation

### Option 1: Manual installation

1. Download the latest `RussianMetadata.dll` from [Releases](https://github.com/Opiumforme/jellifin-russian-metadata/releases)
2. Copy the DLL to your Jellyfin `plugins` directory:
   ```
   /path/to/jellyfin/plugins/RussianMetadata/RussianMetadata.dll
   ```
3. Restart Jellyfin
4. Go to **Dashboard → Plugins → Russian Metadata** and configure
5. Assign **Russian Metadata** as a metadata downloader for your libraries:
   - **Movies**: Dashboard → Libraries → your movie library → Metadata downloaders → check **Russian Metadata**
   - **TV Shows**: Dashboard → Libraries → your TV library → Metadata downloaders → check **Russian Metadata**

### Option 2: Build from source

```bash
git clone https://github.com/Opiumforme/jellifin-russian-metadata.git
cd jellifin-russian-metadata/RussianMetadata
dotnet build -c Release
```

Copy `bin/Release/net9.0/RussianMetadata.dll` to your plugins folder.

## Configuration

![Configuration](https://img.shields.io/badge/Dashboard-Plugins-blue?style=flat-square)

Navigate to **Dashboard → Plugins → Russian Metadata → Settings**:

| Setting | Description |
|---------|-------------|
| **TMDB API Key** | Your TMDB API key (v3 auth). [Get one here](https://www.themoviedb.org/settings/api) |
| **Enable Russian Titles** | Replace English titles with Russian |
| **Enable Russian Overviews** | Replace English descriptions with Russian |
| **Proxy URL** | HTTP proxy URL for TMDB (e.g. `http://proxy.example.com:3128`) |
| **Proxy Username** | Proxy authentication username (optional) |
| **Proxy Password** | Proxy authentication password (optional) |

> **Note:** TMDB is optional. Without an API key, the plugin falls back to Wikidata, which provides Russian labels and descriptions for most well-known movies and TV series.

## How It Works

```
┌─────────────────────────────────────────────────┐
│  Jellyfin Metadata Refresh                       │
│                                                   │
│  1. Extract IMDb ID (ttXXXXXXXX) from file path   │
│     or existing provider keys                     │
│                                                   │
│  2. Try TMDB (with proxy if configured)           │
│     ├── Find movie/series by IMDb ID              │
│     └── Fetch Russian details (title, overview)   │
│                                                   │
│  3. Fallback: Wikidata SPARQL query               │
│     ├── Query by IMDb ID → Russian label/desc     │
│     └── Query by name if no IMDb ID               │
│                                                   │
│  4. Apply Russian metadata to the item            │
└─────────────────────────────────────────────────┘
```

### Priority

1. **TMDB** — rich metadata, Russian overviews (500+ chars), uses optional proxy
2. **Wikidata by IMDb ID** — reliable, shorter descriptions
3. **Wikidata by name** — fallback when no IMDb ID is available

## Building from Source

```bash
# Prerequisites: .NET 9.0 SDK
# Download from: https://dotnet.microsoft.com/download

git clone https://github.com/Opiumforme/jellifin-russian-metadata.git
cd jellifin-russian-metadata/RussianMetadata

# Build
dotnet build

# Build (release)
dotnet build -c Release

# Output: bin/Debug/net9.0/RussianMetadata.dll
```

## Development

### Project Structure

```
RussianMetadata/
├── Configuration/
│   ├── PluginConfiguration.cs    # Plugin settings model
│   └── configPage.html           # Web UI for Dashboard
├── Plugin.cs                     # Plugin entry point
├── RussianMovieProvider.cs       # Movie metadata provider
├── RussianSeriesProvider.cs      # TV series metadata provider
├── RussianMetadata.csproj        # .NET project file
├── README.md
└── LICENSE
```

### Key Interfaces

| Interface | Purpose |
|-----------|---------|
| `IRemoteMetadataProvider<Movie, MovieInfo>` | Remote provider for movies |
| `IRemoteMetadataProvider<Series, SeriesInfo>` | Remote provider for TV series |
| `ICustomMetadataProvider<Movie>` | Applies Russian data after all remote providers |
| `ICustomMetadataProvider<Series>` | Same for TV series |

## Troubleshooting

1. **Check Jellyfin logs** for `RussianMetadata:` entries — they contain detailed diagnostic info
2. **TMDB not responding?** Configure a proxy if TMDB is blocked in your region
3. **No Russian data applied?** Verify the item has an IMDb ID (`ProviderIds.Imdb`) in its metadata
4. **Build fails?** Ensure .NET 9.0 SDK is installed

## License

[MIT](LICENSE) © Tarasov Radomir

---

<p align="center">
  <sub>Built for the Jellyfin community · Not affiliated with Jellyfin or TMDB</sub>
</p>
