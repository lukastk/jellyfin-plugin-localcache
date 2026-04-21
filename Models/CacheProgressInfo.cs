using System;

namespace Jellyfin.Plugin.LocalCache.Models;

/// <summary>
/// Copy progress for a single item.
/// </summary>
public class CacheProgressInfo
{
    /// <summary>
    /// Gets or sets the item ID.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the current state.
    /// </summary>
    public CacheState State { get; set; }

    /// <summary>
    /// Gets or sets the bytes copied so far.
    /// </summary>
    public long BytesCopied { get; set; }

    /// <summary>
    /// Gets or sets the total file size in bytes.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets the progress percentage (0-100).
    /// </summary>
    public double ProgressPercent { get; set; }
}
