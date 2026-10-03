# GK2 Conveyor Round Robin

BepInEx 5 / Harmony code mod for **Graveyard Keeper 2** (Unity, Mono, Windows).

## Goal

Fix the junction behavior of **conveyor chests**. A conveyor chest takes input from one side and
can feed belts on up to 3 other sides. In vanilla the outputs are served in a fixed priority
order, so when supply is short one branch gets everything and the others starve.

Wanted: **round-robin output**. Rotate which side gets the next item
(west -> north -> east -> west ...), skipping any side that is full or missing.

Out of scope for now: the separate "conveyor splitter" building (`ConveyorSplitterComponent`),
which is also buggy.

## Hard rules

- **Never modify original game files.** Compile against the publicized copy in `lib/` only.
  The only things this project writes into the game folder are BepInEx itself and files under
  `BepInEx/plugins`.
- **Never add fields to serialized game classes**, and never write new data into saves.
  Per-object mod state lives in a `ConditionalWeakTable` keyed by the game object.
- **Never commit the developers' code**: `decompiled/`, `lib/` and all `*.dll` are gitignored.
  Do not paste large decompiled excerpts into committed files either.

## Environment

| | |
|---|---|
| Game path | `C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2` (Steam app 4358690) |
| Executable | `GraveyardKeeper2.exe`, data in `GraveyardKeeper2_Data\` |
| Unity | 6000.3.9f1, Mono backend (not IL2CPP) |
| Mod loader | BepInEx 5.4.23.5 x64 (Mono), HarmonyX `0Harmony.dll` in `BepInEx\core` |
| Plugin target | `net472` (the game assembly references `mscorlib 4.0.0.0`) |
| SDK | .NET SDK 10 only; no .NET Framework targeting pack (the `Microsoft.NETFramework.ReferenceAssemblies` package supplies it) |
| Shell | Windows PowerShell 5.1 (no `&&`; use `;`) and Git Bash |

The game path is defined once, as `GamePath` in `Directory.Build.props`. Override with
`dotnet build -p:GamePath="..."` or a `GamePath` environment variable.

The game folder is user-writable, so no admin rights are needed. The game locks loaded plugin
DLLs, so **close the game before a deploying build**.

## Layout

- `src/GK2.ConveyorRoundRobin/` - the plugin (`Plugin.cs` is the `[BepInPlugin]` entry point).
- `Directory.Build.props` - `GamePath` and the paths derived from it.
- `decompiled/` - ILSpy output of the game's `Assembly-CSharp.dll`, for reading only. Gitignored.
- `lib/Assembly-CSharp.dll` - publicized, method-body-stripped reference copy. Gitignored.

## Build and deploy

```powershell
dotnet build -c Release                         # builds, then copies the DLL to <game>\BepInEx\plugins
dotnet build -c Release -p:DeployToGame=false   # build only (e.g. while the game is running)
```

Then launch the game through Steam and read `<game>\BepInEx\LogOutput.log`. The plugin logs
`GK2 Conveyor Round Robin <version> loaded` on startup.

## Refreshing after a game update

A game patch makes `decompiled/` and `lib/` stale (and can break Harmony patches). Regenerate both:

```powershell
$managed = "C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2\GraveyardKeeper2_Data\Managed"
Remove-Item -Recurse -Force decompiled
ilspycmd -p -o decompiled "$managed\Assembly-CSharp.dll"
$env:DOTNET_ROLL_FORWARD = "LatestMajor"   # the publicizer targets .NET 6; only .NET 9/10 runtimes are installed
assembly-publicizer "$managed\Assembly-CSharp.dll" --strip -f -o lib\Assembly-CSharp.dll
```

Both are .NET global tools in `%USERPROFILE%\.dotnet\tools`. Last generated from Steam build
25676698 (game files dated 2026-10-03).

## How the patch works

`ChestRoundRobin.cs`, applied only to `ConveyorChestComponent`s with two or more output belts:

- Postfix on `ConveyorChestComponent.CanGiveItem` refuses the belts' own mid-tick pulls.
- A handler placed first in `ConveyorSystem.OnUpdated` (installed from a prefix on
  `ConveyorSystem.CustomUpdate`) then hands items out at the end of the tick: it walks `SlotsData`
  starting after the side served last and calls each output belt's vanilla `PerformItemTransfer()`.
  It must run before the belt animators' handlers on the same event.
- Postfix on `ConveyorChestComponent.GiveItem` records that a hand-out happened, which advances the rotation.
- Any failure disables the gate and logs an error, so the chest falls back to vanilla rather than jamming.

Config is `<game>\BepInEx\config\gk2.conveyorroundrobin.cfg`: `General.Enabled` (vanilla when false)
and `Debug.LogHandOuts` (one log line per hand-out; off by default in code, switched on in the
local config while testing). First in-game test on 2026-10-03 (Steam build 25676698): chests
alternated sides and the log showed no errors.

## How the conveyor system works (vanilla)

All classes are in the global namespace of `Assembly-CSharp`.

- `ConveyorSystem.CustomUpdate` runs one tick every `conveyor_system_update_interval`. Each tick it
  walks the graph **from the end of each belt line upstream**: `graphEndElements` -> `DoJob()`,
  which calls `PerformItemTransfer()` and then recurses into the element's parents.
- Transfers are **pull-based**. A belt cell (`ConveyorCellComponent`, also
  `ConveyorCellUndergroundComponent` and `ConveyorSplitterComponent`) pulls from a parent when its
  own inventory is empty: `parent.CanGiveItem(this)` then `parent.GiveItem(this)`.
- **One item per element per tick** (`RemoveItemById(id, 1)`, guarded by `wasPerformedItemTransfer`).
- `ConveyorChestComponent` never chooses an output. `CanGiveItem` / `TryGetItemIdToGive` say yes to
  any connected belt that asks, so the winner is whichever branch the traversal reaches first. That
  order comes from `ConveyorSystem.UpdateEndElements` / `GetEndElement`, i.e. the order the belts
  were connected, and it is the same every tick.
- `ConveyorChestComponent.SlotsData` holds the four sides in the order Left, Up, Right, Down, with an
  optional per-side item filter (`slotItemId`).
- `ConveyorChestOutComponent` is an output-only storage (it never pulls) and is not the target.
- Animations read `ConveyorComponent.InItem` / `OutItem` in handlers of `ConveyorSystem.OnUpdated`,
  which fires at the end of the tick.
