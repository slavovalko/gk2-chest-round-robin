# GK2 Chest Round Robin

A small [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Graveyard Keeper 2** that makes
conveyor chests share their items fairly between their output belts.

## The problem

A conveyor chest can feed belts on up to three sides. In the unmodded game, when the chest has
fewer items than its belts want, one belt takes everything and the others get nothing. The
other belts are only fed once the favoured one is completely backed up.

This happens because the chest never chooses a side. Each belt pulls for itself, the belts are
processed in the same order every tick, and the chest hands its item to whichever belt asks first.

## What the mod does

For every conveyor chest with **two or more output belts**, items are handed out in rotation:
west, north, east, south, then west again. A side is skipped when its belt is full, when there
is no belt there, or when that side is filtered to an item the chest does not have.

- With plenty of items in the chest, nothing changes: every output still gets one item per tick.
- When items are scarce, the outputs take turns instead of one belt starving the rest.
- Per-side item filters set in the chest window keep working.

Not changed: chests with zero or one output belt, output-only storages, and conveyor splitters.

## Requirements

- Graveyard Keeper 2 on Windows (Steam)
- BepInEx 5 for Mono, x64. Built and tested with 5.4.23.5.

Tested on Steam build 25676698 (Unity 6000.3.9f1). A game update can break the mod; if the mod
cannot apply its changes, or hits an error while running, it switches itself off, writes the
reason to the BepInEx log and leaves the chests behaving as in the unmodded game.

## Install

1. Install BepInEx: download `BepInEx_win_x64_5.4.23.5.zip` from the
   [BepInEx releases](https://github.com/BepInEx/BepInEx/releases) and extract it into the game
   folder, so that `winhttp.dll` and the `BepInEx` folder sit next to `GraveyardKeeper2.exe`.
2. Start the game once and quit, so BepInEx creates its folders.
3. Build `GK2.ConveyorRoundRobin.dll` (see [Building](#building)) and put it in
   `<game folder>\BepInEx\plugins`. The build does this copy for you.
4. Start the game. `<game folder>\BepInEx\LogOutput.log` should contain
   `GK2 Conveyor Round Robin 0.2.0 loaded`.

To uninstall, delete the DLL from `BepInEx\plugins`.

## Saves

The mod adds nothing to save files. The "whose turn is it" counter of each chest lives in memory
only and starts over when a save is loaded. Saves made with the mod load fine without it, and
the other way round.

## Configuration

After the first run, settings are in `<game folder>\BepInEx\config\gk2.conveyorroundrobin.cfg`.
Changes take effect on the next game start.

| Setting | Default | Meaning |
|---|---|---|
| `General.Enabled` | `true` | Set to `false` to get the unmodded chest behaviour back without removing the mod. |
| `Debug.LogHandOuts` | `false` | Writes one log line for every item a managed chest hands out (which chest, which side, which item). |

## Building

You need the [.NET SDK](https://dotnet.microsoft.com/download) and a game install that already
has BepInEx in it, because the project compiles against DLLs in the game folder.

The game's own code is not part of this repository. The project compiles against a publicized
copy of the game assembly that you generate locally into `lib/`:

```powershell
dotnet tool install -g BepInEx.AssemblyPublicizer.Cli

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2\GraveyardKeeper2_Data\Managed"
$env:DOTNET_ROLL_FORWARD = "LatestMajor"   # only needed if you have no .NET 6 runtime
assembly-publicizer "$managed\Assembly-CSharp.dll" --strip -f -o lib\Assembly-CSharp.dll
```

Then build. With the game closed, this also copies the DLL into `BepInEx\plugins`:

```powershell
dotnet build -c Release
```

If your game is installed somewhere else, pass the path:

```powershell
dotnet build -c Release -p:GamePath="D:\SteamLibrary\steamapps\common\Graveyard Keeper 2"
```

Add `-p:DeployToGame=false` to build without copying, for example while the game is running.

## How it works

Belts in Graveyard Keeper 2 pull: each tick the game walks every belt line from its end back
towards its source, and each empty belt tile asks the thing behind it for an item. Whether a
belt is full is therefore only settled once the whole tick has run.

The mod uses [Harmony](https://github.com/BepInEx/HarmonyX) to refuse those pulls on
multi-output chests while the tick is running. When the tick's belt movement is finished it lets
each output belt run its normal pull, one at a time, starting with the side after the one that
was served last.

## License

[MIT](LICENSE). This is an unofficial mod, not affiliated with or endorsed by the developers or
publisher of Graveyard Keeper 2.
