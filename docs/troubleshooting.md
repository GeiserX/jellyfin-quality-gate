# Troubleshooting

Things that go wrong, in rough order of how much time they waste.

## The cap is not being applied

Work down this list rather than guessing. Each step rules out one layer.

**1. Is the plugin loaded?** In **Dashboard, Plugins**, QualityGate must say **Active**.
Anything else, or absent entirely, means nothing is enforced. There is no fallback: an unloaded
plugin looks exactly like a loaded one that allows everything.

**2. Does the policy set a Maximum Resolution?** This is the most common cause. Filename
patterns and the fallback-transcode fields do not restrict anything in current builds. If your
policy only fills those in, it caps nobody. See
[the fields that do not restrict playback](configuration.md#fields-that-do-not-restrict-playback).

**3. Is the user actually on that policy?** Ask the server rather than reading the admin page:

```bash
curl -s -H 'Authorization: MediaBrowser Token="<admin-token>"' \
  'https://your-server/Plugins/9cab70ca0af34d3aadab6a0df2496a33/Configuration' \
  | python3 -m json.tool
```

Every `UserPolicies` entry should have a `PolicyId` that appears in `Policies` in that same
response, with `Enabled` true.

**4. Does Jellyfin know the item's height?** The cap is measured against the video stream. An
item that has never been probed has no height, so it is allowed and a warning is logged.
Rescan, or check the item's media info.

**5. Is the request authenticated as a user at all?** Jellyfin's API-key authentication resolves
to an empty user id, which matches no assignment, so **an API key is uncapped on every route unless
you set one**. If you are testing with an API key rather than a user's access token, you are
testing the wrong thing. Anonymous requests behave the same way. To cap them, set
**API key and anonymous requests** in the admin page. If you already did and it is not taking
effect, check that the policy it names still exists and is enabled: an unresolvable one leaves
those requests uncapped by design.

**6. Read the log.** The filter is talkative on purpose. Search the Jellyfin log for
`QualityGate`. When it acts you get lines naming the cap, the user and the policy, for example
`capped PlaybackInfo at 720p for user ... offering 1 of 2 sources`. If the log says nothing for
a playback you expected to be capped, the filter did not consider that user restricted, which
sends you back to steps 2 and 3.

## A user I disabled the policy for now has more access, not less

That is the documented behaviour, and it surprises everyone.

Deleting or disabling a policy that users are assigned to does not deny them. It returns an
internal deny-all sentinel that carries no maximum resolution, so the resolution cap sees no
height to enforce and treats those users as unrestricted.

To take access away, point the users at a policy with a low `MaxHeight`. Do not rely on removing
the policy they are on. There is more detail in
[configuration](configuration.md#a-gap-worth-knowing-about).

## The plugin vanished after an update

Symptoms: QualityGate is absent from **Dashboard, Plugins** entirely, its configuration endpoint
returns 404, and the log has no `Loaded plugin: QualityGate` line.

This is a Jellyfin plus overlay-filesystem interaction, seen on Unraid's shfs and possible on
any filesystem that keeps open files alive after unlink, including some Docker storage drivers.

Jellyfin marks a superseded version by writing `"status": "Deleted"` into its `meta.json`. If
the old DLL is still open, the filesystem renames it to a hidden file rather than removing it,
the folder never empties, `RemoveDirectoryRecursive` throws `Directory not empty`, and
`DiscoverPlugins()` purges the whole GUID. That takes the newly installed version with it.

**Check for it** by reading the status of every plugin folder. The folder names tell you
nothing:

```bash
P=/path/to/jellyfin/config/plugins
find "$P" -mindepth 1 -maxdepth 1 -type d | while IFS= read -r d; do
  s=$(python3 -c "import json,sys;print(json.load(open(sys.argv[1])).get('status','?'))" \
      "$d/meta.json" 2>/dev/null || echo NO-META)
  echo "[$s] $(basename "$d")"
done
```

A folder reporting `Deleted` is armed to purge its plugin at the next restart. A folder with no
`meta.json`, or an empty one, is a plugin that has already been lost.

**Recover it:**

1. **Move** the stale folders out of `config/plugins/`. Do not delete them. `rm -rf` fails
   repeatedly, because the filesystem recreates the hidden file on each unlink while it is open.
2. Reinstall through Jellyfin's own installer. Never hand-copy a `meta.json` from the old
   folder: it carries `"status": "Deleted"` into the new version, which then deletes itself on
   the next boot.
   ```
   POST /Packages/Installed/QualityGate?version=<v>&assemblyGuid=9cab70ca0af34d3aadab6a0df2496a33
   ```
3. Restart, then confirm **Active** and that a field only the new version knows about appears in
   `GET /Plugins/<guid>/Configuration`. Status alone does not prove the new code is running.

**Your configuration is safe.** It lives in
`config/plugins/configurations/Jellyfin.Plugin.QualityGate.xml`, outside the plugin folder, and
survives all of this. An unloaded plugin returns an empty policy list from the API, which looks
like data loss and is not.

**Worth monitoring.** This failure is silent. If you rely on the cap rather than on separate
libraries, alert on the plugin not being `Active`, and on any plugin folder still marked
`Deleted`.

## The API returns 401 and I am sure the token is right

Jellyfin 12 accepts only this header form:

```text
Authorization: MediaBrowser Token="<token>"
```

`X-Emby-Token` and `?api_key=` both return 401. From outside, that is indistinguishable from the
plugin being absent, so check the header before concluding anything about the plugin.

## An encoded copy still shows as a separate film

Covered in full at
[one library, two qualities](one-library.md#checking-that-it-worked). The short version:
confirm the flag is on in the live config, the library is a Movies library, the folder is under a
configured root, the two names differ by exactly one configured suffix, and you have run a scan
since saving.

## A capped user can still download the original

Test it explicitly rather than assuming:

```bash
curl -s -o /dev/null -w '%{http_code}\n' \
  -H 'Authorization: MediaBrowser Token="<capped-user-token>"' \
  'https://your-server/Items/<item-id>/Download'
```

`403` is correct for an item above the cap. If you get `200`, work through
"the cap is not being applied" above. Run the same request as an unrestricted user and confirm
`200`, so you know the test can distinguish the two cases.

One route is a known, accepted bypass: the legacy HLS segment route
`/Videos/{itemId}/hls/{playlistId}/{segmentId}.{container}`. It cannot be mapped back to an
item, so it is not gated. It can only return segments that already exist in the transcode
folder, which for a capped user were produced under the cap. [How it works](how-it-works.md#the-one-route-that-is-not-covered)
explains why closing it is a bigger change than the exposure warrants.

## Intros play twice, or play when I do not expect them

Jellyfin aggregates every registered `IIntroProvider`. If the built-in **Local Intros** plugin is
also enabled, its intros play in addition to QualityGate's. Disable Local Intros if you only want
these.

Intros are skipped when a user resumes a film mid-playback. For a series, the intro plays once
per user and never again for that show, in any season: the provider asks the library whether the
user has played, or stopped part-way through, any episode under the series, and keeps an
in-memory note for the gap before Jellyfin saves the first progress report. Before 3.8.1.0 the
persistent check read `LastPlayedDate` on the Series item, which Jellyfin never writes, so the
intro came back for every show after each server restart.
