#!/usr/bin/env python3
"""Build without deploying; optionally produce a deterministic manual-install ZIP."""
import argparse
import hashlib
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET
import zipfile


def main():
    root = Path(__file__).resolve().parent
    project = next(root.glob("*.csproj"))
    name = project.stem
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-dir", type=Path, default=os.environ.get("SILKSONG_GAME_DIR"))
    parser.add_argument("--save-scoped-dll", type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--check", action="store_true", help="Check version metadata without building")
    parser.add_argument("--package", action="store_true", help="Create dist/NAME-VERSION.zip")
    args = parser.parse_args()
    version = ET.parse(project).findtext(".//InformationalVersion")
    plugin = root / ("SaveScopedConfigPlugin.cs" if name == "SaveScopedConfig" else "Plugin.cs")
    declared = re.search(r'PluginVersion = "([^"]+)"', plugin.read_text()).group(1)
    if version != declared:
        parser.error("Assembly and plugin versions differ")
    for tag in ["AssemblyVersion", "FileVersion"]:
        if ET.parse(project).findtext(".//" + tag) != version + ".0":
            parser.error(tag + " does not match the plugin version")
    if ET.parse(project).findtext(".//IncludeSourceRevisionInInformationalVersion") != "false":
        parser.error("Release version metadata must not acquire an unrelated Git revision")
    if args.check:
        print(name + " " + version + ": metadata OK")
        return
    if not args.game_dir:
        parser.error("Pass --game-dir or set SILKSONG_GAME_DIR")
    game = Path(args.game_dir).expanduser().resolve()
    if not (game / "Hollow Knight Silksong_Data/Managed/Assembly-CSharp.dll").is_file():
        parser.error("Game directory does not contain the managed game assemblies")
    command = [args.dotnet, "build", str(project), "-c", "Release", "--nologo", "-p:GameDir=" + str(game)]
    if args.save_scoped_dll:
        command.append("-p:SaveScopedConfigPath=" + str(args.save_scoped_dll.resolve()))
    subprocess.run(command, cwd=root, check=True)
    dll = root / "bin/Release/netstandard2.1" / (name + ".dll")
    if args.package:
        output = root / "dist" / (name + "-" + version + ".zip")
        output.parent.mkdir(exist_ok=True)
        files = {"BepInEx/plugins/" + name + "/" + dll.name: dll.read_bytes(), "README.md": (root / "README.md").read_bytes()}
        files["SHA256SUMS"] = "".join(hashlib.sha256(value).hexdigest() + "  " + path + "\n" for path, value in sorted(files.items())).encode()
        with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for path, value in sorted(files.items()):
                info = zipfile.ZipInfo(path, date_time=(1980, 1, 1, 0, 0, 0))
                info.create_system = 3
                info.external_attr = 0o100644 << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(info, value)
        print("Packaged " + str(output))
    else:
        print("Built " + str(dll))


if __name__ == "__main__":
    main()
