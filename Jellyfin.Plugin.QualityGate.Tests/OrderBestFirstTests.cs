using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.QualityGate.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.QualityGate.Tests;

/// <summary>
/// Source ordering on its own. Clients play MediaSources[0], so this decides which version
/// a user gets when they never open the version picker.
/// </summary>
public class OrderBestFirstTests
{
    private static MediaSourceInfo Source(string path, int? height, int? bitrate = null) =>
        new MediaSourceInfo
        {
            Path = path,
            Bitrate = bitrate,
            MediaStreams = height is null
                ? new List<MediaStream>()
                : new List<MediaStream> { new MediaStream { Type = MediaStreamType.Video, Height = height } },
        };

    [Fact]
    public void PutsTheOriginalFirstWhenJellyfinPromotedTheEncode()
    {
        // The real shape: neither filename carries a resolution token, and Jellyfin handed
        // over the 720p encode first.
        var ordered = QualityGateService.OrderBestFirst(new[]
        {
            Source("/media/Series/Show/S01E01 - Titulo - 720p.mkv", 720),
            Source("/media/Series/Show/S01E01 - Titulo.mkv", 1080),
        });

        Assert.Equal(1080, QualityGateService.GetVideoHeight(ordered[0]));
        Assert.Equal(2, ordered.Length);
    }

    [Fact]
    public void LeavesAnAlreadyCorrectOrderAlone()
    {
        var ordered = QualityGateService.OrderBestFirst(new[]
        {
            Source("/a/original.mkv", 2160),
            Source("/a/original - 720p.mkv", 720),
        });

        Assert.Equal(2160, QualityGateService.GetVideoHeight(ordered[0]));
        Assert.Equal(720, QualityGateService.GetVideoHeight(ordered[1]));
    }

    [Fact]
    public void BreaksATieOnBitrate()
    {
        var ordered = QualityGateService.OrderBestFirst(new[]
        {
            Source("/a/low.mkv", 1080, 2_000_000),
            Source("/a/high.mkv", 1080, 9_000_000),
        });

        Assert.Equal("/a/high.mkv", ordered[0].Path);
    }

    [Fact]
    public void AnUnprobedHeightNeverDisplacesAMeasuredOne()
    {
        // A source whose height never probed must not be promoted over a known 1080p.
        var ordered = QualityGateService.OrderBestFirst(new[]
        {
            Source("/a/sin-probar.mkv", null),
            Source("/a/original.mkv", 1080),
        });

        Assert.Equal("/a/original.mkv", ordered[0].Path);
    }

    [Fact]
    public void KeepsRelativeOrderWhenNothingDistinguishesTheSources()
    {
        var ordered = QualityGateService.OrderBestFirst(new[]
        {
            Source("/a/primero.mkv", null),
            Source("/a/segundo.mkv", null),
        });

        Assert.Equal("/a/primero.mkv", ordered[0].Path);
        Assert.Equal("/a/segundo.mkv", ordered[1].Path);
    }

    [Fact]
    public void HandlesNullAndEmpty()
    {
        Assert.Empty(QualityGateService.OrderBestFirst(null));
        Assert.Empty(QualityGateService.OrderBestFirst(Array.Empty<MediaSourceInfo>()));
    }

    [Fact]
    public void DoesNotDropOrDuplicateSources()
    {
        var input = new[]
        {
            Source("/a/1.mkv", 720), Source("/a/2.mkv", 1080),
            Source("/a/3.mkv", null), Source("/a/4.mkv", 2160),
        };

        var ordered = QualityGateService.OrderBestFirst(input);

        Assert.Equal(4, ordered.Length);
        Assert.Equal(input.Select(s => s.Path).OrderBy(p => p), ordered.Select(s => s.Path).OrderBy(p => p));
    }
}

/// <summary>
/// The forced-transcode case: every source is over the cap, so all of them will be scaled
/// down to it. The output is identical whichever source feeds it, so the cheapest wins.
/// </summary>
public class OrderCheapestFirstTests
{
    private static MediaSourceInfo Source(string path, int? height, int? bitrate = null) =>
        new MediaSourceInfo
        {
            Path = path,
            Bitrate = bitrate,
            MediaStreams = height is null
                ? new List<MediaStream>()
                : new List<MediaStream> { new MediaStream { Type = MediaStreamType.Video, Height = height } },
        };

    [Fact]
    public void TranscodesFromTheSmallestSourceNotTheLargest()
    {
        var ordered = QualityGateService.OrderCheapestFirst(new[]
        {
            Source("/a/4k.mkv", 2160),
            Source("/a/1080.mkv", 1080),
        });

        Assert.Equal("/a/1080.mkv", ordered[0].Path);
    }

    [Fact]
    public void BreaksATieOnTheLowerBitrate()
    {
        var ordered = QualityGateService.OrderCheapestFirst(new[]
        {
            Source("/a/alto.mkv", 1080, 9_000_000),
            Source("/a/bajo.mkv", 1080, 2_000_000),
        });

        Assert.Equal("/a/bajo.mkv", ordered[0].Path);
    }

    [Fact]
    public void AnUnprobedHeightIsNotAssumedCheap()
    {
        // An unknown height could be anything, so it must not be picked over a measured one.
        var ordered = QualityGateService.OrderCheapestFirst(new[]
        {
            Source("/a/sin-probar.mkv", null),
            Source("/a/1080.mkv", 1080),
        });

        Assert.Equal("/a/1080.mkv", ordered[0].Path);
    }

    [Fact]
    public void DoesNotDropOrDuplicateSources()
    {
        var input = new[] { Source("/a/1.mkv", 2160), Source("/a/2.mkv", 1080), Source("/a/3.mkv", null) };
        var ordered = QualityGateService.OrderCheapestFirst(input);

        Assert.Equal(3, ordered.Length);
        Assert.Equal(input.Select(s => s.Path).OrderBy(p => p), ordered.Select(s => s.Path).OrderBy(p => p));
    }

    [Fact]
    public void HandlesNullAndEmpty()
    {
        Assert.Empty(QualityGateService.OrderCheapestFirst(null));
        Assert.Empty(QualityGateService.OrderCheapestFirst(Array.Empty<MediaSourceInfo>()));
    }
}
