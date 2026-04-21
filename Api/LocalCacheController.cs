using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LocalCache.Models;
using Jellyfin.Plugin.LocalCache.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalCache.Api;

/// <summary>
/// API controller for local cache management.
/// </summary>
[ApiController]
[Route("LocalCache")]
[Authorize(Policy = Policies.RequiresElevation)]
public class LocalCacheController : ControllerBase
{
    private readonly ICacheManager _cacheManager;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalCacheController"/> class.
    /// </summary>
    /// <param name="cacheManager">Cache manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    public LocalCacheController(ICacheManager cacheManager, ILibraryManager libraryManager)
    {
        _cacheManager = cacheManager;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets overall cache status.
    /// </summary>
    /// <returns>Cache status.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<CacheStatus> GetStatus()
    {
        return _cacheManager.GetStatus();
    }

    /// <summary>
    /// Gets cacheable items from the library.
    /// </summary>
    /// <param name="startIndex">Start index for pagination.</param>
    /// <param name="limit">Maximum items to return.</param>
    /// <param name="searchTerm">Optional search term.</param>
    /// <returns>List of cacheable items.</returns>
    [HttpGet("Items")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<CacheItemInfo>> GetCacheableItems(
        [FromQuery] int startIndex = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? searchTerm = null)
    {
        var config = Plugin.Instance?.Configuration;
        var prefixes = config?.SlowPathPrefixes ?? [];

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
            Recursive = true,
            IsVirtualItem = false,
        };

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query.SearchTerm = searchTerm;
        }

        var items = _libraryManager.GetItemList(query);

        var result = items
            .Where(item => !string.IsNullOrEmpty(item.Path)
                && prefixes.Any(p => item.Path.StartsWith(p, StringComparison.Ordinal)))
            .OrderBy(item => item.SortName)
            .Skip(startIndex)
            .Take(limit)
            .Select(item =>
            {
                var entry = _cacheManager.GetCacheEntry(item.Id);
                var progress = _cacheManager.GetProgress(item.Id);
                var episode = item as Episode;

                return new CacheItemInfo
                {
                    ItemId = item.Id,
                    Name = item.Name,
                    SeriesName = episode?.SeriesName,
                    SeasonName = episode?.SeasonName,
                    IndexNumber = episode?.IndexNumber,
                    Path = item.Path ?? string.Empty,
                    FileSize = item.Size,
                    Container = item.Container,
                    RunTimeTicks = item.RunTimeTicks,
                    CacheState = entry?.State,
                    CopyProgress = progress?.ProgressPercent,
                };
            })
            .ToList();

        return result;
    }

    /// <summary>
    /// Starts caching an item.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <returns>The cache entry.</returns>
    [HttpPost("Cache/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CacheEntry>> CacheItem([FromRoute] Guid itemId)
    {
        try
        {
            var entry = await _cacheManager.CacheItemAsync(itemId, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            return entry;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Evicts a cached item.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <returns>No content.</returns>
    [HttpDelete("Cache/{itemId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> EvictItem([FromRoute] Guid itemId)
    {
        await _cacheManager.EvictItemAsync(itemId).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Gets copy progress for an item.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <returns>Progress info.</returns>
    [HttpGet("Progress/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<CacheProgressInfo> GetProgress([FromRoute] Guid itemId)
    {
        var progress = _cacheManager.GetProgress(itemId);
        if (progress is null)
        {
            return NotFound();
        }

        return progress;
    }
}
