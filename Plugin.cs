using System;
using System.Collections.Generic;
using Jellyfin.Plugin.LocalCache.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LocalCache;

/// <summary>
/// LocalCache plugin — caches media files to local SSD for faster playback.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths.</param>
    /// <param name="xmlSerializer">XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the shared plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override Guid Id => new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    /// <inheritdoc />
    public override string Name => "Local Cache";

    /// <inheritdoc />
    public override string Description =>
        "Cache media files from slow network storage to local SSD for faster playback.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
        };

        yield return new PluginPageInfo
        {
            Name = "LocalCacheDashboard",
            DisplayName = "Local Cache",
            EmbeddedResourcePath = GetType().Namespace + ".Pages.cacheDashboard.html",
            EnableInMainMenu = true,
            MenuSection = "server",
            MenuIcon = "storage"
        };
    }
}
