<p align="center"><img src="docs/images/banner.svg" alt="Quality Gate banner" width="900"/></p>

<h1 align="center">Quality Gate</h1>

<p align="center">
  <a href="https://github.com/GeiserX/quality-gate/releases"><img src="https://img.shields.io/github/v/release/GeiserX/quality-gate?style=flat-square&logo=github" alt="GitHub Release"></a>
  <a href="https://jellyfin.org"><img src="https://img.shields.io/badge/Jellyfin-12.0+-00a4dc?style=flat-square&logo=jellyfin" alt="Jellyfin Version"></a>
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square&logo=dotnet" alt=".NET"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/GeiserX/quality-gate?style=flat-square" alt="License"></a>
  <a href="https://github.com/GeiserX/quality-gate/actions"><img src="https://img.shields.io/github/actions/workflow/status/GeiserX/quality-gate/build.yml?style=flat-square&logo=github-actions&logoColor=white&label=CI" alt="CI"></a>
  <a href="https://github.com/GeiserX/quality-gate/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/GeiserX/quality-gate/build.yml?branch=main&style=flat-square&label=tests" alt="Tests"></a>
  <a href="https://codecov.io/gh/GeiserX/quality-gate"><img src="https://codecov.io/gh/GeiserX/quality-gate/graph/badge.svg" alt="codecov"></a>

</p>

<p align="center"><strong>Intelligent media access control for Jellyfin</strong></p>

> **v3.5.0.0 enforces a resolution cap on Jellyfin 12.** The cap is measured against the media's actual height, read from the item's video stream, so it holds whatever the file is called. Set **Maximum Resolution** on a policy to switch it on; the default, **No limit**, leaves playback exactly as it is, so upgrading changes nothing until you choose a height.
>
> Filename regex patterns still exist in the config and still describe the older `MediaSourceResultFilter`, which stays unregistered on Jellyfin 12. They do **not** drive the resolution cap. A pattern like `- 720p` matches nothing when the served path keeps the original name — which is exactly why the cap is measured, not read off the filename.

---

## Features

