# WorldBoxMultiplay (WMod)

A work-in-progress multiplayer mod for [WorldBox](https://store.steampowered.com/app/1206560/WorldBox__God_Simulator/), built on top of [NeoModLoader (NML)](https://github.com/WorldBoxOpenMods/ModLoader).

> **Status:** early scaffold. Loads into the game and logs a hello message. Networking, sync, and competitive rules are not implemented yet.

## Goal

Enable 2+ players who each own WorldBox to play together in a competitive session. Web-based matchmaking / spectating is a future stretch goal.

## Architecture (planned)

- **Host-authoritative + state sync** (not lockstep) — WorldBox uses `System.Random` heavily and runs simulation across multiple threads, so deterministic lockstep is impractical.
- One peer hosts the authoritative simulation; other peers send inputs and receive state deltas.
- Transport: TCP with length-prefixed JSON frames in early iterations. May move to MessagePack / UDP later.

## Requirements

- WorldBox (Steam)
- [NML](https://github.com/WorldBoxOpenMods/ModLoader) installed
- .NET SDK 6+ (for `dotnet build`); the mod itself targets `net48` (Unity Mono)

## Build

```powershell
dotnet build .\WMod.sln -c Debug
```

This compiles `src/WMod/WMod.csproj` and, via the post-build `DeployMod` target, copies `WMod.dll` + `mod.json` into:

```
<worldbox-install>\Mods\WMod\
```

The `WorldBoxRoot` property in `src/WMod/WMod.csproj` defaults to the standard Steam path. Override it if your install lives elsewhere:

```powershell
dotnet build .\WMod.sln -c Debug -p:WorldBoxRoot="D:\Games\worldbox"
```

## Run

1. Launch WorldBox.
2. Main menu → **Mods** → enable `WMod`.
3. Restart the game.
4. Check the log for the hello line:

```
%USERPROFILE%\AppData\LocalLow\mkarpenko\WorldBox\Player.log
```

## Layout

```
WMod.sln
src/
  WMod/
    WMod.csproj      # net48, references NML + UnityEngine via absolute paths
    WModMain.cs      # entry point (BasicMod<WModMain>)
    mod.json         # NML manifest
```

## License

MIT — see [LICENSE](LICENSE).
