using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LocalCache.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        CacheDirectory = "/var/cache/jellyfin-localcache";
        MaxCacheSizeGB = 50;
        SlowPathPrefixes = ["/mnt/storagebox"];
        MediaSourceNameSuffix = " [Local Cache]";
    }

    /// <summary>
    /// Gets or sets the local SSD directory for cached files.
    /// </summary>
    public string CacheDirectory { get; set; }

    /// <summary>
    /// Gets or sets the maximum cache size in GB. 0 means unlimited.
    /// </summary>
    public int MaxCacheSizeGB { get; set; }

    /// <summary>
    /// Gets or sets path prefixes that identify slow network mounts.
    /// Only items with paths starting with one of these are eligible for caching.
    /// </summary>
    public string[] SlowPathPrefixes { get; set; }

    /// <summary>
    /// Gets or sets the suffix appended to cached media source names.
    /// </summary>
    public string MediaSourceNameSuffix { get; set; }
}
