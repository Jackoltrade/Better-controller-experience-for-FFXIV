"""Verify public release boundaries with synthetic payloads; no network or controller I/O."""
import importlib.util
import json
import tempfile
import unittest
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
                         "DalamudApiLevel": 15, "Name": "PS5 DualSense Controller for FFXIV"}
        for path in (source / "DualSenseLighting.json", self.build / "DualSenseLighting.json"):
            path.write_text(json.dumps(self.manifest), encoding="utf-8")
        # The real build's PE/optimization verification is performed by VerifyAssembly.
        # Here only archive selection, proof checks and store-entry construction are tested.
        dll = b"synthetic-verified-payload"
        (self.build / "DualSenseLighting.dll").write_bytes(dll)
        proof = dict(self.manifest, Architecture="x64", Optimized=True, DllSha256=PACKAGER.sha256(dll))
        proof["SourceSha256"] = PACKAGER.source_fingerprint(source)
        (self.build / "build-verification.json").write_text(json.dumps(proof), encoding="utf-8")
        for name in PACKAGER.LICENSES + PACKAGER.GUIDES:
            (self.root / name).write_text("Fixture for " + name, encoding="utf-8")

    def test_testing_only_store_and_exact_payload(self):
        (self.build / "sfx-trace.json").write_text("private diagnostic")
        (self.build / "Dalamud.dll").write_text("runtime dependency")
        archive = PACKAGER.package(self.root, self.build, self.output, "v0.13.0")
        with zipfile.ZipFile(archive) as bundle:
            self.assertEqual(set(bundle.namelist()), set(PACKAGER.LICENSES + PACKAGER.GUIDES) |
                             {"DualSenseLighting.dll", "DualSenseLighting.json"})
            self.assertIsNone(bundle.testzip())
        store, = json.loads((self.output / "pluginmaster.json").read_text())
        self.assertTrue(store["IsTestingExclusive"])
        self.assertEqual(store["AssemblyVersion"], "0.0.0.0")
        self.assertEqual(store["TestingAssemblyVersion"], "0.13.0.0")
        self.assertEqual(store["TestingDalamudApiLevel"], 15)
        self.assertEqual(store["DownloadLinkTesting"], store["DownloadLinkInstall"])
        self.assertTrue(store["DownloadLinkTesting"].endswith("/v0.13.0/latest.zip"))

    def test_zip_is_repeatable_for_same_payload(self):
        path = PACKAGER.package(self.root, self.build, self.output)
        first = path.read_bytes()
        PACKAGER.package(self.root, self.build, self.output)
        self.assertEqual(path.read_bytes(), first)

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
