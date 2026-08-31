using Jellyfin.Plugin.QualityGate.Library;

namespace Jellyfin.Plugin.QualityGate.Tests;

public class VersionPairingTests
{
    private static readonly List<string> Suffixes = new() { " - 720p" };

    [Fact]
    public void PlainFile_HasNoBaseStem()
    {
        Assert.Null(VersionPairing.GetBaseStem("/media/Peliculas/Alien (1979).mkv", Suffixes));
    }

    [Fact]
    public void EncodedCopy_ReturnsTheOriginalStem()
    {
        Assert.Equal("Alien (1979)", VersionPairing.GetBaseStem("/media/Peliculas/Alien (1979) - 720p.mkv", Suffixes));
    }

    [Fact]
    public void SuffixIsCaseSensitive()
    {
        Assert.Null(VersionPairing.GetBaseStem("/media/Peliculas/Alien (1979) - 720P.mkv", Suffixes));
    }

    [Fact]
    public void NameThatIsOnlyTheSuffix_IsNotACopy()
    {
        Assert.Null(VersionPairing.GetBaseStem("/media/Peliculas/ - 720p.mkv", Suffixes));
    }

    [Fact]
    public void NullAndEmptyInputs_AreSafe()
    {
        Assert.Null(VersionPairing.GetBaseStem(null, Suffixes));
        Assert.Null(VersionPairing.GetBaseStem(string.Empty, Suffixes));
        Assert.Null(VersionPairing.GetBaseStem("/media/x - 720p.mkv", null));
        Assert.Empty(VersionPairing.PairVersions(null, Suffixes));
        Assert.Empty(VersionPairing.PairVersions(new List<string?> { "/a.mkv" }, Suffixes));
        Assert.Empty(VersionPairing.PairVersions(new List<string?> { "/a.mkv", "/a - 720p.mkv" }, null));
    }

    [Fact]
    public void SiblingCopy_PairsWithItsOriginal()
    {
        var paths = new List<string?>
        {
            "/media/Peliculas/Alien (1979).mkv",
            "/media/Peliculas/Alien (1979) - 720p.mkv"
        };

        var pairs = VersionPairing.PairVersions(paths, Suffixes);

        Assert.Single(pairs);
        Assert.Equal(0, pairs[1]);
    }

    [Fact]
    public void ContainersNeedNotMatch()
    {
        var paths = new List<string?>
        {
            "/media/Peliculas/Alien (1979).mkv",
            "/media/Peliculas/Alien (1979) - 720p.mp4"
        };

        Assert.Equal(0, VersionPairing.PairVersions(paths, Suffixes)[1]);
    }

    [Fact]
    public void OriginalIsTheFileWithoutTheSuffix_WhateverTheOrder()
    {
        var paths = new List<string?>
        {
            "/media/Peliculas/Alien (1979) - 720p.mkv",
            "/media/Peliculas/Alien (1979).mkv"
        };

        var pairs = VersionPairing.PairVersions(paths, Suffixes);

        Assert.Single(pairs);
        Assert.Equal(1, pairs[0]);
    }

    [Fact]
    public void LanguageOrEditionSibling_DoesNotPair()
    {
        // Jellyfin's own rule merges anything starting with the folder name, which swallows this.
        var paths = new List<string?>
        {
            "/media/Peliculas/Alien (1979).mkv",
            "/media/Peliculas/Alien (1979) - German.mkv",
            "/media/Peliculas/Alien (1979) - Director's Cut.mkv"
        };

        Assert.Empty(VersionPairing.PairVersions(paths, Suffixes));
    }

    [Fact]
    public void CopyWithNoOriginal_StaysOnItsOwn()
    {
        var paths = new List<string?>
        {
            "/media/Peliculas/Alien (1979) - 720p.mkv",
            "/media/Peliculas/Blade Runner (1982).mkv"
        };

        Assert.Empty(VersionPairing.PairVersions(paths, Suffixes));
    }

    [Fact]
    public void AmbiguousOriginal_PairsNothing()
    {
        // Two files could equally be the original, so choosing one would be a coin toss.
        var paths = new List<string?>
        {
            "/media/Peliculas/Alien (1979).mkv",
            "/media/Peliculas/Alien (1979).avi",
            "/media/Peliculas/Alien (1979) - 720p.mkv"
        };

        Assert.Empty(VersionPairing.PairVersions(paths, Suffixes));
    }

    [Fact]
    public void TwoCopiesOfOneOriginal_BothPair()
    {
        var paths = new List<string?>
        {
            "/media/Peliculas/Alien (1979).mkv",
            "/media/Peliculas/Alien (1979) - 720p.mkv",
            "/media/Peliculas/Alien (1979) - 720p.mp4"
        };

        var pairs = VersionPairing.PairVersions(paths, Suffixes);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(0, pairs[1]);
        Assert.Equal(0, pairs[2]);
    }

    [Fact]
    public void UnrelatedFilmsInTheSameFolder_AreUntouched()
    {
        var paths = new List<string?>
        {
            "/media/Peliculas/Alien (1979).mkv",
            "/media/Peliculas/Alien (1979) - 720p.mkv",
            "/media/Peliculas/Aliens (1986).mkv",
            "/media/Peliculas/Alien 3 (1992).mkv"
        };

        var pairs = VersionPairing.PairVersions(paths, Suffixes);

        Assert.Single(pairs);
        Assert.Equal(0, pairs[1]);
    }

    [Fact]
    public void TrailingSpaceInTheOriginal_DoesNotPair()
    {
        // Real trees contain names with a trailing space. Matching them loosely would risk
        // merging two genuinely different films, so a mismatch simply leaves both alone.
        var paths = new List<string?>
        {
            "/media/Peliculas/Alien (1979) .mkv",
            "/media/Peliculas/Alien (1979) - 720p.mkv"
        };

        Assert.Empty(VersionPairing.PairVersions(paths, Suffixes));
    }

    [Fact]
    public void ExtrasAreExcludedByTheCaller_ModelledAsNullPaths()
    {
        var paths = new List<string?>
        {
            "/media/Peliculas/Alien (1979).mkv",
            null,
            "/media/Peliculas/Alien (1979) - 720p.mkv"
        };

        var pairs = VersionPairing.PairVersions(paths, Suffixes);

        Assert.Single(pairs);
        Assert.Equal(0, pairs[2]);
    }

    [Fact]
    public void MultipleConfiguredSuffixes_AllPair()
    {
        var suffixes = new List<string> { " - 720p", " - SD" };
        var paths = new List<string?>
        {
            "/media/Peliculas/Alien (1979).mkv",
            "/media/Peliculas/Alien (1979) - 720p.mkv",
            "/media/Peliculas/Alien (1979) - SD.mkv"
        };

        Assert.Equal(2, VersionPairing.PairVersions(paths, suffixes).Count);
    }
}
