# Getting started

This walks through installing QualityGate and capping one user at 720p. It takes about ten
minutes, and the last step tells you how to prove the cap actually holds.

## Requirements

- Jellyfin 12 (`targetAbi` is `12.0.0.0`). QualityGate 3.4.0.0 and later will not load on 10.x.
- Admin access to the Jellyfin web UI.
- Your media must have been scanned, so Jellyfin knows each file's real resolution. The cap
  reads the height from the video stream, so an item Jellyfin has never probed cannot be
  measured.

## Install from the plugin repository

1. In Jellyfin, go to **Dashboard, Plugins, Repositories** and add:

   ```
   https://geiserx.github.io/quality-gate/manifest.json
   ```

2. Go to **Catalogue**, find **QualityGate**, and install the newest version.
3. Restart Jellyfin. A plugin does not load until the server restarts.
4. Go back to **Plugins** and check that QualityGate shows **Active**. Anything else means it
   did not load, and nothing is being enforced. See
   [troubleshooting](troubleshooting.md#the-plugin-vanished-after-an-update).

Manual installation and building from source are covered in [installation](installation.md).

## Create a policy

Open **Dashboard, Plugins, QualityGate**.

1. Under **Policies**, add a policy.
2. Name it something you will recognise later, for example `720p Only`.
3. Set **Maximum Resolution** to **720p**.
4. Leave everything else alone. Several of the other fields do nothing in the current build,
   and [configuration](configuration.md#fields-that-do-nothing) explains exactly which.
5. Save.

**Maximum Resolution is the only field that restricts playback.** If you set it to `No limit`,
the policy caps nothing, no matter what else you fill in.

## Assign a user

In the **User Access** table, set the user to your new policy and save.

Three things decide which policy a user gets, in this order:

1. An explicit assignment for that user wins.
2. Otherwise the default policy applies, if you set one.
3. Otherwise the user is unrestricted.

`Full access` is an explicit choice you can assign, and it beats the default policy. That is
the way to exempt one person while everyone else inherits a default.

## Prove it works

Do not skip this. A cap you have not tested is a guess, and the failure mode is silent: an
unloaded plugin looks exactly like a loaded one that is allowing everything.

Sign in as the capped user and play something you know is 1080p or larger. You should see it
play, because QualityGate does not hide media. What changes is how it is delivered. Check
**Dashboard, Activity** while it plays: the session should say **Transcoding**, with an output
height at or below your cap, rather than **Direct playing**.

For a harder check, ask the server for the original bytes directly. As the capped user, with
their access token:

```bash
curl -s -o /dev/null -w '%{http_code}\n' \
  -H 'Authorization: MediaBrowser Token="<capped-user-token>"' \
  'http://your-server:8096/Items/<item-id>/Download'
```

A capped user must get `403` for an item above the cap. If you get `200`, the cap is not
working. Run the same request as an unrestricted user and confirm you get `200` there, so you
know the test itself is meaningful.

## What to read next

- [Configuration](configuration.md) for every setting, and for the ones that do nothing.
- [How it works](how-it-works.md) for the routes that are covered, and the one that is not.
- [One library, two qualities](one-library.md) if you keep a smaller encode beside each
  original and want both to appear as a single item.
- [Troubleshooting](troubleshooting.md) when something is not behaving.
