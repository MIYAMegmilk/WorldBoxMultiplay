# WorldBoxMultiplay (WMod)
 
A work-in-progress multiplayer mod for [WorldBox](https://store.steampowered.com/app/1206560/WorldBox__God_Simulator/), built on top of [NeoModLoader (NML)](https://github.com/WorldBoxOpenMods/ModLoader).
 
> **Status:** Core networking, state synchronization, and competitive game rules are implemented. Local 2-player sessions (Host + Client on localhost) are functional. Remote play and matchmaking are not yet implemented.
 
## Goal
 
Enable 2+ players who each own WorldBox to play together in a competitive session. Web-based matchmaking / spectating is a future stretch goal.
 
## Architecture
 
- **Host-authoritative + state sync** (not lockstep) — WorldBox uses `System.Random` heavily and runs simulation across multiple threads, so deterministic lockstep is impractical.
- One peer hosts the authoritative simulation; other peers send inputs and receive state deltas.
- Transport: TCP with length-prefixed JSON frames.
## Implemented Features
 
- **Networking** — TCP-based host/client communication with message framing (`NetworkManager`, `NetMessage`)
- **World Sync** — Full world snapshot synchronization and world generation sync (`WorldSnapshotSync`, `WorldGenSync`)
- **Unit Sync** — Unit position, spawn, and death synchronization (`UnitPositionSync`, `UnitLifecycleSync`)
- **Tile Sync** — Tile change synchronization (`TileChangeSync`)
- **God Power Sync** — Remote god power click synchronization (`GodPowerSync`)
- **Game Rules** — Kingdom claiming, mana system, match runner, player registry (`KingdomClaimHandler`, `ManaSystem`, `MatchRunner`, `PlayerRegistry`)
- **Simulation Control** — Fixed tick rate, parallel lockstep toggle, client simulation pause (`FixedTick`, `ParallelLockstep`, `ClientSimMode`)
- **Auto Test** — Built-in automated testing (`AutoTest`)
- **Harmony Patches** — Hooks into WorldBox internals via HarmonyLib
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
    WMod.csproj        # net48, references NML + UnityEngine
    WModMain.cs        # entry point (BasicMod<WModMain>)
    WModBridge.cs      # bridge utilities
    mod.json           # NML manifest
    Net/
      NetworkManager.cs  # TCP host/client networking
      NetMessage.cs      # message framing and serialization
    Sync/
      WorldSnapshotSync.cs   # full world state sync
      WorldGenSync.cs        # synchronized world generation
      UnitPositionSync.cs    # unit position delta sync
      UnitLifecycleSync.cs   # unit spawn/death sync
      TileChangeSync.cs      # tile change sync
      GodPowerSync.cs        # god power click sync
      ClientSimMode.cs       # client simulation pause/resume
      FixedTick.cs           # fixed tick rate control
      ParallelLockstep.cs    # lockstep toggle
      AutoTest.cs            # automated testing
    Rules/
      KingdomClaimHandler.cs # kingdom claiming logic
      ManaSystem.cs          # mana resource system
      MatchRunner.cs         # match lifecycle management
      PlayerRegistry.cs      # player registration and tracking
```
 
## License
 
MIT — see [LICENSE](LICENSE).
