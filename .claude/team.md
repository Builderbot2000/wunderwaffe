# Agent team conventions

How the silo teammates (A-G) share this codebase. Read `CLAUDE.md` and `index.md` first; this file adds the team
rules on top. Branch from the Wave 0 (foundation) commit.

## What Wave 0 settled

- **No tech tree (TT-001).** Every building and recipe is available from the start. `Progression` is only the goal
  chain (`goals.json`, `UnlockCondition` types `produce`/`claim`/`ship`/`launch`, counters on `PlayerState`).
- **Building owner.** `PlacedBuilding.Owner` is the player who placed it (`PlaceBuildingCommand.Player`), the tile's
  owner for a tile core (`Simulation.PlaceCore`), or the unit's owner for blocks put back by landing
  (`UnitAssembly.Deploy`). It need not equal `TileState.Owner`; placement rules still require owning the tile (silo A
  changes that). Frame cells have no owner: they belong to whoever owns the tile.
- **Hit points.** `BuildingDef.Hp` (`hp` in `buildings.json`, validated > 0) is one block's full HP.
  `PlacedBuilding.Hp` and `UnitBlock.Hp` are the current value (both `internal set`), carried across lift-off
  (`UnitAssembly.Activate`) and landing (`UnitAssembly.Deploy`), hashed in `StateHash`. Nothing deals damage yet (F).
- **Players.** `GameSetup.Players` (default 1, max `GameSetup.MaxPlayers`) is saved with the setup. Player ids are
  0..Players-1. `Simulation.NewGame` settles each on `StartTileIds[p]`; `StartTileId` is player 0's.
  `Simulation.ChooseStarts` is a placeholder farthest-point spread over tile-graph steps (A replaces it, OQ-22). The
  local player is still `SimHost.LocalPlayer` = 0, and the new-game menu has no player count yet (G).
- **Saves** are not backward compatible (greenfield). Bump `SaveFile.CurrentVersion` when replay results change.

## Tick order inside `Simulation.Step`

`Simulation.Step` (`src/Wunderwaffe.Sim/Simulation.cs`) runs these phases in this order. Today only the phases marked
"exists" have code; each silo adds its call at its place in the list, as one line calling a static `Step` in its own
file. Don't add empty stubs ahead of time.

| # | Phase | Owner | Call today / where it goes |
|---|---|---|---|
| 1 | Commands | shared | exists: the `_pending` loop (`LoggedCommand` into `_log`, then `Command.Apply`) |
| 2 | Territory / claim timers | A | after the command loop, before the tile loop |
| 3 | Tile entities, incl. power and boosters | B | exists: `foreach (var tile in _tiles) TileEntities.Step(this, tile);` (power/boosters go inside `TileEntities.Step` or right beside it) |
| 4 | Combat (weapons, damage, wrecks) | F | after the tile loop, before `UnitPhysics.Step` |
| 5 | Unit physics and steering | E | exists: `UnitPhysics.Step(this);` |
| 6 | Rate mode / abstract travel | C | after `UnitPhysics.Step`, before `Progression.Step` |
| 7 | Goals / victory | G | exists: `Progression.Step(this);` (victory checks go right after it) |
| 8 | `Tick++` | shared | exists: last line |

D's calls: `UnitFactory.Step(this)` right after the tile loop (end of phase 3: factories build and lift off), and
`UnitUpkeep.Step(this)` right before `UnitPhysics.Step` (after combat). Every lift-off, by hand or from a factory, goes
through `UnitAssembly.Launch` and ends in its `Built(sim, unit, tile, source)` hook: that is where rally/auto-squad
(E, UC-006) and the unit-completed alert (G) belong.

C owns the scheduling in `Simulation.Step` (for example rate-mode tiles skipping phase 3); other silos only insert
their one call.

## Silo ownership

Owners make the structural changes in their files; others make only small additive edits there and say so in their
report.

| Silo | Owns |
|---|---|
| A: territory | `World/Gen/*`, `World/Expansion.cs`, ownership rules in `World/Placement.cs`, `UnitAssembly.CheckDeploy`, start selection (`Simulation.ChooseStarts`, `Simulation.World.cs`) |
| B: production | `World/TileEntities.cs`, `World/Entities.cs`, content in `buildings.json` / `recipes.json` / `items.json` |
| C: scale | scheduling in `Simulation.Step`, `Core/SaveFile.cs`, new `RateMode` files |
| D: unit build | blueprints, `UnitAssembly` lift-off and validation (`CheckActivate`, `Activate`, frames), unit factory |
| E: unit movement | `World/UnitPath.cs`, `World/UnitSim.cs`, steering in `World/UnitPhysics.cs`, `game/input/UnitTool.cs` |
| F: combat | new `Combat` files, weapons, wrecks, resource radius, all damage to `Hp` |
| G: game and UI | query API, AI, networking, victory, menus (`TitleScreen`, `PauseMenu`), HUD alerts |

## Shared hotspots

These files are touched by several silos: `Core/StateHash.cs`, `Core/Command.cs`, `ContentDb.Validate`,
`game/data/buildings.json`, `game/ui/Hud.cs`, `.claude/index.md` and `Simulation.Step`.

- Keep edits small and additive. Append new entries at the end of their section (a new `Mix` at the end of the
  method that covers the object, a new `[JsonDerivedType]` after the last one, a new check at the end of its loop in
  `Validate`, a new building at the end of `buildings.json`, a new index row at the end of its table).
- Never reorder, reformat or rename existing lines there. That turns a clean merge into a conflict.
- New command tags are snake_case and unique (for example `set_rate_mode`); never reuse a removed tag
  (`debug_unlock` is gone).
- New fields on `BuildingDef` take a default so other silos' JSON still loads, unless every building must set it.
- Hash every new piece of sim state in `StateHash`, in the same change.
