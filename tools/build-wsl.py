#!/usr/bin/env python3
"""Build from WSL using the installed Windows runtime and offline Roslyn compiler."""
import json
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
POWERSHELL = "/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"


def windows_path(path):
    return subprocess.check_output(["wslpath", "-w", str(path)], text=True).strip()


def wsl_path(path):
    return "/mnt/" + path[0].lower() + path[2:].replace("\\", "/")


subprocess.run([POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                windows_path(ROOT / "tools/build.ps1"), "-PrepareOffline"], check=True)
plan = json.loads((ROOT / "artifacts/build-plan.json").read_text(encoding="utf-8-sig"))
dotnet = wsl_path(plan["Dotnet"])
for output, kind, sources in [("Plugin", "library", "PluginSources"), ("Checks", "console", "CheckSources"),
                              ("Verifier", "console", "VerifierSources")]:
    subprocess.run([dotnet, plan["Bootstrap"], plan[output], kind, plan["Dalamud"],
                    plan["Compiler"], *plan[sources]], check=True)
subprocess.run([dotnet, plan["Checks"]], check=True)
subprocess.run([dotnet, plan["Verifier"], plan["Plugin"],
                windows_path(ROOT / "src/DualSenseLighting/DualSenseLighting.json"), plan["Dalamud"]], check=True)
if "--probe" in sys.argv:
    subprocess.run([dotnet, plan["Checks"], "--probe"], check=True)
output = Path(wsl_path(plan["Plugin"])).parent
shutil.copy2(ROOT / "src/DualSenseLighting/DualSenseLighting.json", output)
for name in ["LICENSE.txt", "LICENSE-HidLibrary.txt", "LICENSE-FFXIVClientStructs.txt", "THIRD-PARTY-NOTICES.txt"]:
    shutil.copy2(ROOT / name, output)
publish = ROOT / "artifacts/plugin"
publish.mkdir(parents=True, exist_ok=True)
# Publish only after the staged build and checks succeed: the dev plugin may be live.
for source in output.iterdir():
    if source.is_file():
        temporary = publish / (source.name + ".new")
        shutil.copy2(source, temporary)
        temporary.replace(publish / source.name)
print(f"Plugin ready: {publish / 'DualSenseLighting.dll'}")
