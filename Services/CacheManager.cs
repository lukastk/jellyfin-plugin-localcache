using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalCache.Models;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalCache.Services;

/// <summary>
/// Manages local file caching — copy, evict, track, persist.
/// </summary>
public class CacheManager : ICacheManager, IDisposable
{
    private const int BufferSize = 4 * 1024 * 1024; // 4 MB

    private readonly ILibraryManager _libraryManager;
    private readonly IServerConfigurationManager _configManager;
    private readonly ILogger<CacheManager> _logger;
    private readonly ConcurrentDictionary<Guid, CacheEntry> _entries = new();
    private readonly ConcurrentDictionary<Guid, CopyOperation> _activeOperations = new();
    private readonly SemaphoreSlim _persistLock = new(1, 1);
    private readonly string _manifestPath;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheManager"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="configManager">Server configuration manager.</param>
    /// <param name="logger">Logger.</param>
    public CacheManager(
        ILibraryManager libraryManager,
        IServerConfigurationManager configManager,
        ILogger<CacheManager> logger)
    {
        _libraryManager = libraryManager;
        _configManager = configManager;
        _logger = logger;

        var dataFolder = Plugin.Instance?.DataFolderPath
            ?? throw new InvalidOperationException("Plugin instance not available");

        Directory.CreateDirectory(dataFolder);
        _manifestPath = Path.Combine(dataFolder, "cache-manifest.json");

        LoadManifest();
        RecoverFromRestart();
        RestorePathSubstitutions();
    }

    /// <inheritdoc />
    public bool IsItemCached(Guid itemId)
    {
        return _entries.TryGetValue(itemId, out var entry) && entry.State == CacheState.Cached;
    }

    /// <inheritdoc />
    public string? GetCachedPath(Guid itemId)
    {
        if (!_entries.TryGetValue(itemId, out var entry) || entry.State != CacheState.Cached)
        {
            return null;
        }

        if (!File.Exists(entry.CachedPath))
        {
            _logger.LogWarning("Cached file missing for item {ItemId}: {Path}", itemId, entry.CachedPath);
            _entries.TryRemove(itemId, out _);
            _ = SaveManifestAsync();
            return null;
        }

        return entry.CachedPath;
    }

    /// <inheritdoc />
    public CacheEntry? GetCacheEntry(Guid itemId)
    {
        return _entries.TryGetValue(itemId, out var entry) ? entry : null;
    }

    /// <inheritdoc />
    public CacheStatus GetStatus()
    {
        var config = Plugin.Instance?.Configuration;
        var entries = _entries.Values.ToList();

        return new CacheStatus
        {
            UsedCacheSizeBytes = entries
                .Where(e => e.State == CacheState.Cached)
                .Sum(e => e.FileSize),
            MaxCacheSizeBytes = (config?.MaxCacheSizeGB ?? 0) * 1024L * 1024L * 1024L,
            CachedItemCount = entries.Count(e => e.State == CacheState.Cached),
            CopyingItemCount = entries.Count(e => e.State == CacheState.Copying),
            Entries = entries,
        };
    }

    /// <inheritdoc />
    public CacheProgressInfo? GetProgress(Guid itemId)
    {
        if (!_activeOperations.TryGetValue(itemId, out var op))
        {
            if (_entries.TryGetValue(itemId, out var entry))
            {
                return new CacheProgressInfo
                {
                    ItemId = itemId,
                    State = entry.State,
                    BytesCopied = entry.State == CacheState.Cached ? entry.FileSize : 0,
                    TotalBytes = entry.FileSize,
                    ProgressPercent = entry.State == CacheState.Cached ? 100 : 0,
                };
            }

            return null;
        }

        var total = op.TotalBytes;
        var copied = op.BytesCopied;
        return new CacheProgressInfo
        {
            ItemId = itemId,
            State = CacheState.Copying,
            BytesCopied = copied,
            TotalBytes = total,
            ProgressPercent = total > 0 ? Math.Round(100.0 * copied / total, 1) : 0,
        };
    }

    /// <inheritdoc />
    public async Task<CacheEntry> CacheItemAsync(Guid itemId, CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue(itemId, out var existing))
        {
            if (existing.State == CacheState.Cached || existing.State == CacheState.Copying)
            {
                throw new InvalidOperationException(
                    $"Item {itemId} is already {existing.State}.");
            }
        }

        var item = _libraryManager.GetItemById(itemId)
            ?? throw new InvalidOperationException($"Item {itemId} not found.");

        if (item is not Video)
        {
            throw new InvalidOperationException("Only video items can be cached.");
        }

        if (!IsItemCacheable(item))
        {
            throw new InvalidOperationException(
                "Item is not on a slow storage path.");
        }

