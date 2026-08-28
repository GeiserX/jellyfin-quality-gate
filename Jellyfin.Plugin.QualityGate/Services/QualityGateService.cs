using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.QualityGate.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.QualityGate.Services;

/// <summary>
/// Service for applying quality gate policies to media access.
/// </summary>
public static class QualityGateService
{
    /// <summary>
    /// Sentinel policy returned when a user override references an invalid, disabled, or deleted PolicyId.
    /// Blocks everything (fail-closed) so that admin misconfiguration cannot widen access.
    /// </summary>
    internal static readonly QualityPolicy DenyAllPolicy = new()
    {
        Id = "__DENY_ALL__",
        Name = "Deny All (misconfigured override)",
        Enabled = true,
        AllowedFilenamePatterns = new List<string> { "^$" },
    };

    /// <summary>
    /// Gets the effective policy for a user.
    /// Priority: User-specific override > Default policy > No policy (full access).
    /// If a user override points to a missing or disabled policy, access is DENIED (fail-closed).
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>The effective policy, or null if user has full access.</returns>
    public static QualityPolicy? GetUserPolicy(Guid userId)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
        {
            return null;
        }

        // Check for user-specific override
        var userAssignment = config.UserPolicies.FirstOrDefault(up => up.UserId == userId);

        if (userAssignment != null)
        {
            // User has a specific assignment
            if (userAssignment.PolicyId == UserPolicyAssignment.FullAccessPolicyId)
            {
                // User explicitly has full access
                return null;
            }

            if (!string.IsNullOrEmpty(userAssignment.PolicyId))
            {
                // User has a specific policy — it MUST resolve to a valid, enabled policy.
                // If not found (deleted/mistyped/disabled), fail-closed: deny all access.
                var policy = config.Policies.FirstOrDefault(p => p.Id == userAssignment.PolicyId && p.Enabled);
                return policy ?? DenyAllPolicy;
            }
        }

        // No user-specific override, check for default policy
        if (!string.IsNullOrEmpty(config.DefaultPolicyId))
        {
            // Default policy MUST resolve to a valid, enabled policy.
            // If not found (deleted/mistyped/disabled), fail-closed: deny all access.
            var defaultPolicy = config.Policies.FirstOrDefault(p => p.Id == config.DefaultPolicyId && p.Enabled);
            return defaultPolicy ?? DenyAllPolicy;
        }

