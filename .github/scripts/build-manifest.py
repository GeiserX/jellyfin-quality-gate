#!/usr/bin/env python3
"""Build the plugin repository manifest from the releases that actually exist.

Why this is not simply read from manifest.json in the repo.

The published manifest used to be the committed copy plus one new entry, which made a bookkeeping
commit load-bearing: if it never landed, the next release rebuilt from a stale base and the
missing version disappeared from the catalogue. That is how 3.3.6.0 and 3.4.0.0 went, and the
recording step has been failing silently ever since, because Actions is not permitted to open pull
requests here and the step is continue-on-error. Three branches were left unmerged before anyone
noticed.

Deriving the manifest from the release list removes the failure entirely. Every release that
exists is in the manifest, whether or not any commit was recorded, and a run that loses a version
is no longer possible.

targetAbi comes from the build.yaml inside each zip rather than being assumed, because it is not
the same for every release: 3.3.6.0 targets 10.11.0.0 and everything from 3.4.0.0 targets
12.0.0.0. The checksum is the md5 of the asset actually served, computed from the bytes rather
than copied from anywhere.

Usage: build-manifest.py <owner/repo> <out.json> [seed.json]
"""
import hashlib
import io
import json
import os
import re
import subprocess
import sys
import urllib.request
import zipfile

REPO = sys.argv[1]
OUT = sys.argv[2]
SEED = sys.argv[3] if len(sys.argv) > 3 else None


def gh(path):
    # One page of 100 rather than --paginate --slurp: this repo has ~40 releases, and a plain
    # request avoids depending on a gh new enough to have --slurp on whatever runner image is
    # current. If the repo ever passes 100 releases the count assertion below catches it.
    out = subprocess.run(["gh", "api", path], capture_output=True, text=True, check=True)
    return json.loads(out.stdout)


def version_tuple(v):
    return tuple(int(x) for x in v.split("."))


# The catalogue has advertised 2.0.2.0 and up for as long as it has existed. Deriving from the
# release list would otherwise resurrect 25 builds for Jellyfin 10.10 and 10.11 that were never
# offered, including ones whose own changelog says the filtering did not work. The floor keeps the
# published set the shape users already see while making it complete within that range.
MIN_VERSION = (2, 0, 2, 0)


# The header fields (name, guid, owner, overview, description) are editorial and belong in the
# repo. Only the versions array is derived.
header = {
    "guid": "9cab70ca-0af3-4d3a-adab-6a0df2496a33",
    "name": "QualityGate",
    "owner": "GeiserX",
    "category": "General",
    "imageUrl": "",
}
if SEED and os.path.exists(SEED):
    seeded = json.load(open(SEED))[0]
    for key in ("guid", "name", "owner", "category", "imageUrl", "overview", "description"):
        if seeded.get(key) is not None:
            header[key] = seeded[key]

releases = gh("repos/%s/releases?per_page=100" % REPO)
if len(releases) >= 100:
    sys.exit("more than 100 releases: this needs pagination before it silently truncates")
versions = []
for rel in releases:
    tag = rel.get("tag_name") or ""
    m = re.fullmatch(r"v(\d+\.\d+\.\d+\.\d+)", tag)
    if not m or rel.get("draft"):
        continue
    version = m.group(1)
    if version_tuple(version) < MIN_VERSION:
        continue
    # Releases up to 2.0.5.0 shipped an unversioned quality-gate.zip. Those assets are still
    # served, so falling back keeps them in the manifest instead of silently dropping four
    # working entries, which would be the same class of loss this script exists to prevent.
    assets = {a.get("name"): a for a in (rel.get("assets") or [])}
    asset = assets.get("quality-gate_%s.zip" % version) or assets.get("quality-gate.zip")
    if not asset:
        print("  skip %s: no plugin zip among %s"
              % (tag, sorted(assets) or "no assets"), file=sys.stderr)
        continue

    with urllib.request.urlopen(asset["browser_download_url"], timeout=180) as r:
        blob = r.read()
    checksum = hashlib.md5(blob).hexdigest()  # noqa: S324 - the manifest format specifies md5

    # targetAbi is per release and must not be assumed.
    target_abi = None
    try:
        with zipfile.ZipFile(io.BytesIO(blob)) as z:
            for name in z.namelist():
                if name.endswith("build.yaml"):
                    text = z.read(name).decode("utf-8", "replace")
                    hit = re.search(r'^targetAbi:\s*"?([\d.]+)"?', text, re.M)
                    if hit:
                        target_abi = hit.group(1)
                    break
    except Exception as exc:
        print("  %s: could not read build.yaml (%s)" % (tag, exc), file=sys.stderr)
    if not target_abi:
        print("  skip %s: no targetAbi in its build.yaml" % tag, file=sys.stderr)
        continue

    versions.append({
        "version": version,
        "changelog": "See https://github.com/%s/releases" % REPO,
        "targetAbi": target_abi,
        "sourceUrl": asset["browser_download_url"],
        "checksum": checksum,
        "timestamp": (rel.get("published_at") or "").replace("+00:00", "Z"),
    })
    print("  %s  abi=%s  md5=%s" % (version, target_abi, checksum), file=sys.stderr)

if not versions:
    sys.exit("refusing to write an empty manifest: no releases with a plugin zip were found")

versions.sort(key=lambda v: version_tuple(v["version"]), reverse=True)
header["versions"] = versions

with open(OUT, "w", encoding="utf-8") as fh:
    json.dump([header], fh, indent=2, ensure_ascii=False)
    fh.write("\n")
print("wrote %s with %d versions (newest %s)"
      % (OUT, len(versions), versions[0]["version"]), file=sys.stderr)
