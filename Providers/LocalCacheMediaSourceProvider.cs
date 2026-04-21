using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalCache.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalCache.Providers;

/// <summary>
/// Provides cached local media sources for playback redirection.
/// </summary>
public class LocalCacheMediaSourceProvider : IMediaSourceProvider
{
    private readonly ICacheManager _cacheManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ILogger<LocalCacheMediaSourceProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalCacheMediaSourceProvider"/> class.
    /// </summary>
    /// <param name="cacheManager">Cache manager.</param>
    /// <param name="mediaSourceManager">Media source manager.</param>
    /// <param name="logger">Logger.</param>
    public LocalCacheMediaSourceProvider(
        ICacheManager cacheManager,
        IMediaSourceManager mediaSourceManager,
        ILogger<LocalCacheMediaSourceProvider> logger)
    {
        _cacheManager = cacheManager;
        _mediaSourceManager = mediaSourceManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IEnumerable<MediaSourceInfo>> GetMediaSources(
        BaseItem item,
        CancellationToken cancellationToken)
    {
        if (item is not Video)
        {
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        var cachedPath = _cacheManager.GetCachedPath(item.Id);
        if (cachedPath is null)
        {
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        var staticSources = _mediaSourceManager.GetStaticMediaSources(item, false);
        if (staticSources.Count == 0)
        {
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        var original = staticSources[0];
        var config = Plugin.Instance?.Configuration;
        var suffix = config?.MediaSourceNameSuffix ?? " [Local Cache]";

        var cachedSource = new MediaSourceInfo
        {
            Id = original.Id,
            Path = cachedPath,
            Protocol = MediaProtocol.File,
            Name = (original.Name ?? "Unknown") + suffix,
            Container = original.Container,
            Size = original.Size,
            RunTimeTicks = original.RunTimeTicks,
            Bitrate = original.Bitrate,
            VideoType = original.VideoType,
            IsoType = original.IsoType,
            Video3DFormat = original.Video3DFormat,
            Timestamp = original.Timestamp,
            MediaStreams = CloneMediaStreamsWithSortBoost(original.MediaStreams),
            MediaAttachments = original.MediaAttachments,
            Formats = original.Formats,
            Type = MediaSourceType.Default,
            SupportsDirectPlay = true,
            SupportsDirectStream = true,
            SupportsTranscoding = true,
            SupportsProbing = true,
            IsRemote = false,
            RequiresOpening = false,
            RequiresClosing = false,
            DefaultAudioStreamIndex = original.DefaultAudioStreamIndex,
            DefaultSubtitleStreamIndex = original.DefaultSubtitleStreamIndex,
        };

        _cacheManager.UpdateLastAccessed(item.Id);

        _logger.LogDebug(
            "Returning cached media source for item {ItemId} at {Path}",
            item.Id,
            cachedPath);

        return Task.FromResult<IEnumerable<MediaSourceInfo>>([cachedSource]);
    }

    /// <summary>
    /// Clones the media streams list with a +1 width boost on the video stream.
    /// This ensures the cached source sorts before the original in SortMediaSources
    /// (which sorts descending by video width), so the player auto-selects it.
    /// </summary>
    private static IReadOnlyList<MediaStream> CloneMediaStreamsWithSortBoost(
        IReadOnlyList<MediaStream> original)
    {
        var cloned = new List<MediaStream>(original.Count);
        foreach (var stream in original)
        {
            if (stream.Type == MediaStreamType.Video && stream.Width.HasValue)
            {
                // Shallow copy with boosted width for sort priority
                cloned.Add(new MediaStream
                {
                    Type = stream.Type,
                    Index = stream.Index,
                    Codec = stream.Codec,
                    CodecTag = stream.CodecTag,
                    Language = stream.Language,
                    Title = stream.Title,
                    Width = stream.Width.Value + 1,
                    Height = stream.Height,
                    BitRate = stream.BitRate,
                    BitDepth = stream.BitDepth,
                    AverageFrameRate = stream.AverageFrameRate,
                    RealFrameRate = stream.RealFrameRate,
                    Profile = stream.Profile,
                    Level = stream.Level,
                    AspectRatio = stream.AspectRatio,
                    PixelFormat = stream.PixelFormat,
                    IsDefault = stream.IsDefault,
                    IsForced = stream.IsForced,
                    IsInterlaced = stream.IsInterlaced,
                    IsAVC = stream.IsAVC,
                    Channels = stream.Channels,
                    SampleRate = stream.SampleRate,
                    IsExternal = stream.IsExternal,
                    NalLengthSize = stream.NalLengthSize,
                    ColorRange = stream.ColorRange,
                    ColorSpace = stream.ColorSpace,
                    ColorTransfer = stream.ColorTransfer,
                    ColorPrimaries = stream.ColorPrimaries,
                });
            }
            else
            {
                cloned.Add(stream);
            }
        }

        return cloned;
    }

    /// <inheritdoc />
    public Task<ILiveStream> OpenMediaSource(
        string openToken,
        List<ILiveStream> currentLiveStreams,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("LocalCache does not support live streams.");
    }
}