        // No policy applies - full access
        return null;
    }

    /// <summary>
    /// Checks whether the policy caps the resolution a user may be served.
    /// </summary>
    /// <param name="policy">The quality policy.</param>
    /// <returns>True when <see cref="QualityPolicy.MaxHeight"/> is set to a positive height.</returns>
    public static bool HasHeightCap(QualityPolicy? policy)
    {
        return policy != null && policy.MaxHeight > 0;
    }

    /// <summary>
    /// Gets the height of the tallest video stream in a set of media streams.
    /// Audio, subtitle and embedded-image streams are ignored.
    /// </summary>
    /// <param name="streams">The media streams to inspect.</param>
    /// <returns>The video height in pixels, or null when no video stream reports one.</returns>
    public static int? GetVideoHeight(IEnumerable<MediaStream>? streams)
    {
        if (streams == null)
        {
            return null;
        }

        int? tallest = null;
        foreach (var stream in streams)
        {
            if (stream == null || stream.Type != MediaStreamType.Video || !stream.Height.HasValue)
            {
                continue;
            }

            if (!tallest.HasValue || stream.Height.Value > tallest.Value)
            {
                tallest = stream.Height.Value;
            }
        }

        return tallest;
    }

    /// <summary>
    /// Gets the height of the tallest video stream in a media source.
    /// </summary>
    /// <param name="source">The media source.</param>
    /// <returns>The video height in pixels, or null when the source reports none.</returns>
    public static int? GetVideoHeight(MediaSourceInfo? source)
    {
        return source == null ? null : GetVideoHeight(source.MediaStreams);
    }

    /// <summary>
    /// Determines whether a given media height breaks the policy's resolution cap.
    /// </summary>
    /// <remarks>
    /// An unknown height (null) does NOT exceed the cap. Height comes from the server's own
    /// ffprobe metadata, so a null means the item has not been probed rather than that the
    /// media is oversized — treating that as a violation would block items nobody can
    /// distinguish from a plugin bug. Unknown-height items are still capped in practice,
    /// because the height condition the plugin injects into the negotiated DeviceProfile is
    /// marked required and an unknown height fails a required condition, which pushes the
    /// item through a capped transcode instead of direct play.
    /// </remarks>
    /// <param name="policy">The quality policy.</param>
    /// <param name="height">The media height in pixels, or null when unknown.</param>
    /// <returns>True only when the policy caps height, the height is known, and it is above the cap.</returns>
    public static bool ExceedsHeightCap(QualityPolicy? policy, int? height)
    {
        return HasHeightCap(policy) && height.HasValue && height.Value > policy!.MaxHeight;
    }

    /// <summary>
    /// Determines whether a media source breaks the policy's resolution cap.
    /// </summary>
    /// <param name="policy">The quality policy.</param>
    /// <param name="source">The media source.</param>
    /// <returns>True when the source's video height is known and above the cap.</returns>
    public static bool ExceedsHeightCap(QualityPolicy? policy, MediaSourceInfo? source)
    {
        return ExceedsHeightCap(policy, GetVideoHeight(source));
    }

    /// <summary>
    /// Resolves a path, following symlinks to get the actual target path.
    /// </summary>
    /// <param name="path">The path to resolve.</param>
    /// <returns>The resolved path (symlink target), or original path if not a symlink.</returns>
    public static string ResolvePath(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.LinkTarget != null)
            {
                // It's a symlink, return the target
                // If target is relative, resolve it relative to the symlink's directory
                var target = fileInfo.LinkTarget;
                if (!Path.IsPathRooted(target))
                {
                    var dir = Path.GetDirectoryName(path) ?? string.Empty;
                    target = Path.GetFullPath(Path.Combine(dir, target));
                }
                return target;
            }
        }
        catch
        {
            // If we can't resolve, return original
        }
        return path;
    }

    /// <summary>
    /// Matches a filename against a regex pattern with a timeout to prevent ReDoS.
    /// When <paramref name="failClosed"/> is true (used for blocked-pattern checks),
    /// timeouts and invalid patterns return true so the file is blocked (fail-closed).
    /// </summary>
    private static bool MatchesFilenamePattern(string filename, string pattern, bool failClosed = false)
    {
        try
        {
            return Regex.IsMatch(filename, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }
        catch (RegexMatchTimeoutException)
        {
            return failClosed;
        }
        catch (ArgumentException)
        {
            return failClosed;
        }
    }

    /// <summary>
    /// Checks if a file path is allowed by the given policy.
    /// Resolves symlinks to check both original and resolved filenames.
    /// </summary>
    /// <param name="policy">The quality policy.</param>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>True if allowed, false if blocked.</returns>
    public static bool IsPathAllowed(QualityPolicy policy, string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false; // Deny if path is missing — cannot validate
        }

        // Resolve symlinks to get actual target path
        var resolvedPath = ResolvePath(filePath);

        // Check blocked filename patterns (regex against the filename component)
        // Check both original and resolved filenames for symlinked setups
        if (policy.BlockedFilenamePatterns.Count > 0)
        {
            var originalFilename = Path.GetFileName(filePath);
            var resolvedFilename = Path.GetFileName(resolvedPath);
            if (policy.BlockedFilenamePatterns.Any(pattern =>
                MatchesFilenamePattern(originalFilename, pattern, failClosed: true) ||
                MatchesFilenamePattern(resolvedFilename, pattern, failClosed: true)))
            {
                return false;
            }
        }

        // If allowed filename patterns are specified, file must match at least one
        // Check both original and resolved filenames for symlinked setups
        if (policy.AllowedFilenamePatterns.Count > 0)
        {
            var originalFilename = Path.GetFileName(filePath);
            var resolvedFilename = Path.GetFileName(resolvedPath);
            if (!policy.AllowedFilenamePatterns.Any(pattern =>
                MatchesFilenamePattern(originalFilename, pattern) ||
                MatchesFilenamePattern(resolvedFilename, pattern)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks whether a media source is allowed by policy AND actually exists on disk.
    /// Dangling symlinks (e.g. pre-created 720p transcodes not yet finished) are treated as blocked.
    /// This is the single source of truth for "can this source be played" — used by the
    /// result filter, API controller, and intro provider.
    /// </summary>
    /// <param name="policy">The quality policy.</param>
    /// <param name="path">The file path to check.</param>
    /// <returns>True if allowed by policy and file exists.</returns>
    public static bool IsSourcePlayable(QualityPolicy policy, string? path)
    {
        if (!IsPathAllowed(policy, path))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(path) && !File.Exists(path))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks whether the policy configuration allows fallback transcoding.
    /// This is a lightweight check that only examines the policy flags — it does NOT
    /// evaluate sources. Use this in the filter after sources have already been evaluated
    /// to avoid redundant IsSourcePlayable re-scans.
    /// </summary>
    internal static bool PolicyAllowsFallback(QualityPolicy policy)
    {
        return policy.FallbackTranscode && !ReferenceEquals(policy, DenyAllPolicy);
    }

    /// <summary>
    /// Determines whether fallback transcoding should be used for the given policy and sources.
    /// Returns true only when the policy has FallbackTranscode enabled, the policy is not
    /// the DenyAllPolicy sentinel (misconfiguration must stay fail-closed), at least one
    /// source file physically exists on disk, and none of them pass the policy filter.
    /// </summary>
    public static bool ShouldFallbackTranscode(QualityPolicy policy, IEnumerable<MediaSourceInfo> sources)
    {
        if (!PolicyAllowsFallback(policy))
        {
            return false;
        }

        var sourceList = sources as IList<MediaSourceInfo> ?? sources.ToList();
        if (sourceList.Count == 0)
        {
            return false;
        }

        // At least one source must physically exist — dangling symlinks / missing files
        // cannot be transcoded, so fallback would be useless.
        var hasExistingSource = sourceList.Any(s =>
            !string.IsNullOrEmpty(s.Path) && File.Exists(s.Path));

        return hasExistingSource && !sourceList.Any(s => IsSourcePlayable(policy, s.Path));
    }

    /// <summary>
    /// Returns deep-cloned copies of the sources with direct play and direct stream disabled,
    /// forcing Jellyfin to transcode server-side. Cloning prevents mutation of cached
    /// MediaSourceInfo objects that Jellyfin may reuse across requests.
    /// </summary>
    public static MediaSourceInfo[] ApplyFallbackTranscode(IEnumerable<MediaSourceInfo> sources)
    {
        return sources.Select(s =>
        {
            var clone = JsonSerializer.Deserialize<MediaSourceInfo>(
                JsonSerializer.SerializeToUtf8Bytes(s))!;
            clone.SupportsDirectPlay = false;
            clone.SupportsDirectStream = false;
            return clone;
        }).ToArray();
    }

    /// <summary>
    /// Filters media sources based on user policy.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="mediaSources">The available media sources.</param>
    /// <returns>Filtered list of media sources.</returns>
    public static IEnumerable<MediaSourceInfo> FilterMediaSources(Guid userId, IEnumerable<MediaSourceInfo> mediaSources)
    {
        var policy = GetUserPolicy(userId);
        if (policy == null)
        {
            return mediaSources;
        }

        return mediaSources.Where(source => IsSourcePlayable(policy, source.Path));
    }

    /// <summary>
    /// Checks if a user can access a specific file path.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="path">The file path.</param>
    /// <returns>True if access is allowed.</returns>
    public static bool CanAccessPath(Guid userId, string? path)
    {
        var policy = GetUserPolicy(userId);
        if (policy == null)
        {
            return true;
        }

        return IsSourcePlayable(policy, path);
    }

    /// <summary>
    /// Checks if a user has full access (no restrictions).
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>True if user has full access.</returns>
    public static bool HasFullAccess(Guid userId)
    {
        return GetUserPolicy(userId) == null;
    }
}
