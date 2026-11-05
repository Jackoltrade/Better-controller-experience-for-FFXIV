"""Summarize collector metrics without treating absent files as covered code."""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def summarize(report):
    root = ET.parse(report).getroot()
    authored = {p.relative_to(ROOT).as_posix() for p in (ROOT / "src").rglob("*.cs")
                if "obj" not in p.parts and "bin" not in p.parts and not p.name.endswith(".Generated.cs") and p.name != "EnemyBossCatalog.cs"}
    modules = []
    for package in root.findall("./packages/package"):
        module_lines = {}
        for cls in package.findall("./classes/class"):
            for line in cls.findall("./lines/line"):
                key = (cls.get("filename"), line.get("number"))
                module_lines[key] = max(module_lines.get(key, 0), int(line.get("hits", "0")))
        modules.append({"Name": package.get("name"), "CollectorEligibleLines": len(module_lines),
                        "CollectorCoveredLines": sum(v > 0 for v in module_lines.values()),
                        "LineRate": package.get("line-rate"), "BranchRate": package.get("branch-rate")})
    files = {}
    for cls in root.findall(".//class"):
        filename = cls.attrib["filename"].replace("\\", "/")
        marker = filename.lower().find("/src/")
        relative = filename[marker + 1:] if marker >= 0 else filename
        if relative not in authored:
            continue
        lines = files.setdefault(relative, {})
        for line in cls.findall("./lines/line"):
            number = int(line.attrib["number"])
            lines[number] = max(lines.get(number, 0), int(line.attrib["hits"]))
    if not files or not any(files.values()):
        raise ValueError("STRUCTURAL_COVERAGE_UNAVAILABLE: no authored production lines mapped")
    covered = sum(hits > 0 for lines in files.values() for hits in lines.values())
    valid = sum(len(lines) for lines in files.values())
    return {
        "Scope": "Instrumented executed/loaded modules only; not all plugin paths or native code",
        "Collector": "dotnet-coverage 18.12.0",
        "CollectorModules": modules,
        "CompiledPluginModulePresent": any(m["Name"] == "DualSenseLighting" for m in modules),
        "CompiledPluginSourceLinesPresent": any(m["Name"] == "DualSenseLighting" and m["CollectorEligibleLines"] > 0 for m in modules),
        "ZeroHitMappedProductionLines": valid - covered,
        "ProductionMappedLines": valid, "ProductionCoveredLines": covered,
        "ProductionMappedLineRate": covered / valid,
        "MappedRateIsWholePluginCoverage": False,
        "CollectorBranchesValid": root.attrib.get("branches-valid"),
        "CollectorBranchesCovered": root.attrib.get("branches-covered"),
        "BranchScope": "Collector total only; source-filtered branch percentage not inferred",
        "AuthoredProductionFiles": len(authored), "MappedProductionFiles": len(files),
        "UnmappedProductionFiles": sorted(authored - files.keys()),
        "Exclusions": ["tests", "obj/bin", "*.Generated.cs", "generated EnemyBossCatalog.cs", "third-party modules"],
        "Files": {name: {"Lines": len(lines), "Covered": sum(v > 0 for v in lines.values())}
                  for name, lines in sorted(files.items())},
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("report", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--require-plugin", action="store_true")
    args = parser.parse_args()
    result = summarize(args.report)
    if args.require_plugin and not result["CompiledPluginSourceLinesPresent"]:
        raise ValueError("STRUCTURAL_COVERAGE_UNAVAILABLE: current compiled plugin module has no eligible source lines")
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Mapped production lines {result['ProductionCoveredLines']}/{result['ProductionMappedLines']}; "
          f"files {result['MappedProductionFiles']}/{result['AuthoredProductionFiles']}; see exclusions/unmapped list")
