#!/usr/bin/env python3
"""Run the standard Windows .NET SDK build from WSL."""
import argparse
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
POWERSHELL = "/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"


def windows_path(path):
    return subprocess.check_output(["wslpath", "-w", str(path)], text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet-path", type=Path, help="WSL path to a Windows .NET 10 SDK's dotnet.exe")
    parser.add_argument("--dalamud-path", type=Path, help="WSL path to the API 15 Dalamud directory")
    parser.add_argument("--probe", action="store_true", help="also run read-only controller discovery")
    parser.add_argument("--locked-restore", action="store_true", help="require the committed dependency lockfile")
    args = parser.parse_args()
    command = [POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
               windows_path(ROOT / "tools/build.ps1")]
    for value, flag in [(args.dotnet_path, "-DotnetPath"), (args.dalamud_path, "-DalamudLibPath")]:
        if value is not None:
            command += [flag, windows_path(value.resolve())]
    if args.probe:
        command.append("-Probe")
    if args.locked_restore:
        command.append("-LockedRestore")
    subprocess.run(command, check=True, cwd=ROOT)


if __name__ == "__main__":
    main()
