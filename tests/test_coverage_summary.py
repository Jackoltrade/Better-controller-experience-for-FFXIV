"""Protect the structural coverage denominator and missing-report failure contract."""
import importlib.util
from pathlib import Path
import tempfile
import subprocess
import sys
import json
import unittest

SPEC = importlib.util.spec_from_file_location("coverage_summary", Path(__file__).parents[1] / "tools/summarize-coverage.py")
SUMMARY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SUMMARY)


class CoverageSummaryChecks(unittest.TestCase):
    def test_mapped_lines_are_deduplicated_and_test_sources_excluded(self):
        source = "src/DualSenseLighting/Core/LightingSession.cs"
        xml = f'''<coverage branches-valid="4" branches-covered="2"><packages><package><classes>
<class filename="D:/repo/{source}"><lines><line number="10" hits="0"/><line number="11" hits="1"/></lines></class>
<class filename="D:/repo/{source}"><lines><line number="10" hits="1"/></lines></class>
<class filename="D:/repo/tests/Program.cs"><lines><line number="1" hits="1"/></lines></class>
</classes></package></packages></coverage>'''
        with tempfile.TemporaryDirectory() as folder:
            report = Path(folder) / "report.xml"
            report.write_text(xml)
            result = SUMMARY.summarize(report)
        self.assertEqual(result["ProductionMappedLines"], 2)
        self.assertEqual(result["ProductionCoveredLines"], 2)
        self.assertEqual(result["MappedProductionFiles"], 1)
        self.assertIn("src/DualSenseLighting/Plugin.cs", result["UnmappedProductionFiles"])
        self.assertEqual(result["CollectorBranchesValid"], "4")

    def test_missing_authored_sources_is_an_error(self):
        with tempfile.TemporaryDirectory() as folder:
            report = Path(folder) / "report.xml"
            report.write_text('<coverage><class filename="tests/Program.cs"/></coverage>')
            with self.assertRaisesRegex(ValueError, "STRUCTURAL_COVERAGE_UNAVAILABLE"):
                SUMMARY.summarize(report)

    def test_required_plugin_rejects_empty_module_but_accepts_zero_hit_source_lines(self):
        for plugin_lines, should_pass in [("", False), ('<class filename="D:/repo/src/DualSenseLighting/Plugin.cs"><lines><line number="1" hits="0"/></lines></class>', True)]:
            with self.subTest(plugin_lines=plugin_lines), tempfile.TemporaryDirectory() as folder:
                report = Path(folder) / "report.xml"
                output = Path(folder) / "summary.json"
                report.write_text(f'''<coverage><packages>
<package name="LightingChecks"><classes><class filename="D:/repo/src/DualSenseLighting/Core/LightingSession.cs"><lines><line number="10" hits="1"/></lines></class></classes></package>
<package name="DualSenseLighting"><classes>{plugin_lines}</classes></package>
</packages></coverage>''')
                result = SUMMARY.summarize(report)
                self.assertTrue(result["CompiledPluginModulePresent"])
                self.assertEqual(result["CompiledPluginSourceLinesPresent"], should_pass)
                process = subprocess.run([sys.executable, str(SPEC.origin), str(report), "--output", str(output), "--require-plugin"],
                                         capture_output=True, text=True)
                self.assertEqual(process.returncode == 0, should_pass, process.stderr)
                self.assertEqual(output.exists(), should_pass)
                if should_pass:
                    self.assertTrue(json.loads(output.read_text())["CompiledPluginSourceLinesPresent"])
                else:
                    self.assertIn("no eligible source lines", process.stderr)
