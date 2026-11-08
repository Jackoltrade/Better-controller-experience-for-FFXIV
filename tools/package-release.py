#!/usr/bin/env python3
"""Package a verified build for a public Dalamud custom repository."""
import argparse
import hashlib
import json
import re
import time
import zipfile
from pathlib import Path
from urllib.parse import quote, unquote, urlsplit, urlunsplit

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = "https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV"
LICENSES = ("LICENSE.txt", "LICENSE-HidLibrary.txt", "LICENSE-FFXIVClientStructs.txt", "THIRD-PARTY-NOTICES.txt")
GUIDES = ("README.md", "README.zh-CN.md", "README.ja.md")
# Defaults emitted by the pinned DalamudPackager, including fields omitted in
# the source manifest. CN localization fields are not part of its model.
STANDARD_DEFAULTS = {"ApplicableVersion": "any", "DalamudApiLevel": 15, "LoadRequiredState": 0,
                     "LoadSync": False, "CanUnloadAsync": False, "LoadPriority": 0, "AcceptsFeedback": True}
LOCALIZED_FIELDS = ("DescriptionLoc", "PunchlineLoc")
GUIDE_LINK = re.compile(r'''(?P<image>!?)\[[^\]\n]*\]\((?P<target><[^>\n]+>|[^\s)]+)(?:\s+(?:"[^"\n]*"|'[^'\n]*'))?\)''')


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def source_fingerprint(directory):
    digest = hashlib.sha256()
    extensions = {".cs", ".csproj", ".json", ".png", ".svg", ".targets", ".props"}
    sources = [path for path in directory.rglob("*") if path.is_file() and path.suffix in extensions
               and not {"bin", "obj"}.intersection(path.relative_to(directory).parts)]
    for path in sorted(sources, key=lambda p: p.relative_to(directory).as_posix()):
        digest.update(path.relative_to(directory).as_posix().encode("utf-8") + b"\0")
        digest.update(path.read_bytes())
        digest.update(b"\0")
    return digest.hexdigest()


def packaged_guide(root, text, tag, included):
    """Keep bundled-file links local; pin other repository links without copying their targets."""
    root = root.resolve()

    def replace(match):
        target = match["target"]
        angled = target.startswith("<")
        destination = target[1:-1] if angled else target
        parts = urlsplit(destination)
        if re.match(r"^[A-Za-z]:", destination) or parts.scheme.lower() == "file":
            raise ValueError("Unsafe guide link: " + destination)
        if parts.scheme or parts.netloc or not parts.path:
            return match[0]
        decoded = unquote(parts.path)
        # A guide cannot reference an absolute/local path or escape the public source tree.
        if "\\" in decoded or Path(decoded).is_absolute():
            raise ValueError("Unsafe guide link: " + destination)
        path = (root / decoded).resolve()
        if not path.is_relative_to(root) or not path.is_file():
            raise ValueError("Guide link must name a file inside the repository: " + destination)
        relative = path.relative_to(root).as_posix()
        if relative in included:
            return match[0]
        if match["image"]:
            base = REPOSITORY.replace("https://github.com/", "https://raw.githubusercontent.com/") + "/" + tag
        else:
            base = REPOSITORY + "/blob/" + tag
        remote = urlsplit(base + "/" + quote(relative, safe="/"))
        url = urlunsplit((remote.scheme, remote.netloc, remote.path, parts.query, parts.fragment))
        replacement = "<" + url + ">" if angled else url
        start, end = match.start("target") - match.start(), match.end("target") - match.start()
        return match[0][:start] + replacement + match[0][end:]

    return GUIDE_LINK.sub(replace, text)


def japanese_manifest_text(path):
    def unique_fields(pairs):
        fields = dict(pairs)
        if len(fields) != len(pairs):
            raise ValueError("Japanese manifest localization contains duplicate fields")
        return fields

    try:
        text = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_fields)
    except (json.JSONDecodeError, UnicodeDecodeError) as error:
        raise ValueError("Invalid Japanese manifest localization") from error
    if (not isinstance(text, dict) or set(text) != {"Description", "Punchline"} or
            any(not isinstance(value, str) or not value.strip() for value in text.values())):
        raise ValueError("Japanese manifest localization must contain exactly Description and Punchline as nonempty strings")
    return text


def write_archive(archive, payload):
    temporary = archive.with_name(archive.name + ".new")
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
            for name, data in sorted(payload.items()):
                entry = zipfile.ZipInfo(name, (2020, 1, 1, 0, 0, 0))
                entry.compress_type = zipfile.ZIP_DEFLATED
                entry.external_attr = 0o100644 << 16
                bundle.writestr(entry, data)
        with zipfile.ZipFile(temporary) as bundle:
            if bundle.testzip() is not None or set(bundle.namelist()) != set(payload):
                raise ValueError("Release archive failed verification")
        temporary.replace(archive)
    finally:
        temporary.unlink(missing_ok=True)


def standard_payload(build, expected, icon):
    """Read the SDK package without extracting it or trusting additional files."""
    directory = build / "DualSenseLighting"
    if (directory / "images/icon.png").read_bytes() != icon:
        raise ValueError("Standard SDK icon differs from the verified icon")
    try:
        with zipfile.ZipFile(directory / "latest.zip") as bundle:
            entries = bundle.infolist()
            if len(entries) != len(expected) or {entry.filename for entry in entries} != set(expected):
                raise ValueError("Standard SDK archive must contain only the plugin, manifest and license notices")
            payload = {}
            for entry in entries:
                # Checking length before reading also bounds decompression. Reject
                # symbolic links even when their names match the allowlist.
                mode = entry.external_attr >> 16 & 0o170000
                if mode not in (0, 0o100000) or entry.file_size != len(expected[entry.filename]):
                    raise ValueError("Standard SDK archive has an invalid entry: " + entry.filename)
                data = bundle.read(entry)
                if data != expected[entry.filename]:
                    raise ValueError("Standard SDK archive differs from the verified payload: " + entry.filename)
                payload[entry.filename] = data
            return payload
    except zipfile.BadZipFile as error:
        raise ValueError("Standard SDK archive is invalid") from error


