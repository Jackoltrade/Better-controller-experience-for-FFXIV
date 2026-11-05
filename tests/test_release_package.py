"""Verify public release boundaries with synthetic payloads; no network or controller I/O."""
import importlib.util
import json
import tempfile
import unittest
import warnings
import zipfile
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("package_release", Path(__file__).resolve().parents[1] / "tools/package-release.py")
PACKAGER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PACKAGER)


class ReleasePackageChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.build = self.root / "build"
        self.output = self.root / "release"
        self.build.mkdir()
        source = self.root / "src/DualSenseLighting"
        source.mkdir(parents=True)
        self.manifest = {"InternalName": "DualSenseLighting", "AssemblyVersion": "0.13.0.0",
                         "DalamudApiLevel": 15, "Name": "Better controller experience for FFXIV",
                         "Description": "English controller description", "Punchline": "English controller summary",
                         "DescriptionLoc": "中文手柄介绍", "PunchlineLoc": "中文简介",
                         "IconUrl": "https://example.com/icon.png", "ApplicableVersion": "any",
                         "LoadRequiredState": 0, "LoadSync": False, "CanUnloadAsync": False}
        (source / "DualSenseLighting.json").write_text(json.dumps(self.manifest), encoding="utf-8")
        self.generated = dict(LoadPriority=0, AcceptsFeedback=True,
                              **{key: value for key, value in self.manifest.items()
                                 if key not in {"DescriptionLoc", "PunchlineLoc"}})
        (self.build / "DualSenseLighting.json").write_text(json.dumps(self.generated), encoding="utf-8")
        # The real build's PE/optimization verification is performed by VerifyAssembly.
        # Here only archive selection, proof checks and store-entry construction are tested.
        dll = b"synthetic-verified-payload"
        (self.build / "DualSenseLighting.dll").write_bytes(dll)
        (source / "Assets").mkdir()
        (self.build / "images").mkdir()
        self.icon = b"synthetic-icon-payload"
        (source / "Assets/icon.png").write_bytes(self.icon)
        (self.build / "images/icon.png").write_bytes(self.icon)
        self.japanese = {"Description": "コントローラーの日本語の説明。", "Punchline": "日本語の概要。"}
        self.japanese_path = source / "Manifest.ja.json"
        self.japanese_path.write_text(json.dumps(self.japanese, ensure_ascii=False), encoding="utf-8")
        proof = dict(self.manifest, Architecture="x64", Optimized=True, DllSha256=PACKAGER.sha256(dll))
        proof["SourceSha256"] = PACKAGER.source_fingerprint(source)
        (self.build / "build-verification.json").write_text(json.dumps(proof), encoding="utf-8")
        for name in PACKAGER.LICENSES + PACKAGER.GUIDES:
            (self.root / name).write_text("Fixture for " + name, encoding="utf-8")
        self.standard = self.build / "DualSenseLighting"
        self.standard.mkdir()
        (self.standard / "images").mkdir()
        self.write_standard()

    def write_standard(self, entries=None):
        """Fixture for Packager's six-file ZIP and external manifest/icon."""
        (self.standard / "DualSenseLighting.json").write_bytes((self.build / "DualSenseLighting.json").read_bytes())
        (self.standard / "images/icon.png").write_bytes((self.build / "images/icon.png").read_bytes())
        if entries is None:
            entries = {"DualSenseLighting.dll": (self.build / "DualSenseLighting.dll").read_bytes(),
                       "DualSenseLighting.json": (self.build / "DualSenseLighting.json").read_bytes()}
            entries.update({name: (self.root / name).read_bytes() for name in PACKAGER.LICENSES})
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            with zipfile.ZipFile(self.standard / "latest.zip", "w") as bundle:
                for name, data in entries.items() if isinstance(entries, dict) else entries:
                    bundle.writestr(name, data)

    def assert_candidate_unchanged(self, previous):
        self.assertEqual(previous, {path.name: path.read_bytes() for path in self.output.iterdir()})

    def test_testing_only_store_and_exact_payload(self):
        (self.build / "sfx-trace.json").write_text("private diagnostic")
        (self.build / "Dalamud.dll").write_text("runtime dependency")
        (self.build / "private").mkdir()
        (self.build / "private/config.json").write_text("private configuration")
        with zipfile.ZipFile(self.build / "untrusted.zip", "w") as untrusted:
            untrusted.writestr("../../outside.txt", "must not enter the release")
        archive = PACKAGER.package(self.root, self.build, self.output, "v0.13.0")
        for path in [archive, self.output / "latest-ja.zip"]:
            with zipfile.ZipFile(path) as bundle:
                self.assertEqual(set(bundle.namelist()), set(PACKAGER.LICENSES + PACKAGER.GUIDES) |
                                 {"DualSenseLighting.dll", "DualSenseLighting.json", "images/icon.png"})
                self.assertEqual(bundle.read("images/icon.png"), self.icon)
                self.assertIsNone(bundle.testzip())
        store, = json.loads((self.output / "pluginmaster.json").read_text())
        self.assertTrue(store["IsTestingExclusive"])
        self.assertEqual(store["AssemblyVersion"], "0.0.0.0")
        self.assertEqual(store["TestingAssemblyVersion"], "0.13.0.0")
        self.assertEqual(store["TestingDalamudApiLevel"], 15)
        self.assertEqual(store["DownloadLinkTesting"], store["DownloadLinkInstall"])
        self.assertTrue(store["DownloadLinkTesting"].endswith("/v0.13.0/latest.zip"))
        self.assertEqual(store["IconUrl"], self.manifest["IconUrl"])
        self.assertEqual(store["Description"], self.manifest["Description"])
        self.assertEqual(store["DescriptionLoc"], self.manifest["DescriptionLoc"])

    def test_standard_sdk_archive_is_required_before_publication(self):
        (self.standard / "latest.zip").unlink()
        with self.assertRaises(FileNotFoundError):
            PACKAGER.package(self.root, self.build, self.output)
        self.assertFalse(self.output.exists(), "Missing SDK package created a public candidate")

    def test_japanese_archive_and_feed_keep_identity_and_localize_installed_metadata(self):
        generated_bytes = (self.build / "DualSenseLighting.json").read_bytes()
        standard_bytes = (self.standard / "latest.zip").read_bytes()
        source_bytes = (self.root / "src/DualSenseLighting/DualSenseLighting.json").read_bytes()
        archive = PACKAGER.package(self.root, self.build, self.output)
        self.assertEqual((self.build / "DualSenseLighting.json").read_bytes(), generated_bytes)
        self.assertEqual((self.standard / "DualSenseLighting.json").read_bytes(), generated_bytes)
        self.assertEqual((self.standard / "latest.zip").read_bytes(), standard_bytes)
        self.assertEqual((self.root / "src/DualSenseLighting/DualSenseLighting.json").read_bytes(), source_bytes)
        self.assertNotIn("DescriptionLoc", json.loads(generated_bytes), "Fixture must model the real Packager field loss")
        with zipfile.ZipFile(archive) as original, zipfile.ZipFile(self.output / "latest-ja.zip") as japanese:
            self.assertEqual(set(original.namelist()), set(japanese.namelist()))
            self.assertEqual(len(japanese.namelist()), 10)
            self.assertIsNone(japanese.testzip())
            for name in original.namelist():
                if name != "DualSenseLighting.json":
                    self.assertEqual(original.read(name), japanese.read(name), name)
            installed = json.loads(japanese.read("DualSenseLighting.json"))
            default = json.loads(original.read("DualSenseLighting.json"))
        self.assertEqual(default, dict(self.manifest, LoadPriority=0, AcceptsFeedback=True))
        self.assertEqual(default["LoadPriority"], 0)
        self.assertIs(default["AcceptsFeedback"], True)
        localized_keys = {"Description", "Punchline", "DescriptionLoc", "PunchlineLoc"}
        self.assertEqual({k: v for k, v in installed.items() if k not in localized_keys},
                         {k: v for k, v in default.items() if k not in localized_keys})
        for field in ["Description", "Punchline"]:
            self.assertEqual(installed[field], self.japanese[field])
            self.assertEqual(installed[field + "Loc"], self.japanese[field])

        default_feed, = json.loads((self.output / "pluginmaster.json").read_text())
        feed, = json.loads((self.output / "pluginmaster-ja.json").read_text())
        for field in ["Description", "Punchline", "DescriptionLoc", "PunchlineLoc"]:
            self.assertEqual(feed[field], installed[field], field)
        links = {"DownloadLinkInstall", "DownloadLinkUpdate", "DownloadLinkTesting"}
        for field in links:
            self.assertEqual(feed[field], PACKAGER.REPOSITORY + "/releases/download/v0.13.0/latest-ja.zip")
        self.assertEqual({k: v for k, v in feed.items() if k not in localized_keys | links},
                         {k: v for k, v in default_feed.items() if k not in localized_keys | links})
        self.assertEqual(feed["InternalName"], self.manifest["InternalName"])
        self.assertEqual(feed["Name"], self.manifest["Name"])
        self.assertEqual(feed["AssemblyVersion"], "0.0.0.0")
        self.assertEqual(feed["TestingAssemblyVersion"], installed["AssemblyVersion"])

        checksums = dict(line.split("  ", 1)[::-1] for line in (self.output / "SHA256SUMS.txt").read_text().splitlines())
        self.assertEqual(set(checksums), {"latest.zip", "latest-ja.zip"})
        for name, checksum in checksums.items():
            self.assertEqual(checksum, PACKAGER.sha256((self.output / name).read_bytes()))
        summary = json.loads((self.output / "release-summary.json").read_text())
        self.assertEqual(summary["Archive"], "latest.zip")
        self.assertEqual(summary["DownloadUrl"], default_feed["DownloadLinkTesting"])
        self.assertEqual(summary["LocalizedPackages"], {"ja": {
            "Archive": "latest-ja.zip", "InstallerFeed": "pluginmaster-ja.json",
            "DownloadUrl": feed["DownloadLinkTesting"], "Files": summary["Files"]}})
        self.assertFalse(any(path.suffix == ".new" for path in self.output.iterdir()))

    def test_stale_icon_is_rejected(self):
        (self.build / "images/icon.png").write_bytes(b"old icon")
        with self.assertRaisesRegex(ValueError, "icon is stale"):
            PACKAGER.package(self.root, self.build, self.output)
        self.assertFalse(self.output.exists(), "Rejected icon created a partial public candidate")

    def test_standard_archive_cannot_replace_verified_payload(self):
        PACKAGER.package(self.root, self.build, self.output)
        previous = {path.name: path.read_bytes() for path in self.output.iterdir()}
        with zipfile.ZipFile(self.standard / "latest.zip") as bundle:
            original = {entry.filename: bundle.read(entry) for entry in bundle.infolist()}
        for name in original:
            with self.subTest(entry=name):
                self.write_standard(dict(original, **{name: b"changed standard payload"}))
                with self.assertRaisesRegex(ValueError, "Standard SDK archive"):
                    PACKAGER.package(self.root, self.build, self.output)
                self.assert_candidate_unchanged(previous)

    def test_standard_archive_rejects_extra_missing_duplicate_and_unsafe_entries(self):
        PACKAGER.package(self.root, self.build, self.output)
        previous = {path.name: path.read_bytes() for path in self.output.iterdir()}
        with zipfile.ZipFile(self.standard / "latest.zip") as bundle:
            original = {entry.filename: bundle.read(entry) for entry in bundle.infolist()}
        cases = [dict(original, **{name: b"not distributable"}) for name in
                 ["Dalamud.dll", "DualSenseLighting.pdb", "build-verification.json", "images/icon.png",
                  "../outside.txt", "C:/outside.txt", "private/config.json", "images\\icon.png"]]
        cases.extend([dict(list(original.items())[1:]),
                      list(original.items()) + [("DualSenseLighting.dll", original["DualSenseLighting.dll"])]])
        for entries in cases:
            with self.subTest(names=list(entries)):
                self.write_standard(entries)
                with self.assertRaisesRegex(ValueError, "Standard SDK archive"):
                    PACKAGER.package(self.root, self.build, self.output)
                self.assert_candidate_unchanged(previous)

    def test_standard_archive_rejects_symlink_even_for_an_allowlisted_name(self):
        entries = {"DualSenseLighting.dll": (self.build / "DualSenseLighting.dll").read_bytes(),
                   "DualSenseLighting.json": (self.build / "DualSenseLighting.json").read_bytes()}
        entries.update({name: (self.root / name).read_bytes() for name in PACKAGER.LICENSES})
        with zipfile.ZipFile(self.standard / "latest.zip", "w") as bundle:
            for name, data in entries.items():
                entry = zipfile.ZipInfo(name)
                entry.external_attr = (0o120777 if name == "DualSenseLighting.dll" else 0o100644) << 16
                bundle.writestr(entry, data)
        with self.assertRaisesRegex(ValueError, "Standard SDK archive has an invalid entry"):
            PACKAGER.package(self.root, self.build, self.output)
        self.assertFalse(self.output.exists())

    def test_standard_archive_rejects_corrupt_zip_before_publication(self):
        (self.standard / "latest.zip").write_bytes(b"not a zip")
        with self.assertRaisesRegex(ValueError, "Standard SDK archive is invalid"):
            PACKAGER.package(self.root, self.build, self.output)
        self.assertFalse(self.output.exists())

    def test_standard_companion_manifest_and_icon_must_match_verified_files(self):
        PACKAGER.package(self.root, self.build, self.output)
        previous = {path.name: path.read_bytes() for path in self.output.iterdir()}
        for name in ["DualSenseLighting.json", "images/icon.png"]:
            self.write_standard()
            (self.standard / name).write_bytes(b"stale standard companion")
            with self.assertRaisesRegex(ValueError, "Standard SDK (manifest|icon) differs"):
                PACKAGER.package(self.root, self.build, self.output)
            self.assert_candidate_unchanged(previous)

    def test_generated_standard_defaults_are_checked(self):
        for key, value in [("LoadPriority", 3), ("AcceptsFeedback", False), ("AcceptsFeedback", 1)]:
            with self.subTest(field=key, value=value):
                (self.build / "DualSenseLighting.json").write_text(json.dumps(dict(self.generated, **{key: value})))
                self.write_standard()
                with self.assertRaisesRegex(ValueError, "manifest is stale"):
                    PACKAGER.package(self.root, self.build, self.output)
                self.assertFalse(self.output.exists())

    def test_packaging_targets_and_props_are_part_of_the_build_fingerprint(self):
        source = self.root / "src/DualSenseLighting"
        for extension in [".targets", ".props"]:
            with self.subTest(extension=extension):
                path = source / ("Packaging" + extension)
                path.write_text("<Project />", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "Source changed"):
                    PACKAGER.package(self.root, self.build, self.output)
                self.assertFalse(self.output.exists())
                path.unlink()

    def test_zip_is_repeatable_for_same_payload(self):
        path = PACKAGER.package(self.root, self.build, self.output)
        first = path.read_bytes()
        japanese = (self.output / "latest-ja.zip").read_bytes()
        PACKAGER.package(self.root, self.build, self.output)
        self.assertEqual(path.read_bytes(), first)
        self.assertEqual((self.output / "latest-ja.zip").read_bytes(), japanese)

    def test_missing_japanese_localization_preserves_previous_candidate(self):
        PACKAGER.package(self.root, self.build, self.output)
        previous = {path.name: path.read_bytes() for path in self.output.iterdir()}
        self.japanese_path.unlink()
        for output in [self.output, self.root / "missing-localization-output"]:
            with self.assertRaises(FileNotFoundError):
                PACKAGER.package(self.root, self.build, output)
        self.assertFalse((self.root / "missing-localization-output").exists())
        self.assertEqual(previous, {path.name: path.read_bytes() for path in self.output.iterdir()})

    def test_invalid_japanese_localization_fails_before_any_output_mutation(self):
        PACKAGER.package(self.root, self.build, self.output)
        previous = {path.name: path.read_bytes() for path in self.output.iterdir()}
        invalid = ["not json", "[]", "null", "{}", '{"Description":"説明"}',
                   json.dumps(dict(self.japanese, Name="another plugin")),
                   '{"Description":"先","Description":"後","Punchline":"説明"}']
        for field in ["Description", "Punchline"]:
            invalid.extend(json.dumps(dict(self.japanese, **{field: value})) for value in ["", " \t\n", None, 1, True, {}, []])
        for number, text in enumerate(invalid):
            with self.subTest(localization=text):
                self.japanese_path.write_text(text, encoding="utf-8")
                fresh = self.root / ("invalid-localization-output-" + str(number))
                for output in [self.output, fresh]:
                    with self.assertRaisesRegex(ValueError, "localization"):
                        PACKAGER.package(self.root, self.build, output)
                self.assertFalse(fresh.exists())
                self.assertEqual(previous, {path.name: path.read_bytes() for path in self.output.iterdir()})

    def test_changed_japanese_localization_requires_rebuild(self):
        PACKAGER.package(self.root, self.build, self.output)
        previous = {path.name: path.read_bytes() for path in self.output.iterdir()}
        self.japanese_path.write_text(json.dumps(dict(self.japanese, Description="変更された説明")), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Source changed"):
            PACKAGER.package(self.root, self.build, self.output)
        self.assertEqual(previous, {path.name: path.read_bytes() for path in self.output.iterdir()})

    def test_invalid_localization_encoding_is_rejected_before_publication(self):
        self.japanese_path.write_bytes(b"\xff\xfe\x00")
        with self.assertRaisesRegex(ValueError, "localization"):
            PACKAGER.package(self.root, self.build, self.output)
        self.assertFalse(self.output.exists())

    def test_packaged_guides_keep_local_links_and_pin_repository_targets(self):
        (self.root / "docs").mkdir()
        (self.root / "docs/QA report.md").write_text("Public QA guide", encoding="utf-8")
        (self.root / "docs/说明.md").write_text("Public translated guide", encoding="utf-8")
        (self.root / "assets").mkdir()
        (self.root / "assets/controller.png").write_bytes(b"public-image-fixture")
        guide = (
            '[中文](README.zh-CN.md#setup) · [License](./LICENSE.txt) · [Setup](#setup)\n'
            '[QA](<docs/QA report.md#checks> "Read the QA guide")\n'
            '[Details](docs/%E8%AF%B4%E6%98%8E.md)\n'
            '![Controller](assets/controller.png)\n'
            '[Website](https://example.com/guide#setup)\n'
        )
        (self.root / "README.md").write_text(guide, encoding="utf-8")
        archive = PACKAGER.package(self.root, self.build, self.output, "v0.13.0")
        with zipfile.ZipFile(archive) as bundle:
            packaged = bundle.read("README.md").decode("utf-8")
            self.assertEqual(set(bundle.namelist()), set(PACKAGER.LICENSES + PACKAGER.GUIDES) |
                             {"DualSenseLighting.dll", "DualSenseLighting.json", "images/icon.png"})
        self.assertIn('[中文](README.zh-CN.md#setup)', packaged)
        self.assertIn('[License](./LICENSE.txt)', packaged)
        self.assertIn('[Setup](#setup)', packaged)
        self.assertIn(f'[QA](<{PACKAGER.REPOSITORY}/blob/v0.13.0/docs/QA%20report.md#checks> "Read the QA guide")', packaged)
        self.assertIn(f'[Details]({PACKAGER.REPOSITORY}/blob/v0.13.0/docs/%E8%AF%B4%E6%98%8E.md)', packaged)
        self.assertIn('![Controller](https://raw.githubusercontent.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-/v0.13.0/assets/controller.png)', packaged)
        self.assertIn('[Website](https://example.com/guide#setup)', packaged)
        self.assertEqual((self.root / "README.md").read_text(encoding="utf-8"), guide,
                         "Packaging must not rewrite the repository's guide")

    def test_unsafe_or_missing_guide_targets_fail_before_publication(self):
        # A complete previous candidate must survive rejected guide paths.
        archive = PACKAGER.package(self.root, self.build, self.output)
        previous = {path.name: path.read_bytes() for path in self.output.iterdir()}
        with tempfile.TemporaryDirectory() as outside:
            secret = Path(outside) / "private.txt"
            secret.write_text("private fixture, never read into the package", encoding="utf-8")
            relative = "../" + Path(outside).name + "/private.txt"
            for target in [relative, relative.replace("../", "%2e%2e/"), str(secret),
                           "C:/private.txt", "file:///private.txt", "..\\private.txt", "docs/missing.md"]:
                with self.subTest(target=target):
                    (self.root / "README.md").write_text(f"[Guide](<{target}>)", encoding="utf-8")
                    with self.assertRaisesRegex(ValueError, "guide link|Guide link"):
                        PACKAGER.package(self.root, self.build, self.output)
                    self.assertEqual(previous, {path.name: path.read_bytes() for path in self.output.iterdir()})
        self.assertTrue(archive.is_file())

    def test_proof_metadata_changes_are_rejected(self):
        proof_path = self.build / "build-verification.json"
        original = json.loads(proof_path.read_text())
        for key, value in [("InternalName", "AnotherPlugin"), ("AssemblyVersion", "0.13.1.0"),
                           ("DalamudApiLevel", 16), ("Architecture", "arm64"),
                           ("SourceSha256", "0" * 64), ("DllSha256", "0" * 64)]:
            with self.subTest(field=key):
                proof_path.write_text(json.dumps(dict(original, **{key: value})))
                with self.assertRaises(ValueError):
                    PACKAGER.package(self.root, self.build, self.output)
        self.assertFalse(self.output.exists(), "Rejected proof created a partial public candidate")

    def test_release_tag_is_not_an_unchecked_workflow_argument(self):
        for tag in ["--help", "v0.13.0;echo unsafe", "v0.13.0\nextra", "v0.13.0/../other"]:
            with self.subTest(tag=tag):
                with self.assertRaisesRegex(ValueError, "tag must match"):
                    PACKAGER.package(self.root, self.build, self.output, tag)
        self.assertFalse(self.output.exists(), "Malformed tag created a partial public candidate")

    def test_store_downloads_ignore_manifest_supplied_urls(self):
        manifest = dict(self.manifest, DownloadLinkInstall="file:///private.zip",
                        DownloadLinkUpdate="https://example.invalid/other.zip", DownloadLinkTesting="javascript:invalid")
        source = self.root / "src/DualSenseLighting"
        (source / "DualSenseLighting.json").write_text(json.dumps(manifest), encoding="utf-8")
        generated = dict(LoadPriority=0, AcceptsFeedback=True,
                         **{key: value for key, value in manifest.items()
                            if key not in {"DescriptionLoc", "PunchlineLoc"}})
        (self.build / "DualSenseLighting.json").write_text(json.dumps(generated), encoding="utf-8")
        self.write_standard()
        proof_path = self.build / "build-verification.json"
        proof = json.loads(proof_path.read_text())
        proof["SourceSha256"] = PACKAGER.source_fingerprint(source)
        proof_path.write_text(json.dumps(proof))
        PACKAGER.package(self.root, self.build, self.output)
        for feed, archive in [("pluginmaster.json", "latest.zip"), ("pluginmaster-ja.json", "latest-ja.zip")]:
            store, = json.loads((self.output / feed).read_text())
            for key in ["DownloadLinkInstall", "DownloadLinkUpdate", "DownloadLinkTesting"]:
                self.assertEqual(store[key], PACKAGER.REPOSITORY + "/releases/download/v0.13.0/" + archive)

    def test_tampered_dll_is_rejected(self):
        (self.build / "DualSenseLighting.dll").write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "DLL changed"):
            PACKAGER.package(self.root, self.build, self.output)

    def test_stale_manifest_is_rejected(self):
        manifest = dict(self.manifest, AssemblyVersion="0.12.7.0")
        (self.build / "DualSenseLighting.json").write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError, "stale"):
            PACKAGER.package(self.root, self.build, self.output)

    def test_wrong_tag_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "tag must match"):
            PACKAGER.package(self.root, self.build, self.output, "v0.99.0")

    def test_changed_source_requires_rebuild(self):
        (self.root / "src/DualSenseLighting/NewFeature.cs").write_text("// source changed after verification")
        with self.assertRaisesRegex(ValueError, "Source changed"):
            PACKAGER.package(self.root, self.build, self.output)

    def test_license_is_required(self):
        (self.root / "LICENSE-HidLibrary.txt").unlink()
        with self.assertRaises(FileNotFoundError):
            PACKAGER.package(self.root, self.build, self.output)

    def test_debug_build_is_rejected(self):
        proof_path = self.build / "build-verification.json"
        proof = json.loads(proof_path.read_text())
        proof["Optimized"] = False
        proof_path.write_text(json.dumps(proof))
        with self.assertRaisesRegex(ValueError, "optimized x64"):
            PACKAGER.package(self.root, self.build, self.output)


if __name__ == "__main__":
    unittest.main()
