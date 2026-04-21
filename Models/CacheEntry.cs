using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.LocalCache.Models;

/// <summary>
/// Represents a cached media item.
/// </summary>
public class CacheEntry
{
    /// <summary>
    /// Gets or sets the Jellyfin item ID.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the original file path on the network mount.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the local cached file path.
    /// </summary>
    public string CachedPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Gets or sets when the cache completed.
    /// </summary>
    public DateTime? CachedAt { get; set; }

    /// <summary>
    /// Gets or sets the last time the cached item was accessed for playback.
    /// </summary>
    public DateTime? LastAccessedAt { get; set; }

    /// <summary>
    /// Gets or sets the cache state.
    /// </summary>
    public CacheState State { get; set; }

    /// <summary>
    /// Gets or sets an error message if caching failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Cache entry states.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CacheState
{
    /// <summary>Queued for caching.</summary>
    Queued,

    /// <summary>File copy in progress.</summary>
    Copying,

    /// <summary>Fully cached and available.</summary>
    Cached,

    /// <summary>Cache operation failed.</summary>
    Failed,
}