- **Resolution Cap** -- Cap a user at 480p/720p/1080p/1440p/4K, measured against the media's real height
- **Covers Both Playback Paths** -- Applies during negotiation and refuses the direct stream, HLS and original-file routes that skip it
- **Filename Regex Patterns** -- Legacy matching for [Jellyfin multi-version](https://jellyfin.org/docs/general/server/media/movies/#multiple-versions) setups (inert on Jellyfin 12)
- **Per-User Assignments** -- Assign different policies to different users
- **Web Configuration** -- Easy-to-use admin interface in Jellyfin dashboard
- **Multi-Version Support** -- Seamlessly filter available media versions per user
- **Custom Intros** -- Optional intro video per policy (e.g. a "lite" branding for restricted users)
- **Dangling Symlink Protection** -- Sources whose files don't exist on disk are automatically hidden
- **Detailed Logging** -- Full audit trail of access decisions

## Use Cases

This plugin is designed for Jellyfin's [multi-version naming convention](https://jellyfin.org/docs/general/server/media/movies/#multiple-versions), where multiple quality versions of the same movie live together:

```text
movies/Movie (2021)/Movie (2021) - 2160p.mkv
movies/Movie (2021)/Movie (2021) - 1080p.mkv
movies/Movie (2021)/Movie (2021) - 720p.mkv
```

| Scenario | Solution |
|----------|----------|
| **Bandwidth Management** | Restrict remote users to lower-bitrate versions |
| **Tiered Access** | Premium users get 4K, standard users get 1080p |
| **Device Optimization** | Mobile users automatically get mobile-optimized versions |

## Installation

### Method 1: Plugin Repository (Recommended)

Add this repository to your Jellyfin instance for automatic updates:

1. Go to **Dashboard > Plugins > Repositories**
2. Click **Add** and enter:
   - **Name**: `Quality Gate`
   - **URL**: `https://geiserx.github.io/quality-gate/manifest.json`
3. Go to **Catalog** and install **Quality Gate**
4. Restart Jellyfin

### Method 2: Manual Installation

<details>
<summary><b>Docker</b></summary>

```bash
VERSION="3.2.0.0"
curl -L -o QualityGate.zip \
  "https://github.com/GeiserX/quality-gate/releases/download/v${VERSION}/quality-gate_${VERSION}.zip"

unzip QualityGate.zip -d /path/to/jellyfin/plugins/QualityGate/
docker restart jellyfin
```

Or add to your `docker-compose.yml`:
```yaml
volumes:
  - ./plugins/QualityGate:/config/plugins/QualityGate
```

</details>

<details>
<summary><b>Linux (Native)</b></summary>

```bash
VERSION="3.2.0.0"
curl -L -o QualityGate.zip \
  "https://github.com/GeiserX/quality-gate/releases/download/v${VERSION}/quality-gate_${VERSION}.zip"

sudo unzip QualityGate.zip -d /var/lib/jellyfin/plugins/QualityGate/
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/QualityGate/
sudo systemctl restart jellyfin
```

</details>

<details>
<summary><b>Windows</b></summary>

1. Download the [latest release](https://github.com/GeiserX/quality-gate/releases/latest)
2. Extract to `%LOCALAPPDATA%\jellyfin\plugins\QualityGate\`
3. Restart Jellyfin from Services or the tray icon

</details>

<details>
<summary><b>macOS</b></summary>

```bash
VERSION="3.2.0.0"
curl -L -o QualityGate.zip \
  "https://github.com/GeiserX/quality-gate/releases/download/v${VERSION}/quality-gate_${VERSION}.zip"

unzip QualityGate.zip -d ~/.local/share/jellyfin/plugins/QualityGate/
```

</details>

## Configuration

Navigate to **Dashboard > Quality Gate** to configure the plugin.

### Step 1: Create Policies

Policies define which filename patterns are allowed or blocked. Click **"Add Policy"** to create one.

| Field | Description |
|-------|-------------|
| **Policy Name** | A descriptive name (e.g., "720p Only", "No 4K") |
| **Maximum Resolution** | The tallest video a user under this policy may be served. Measured against the media's actual height, not its filename. `No limit` (the default) disables the cap. |
| **Allowed Filename Patterns** | Regex patterns matched against the filename. Files must match at least one pattern. |
| **Blocked Filename Patterns** | Regex patterns matched against the filename. Matching files are always blocked. |
| **Custom Intro Video** | Optional intro video for users under this policy. Disable the built-in "Local Intros" plugin if you only want Quality Gate intros. |
| **Enabled** | Toggle policy on/off |

### Step 2: Set Default Policy

Choose a policy from the **Default Policy** dropdown. This applies to ALL users who don't have a specific override.

- Select **(No default -- Full Access)** to allow unrestricted access by default
- Select a policy to restrict all users by default

### Step 3: Configure User Access

The **User Access** table shows all Jellyfin users and their current policy:

- **Use Default** -- inherits the default policy
- **Full Access** -- no restrictions
- Any named policy -- applies that policy's rules

If an override or the default policy points to a deleted or disabled policy, the dropdown shows **DENIED** until you choose a replacement (fail-closed). This applies to both per-user overrides and the default policy.

### Policy Logic

Evaluation order:

1. **Blocked Filename Patterns**: If filename matches any blocked regex -- **BLOCKED**
2. **Allowed Filename Patterns**: If defined and filename doesn't match any -- **BLOCKED**
3. **File existence**: If the file doesn't exist on disk (dangling symlink) -- **BLOCKED**
4. Otherwise -- **ALLOWED**

| Allowed Pattern | Blocked Pattern | Filename | Result |
|-----------------|-----------------|----------|--------|
| `- 720p` | -- | `Movie (2021) - 720p.mkv` | Allowed |
| `- 720p` | -- | `Movie (2021) - 2160p.mkv` | Blocked |
| (empty) | `- 2160p\|- 4K` | `Movie (2021) - 1080p.mkv` | Allowed |
| (empty) | `- 2160p\|- 4K` | `Movie (2021) - 2160p.mkv` | Blocked |

> **Tip**: Patterns are case-insensitive regex with a 1-second timeout to prevent ReDoS. Jellyfin also supports bracketed labels (e.g. `Movie (2021) - [1080p].mkv`), so use `\[?1080p\]?` to match both formats.

---

## Examples

### Restrict to 720p Only

```text
Policy Name: 720p Only
Allowed Filename Patterns:
  - 720p
```

Only files with `- 720p` in the filename are visible.

### Block 4K Content

```text
Policy Name: No 4K
Blocked Filename Patterns:
  - 2160p
  - 4K
```

Everything is visible except 4K versions.

### Standard Quality (1080p max)

```text
Policy Name: Standard
Blocked Filename Patterns:
  - 2160p
  - 4K
  - Remux
```

### Tiered Access

1. Create **"Standard"** policy (block 4K as above)
2. Set **Default Policy** to "Standard"
3. Add **Full Access** overrides for premium users

---

## How It Works

The resolution cap is an ASP.NET Core MVC filter registered through `PostConfigure<MvcOptions>`, so it is still on the filter collection after Jellyfin's own MVC setup has run. It works in two phases, because there are two ways to get video out of Jellyfin and covering only one leaves the other open.

1. **Negotiation** (`POST /Items/{id}/PlaybackInfo`): before model binding, the filter adds a required `Height <= cap` video condition to the DeviceProfile in the request body. Jellyfin's `StreamBuilder` turns a failed Height condition into `VideoResolutionNotSupported`, which rules out direct play, and maps the same condition onto the transcode's own `MaxHeight`. On the way back out, the filter checks the response itself: an over-cap source is dropped when a source within the cap exists, so the lower-resolution version of the item is offered instead; when every source is over the cap they are kept but marked transcode-only.

2. **Direct delivery**: `GET /Videos/{id}/stream`, `stream.{container}`, `master.m3u8`, `main.m3u8`, `live.m3u8`, `hls1` segments, `/Audio/{id}/stream` and `/Items/{id}/File` and `/Download` are separate endpoints a client can call without negotiating at all — and the GET form of `PlaybackInfo` applies no limits. The filter answers 403 when the item is over the cap and the request asks for the bytes as they are, or for a transcode not held to the cap. A properly negotiated request carries `MaxHeight` at or below the cap and passes untouched. The legacy `params` query blob is parsed too, because Jellyfin applies its positional fields *inside* the action and they would otherwise overwrite the static flag and the max height after the filter had looked.

3. **Unknown heights**: an item with no probed height is allowed and logged as a warning naming the item. A null height means the item was never probed, which cannot be told apart from a plugin defect. Negotiation still holds those items to the cap, because the injected Height condition is marked required and an unknown value fails a required condition.

4. **Failing open**: anything the filter cannot evaluate is logged and allowed. A defect here must never take playback away from everyone.

One route is deliberately not covered: the legacy HLS segment route `/Videos/{itemId}/hls/{playlistId}/{segmentId}.{container}`. Its `itemId` is declared but never read — the file is found purely by `segmentId`, an MD5 of media path, user agent, device id and play session id — so a request cannot be mapped back to an item. It can only return segments already sitting in the transcode folder, which for a restricted user are segments this filter already capped.

### Library Setup

All quality versions must be in the **same Jellyfin library** using Jellyfin's [multi-version naming](https://jellyfin.org/docs/general/server/media/movies/#multiple-versions). Each version needs a ` - label` suffix (space, hyphen, space, label):

```text
movies/
  Movie (2021)/
    Movie (2021) - 2160p.mkv
    Movie (2021) - 1080p.mkv
    Movie (2021) - 720p.mkv
```

Jellyfin merges these into a single item with multiple MediaSources. The plugin then filters which sources each user can see.

## Building from Source

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

### Build

```bash
git clone https://github.com/GeiserX/quality-gate.git
cd quality-gate/Jellyfin.Plugin.QualityGate
dotnet build -c Release
```

The compiled plugin will be in `bin/Release/net10.0/`.

## Security

- This plugin handles access control -- review your policies carefully
- Only administrators can configure policies
- See [SECURITY.md](SECURITY.md) for vulnerability reporting

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## Other Jellyfin Projects by GeiserX

- [smart-covers](https://github.com/GeiserX/smart-covers) -- Cover extraction for books, audiobooks, comics, magazines, and music libraries with online fallback
- [whisper-subs](https://github.com/GeiserX/whisper-subs) -- Automatically generates subtitles using local AI models powered by Whisper
- [jellyfin-encoder](https://github.com/GeiserX/jellyfin-encoder) -- Automatic 720p HEVC/AV1 transcoding service with optional symlink creation for Jellyfin multi-version support
- [jellyfin-telegram-channel-sync](https://github.com/GeiserX/jellyfin-telegram-channel-sync) -- Sync Jellyfin access with Telegram channel membership

## License

This project is licensed under the GPL-3.0 License -- see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [Jellyfin](https://jellyfin.org) -- The Free Software Media System
- The Jellyfin plugin development community

---

<div align="center">

**[Back to Top](#quality-gate)**

</div>
