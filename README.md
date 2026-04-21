# Jellyfin Local Cache Plugin

A Jellyfin plugin that caches media files from slow network-mounted storage to local SSD for faster playback.

Designed for setups where the Jellyfin server has limited fast local storage (e.g. a VPS with SSD) and large media files on a slower network mount (e.g. NFS, SMB/CIFS, rclone). Large files that stutter or fail to play over the network mount can be cached locally for smooth playback.

## Features

- **Manual caching** — select which movies/episodes to cache from the dashboard or directly from the movie page
- **Automatic playback from cache** — cached items automatically play from local SSD, no manual source selection needed
- **Disk quota management** — configurable max cache size with usage tracking
- **Dashboard UI** — browse library, search, cache/evict items, monitor copy progress
- **Detail page integration** — cache/evict button and file size info injected into movie detail pages via a small JS script
- **Background file copy** — caching happens in the background with progress tracking
- **Persistent state** — cache manifest survives server restarts

## How It Works

1. **Caching**: When you cache a movie, the plugin copies the file from the network mount to local SSD storage in the background.

2. **Playback**: The plugin implements `IMediaSourceProvider` to inject a local media source that sorts ahead of the network source. When you press Play, Jellyfin automatically uses the local copy.

3. **Management**: A dashboard page lets you browse your library, see file sizes, cache/evict items, and monitor disk usage. A small JavaScript file adds cache/evict buttons directly on movie detail pages.

## Installation

### Prerequisites

- Jellyfin 10.10.x or 10.11.x
- .NET 9 SDK (for building)
- Local SSD storage on the Jellyfin server

### Build

```bash
dotnet build -c Release
```

### Deploy the Plugin

1. Create a plugin directory on your Jellyfin server:
   ```bash
   mkdir -p /var/lib/jellyfin/plugins/Local\ Cache_1.0.0.0
   ```

2. Copy the plugin DLL:
   ```bash
   cp bin/Release/net9.0/Jellyfin.Plugin.LocalCache.dll \
     /var/lib/jellyfin/plugins/Local\ Cache_1.0.0.0/
   ```

3. Create `meta.json` in the same directory:
   ```json
   {
     "category": "General",
     "changelog": "Initial release",
     "description": "Cache media files from slow network storage to local SSD for faster playback",
     "guid": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
     "name": "Local Cache",
     "overview": "Cache media to local SSD for faster playback",
     "owner": "",
     "targetAbi": "10.11.0.0",
     "timestamp": "2026-04-21T00:00:00.0000000Z",
     "version": "1.0.0.0",
     "status": "Active",
     "autoUpdate": false,
     "assemblies": []
   }
   ```

4. Set ownership:
   ```bash
   chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/Local\ Cache_1.0.0.0
   ```

### Deploy the Detail Page Script (Optional)

This adds cache/evict buttons and file size info directly on movie detail pages.

1. Copy the script to the Jellyfin web directory:
   ```bash
   cp Web/localcache-inject.js /usr/share/jellyfin/web/
   ```

2. Add a script tag to the Jellyfin web `index.html` (in the `<head>`, before other scripts):
   ```bash
   sed -i 's|<title>Jellyfin</title>|<title>Jellyfin</title><script src="localcache-inject.js"></script>|' \
     /usr/share/jellyfin/web/index.html
   ```

> **Note**: The `index.html` modification will be overwritten when Jellyfin is updated. You'll need to re-apply the script tag after updates.

### Configure

1. Restart Jellyfin
2. Go to **Dashboard** > **Plugins** > **Local Cache** to configure:
   - **Cache Directory** — local SSD path (e.g. `/var/cache/jellyfin-localcache`)
   - **Max Cache Size (GB)** — disk quota for cached files
   - **Slow Storage Path Prefixes** — path prefixes identifying your network mount (e.g. `/mnt/storagebox`)

3. Create the cache directory and set permissions:
   ```bash
   mkdir -p /var/cache/jellyfin-localcache
   chown jellyfin:jellyfin /var/cache/jellyfin-localcache
   ```

## Usage

### From the Dashboard

Open **Local Cache** from the Jellyfin server menu to:
- Browse all movies/episodes on slow storage
- See file sizes and cache status
- Click **Cache** to start caching an item
- Click **Evict** to remove a cached item
- Monitor disk usage and active copy progress

### From Movie Detail Pages

If the detail page script is installed, each movie page shows:
- A **cache/evict button** alongside the Play button
- **File size**, cache status, and remaining cache space below the movie info

### Playback

Just press Play as normal. If the item is cached, playback automatically uses the local copy. No manual source selection needed.

## Architecture

```
Jellyfin.Plugin.LocalCache/
  Plugin.cs                              # Entry point, dashboard + config pages
  ServiceRegistrator.cs                  # DI registration for CacheManager
  Configuration/
    PluginConfiguration.cs               # Settings model
    configPage.html                      # Plugin settings page
  Models/                                # API response models
  Services/
    ICacheManager.cs                     # Cache manager interface
    CacheManager.cs                      # File copy, manifest, disk management
  Providers/
    LocalCacheMediaSourceProvider.cs     # Playback redirect via IMediaSourceProvider
  Api/
    LocalCacheController.cs              # REST API for cache operations
  Pages/
    cacheDashboard.html                  # Cache management dashboard
  Web/
    localcache-inject.js                 # Detail page button injection
```

### Key Design Decisions

- **`IMediaSourceProvider`** injects a cached media source with a +1px width boost so it sorts ahead of the network source in Jellyfin's `SortMediaSources`. Uses the same source ID as the original so all clients auto-select it.
- **Path substitutions** are added to `ServerConfiguration` for each cached item, so the Jellyfin PlaybackInfo API also reflects the local path.
- **Cache manifest** is persisted as JSON in the plugin data folder, surviving server restarts.
- **Detail page integration** uses a standalone JS file injected into the Jellyfin web app (since the plugin system has no extension point for item detail pages).

## API Endpoints

All endpoints require admin authentication.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/LocalCache/Status` | Cache usage stats and all entries |
| GET | `/LocalCache/Items?startIndex=&limit=&searchTerm=` | Browsable list of cacheable items |
| POST | `/LocalCache/Cache/{itemId}` | Start caching an item |
| DELETE | `/LocalCache/Cache/{itemId}` | Evict a cached item |
| GET | `/LocalCache/Progress/{itemId}` | Copy progress for an item |

## License

MIT
