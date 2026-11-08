"""Exercise the actual Ubuntu publish block with an isolated, offline gh replacement."""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ("latest.zip", "latest-ja.zip", "SHA256SUMS.txt", "release-summary.json")
FEEDS = ("pluginmaster.json", "pluginmaster-ja.json")
MOCK_GH = r'''
import json
import os
import sys
from pathlib import Path

path = Path(os.environ["MOCK_GH_STATE"])
state = json.loads(path.read_text())
args = sys.argv[1:]
state["calls"].append(args)
try:
    if args[:2] == ["release", "view"]:
        tag = args[2]
        if tag == "plugin-repository":
            raise SystemExit(0 if state["feed_exists"] else 1)
        assert tag == os.environ["RELEASE_TAG"], args
        if not state["version_exists"]:
            raise SystemExit(1)
        field = args[args.index("--json") + 1]
        if field == "isPrerelease,isDraft":
            print(str(state["prerelease"]).lower() + "\t" + str(state["draft"]).lower())
        elif field == "assets":
            print("\n".join(state["assets"]))
        else:
            raise AssertionError(args)
    elif args[:2] == ["release", "create"]:
        if args[2] == "plugin-repository":
            assert not state["feed_exists"], args
            state["feed_exists"] = True
        else:
            assert args[2] == os.environ["RELEASE_TAG"] and not state["version_exists"], args
            assert "--verify-tag" in args and "--prerelease" not in args, args
            state["version_exists"] = True
            state["prerelease"] = False
            state["draft"] = False
            for asset in args[3:]:
                if asset.startswith("--"):
                    break
                state["assets"][Path(asset).name] = Path(asset).read_bytes().hex()
            assert set(state["assets"]) == set(state["required_assets"])
    elif args[:2] == ["release", "download"]:
        if state.get("fail_download"):
            raise SystemExit(1)
        name = args[args.index("--pattern") + 1]
        target = Path(args[args.index("--dir") + 1]) / name
        target.write_bytes(bytes.fromhex(state["assets"][name]))
    elif args[:2] == ["release", "upload"]:
        if args[2] == "plugin-repository":
            assert "--clobber" in args and state["feed_exists"], args
            uploaded = []
            for asset in args[3:]:
                if asset.startswith("--"):
                    break
                name = Path(asset).name
                assert name in state["required_feeds"], args
                if state.get("fail_feed", 0):
                    state["fail_feed"] -= 1
                    raise SystemExit(1)
                if state.get("fail_feed_name") == name:
                    del state["fail_feed_name"]
                    raise SystemExit(1)
                state.setdefault("feeds", {})[name] = Path(asset).read_text()
                uploaded.append(name)
            assert set(uploaded) == set(state["required_feeds"])
        else:
            assert args[2] == os.environ["RELEASE_TAG"] and "--clobber" not in args, args
            if state.get("fail_upload"):
                raise SystemExit(1)
            for asset in args[3:]:
                name = Path(asset).name
                assert name not in state["assets"], args
                state["assets"][name] = Path(asset).read_bytes().hex()
    elif args[:2] == ["release", "edit"]:
        assert args[2] == os.environ["RELEASE_TAG"] and "--draft=false" in args and "--prerelease=false" in args, args
        assert set(state["assets"]) == set(state["required_assets"])
        state["draft"] = False
        state["prerelease"] = False
    elif args[0] == "api":
        print("a" * 40)
    else:
        raise AssertionError(args)
finally:
    path.write_text(json.dumps(state))
'''


class ReleaseReferenceChecks(unittest.TestCase):
    def test_version_build_uses_an_explicit_tag_reference(self):
        # checkout prefers a matching branch for unqualified names, so a release tag must retain its namespace.
        workflow = (ROOT / ".github/workflows/release.yml").read_text(encoding="utf-8")
        build = workflow.split("  build:\n", 1)[1].split("  publish:\n", 1)[0]
        self.assertIn("      ref: refs/tags/${{ inputs.tag }}\n", build)
        self.assertIn("      tag: ${{ inputs.tag }}\n", build)
        self.assertIn("          RELEASE_TAG: ${{ inputs.tag }}\n", workflow)

    def test_publishing_requires_manual_dispatch(self):
        workflow = (ROOT / ".github/workflows/release.yml").read_text(encoding="utf-8")
        triggers = workflow.split("\non:\n", 1)[1].split("\npermissions:\n", 1)[0]
        events = [line.strip()[:-1] for line in triggers.splitlines()
                  if line.startswith("  ") and not line.startswith("   ") and line.endswith(":")]
        self.assertEqual(events, ["workflow_dispatch"],
                         "Publishing must not run on pushes, tags, schedules or other automatic events")


