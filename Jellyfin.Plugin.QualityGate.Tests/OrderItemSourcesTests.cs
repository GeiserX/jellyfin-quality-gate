using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.QualityGate.Configuration;
using Jellyfin.Plugin.QualityGate.Filters;
using Jellyfin.Plugin.QualityGate.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.QualityGate.Tests;

/// <summary>
/// The item's own source list fills the version picker. A client that plays whatever the
/// picker shows first must not walk past the order applied to the playback response.
/// </summary>
public class OrderItemSourcesTests
{
    private static MediaSourceInfo Source(string path, int height) =>
        new MediaSourceInfo
        {
            Path = path,
            MediaStreams = new List<MediaStream> { new MediaStream { Type = MediaStreamType.Video, Height = height } },
        };

    private static BaseItemDto Item() =>
        new BaseItemDto
        {
            Name = "Episodio",
            MediaSources = new[] { Source("/a/x - 720p.mkv", 720), Source("/a/x.mkv", 1080) },
        };

    private static int? FirstHeight(BaseItemDto item) =>
        QualityGateService.GetVideoHeight(item.MediaSources![0]);

    [Fact]
    public void OrdersASingleItem()
    {
        var item = Item();
        ResolutionCapFilter.OrderItemSources(item, null);
        Assert.Equal(1080, FirstHeight(item));
    }

    [Fact]
    public void OrdersEveryItemOnAPageOfResults()
    {
        var page = new QueryResult<BaseItemDto>(new[] { Item(), Item() });
        ResolutionCapFilter.OrderItemSources(page, null);
        Assert.All(page.Items, i => Assert.Equal(1080, FirstHeight(i)));
    }

    [Fact]
    public void OrdersAPlainEnumerableOfItems()
    {
        var items = new List<BaseItemDto> { Item() };
        ResolutionCapFilter.OrderItemSources(items, null);
        Assert.Equal(1080, FirstHeight(items[0]));
    }

    [Fact]
    public void LeavesASingleSourceItemAlone()
    {
        var only = Source("/a/x.mkv", 1080);
        var item = new BaseItemDto { MediaSources = new[] { only } };
        ResolutionCapFilter.OrderItemSources(item, null);
        Assert.Same(only, item.MediaSources[0]);
    }

    [Fact]
    public void IgnoresValuesItDoesNotUnderstand()
    {
        // Must not throw on a response shape it was never meant to touch.
        ResolutionCapFilter.OrderItemSources(null, null);
        ResolutionCapFilter.OrderItemSources("no soy un item", null);
        ResolutionCapFilter.OrderItemSources(new BaseItemDto { MediaSources = null }, null);
    }

    [Fact]
    public void DropsNothingFromTheList()
    {
        var item = Item();
        var before = item.MediaSources!.Select(s => s.Path).OrderBy(p => p).ToArray();
        ResolutionCapFilter.OrderItemSources(item, null);
        Assert.Equal(before, item.MediaSources!.Select(s => s.Path).OrderBy(p => p));
    }
}

/// <summary>
/// The picker order under a policy. A restricted user must not be offered, at the top of
/// their list, the one version they are not allowed to play.
/// </summary>
public class OrderForPolicyTests
{
    private static QualityPolicy Cap(int maxHeight) =>
        new QualityPolicy { Id = "p1", Name = "Test", Enabled = true, MaxHeight = maxHeight };

    private static MediaSourceInfo Source(string path, int height) =>
        new MediaSourceInfo
        {
            Path = path,
            MediaStreams = new List<MediaStream> { new MediaStream { Type = MediaStreamType.Video, Height = height } },
        };

    [Fact]
    public void SinksAnOverCapSourceBelowOneTheUserCanPlay()
    {
        var ordered = QualityGateService.OrderForPolicy(Cap(720), new[]
        {
            Source("/a/original.mkv", 1080),
            Source("/a/original - 720p.mkv", 720),
        });

        Assert.Equal(720, QualityGateService.GetVideoHeight(ordered[0]));
        Assert.Equal(1080, QualityGateService.GetVideoHeight(ordered[1]));
    }

    [Fact]
    public void WithoutAPolicyItIsPlainBestFirst()
    {
        var ordered = QualityGateService.OrderForPolicy(null, new[]
        {
            Source("/a/original - 720p.mkv", 720),
            Source("/a/original.mkv", 1080),
        });

        Assert.Equal(1080, QualityGateService.GetVideoHeight(ordered[0]));
    }

    [Fact]
    public void StillPrefersTheTallestAmongSourcesTheUserCanPlay()
    {
        var ordered = QualityGateService.OrderForPolicy(Cap(1080), new[]
        {
            Source("/a/480.mkv", 480),
            Source("/a/1080.mkv", 1080),
            Source("/a/2160.mkv", 2160),
        });

        Assert.Equal(1080, QualityGateService.GetVideoHeight(ordered[0]));
        Assert.Equal(480, QualityGateService.GetVideoHeight(ordered[1]));
        Assert.Equal(2160, QualityGateService.GetVideoHeight(ordered[2]));
    }

    [Fact]
    public void WhenEverySourceIsOverTheCapTheTallestStillLeads()
    {
        // Nothing is playable as-is, so the group rule cannot separate them and the
        // ordinary best-first rule applies.
        var ordered = QualityGateService.OrderForPolicy(Cap(720), new[]
        {
            Source("/a/1080.mkv", 1080),
            Source("/a/2160.mkv", 2160),
        });

        Assert.Equal(2160, QualityGateService.GetVideoHeight(ordered[0]));
    }

    [Fact]
    public void DropsNothing()
    {
        var input = new[] { Source("/a/1.mkv", 2160), Source("/a/2.mkv", 720), Source("/a/3.mkv", 1080) };
        var ordered = QualityGateService.OrderForPolicy(Cap(720), input);

        Assert.Equal(3, ordered.Length);
        Assert.Equal(input.Select(s => s.Path).OrderBy(p => p), ordered.Select(s => s.Path).OrderBy(p => p));
    }
}
