using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emby.Naming.Common;
using Emby.Naming.Video;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.QualityGate.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.QualityGate.Library;

/// <summary>
/// Groups an encoded copy with the original it was made from, wherever the two sit side by side.
/// </summary>
/// <remarks>
/// Jellyfin only merges alternate versions inside a folder named after the film, because its own
/// rule keys grouping to the containing folder's name and demands every file start with it
/// (<c>VideoListResolver.GetVideosGroupedByVersion</c>). A library whose films sit loose at the
/// root therefore cannot carry a second version as a sibling: the copy resolves as a separate
/// film. Merging the two through <c>POST /Videos/MergeVersions</c> does not survive, because that
/// writes a linked alternate while the next scan re-resolves the file as standalone again.
/// Grouping only lasts when it is re-made at resolve time, which is what this does.
///
/// The claim is deliberately conservative. The folder is left entirely to the default resolvers
/// unless there is at least one real pair to merge, and Jellyfin's own naming, stacking and extra
/// detection still do all the work — this only folds the pairs together afterwards.
/// </remarks>
public class VersionGroupingResolver : IItemResolver, IMultiItemResolver
{
    private readonly NamingOptions _namingOptions;
    private readonly VideoListResolver _videoListResolver;
    private readonly ILogger<VersionGroupingResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionGroupingResolver"/> class.
    /// </summary>
    /// <param name="namingOptions">Jellyfin's naming options.</param>
    /// <param name="videoListResolver">Jellyfin's own video list resolver.</param>
    /// <param name="logger">Logger.</param>
    public VersionGroupingResolver(
        NamingOptions namingOptions,
        VideoListResolver videoListResolver,
        ILogger<VersionGroupingResolver> logger)
    {
        // Nothing here may throw: the host fails the whole plugin if a part cannot be constructed.
        _namingOptions = namingOptions;
        _videoListResolver = videoListResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public ResolverPriority Priority => ResolverPriority.Plugin;

    /// <inheritdoc />
    /// <remarks>Single files are never claimed; grouping is only meaningful across a listing.</remarks>
    public BaseItem? ResolvePath(ItemResolveArgs args) => null;

    /// <inheritdoc />
    /// <remarks>
    /// Returns null to decline the folder. The interface is annotated non-nullable, but
    /// LibraryManager.ResolvePaths tests the result with <c>result?.Items.Count > 0</c> and
    /// Jellyfin's own MovieResolver declines the same way, so declining is the supported contract.
    /// </remarks>
    public MultiItemResolverResult ResolveMultiple(
        Folder parent,
        List<FileSystemMetadata> files,
        CollectionType? collectionType,
        IDirectoryService directoryService)
    {
        try
        {
            return Group(parent, files, collectionType)!;
        }
        catch (Exception ex)
        {
            // A throw here would break the library scan itself, so failure means "not mine".
            _logger.LogError(ex, "Version grouping failed for {Path}; leaving it to the default resolvers", parent?.Path);
            return null!;
        }
    }

    private static bool IsUnderConfiguredRoot(string? path, IReadOnlyList<string>? roots)
    {
        // No configured root means every movie library, which is what a global toggle should do.
        if (roots is null || roots.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        for (var i = 0; i < roots.Count; i++)
        {
            var root = roots[i];
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (path.Equals(trimmed, StringComparison.Ordinal)
                || path.StartsWith(trimmed + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Files whose real type cannot be told from the name alone — disc images, stubs, shortcuts.
    /// Jellyfin inspects these to set VideoType and IsoType, so the folder is handed straight back.
    /// </summary>
    private static bool IsUnsupported(string name)
    {
        var extension = Path.GetExtension(name.AsSpan());
        return extension.Equals(".iso", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".img", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".strm", StringComparison.OrdinalIgnoreCase);
    }

    private MultiItemResolverResult? Group(
        Folder parent,
        List<FileSystemMetadata> files,
        CollectionType? collectionType)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null
            || !config.EnableVersionGrouping
            || collectionType != CollectionType.movies
            || parent is null
            || files is null
            || files.Count < 2
            || !IsUnderConfiguredRoot(parent.Path, config.VersionGroupingRoots))
        {
            return null;
        }

        var suffixes = config.VersionGroupingSuffixes;
        if (suffixes is null || suffixes.Count == 0)
        {
            return null;
        }

        var candidates = new List<FileSystemMetadata>();
        var leftOver = new List<FileSystemMetadata>();
        foreach (var child in files)
        {
            if (child.IsDirectory)
            {
                // Directories are recursed into separately, exactly as MovieResolver leaves them.
                leftOver.Add(child);
            }
            else if (IsUnsupported(child.Name))
            {
                return null;
            }
            else
            {
                candidates.Add(child);
            }
        }

        if (candidates.Count < 2)
        {
            return null;
        }

        var videoInfos = candidates
            .Select(i => VideoResolver.Resolve(i.FullName, i.IsDirectory, _namingOptions, true, parent.ContainingFolderPath))
            .Where(f => f is not null)
            .ToList();

        if (videoInfos.Count < 2 || videoInfos.Any(v => v!.IsStub))
        {
            return null;
        }

        var groups = _videoListResolver.Resolve(videoInfos!, true, true, parent.ContainingFolderPath, collectionType);

        // Extras never pair, so they are excluded from the candidate list by holding a null path.
        var groupPaths = groups
            .Select(g => g.ExtraType is null && g.Files.Count > 0 ? g.Files[0].Path : null)
            .ToList();

        var pairs = VersionPairing.PairVersions(groupPaths, suffixes);
        if (pairs.Count == 0)
        {
            // Nothing to merge here, so claim nothing and let Jellyfin resolve the folder as usual.
            return null;
        }

        var alternatesOf = new Dictionary<int, List<int>>();
        foreach (var pair in pairs)
        {
            if (!alternatesOf.TryGetValue(pair.Value, out var list))
            {
                list = new List<int>();
                alternatesOf[pair.Value] = list;
            }

            list.Add(pair.Key);
        }

        var emitted = groups.Count(g => g.ExtraType is null) - pairs.Count;
        var isInMixedFolder = emitted > 1 || parent.IsTopParent;

        var result = new MultiItemResolverResult { ExtraFiles = leftOver };
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < groups.Count; i++)
        {
            var video = groups[i];
            if (video.Files.Count == 0)
            {
                continue;
            }

            foreach (var file in video.Files)
            {
                covered.Add(file.Path);
            }

            if (video.ExtraType is not null)
            {
                var extra = candidates.Find(f => string.Equals(f.FullName, video.Files[0].Path, StringComparison.OrdinalIgnoreCase));
                if (extra is not null)
                {
                    result.ExtraFiles.Add(extra);
                }

                continue;
            }

            if (pairs.ContainsKey(i))
            {
                // Folded into its original below.
                continue;
            }

            var alternates = new List<string>();
            foreach (var existing in video.AlternateVersions)
            {
                if (existing.Files.Count > 0)
                {
                    alternates.Add(existing.Files[0].Path);
                    foreach (var file in existing.Files)
                    {
                        covered.Add(file.Path);
                    }
                }
            }

            if (alternatesOf.TryGetValue(i, out var mine))
            {
                foreach (var index in mine)
                {
                    var child = groups[index];
                    if (child.Files.Count > 0)
                    {
                        alternates.Add(child.Files[0].Path);
                        foreach (var file in child.Files)
                        {
                            covered.Add(file.Path);
                        }
                    }

                    foreach (var nested in child.AlternateVersions)
                    {
                        if (nested.Files.Count > 0)
                        {
                            alternates.Add(nested.Files[0].Path);
                            foreach (var file in nested.Files)
                            {
                                covered.Add(file.Path);
                            }
                        }
                    }
                }
            }

            var first = video.Files[0];
            var movie = new Movie
            {
                Path = first.Path,
                IsInMixedFolder = isInMixedFolder,
                ProductionYear = video.Year,
                Name = video.Name,
                AdditionalParts = video.Files.Count > 1
                    ? video.Files.Skip(1).Select(f => f.Path).ToArray()
                    : Array.Empty<string>(),
                LocalAlternateVersions = alternates.ToArray()
            };

            Set3DFormat(movie, first);
            result.Items.Add(movie);
        }

        result.ExtraFiles.AddRange(candidates.Where(f => !covered.Contains(f.FullName)));

        _logger.LogInformation(
            "Version grouping merged {Pairs} encoded copies into {Films} films in {Path}",
            pairs.Count,
            result.Items.Count,
            parent.Path);

        return result;
    }

    /// <summary>
    /// Mirrors BaseVideoResolver.Set3DFormat, which lives in the server assembly a plugin cannot reference.
    /// </summary>
    private static void Set3DFormat(Video video, VideoFileInfo info)
    {
        if (!info.Is3D)
        {
            return;
        }

        video.Video3DFormat = info.Format3D?.ToLowerInvariant() switch
        {
            "fsbs" => Video3DFormat.FullSideBySide,
            "ftab" => Video3DFormat.FullTopAndBottom,
            "hsbs" => Video3DFormat.HalfSideBySide,
            "htab" => Video3DFormat.HalfTopAndBottom,
            "sbs" => Video3DFormat.HalfSideBySide,
            "sbs3d" => Video3DFormat.HalfSideBySide,
            "tab" => Video3DFormat.HalfTopAndBottom,
            "mvc" => Video3DFormat.MVC,
            _ => video.Video3DFormat
        };
    }
}
