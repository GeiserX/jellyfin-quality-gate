using System;
using System.Collections.Generic;
using System.IO;

namespace Jellyfin.Plugin.QualityGate.Library;

/// <summary>
/// The filename rule that decides whether one file is an encoded copy of another.
/// Kept free of Jellyfin types so it can be unit tested on its own.
/// </summary>
public static class VersionPairing
{
    /// <summary>
    /// Gets the stem this file would be an encoded copy of, or <c>null</c> when its name
    /// carries none of the configured suffixes.
    /// </summary>
    /// <param name="path">Path or file name to inspect.</param>
    /// <param name="suffixes">Suffixes that mark an encoded copy, for example <c>" - 720p"</c>.</param>
    /// <returns>The original's stem, or <c>null</c>.</returns>
    public static string? GetBaseStem(string? path, IReadOnlyList<string>? suffixes)
    {
        if (string.IsNullOrEmpty(path) || suffixes is null)
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(stem))
        {
            return null;
        }

        for (var i = 0; i < suffixes.Count; i++)
        {
            var suffix = suffixes[i];

            // A file called exactly " - 720p.mkv" is not an encode of the empty name.
            if (!string.IsNullOrEmpty(suffix)
                && stem.Length > suffix.Length
                && stem.EndsWith(suffix, StringComparison.Ordinal))
            {
                return stem[..^suffix.Length];
            }
        }

        return null;
    }

    /// <summary>
    /// Maps each encoded copy to the index of the original it belongs to.
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than Jellyfin's own multi-version rule, which merges every file
    /// whose name starts with the folder name and so also swallows <c>Movie - German.mkv</c>.
    /// Here a file pairs only when its stem is exactly another file's stem plus a configured
    /// suffix. The original is always the file without the suffix, so the choice never depends
    /// on probe data and cannot change between scans. A stem claimed by more than one file is
    /// ambiguous and pairs nothing.
    /// </remarks>
    /// <param name="paths">Candidate paths, one per resolved group.</param>
    /// <param name="suffixes">Suffixes that mark an encoded copy.</param>
    /// <returns>Map of encoded-copy index to original index.</returns>
    public static IReadOnlyDictionary<int, int> PairVersions(
        IReadOnlyList<string?>? paths,
        IReadOnlyList<string>? suffixes)
    {
        var pairs = new Dictionary<int, int>();
        if (paths is null || paths.Count < 2 || suffixes is null || suffixes.Count == 0)
        {
            return pairs;
        }

        var originals = new Dictionary<string, int>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            if (string.IsNullOrEmpty(path) || GetBaseStem(path, suffixes) is not null)
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(stem) && !originals.TryAdd(stem, i))
            {
                ambiguous.Add(stem);
            }
        }

        for (var i = 0; i < paths.Count; i++)
        {
            var baseStem = GetBaseStem(paths[i], suffixes);
            if (baseStem is null
                || ambiguous.Contains(baseStem)
                || !originals.TryGetValue(baseStem, out var original)
                || original == i)
            {
                continue;
            }

            pairs[i] = original;
        }

        return pairs;
    }
}
