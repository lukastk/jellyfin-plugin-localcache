using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalCache.Models;

/// <summary>
/// Overall cache status.
/// </summary>
public class CacheStatus
{
    /// <summary>
    /// Gets or sets total bytes used by cached files.
    /// </summary>
    public long UsedCacheSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets maximum cache size in bytes (0 = unlimited).
    /// </summary>
    public long MaxCacheSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the count of fully cached items.
    /// </summary>
    public int CachedItemCount { get; set; }

    /// <summary>
    /// Gets or sets the count of items currently being copied.
    /// </summary>
    public int CopyingItemCount { get; set; }

    /// <summary>
    /// Gets or sets all cache entries.
    /// </summary>
    public List<CacheEntry> Entries { get; set; } = new();
}
