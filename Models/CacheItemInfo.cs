using System;

namespace Jellyfin.Plugin.LocalCache.Models;

/// <summary>
/// Information about a cacheable library item.
/// </summary>
public class CacheItemInfo
{
    /// <summary>
    /// Gets or sets the item ID.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the series name (for episodes).
    /// </summary>
    public string? SeriesName { get; set; }

    /// <summary>
    /// Gets or sets the season name (for episodes).
    /// </summary>
    public string? SeasonName { get; set; }

    /// <summary>
    /// Gets or sets the episode number (for episodes).
    /// </summary>
    public int? IndexNumber { get; set; }

    /// <summary>
    /// Gets or sets the file path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long? FileSize { get; set; }

    /// <summary>
    /// Gets or sets the container format.
    /// </summary>
    public string? Container { get; set; }

    /// <summary>
    /// Gets or sets the runtime in ticks.
    /// </summary>
    public long? RunTimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the cache state, or null if not cached.
    /// </summary>
    public CacheState? CacheState { get; set; }

    /// <summary>
    /// Gets or sets the copy progress percentage (0-100).
    /// </summary>
    public double? CopyProgress { get; set; }
}