def package(root, build, output, tag=None):
    japanese = japanese_manifest_text(root / "src/DualSenseLighting/Manifest.ja.json")
    source = json.loads((root / "src/DualSenseLighting/DualSenseLighting.json").read_text(encoding="utf-8"))
    # The flat manifest serves Dev Tools and may contain local metadata. Public
    # packages always start from the untouched SDK output, verified against its ZIP.
    manifest_bytes = (build / "DualSenseLighting/DualSenseLighting.json").read_bytes()
    manifest = json.loads(manifest_bytes)
    proof = json.loads((build / "build-verification.json").read_text(encoding="utf-8"))
    version = manifest["AssemblyVersion"]
    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version):
        raise ValueError("Invalid assembly version")
    expected_manifest = dict(STANDARD_DEFAULTS, **{key: value for key, value in source.items()
                                                  if key not in LOCALIZED_FIELDS and value is not None})
    if json.dumps(manifest, sort_keys=True) != json.dumps(expected_manifest, sort_keys=True):
        raise ValueError("Published manifest is stale; rebuild before packaging")
    if proof.get("SourceSha256") != source_fingerprint(root / "src/DualSenseLighting"):
        raise ValueError("Source changed after build verification; rebuild before packaging")
    for key in ("InternalName", "AssemblyVersion", "DalamudApiLevel"):
        if proof.get(key) != manifest[key]:
            raise ValueError("Build verification differs from manifest: " + key)
    if manifest["InternalName"] != "DualSenseLighting" or proof.get("Architecture") != "x64" or proof.get("Optimized") is not True:
        raise ValueError("A verified, optimized x64 build is required")
    dll = (build / "DualSenseLighting.dll").read_bytes()
    if proof.get("DllSha256") != sha256(dll):
        raise ValueError("DLL changed after build verification")
    expected_tag = "v" + version.removesuffix(".0")
    if tag is not None and tag != expected_tag:
        raise ValueError(f"Release tag must match the assembly: {expected_tag}")
    tag = expected_tag
    icon = (build / "images/icon.png").read_bytes()
    if icon != (root / "src/DualSenseLighting/Assets/icon.png").read_bytes():
        raise ValueError("Published icon is stale; rebuild before packaging")
    expected_payload = {"DualSenseLighting.dll": dll, "DualSenseLighting.json": manifest_bytes}
    for name in LICENSES:
        expected_payload[name] = (root / name).read_bytes()
    payload = standard_payload(build, expected_payload, icon)
    # Retain CN metadata that the upstream manifest model does not serialize.
    manifest.update({key: source[key] for key in LOCALIZED_FIELDS if key in source})
    payload["DualSenseLighting.json"] = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    payload["images/icon.png"] = icon
    included = set(payload) | set(GUIDES)
    for name in GUIDES:
        payload[name] = packaged_guide(root, (root / name).read_text(encoding="utf-8"), tag, included).encode("utf-8")
    localized_manifest = dict(manifest, **japanese, DescriptionLoc=japanese["Description"], PunchlineLoc=japanese["Punchline"])
    localized_payload = dict(payload)
    localized_payload["DualSenseLighting.json"] = (json.dumps(localized_manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    # Both variants use this same allowlisted payload. No diagnostics, local
    # settings, compiler/runtime libraries or localization source enter either ZIP.
    output.mkdir(parents=True, exist_ok=True)
    updated = int(time.time())
    checksums = []
    localized_packages = {}
    for filename, feed, metadata, files in [
        ("latest.zip", "pluginmaster.json", manifest, payload),
        ("latest-ja.zip", "pluginmaster-ja.json", localized_manifest, localized_payload),
    ]:
        archive = output / filename
        write_archive(archive, files)
        download = f"{REPOSITORY}/releases/download/{tag}/{filename}"
        store = dict(metadata)
        store.pop("TestingAssemblyVersion", None)
        store.pop("TestingDalamudApiLevel", None)
        store.update(AssemblyVersion=version, IsTestingExclusive=False, IsHide=False,
                     DownloadLinkInstall=download, DownloadLinkUpdate=download, DownloadLinkTesting=download,
                     LastUpdate=updated)
        (output / feed).write_text(json.dumps([store], ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        checksums.append(f"{sha256(archive.read_bytes())}  {filename}\n")
        if filename == "latest-ja.zip":
            localized_packages["ja"] = {"Archive": filename, "InstallerFeed": feed, "DownloadUrl": download, "Files": sorted(files)}
    download = f"{REPOSITORY}/releases/download/{tag}/latest.zip"
    (output / "SHA256SUMS.txt").write_text("".join(checksums), encoding="utf-8")
    (output / "release-summary.json").write_text(json.dumps({"Tag": tag, "AssemblyVersion": version,
        "InternalName": manifest["InternalName"], "Channel": "release", "Archive": "latest.zip",
        "DownloadUrl": download, "Files": sorted(payload), "LocalizedPackages": localized_packages}, indent=2) + "\n", encoding="utf-8")
    return output / "latest.zip"


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-dir", type=Path, default=ROOT / "artifacts/plugin")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/release")
    parser.add_argument("--tag", help="Optional release tag; must agree with the compiled manifest")
    args = parser.parse_args()
    print("Public package:", package(ROOT, args.build_dir, args.output, args.tag))
