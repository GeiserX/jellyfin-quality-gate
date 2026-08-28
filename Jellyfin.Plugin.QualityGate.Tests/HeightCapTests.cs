using System;
using System.Collections.Generic;
using Jellyfin.Plugin.QualityGate.Configuration;
using Jellyfin.Plugin.QualityGate.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.QualityGate.Tests;

/// <summary>
/// The height rule on its own, with no HTTP pipeline in the way.
/// </summary>
public class HeightCapTests
{
    private static QualityPolicy Policy(int maxHeight) =>
        new QualityPolicy { Id = "p1", Name = "Test", Enabled = true, MaxHeight = maxHeight };

    [Fact]
    public void HasHeightCap_IsFalseForZeroNegativeAndNull()
    {
        Assert.False(QualityGateService.HasHeightCap(null));
        Assert.False(QualityGateService.HasHeightCap(Policy(0)));
        Assert.False(QualityGateService.HasHeightCap(Policy(-1)));
    }

    [Fact]
    public void HasHeightCap_IsTrueForAPositiveHeight()
    {
        Assert.True(QualityGateService.HasHeightCap(Policy(720)));
    }

    [Theory]
    [InlineData(720, 1080, true)]
    [InlineData(720, 2160, true)]
    [InlineData(720, 721, true)]
    [InlineData(720, 720, false)]
    [InlineData(720, 480, false)]
    [InlineData(1080, 1080, false)]
    public void ExceedsHeightCap_ComparesAgainstTheCap(int cap, int height, bool expected)
    {
        Assert.Equal(expected, QualityGateService.ExceedsHeightCap(Policy(cap), height));
    }

    [Fact]
    public void ExceedsHeightCap_IsFalseWhenThePolicyDoesNotCap()
    {
        Assert.False(QualityGateService.ExceedsHeightCap(Policy(0), 2160));
        Assert.False(QualityGateService.ExceedsHeightCap(null, 2160));
    }

    /// <summary>
    /// An unknown height is not a violation. See ExceedsHeightCap's remarks: a null comes from
    /// the item never having been probed, not from oversized media.
    /// </summary>
    [Fact]
    public void ExceedsHeightCap_IsFalseForAnUnknownHeight()
    {
        Assert.False(QualityGateService.ExceedsHeightCap(Policy(720), (int?)null));
    }

    [Fact]
    public void GetVideoHeight_IgnoresNonVideoStreams()
    {
        var streams = new List<MediaStream>
        {
            new MediaStream { Type = MediaStreamType.Audio, Height = 9999 },
            new MediaStream { Type = MediaStreamType.Subtitle, Height = 8888 },
            new MediaStream { Type = MediaStreamType.Video, Height = 1080 },
        };

        Assert.Equal(1080, QualityGateService.GetVideoHeight(streams));
    }

    [Fact]
    public void GetVideoHeight_TakesTheTallestVideoStream()
    {
        var streams = new List<MediaStream>
        {
            new MediaStream { Type = MediaStreamType.Video, Height = 480 },
            new MediaStream { Type = MediaStreamType.Video, Height = 2160 },
            new MediaStream { Type = MediaStreamType.Video, Height = 720 },
        };

        Assert.Equal(2160, QualityGateService.GetVideoHeight(streams));
    }

    [Fact]
    public void GetVideoHeight_IsNullWhenNoVideoStreamReportsAHeight()
    {
        Assert.Null(QualityGateService.GetVideoHeight((IEnumerable<MediaStream>?)null));
        Assert.Null(QualityGateService.GetVideoHeight(new List<MediaStream>()));
        Assert.Null(QualityGateService.GetVideoHeight(new List<MediaStream>
        {
            new MediaStream { Type = MediaStreamType.Video, Height = null },
            new MediaStream { Type = MediaStreamType.Audio, Height = 1080 },
        }));
    }

    [Fact]
    public void GetVideoHeight_ReadsThroughAMediaSource()
    {
        var source = new MediaSourceInfo
        {
            MediaStreams = new[] { new MediaStream { Type = MediaStreamType.Video, Height = 1080 } },
        };

        Assert.Equal(1080, QualityGateService.GetVideoHeight(source));
        Assert.Null(QualityGateService.GetVideoHeight((MediaSourceInfo?)null));
        Assert.True(QualityGateService.ExceedsHeightCap(Policy(720), source));
    }

    /// <summary>
    /// A cinematic aspect ratio is wider than 16:9 at the same height, so a width-derived cap
    /// would wrongly reject media that is within the height cap. The rule is height only.
    /// </summary>
    [Fact]
    public void ExceedsHeightCap_AllowsAWideAspectRatioAtTheCapHeight()
    {
        var scope = new MediaSourceInfo
        {
            MediaStreams = new[]
            {
                new MediaStream { Type = MediaStreamType.Video, Height = 720, Width = 1720 },
            },
        };

        Assert.False(QualityGateService.ExceedsHeightCap(Policy(720), scope));
    }

    /// <summary>
    /// The height rule is independent of the filename, which is the whole point: the live Lite
    /// tree names each symlink after the original, so "- 720p" appears in no stored path.
    /// </summary>
    [Fact]
    public void ExceedsHeightCap_DoesNotLookAtThePath()
    {
        var mislabelled = new MediaSourceInfo
        {
            Path = "/media/Lite/Movie (2021) - 720p.mkv",
            MediaStreams = new[] { new MediaStream { Type = MediaStreamType.Video, Height = 1080 } },
        };

        Assert.True(QualityGateService.ExceedsHeightCap(Policy(720), mislabelled));
    }
}
