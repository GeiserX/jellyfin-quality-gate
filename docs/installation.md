# Installation

QualityGate 3.4.0.0 and later target **Jellyfin 12** (`targetAbi` 12.0.0.0, `net10.0`). They
will not load on Jellyfin 10.x. If you are still on 10.11, the last build for it is 3.3.6.0,
and it is no longer maintained.

## From the plugin repository

This is the way to install it, because it also brings updates.

1. **Dashboard, Plugins, Repositories, Add**
   - Name: `QualityGate`
   - URL: `https://geiserx.github.io/quality-gate/manifest.json`
2. **Catalogue**, find **QualityGate**, install the newest version.
3. Restart Jellyfin.
4. Check **Plugins** shows it as **Active**.

The plugin GUID is `9cab70ca-0af3-4d3a-adab-6a0df2496a33`. You need it for the configuration
API, and for the reinstall command in [troubleshooting](troubleshooting.md#the-plugin-vanished-after-an-update).

## Manual installation

Take the version number from the
[latest release](https://github.com/GeiserX/quality-gate/releases/latest) rather than copying
one from a document. Extract the zip so that `Jellyfin.Plugin.QualityGate.dll` sits directly
inside a folder under `plugins/`.

Jellyfin expects this layout:

```text
config/plugins/
  QualityGate_3.7.0.0/
    Jellyfin.Plugin.QualityGate.dll
    build.yaml
    meta.json          <- written by Jellyfin's installer, not present in the zip
```

The folder name is conventionally `Name_Version`. The release zip contains only the DLL and
`build.yaml`; `meta.json` is written by Jellyfin itself when it installs from the repository, and
it is what Jellyfin reads to know which version is installed and whether an update exists.

Do not hand-write `meta.json`, and never copy one from an older version folder. It carries
`"status": "Deleted"`, and the plugin will remove itself on the next restart. If you need version
tracking, install from the repository instead of by hand.

### Docker

```bash
VERSION="3.7.0.0"
curl -L -o QualityGate.zip \
  "https://github.com/GeiserX/quality-gate/releases/download/v${VERSION}/quality-gate_${VERSION}.zip"
unzip QualityGate.zip -d /path/to/jellyfin/config/plugins/QualityGate_${VERSION}/
docker restart jellyfin
```

### Linux

```bash
VERSION="3.7.0.0"
curl -L -o QualityGate.zip \
  "https://github.com/GeiserX/quality-gate/releases/download/v${VERSION}/quality-gate_${VERSION}.zip"
sudo unzip QualityGate.zip -d /var/lib/jellyfin/plugins/QualityGate_${VERSION}/
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/QualityGate_${VERSION}/
sudo systemctl restart jellyfin
```

### Windows

There are two data directories, and which one applies depends on how Jellyfin was installed:

- Installed as a **Windows service** (the default installer):
  `%PROGRAMDATA%\Jellyfin\Server\plugins\QualityGate_<version>\`
- **Portable** or tray builds run under your own account:
  `%LOCALAPPDATA%\jellyfin\plugins\QualityGate_<version>\`

Putting the DLL in the wrong one leaves it outside the folder Jellyfin scans, and it will simply
never appear. Confirm which applies from **Dashboard, About**, then restart Jellyfin from
Services or the tray icon.

### macOS

Use whichever data directory your install actually reports, rather than assuming. Check
**Dashboard, About**, or `JELLYFIN_DATA_DIR` if you set it. For a default install that is
`~/.local/share/jellyfin`.

```bash
VERSION="3.7.0.0"
DATA_DIR="$HOME/.local/share/jellyfin"   # confirm this against Dashboard, About
curl -L -o QualityGate.zip \
  "https://github.com/GeiserX/quality-gate/releases/download/v${VERSION}/quality-gate_${VERSION}.zip"
unzip QualityGate.zip -d "$DATA_DIR/plugins/QualityGate_${VERSION}/"
```

## Upgrading

Update through the catalogue and restart. Jellyfin marks the old version folder deleted and
removes it on the next start.

On overlay filesystems, notably Unraid's shfs, that removal can fail in a way that takes the new
version with it. If a plugin disappears after an upgrade, that is what happened, and
[troubleshooting](troubleshooting.md#the-plugin-vanished-after-an-update) has the recovery.

Upgrading never touches your policies. They live in
`config/plugins/configurations/Jellyfin.Plugin.QualityGate.xml`, outside the plugin folder.

## Building from source

Requires the [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/GeiserX/quality-gate.git
cd quality-gate
dotnet build -c Release
dotnet test
```

The compiled plugin lands in
`Jellyfin.Plugin.QualityGate/bin/Release/net10.0/Jellyfin.Plugin.QualityGate.dll`. Copy it next
to a `meta.json` from a release zip to install a local build.

CI builds with .NET 10.0.x, runs the test suite with coverage, and reports to Codecov. A
contribution needs the build and the tests green, and the patch covered.

## Releases

Pushing a version bump to `main` cuts a release and publishes the plugin manifest to GitHub
Pages, which is what the repository URL above serves. The version is declared in three places
and they must agree:

- `Jellyfin.Plugin.QualityGate/Jellyfin.Plugin.QualityGate.csproj` (`AssemblyVersion`,
  `FileVersion`, `Version`)
- `Jellyfin.Plugin.QualityGate/build.yaml`
- `Jellyfin.Plugin.QualityGate/meta.json`

The published manifest is **derived from the releases that exist**, not from the copy in the
repo. Every release with a plugin zip is included, its checksum computed from the bytes actually
served and its `targetAbi` read from the `build.yaml` inside the zip.

That is deliberate. It used to be the committed copy plus one new entry, which made a bookkeeping
commit load-bearing: when that commit did not land, the next release rebuilt from a stale base and
the missing version disappeared from the catalogue. That is how 3.3.6.0 and 3.4.0.0 went, and the
step that was supposed to record it had been failing silently for three releases. Deriving the
manifest makes losing a version impossible, so there is no longer a pull request to remember.

`manifest.json` in the repo now only seeds the editorial header, the name, overview and
description. Its version list is ignored.
