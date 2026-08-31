# One library, two qualities

How to keep a smaller encode beside each original and have Jellyfin show them as one film with
two versions, instead of two films.

This is the feature that takes the longest to get right, so this page is long. The rules are
strict on purpose: a resolver that guesses would re-identify your library differently on every
scan.

## The problem it solves

Jellyfin does merge alternate versions, but only inside a folder named after the film. Its rule
keys grouping to the containing folder's name and requires every file to start with it. So this
works out of the box:

```
Films/
  Blade Runner (1982)/
    Blade Runner (1982).mkv
    Blade Runner (1982) - 720p.mp4
```

and this does not:

```
Films/
  Blade Runner (1982).mkv
  Blade Runner (1982) - 720p.mp4      <- shows up as a second film
```

The obvious workaround does not hold. Merging the two by hand through
`POST /Videos/MergeVersions` writes a linked alternate, and the next library scan re-resolves
the file as standalone and throws the link away. Grouping only lasts if it is rebuilt at resolve
time, which is what this resolver does. It runs on every scan, so it persists.

## Turning it on

In **Dashboard, Plugins, QualityGate**, under **Version Grouping**:

1. Tick **Enable version grouping**.
2. Optionally list **library paths**, one per line. Leave empty to apply to every movie library.
3. Set the **suffixes** that mark an encoded copy, one per line. The default is ` - 720p`,
   including the leading space and hyphen.
4. Save, then run a library scan.

It is off by default because it changes how a library resolves, which should be a deliberate
choice rather than something that happens on upgrade.

## The naming rule

A file is an encoded copy of another when its name, without the extension, is **exactly** the
other file's name plus one of your configured suffixes.

That is the whole rule. It never looks at probe data, so the same files always pair the same
way, scan after scan.

With the default suffix ` - 720p`:

| Files in the folder | Result |
|---|---|
| `Film.mkv` + `Film - 720p.mp4` | Pairs. `Film.mkv` is the original |
| `Film (1982).mkv` + `Film (1982) - 720p.mp4` | Pairs |
| `Film.mkv` + `Film - German.mkv` | Does not pair. `- German` is not a configured suffix |
| `Film.mkv` + `Film-720p.mp4` | Does not pair. The suffix is ` - 720p`, with spaces |
| `Film - 720p.mp4` alone | Does not pair. There is no original to attach to |
| ` - 720p.mkv` | Never pairs. A file named only the suffix is not an encode of an empty name |
| `Film.mkv` + `Film.mp4` + `Film - 720p.mkv` | Pairs nothing. Two files claim the stem `Film`, which is ambiguous |

Extensions are irrelevant to pairing, and the two files do not need to share one. The original
is always the file **without** the suffix.

Jellyfin's own rule is looser and would also swallow `Film - German.mkv` as a version of
`Film.mkv`. This one is deliberately narrower, because a wrongly merged foreign-language cut is
much more annoying to unpick than an unmerged file.

## When the resolver declines

It hands the folder straight back to Jellyfin's normal resolvers, changing nothing, when any of
these is true:

- Version grouping is switched off.
- The library is not a **movies** library.
- The folder is not under one of your configured paths.
- The folder holds fewer than two files.
- Your suffix list is empty.
- The folder contains a `.iso`, `.img` or `.strm` file. Jellyfin inspects these to set
  `VideoType` and `IsoType`, so the whole folder is left alone.
- After ignoring samples, fewer than two candidate files remain.
- **No actual pair was found.**

That last one matters most. The resolver claims a folder only when there is a real pair to
merge. A folder of unrelated films is untouched, and everything Jellyfin normally does with it,
naming, stacking, extras detection, still happens.

Files matching `sample` as a whole word, case-insensitively, are ignored, which mirrors
Jellyfin's own movie resolver. Subdirectories are left alone and recursed into as usual.

## Path matching

A folder is under a configured root when its path equals the root, or begins with the root
followed by a separator. `/media/Films` covers `/media/Films` and `/media/Films/Kids`, and does
not cover `/media/Films2`. Trailing slashes are trimmed and both separator characters are
accepted. Matching is case-sensitive except on Windows.

Use the path Jellyfin sees. Inside Docker that is the container path, `/media/Films`, not the
host path.

## Television is already handled

This resolver applies to movie libraries only, and TV needs nothing.

Jellyfin groups episodes natively by parsed episode number rather than by folder name, so
`S06E01.mkv` and `S06E01 - 720p.mkv` in the same season folder already become one episode with
two versions. Switching this on will not change your shows, and you do not need to enable it
for them.

## Which version plays

The original, the file without the suffix, is the primary version and plays by default. Both
appear in the version picker.

For a user under a resolution cap, that choice is made again at playback: QualityGate offers the
version within their cap and refuses the original. See [how it works](how-it-works.md) for the
detail. The two features are independent, and either works without the other.

## Before you switch it on

**Grouping changes item identity, and watch history follows identity.** Jellyfin derives an
item's id from its type and path. Folding a standalone item into a version group removes the
separate item that the encoded copy used to be, and any watch state recorded against that
separate item goes with it. History on the original is unaffected, because its path does not
change.

In practice this matters if people have been watching the encoded copies as if they were their
own films. If that is your situation, migrate the play state first, or accept the loss
deliberately.

Try it on a copy or a small test library before a library of thousands. The resolver is
conservative and declines rather than guessing, but a library scan is a poor place to discover
you meant a different suffix.

## Checking that it worked

After the scan, open a film that has an encode beside it. It should appear once, and the version
picker should offer two entries.

If it still appears twice, work through this in order:

1. Is the plugin **Active**? An unloaded plugin resolves nothing. See
   [troubleshooting](troubleshooting.md#the-plugin-vanished-after-an-update).
2. Is `EnableVersionGrouping` actually true in the live config? Check with the API call in
   [configuration](configuration.md#checking-the-live-configuration), not the admin page.
3. Is the library type **Movies**?
4. Does the folder path sit under one of your configured roots, using the path Jellyfin sees?
5. Do the two filenames differ by *exactly* one configured suffix? A double space or a missing
   hyphen is enough to stop the pairing.
6. Did you run a full scan after saving? The grouping is built at resolve time.

A renamed file does not repair a group that has already formed the wrong way. Rename first, then
rescan.
