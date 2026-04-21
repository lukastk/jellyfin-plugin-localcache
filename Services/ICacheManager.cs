using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalCache.Models;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.LocalCache.Services;

/// <summary>
/// Manages local file caching.
/// </summary>
public interface ICacheManager
{
    /// <summary>
    /// Returns true if the item is fully cached locally.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <returns>Whether the item is cached.</returns>
    bool IsItemCached(Guid itemId);

    /// <summary>
    /// Gets the local cached file path, or null if not cached.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <returns>The cached file path, or null.</returns>
    string? GetCachedPath(Guid itemId);

    /// <summary>
    /// Gets the cache entry for an item.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <returns>The cache entry, or null.</returns>
    CacheEntry? GetCacheEntry(Guid itemId);

    /// <summary>
    /// Gets overall cache status.
    /// </summary>
    /// <returns>Cache status.</returns>
    CacheStatus GetStatus();

    /// <summary>
    /// Gets copy progress for an item.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <returns>Progress info, or null if not copying.</returns>
    CacheProgressInfo? GetProgress(Guid itemId);

    /// <summary>
    /// Starts caching an item in the background.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cache entry.</returns>
    Task<CacheEntry> CacheItemAsync(Guid itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Evicts a cached item, deleting the local copy.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <returns>Task.</returns>
    Task EvictItemAsync(Guid itemId);

    /// <summary>
    /// Returns true if the item is eligible for caching.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>Whether the item can be cached.</returns>
    bool IsItemCacheable(BaseItem item);

    /// <summary>
    /// Updates the last accessed timestamp for a cached item.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    void UpdateLastAccessed(Guid itemId);
}
