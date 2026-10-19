# AutoMap

AutoMap is a BepInEx/Harmony mod for Hollow Knight: Silksong that can keep the current scene mapped without permanently marking region maps as purchased.

## Requirements

- BepInEx 5
- Save Scoped Config 0.4.0 or newer
- Configuration Manager recommended

## Config

Settings appear under `AutoMap` in Configuration Manager.

Global settings apply to every save and are disabled by default. Save-specific settings apply only to the active save.

- `Quill Required`: require the quill before AutoMap features work. Enabled by default for saves.
- `Auto Map Scene`: automatically map the current scene. Enabled by default for saves.
- `Region Map Required for Auto Map Scene`: require the current region's map before Auto Map Scene can work. Enabled by default for saves.
- `Auto Reveal Rough Region Map`: temporarily reveal the rough region map for the active region. Disabled by default.

Auto Map Scene is intended to reveal mapped scene detail without setting the normal purchased-map progression flags.

The `Controls and Status` panel explains whether scene mapping or rough reveal is enabled globally or for this save, and whether a missing quill, region map, or unsupported scene prevents mapping. Status inspection does not grant items or purchased-map progression.

Startup checks report missing game APIs and roll back this mod's patches if patching fails.

## Build and package

Requires the .NET 8 SDK and a local Silksong installation with BepInEx 5. Run from this repository:

```sh
python3 build.py --game-dir "/path/to/Hollow Knight Silksong"
python3 build.py --game-dir "/path/to/Hollow Knight Silksong" --package
```

`SILKSONG_GAME_DIR` can supply the game directory. Use `--dotnet /path/to/dotnet` for a local SDK. Use `--save-scoped-dll /path/to/SaveScopedConfig.dll` when building against a newer helper than the installed copy.
Builds go to `bin/Release/netstandard2.1`. Packaging writes `dist/AutoMap-<version>.zip` with the plugin, README, and SHA-256 checksum; it includes no game assemblies. `--check` validates version metadata without building. Identical inputs produce identical package contents and timestamps. Release metadata excludes Git revision suffixes so checked-in binaries do not identify an earlier source commit.

Builds do not deploy by default. Install the DLL under `BepInEx/plugins/AutoMap/` after closing the game. On Windows, `build.ps1 -GameDir "C:\path\to\game"` also builds locally; explicit `-Target Plugin` deploys a plugin and `-Target Dev` deploys for ScriptEngine. Do not install both copies.