@unittest.skipUnless(os.name == "posix" and shutil.which("bash"), "Publish block targets Ubuntu Bash")
class ReleaseWorkflowChecks(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        (self.root / "release").mkdir()
        for name in ASSETS:
            (self.root / "release" / name).write_bytes(("verified " + name + "\n").encode())
        for name in FEEDS:
            (self.root / "release" / name).write_text(json.dumps([{"release": name}]) + "\n")
        bin_dir = self.root / "bin"
        bin_dir.mkdir()
        mock = bin_dir / "gh"
        mock.write_text("#!" + sys.executable + "\n" + textwrap.dedent(MOCK_GH))
        mock.chmod(0o700)
        self.env = dict(os.environ, PATH=str(bin_dir) + os.pathsep + os.environ["PATH"],
                        GH_TOKEN="synthetic-test-token", GH_REPO="fixture/fixture", RELEASE_TAG="v0.17.7",
                        MOCK_GH_STATE=str(self.root / "state.json"), TMPDIR=str(self.root / "scratch"))
        Path(self.env["TMPDIR"]).mkdir()
        self.assertEqual(shutil.which("gh", path=self.env["PATH"]), str(mock))
        workflow = (ROOT / ".github/workflows/release.yml").read_text()
        publish_step = workflow.split("      - name: Publish ZIP and update the installer feeds\n", 1)[1]
        block = publish_step.split("        run: |\n", 1)[1]
        lines = []
        for line in block.splitlines():
            if line and not line.startswith("          "):
                break
            lines.append(line)
        self.script = textwrap.dedent("\n".join(lines))
        self.assertTrue(self.script.startswith("set -euo pipefail"))
        self.state = {"version_exists": True, "prerelease": False, "draft": False,
                      "assets": self.local_assets(), "feed_exists": True, "calls": [],
                      "required_assets": ASSETS, "required_feeds": FEEDS}

    def local_assets(self):
        return {name: (self.root / "release" / name).read_bytes().hex() for name in ASSETS}

    def local_feeds(self):
        return {name: (self.root / "release" / name).read_text() for name in FEEDS}

    def run_publish(self):
        path = Path(self.env["MOCK_GH_STATE"])
        path.write_text(json.dumps(self.state))
        result = subprocess.run([shutil.which("bash"), "-c", self.script], cwd=self.root,
                                env=self.env, capture_output=True, text=True, timeout=10)
        self.state = json.loads(path.read_text())
        return result

    def version_writes(self):
        return [call for call in self.state["calls"] if len(call) > 2 and call[0] == "release" and
                call[1] in {"create", "upload", "edit"} and call[2] != "plugin-repository"]

    def assert_no_feed_change(self):
        self.assertNotIn("feeds", self.state)
        self.assertFalse(any(call[:2] == ["release", "upload"] and call[2] == "plugin-repository"
                             for call in self.state["calls"]))

    def test_first_release_is_created_and_feed_is_published(self):
        self.state.update(version_exists=False, assets={}, feed_exists=False)
        result = self.run_publish()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.state["assets"], self.local_assets())
        self.assertFalse(self.state["prerelease"])
        self.assertTrue(self.state["feed_exists"])
        self.assertEqual(self.state["feeds"], self.local_feeds())
        self.assertEqual(len(self.version_writes()), 1)
        lookup = next(call for call in self.state["calls"] if call[0] == "api")
        self.assertEqual(lookup[1], "repos/fixture/fixture/commits/refs/tags/v0.17.7")

    def test_matching_release_retry_does_not_modify_immutable_assets(self):
        result = self.run_publish()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.state["assets"], self.local_assets())
        self.assertEqual(self.version_writes(), [])
        self.assertEqual(len([c for c in self.state["calls"] if c[:2] == ["release", "download"]]), len(ASSETS))
        self.assertEqual(self.state["feeds"], self.local_feeds())
        self.assertEqual(list(Path(self.env["TMPDIR"]).iterdir()), [], "Comparison directory was not cleaned")

    def test_failed_feed_upload_can_be_retried_with_original_artifact(self):
        self.state.update(version_exists=False, assets={}, fail_feed=1)
        self.assertNotEqual(self.run_publish().returncode, 0)
        self.assertTrue(self.state["version_exists"])
        self.assertEqual(self.state["assets"], self.local_assets())
        self.assertNotIn("feeds", self.state)
        result = self.run_publish()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.state["feeds"], self.local_feeds())
        self.assertEqual([call[1] for call in self.version_writes()], ["create"])

    def test_partial_japanese_feed_upload_failure_can_be_retried(self):
        self.state["fail_feed_name"] = "pluginmaster-ja.json"
        result = self.run_publish()
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.state["feeds"], {"pluginmaster.json": self.local_feeds()["pluginmaster.json"]})
        self.assertEqual(self.version_writes(), [])
        result = self.run_publish()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.state["feeds"], self.local_feeds())
        self.assertEqual(self.state["assets"], self.local_assets())
        self.assertEqual(self.version_writes(), [])

    def test_incomplete_draft_is_verified_completed_then_published(self):
        self.state.update(draft=True, assets={"latest.zip": self.local_assets()["latest.zip"]})
        result = self.run_publish()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.state["assets"], self.local_assets())
        self.assertFalse(self.state["draft"])
        writes = self.version_writes()
        self.assertEqual([call[1] for call in writes], ["upload", "edit"])
        self.assertEqual(writes[0][3:], ["release/latest-ja.zip", "release/SHA256SUMS.txt", "release/release-summary.json"])
        feed = next(i for i, c in enumerate(self.state["calls"]) if c[:3] == ["release", "upload", "plugin-repository"])
        publish = next(i for i, c in enumerate(self.state["calls"]) if c[:2] == ["release", "edit"])
        self.assertLess(publish, feed)

    def test_missing_japanese_archive_is_added_without_replacing_other_assets(self):
        del self.state["assets"]["latest-ja.zip"]
        result = self.run_publish()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.version_writes(), [["release", "upload", self.env["RELEASE_TAG"], "release/latest-ja.zip"]])
        self.assertEqual(self.state["assets"], self.local_assets())
        self.assertEqual(self.state["feeds"], self.local_feeds())

    def test_changed_japanese_archive_blocks_all_release_and_feed_writes(self):
        self.state.update(draft=True, assets={"latest.zip": self.local_assets()["latest.zip"],
                                             "latest-ja.zip": b"different Japanese archive".hex()})
        result = self.run_publish()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("latest-ja.zip differs", result.stderr)
        self.assertEqual(self.version_writes(), [])
        self.assert_no_feed_change()
        self.assertTrue(self.state["draft"])

    def test_mismatch_blocks_missing_uploads_and_feed_before_any_write(self):
        self.state.update(draft=True, assets={"SHA256SUMS.txt": b"different artifact".hex()})
        result = self.run_publish()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("differs", result.stderr)
        self.assertEqual(self.version_writes(), [])
        self.assert_no_feed_change()
        self.assertTrue(self.state["draft"])

    def test_matching_prerelease_is_promoted_only_after_asset_verification(self):
        self.state["prerelease"] = True
        result = self.run_publish()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(self.state["prerelease"])
        self.assertEqual(self.state["assets"], self.local_assets())
        self.assertEqual([c[1] for c in self.version_writes()], ["edit"])
        last_download = max(i for i, c in enumerate(self.state["calls"]) if c[:2] == ["release", "download"])
        promote = next(i for i, c in enumerate(self.state["calls"]) if c[:2] == ["release", "edit"])
        feed = next(i for i, c in enumerate(self.state["calls"]) if c[:3] == ["release", "upload", "plugin-repository"])
        self.assertLess(last_download, promote)
        self.assertLess(promote, feed)

    def test_changed_stable_release_is_not_repurposed(self):
        self.state["assets"]["latest.zip"] = b"different stable artifact".hex()
        result = self.run_publish()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("differs", result.stderr)
        self.assertEqual(self.version_writes(), [])
        self.assert_no_feed_change()

    def test_changed_prerelease_is_not_promoted(self):
        self.state.update(prerelease=True, assets={"latest.zip": b"different prerelease artifact".hex()})
        result = self.run_publish()
        self.assertNotEqual(result.returncode, 0)
        self.assertTrue(self.state["prerelease"])
        self.assertEqual(self.version_writes(), [])
        self.assert_no_feed_change()

    def test_failed_comparison_does_not_modify_release_or_feed(self):
        self.state["fail_download"] = True
        self.assertNotEqual(self.run_publish().returncode, 0)
        self.assertEqual(self.version_writes(), [])
        self.assert_no_feed_change()

    def test_failed_missing_asset_upload_does_not_publish_draft_or_feed(self):
        self.state.update(draft=True, assets={}, fail_upload=True)
        self.assertNotEqual(self.run_publish().returncode, 0)
        self.assertTrue(self.state["draft"])
        self.assertEqual(self.state["assets"], {})
        self.assert_no_feed_change()

    def test_workflow_tag_stays_a_single_argument(self):
        self.env["RELEASE_TAG"] = "v0.17.7; touch injected"
        self.state.update(version_exists=False, assets={})
        result = self.run_publish()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse((self.root / "injected").exists())
        self.assertEqual(self.version_writes()[0][2], self.env["RELEASE_TAG"])
        # Real publishing additionally requires the upstream package step's canonical version tag.


if __name__ == "__main__":
    unittest.main()