        var sourcePath = item.Path;
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            throw new InvalidOperationException(
                $"Source file not found: {sourcePath}");
        }

        var fileInfo = new FileInfo(sourcePath);
        var fileSize = fileInfo.Length;

        // Check quota
        var config = Plugin.Instance?.Configuration;
        if (config is not null && config.MaxCacheSizeGB > 0)
        {
            var maxBytes = config.MaxCacheSizeGB * 1024L * 1024L * 1024L;
            var currentUsed = _entries.Values
                .Where(e => e.State == CacheState.Cached)
                .Sum(e => e.FileSize);

            if (currentUsed + fileSize > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Not enough cache space. Need {FormatBytes(fileSize)}, " +
                    $"only {FormatBytes(maxBytes - currentUsed)} available of " +
                    $"{FormatBytes(maxBytes)} total.");
            }
        }

        var cacheDir = config?.CacheDirectory ?? "/var/cache/jellyfin-localcache";
        Directory.CreateDirectory(cacheDir);

        var extension = Path.GetExtension(sourcePath);
        var destPath = Path.Combine(cacheDir, itemId.ToString("N", CultureInfo.InvariantCulture) + extension);

        var entry = new CacheEntry
        {
            ItemId = itemId,
            SourcePath = sourcePath,
            CachedPath = destPath,
            FileSize = fileSize,
            State = CacheState.Copying,
        };

        _entries[itemId] = entry;
        await SaveManifestAsync().ConfigureAwait(false);

        // Start background copy
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var op = new CopyOperation(cts, fileSize);
        _activeOperations[itemId] = op;

        _ = Task.Run(
            () => CopyFileAsync(itemId, sourcePath, destPath, op, cts.Token),
            CancellationToken.None);

        return entry;
    }

    /// <inheritdoc />
    public async Task EvictItemAsync(Guid itemId)
    {
        // Cancel active copy if in progress
        if (_activeOperations.TryRemove(itemId, out var op))
        {
            await op.CancelAsync().ConfigureAwait(false);
        }

        if (_entries.TryRemove(itemId, out var entry))
        {
            RemovePathSubstitution(entry.SourcePath);
            TryDeleteFile(entry.CachedPath);
            _ = SetCacheTagAsync(itemId, add: false);
            _logger.LogInformation("Evicted cached item {ItemId}", itemId);
        }

        await SaveManifestAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool IsItemCacheable(BaseItem item)
    {
        if (item is not Video || string.IsNullOrEmpty(item.Path))
        {
            return false;
        }

        var config = Plugin.Instance?.Configuration;
        var prefixes = config?.SlowPathPrefixes ?? [];

        return prefixes.Any(p => item.Path.StartsWith(p, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public void UpdateLastAccessed(Guid itemId)
    {
        if (_entries.TryGetValue(itemId, out var entry) && entry.State == CacheState.Cached)
        {
            entry.LastAccessedAt = DateTime.UtcNow;
            // Fire-and-forget save
            _ = SaveManifestAsync();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    /// <param name="disposing">Whether to dispose managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            foreach (var op in _activeOperations.Values)
            {
                op.Cts.Cancel();
                op.Cts.Dispose();
            }

            _activeOperations.Clear();
            _persistLock.Dispose();
        }

        _disposed = true;
    }

    private async Task CopyFileAsync(
        Guid itemId,
        string sourcePath,
        string destPath,
        CopyOperation op,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting cache copy for {ItemId}: {Source} -> {Dest}",
                itemId, sourcePath, destPath);

            var buffer = new byte[BufferSize];

            await using var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: BufferSize,
                useAsync: true);

            await using var destStream = new FileStream(
                destPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: BufferSize,
                useAsync: true);

            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(
                buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destStream.WriteAsync(
                    buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                op.BytesCopied += bytesRead;
            }

            // Mark as cached, add path substitution, and tag the item
            if (_entries.TryGetValue(itemId, out var entry))
            {
                entry.State = CacheState.Cached;
                entry.CachedAt = DateTime.UtcNow;
                AddPathSubstitution(entry.SourcePath, entry.CachedPath);
                _ = SetCacheTagAsync(itemId, add: true);
            }

            _logger.LogInformation(
                "Cache copy completed for {ItemId}: {Bytes}",
                itemId, FormatBytes(op.TotalBytes));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Cache copy cancelled for {ItemId}", itemId);
            TryDeleteFile(destPath);

            if (_entries.TryGetValue(itemId, out var entry))
            {
                entry.State = CacheState.Failed;
                entry.ErrorMessage = "Cancelled";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache copy failed for {ItemId}", itemId);
            TryDeleteFile(destPath);

            if (_entries.TryGetValue(itemId, out var entry))
            {
                entry.State = CacheState.Failed;
                entry.ErrorMessage = ex.Message;
            }
        }
        finally
        {
            _activeOperations.TryRemove(itemId, out _);
            await SaveManifestAsync().ConfigureAwait(false);
        }
    }

    private void LoadManifest()
    {
        if (!File.Exists(_manifestPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_manifestPath);
            var entries = JsonSerializer.Deserialize<List<CacheEntry>>(json);
            if (entries is not null)
            {
                foreach (var entry in entries)
                {
                    _entries[entry.ItemId] = entry;
                }
            }

            _logger.LogInformation("Loaded {Count} cache entries from manifest", _entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cache manifest");
        }
    }

    private void RecoverFromRestart()
    {
        var changed = false;
        foreach (var entry in _entries.Values)
        {
            // Reset incomplete copies
            if (entry.State == CacheState.Copying)
            {
                entry.State = CacheState.Failed;
                entry.ErrorMessage = "Server restarted during copy";
                TryDeleteFile(entry.CachedPath);
                changed = true;
            }

            // Verify cached files still exist
            if (entry.State == CacheState.Cached && !File.Exists(entry.CachedPath))
            {
                _logger.LogWarning(
                    "Cached file missing for {ItemId}, removing entry", entry.ItemId);
                _entries.TryRemove(entry.ItemId, out _);
                changed = true;
            }
        }

        if (changed)
        {
            _ = SaveManifestAsync();
        }
    }

    private async Task SaveManifestAsync()
    {
        await _persistLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var entries = _entries.Values.ToList();
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            await File.WriteAllTextAsync(_manifestPath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cache manifest");
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private const string CacheTag = "Cached Locally";

    private async Task SetCacheTagAsync(Guid itemId, bool add)
    {
        try
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item is null)
            {
                return;
            }

            var tags = new List<string>(item.Tags);
            if (add && !tags.Contains(CacheTag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(CacheTag);
                item.Tags = tags.ToArray();
                await _libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
                _logger.LogInformation("Added '{Tag}' tag to {ItemId}", CacheTag, itemId);
            }
            else if (!add && tags.Contains(CacheTag, StringComparer.OrdinalIgnoreCase))
            {
                tags.RemoveAll(t => string.Equals(t, CacheTag, StringComparison.OrdinalIgnoreCase));
                item.Tags = tags.ToArray();
                await _libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
                _logger.LogInformation("Removed '{Tag}' tag from {ItemId}", CacheTag, itemId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update cache tag for {ItemId}", itemId);
        }
    }

    private void AddPathSubstitution(string fromPath, string toPath)
    {
        var config = _configManager.Configuration;
        var subs = config.PathSubstitutions.ToList();

        // Remove any existing substitution for this path
        subs.RemoveAll(s => string.Equals(s.From, fromPath, StringComparison.Ordinal));

        // Insert at the beginning so it matches before broader prefix rules
        subs.Insert(0, new PathSubstitution { From = fromPath, To = toPath });

        config.PathSubstitutions = subs.ToArray();
        _configManager.SaveConfiguration();
        _logger.LogInformation("Added path substitution: {From} -> {To}", fromPath, toPath);
    }

    private void RemovePathSubstitution(string fromPath)
    {
        var config = _configManager.Configuration;
        var subs = config.PathSubstitutions.ToList();
        var removed = subs.RemoveAll(s => string.Equals(s.From, fromPath, StringComparison.Ordinal));

        if (removed > 0)
        {
            config.PathSubstitutions = subs.ToArray();
            _configManager.SaveConfiguration();
            _logger.LogInformation("Removed path substitution for {From}", fromPath);
        }
    }

    private void RestorePathSubstitutions()
    {
        // Ensure all cached entries have their path substitutions and tags
        foreach (var entry in _entries.Values)
        {
            if (entry.State == CacheState.Cached && File.Exists(entry.CachedPath))
            {
                AddPathSubstitution(entry.SourcePath, entry.CachedPath);
                _ = SetCacheTagAsync(entry.ItemId, add: true);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort
        }
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024L * 1024L * 1024L => $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB",
            >= 1024L * 1024L => $"{bytes / (1024.0 * 1024.0):F1} MB",
            _ => $"{bytes / 1024.0:F1} KB",
        };
    }

    private sealed class CopyOperation
    {
        public CopyOperation(CancellationTokenSource cts, long totalBytes)
        {
            Cts = cts;
            TotalBytes = totalBytes;
        }

        public CancellationTokenSource Cts { get; }

        public long TotalBytes { get; }

        public long BytesCopied { get; set; }

        public async Task CancelAsync()
        {
            await Cts.CancelAsync().ConfigureAwait(false);
            Cts.Dispose();
        }
    }
}
