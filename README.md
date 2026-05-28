# AutoMap

AutoMap is a BepInEx/Harmony mod for Hollow Knight: Silksong that can keep the current scene mapped without permanently marking region maps as purchased.

## Requirements

- BepInEx 5
- Save Scoped Config

## Config

Settings appear under `AutoMap` in Configuration Manager.

Global settings apply to every save and are disabled by default. Save-specific settings apply only to the active save.

- `Quill Required`: require the quill before AutoMap features work. Enabled by default for saves.
- `Auto Map Scene`: automatically map the current scene. Enabled by default for saves.
- `Region Map Required for Auto Map Scene`: require the current region's map before Auto Map Scene can work. Enabled by default for saves.
- `Auto Reveal Rough Region Map`: temporarily reveal the rough region map for the active region. Disabled by default.

Auto Map Scene is intended to reveal mapped scene detail without setting the normal purchased-map progression flags.

## Build

From the game directory:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\ModDev\AutoMap\build.ps1
```

The default build target is development mode:

```text
BepInEx/scripts/AutoMap.dll
```

For a normal BepInEx plugin install:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\ModDev\AutoMap\build.ps1 -Target Plugin
```
