# Agent team conventions

How the silo teammates (A-G) share this codebase. Read `CLAUDE.md` and `index.md` first; this file adds the team
rules on top. Branch from the Wave 0 (foundation) commit.

## What Wave 0 settled

- **No tech tree (TT-001).** Every building and recipe is available from the start. `Progression` is only the goal
  chain (`goals.json`, `UnlockCondition` types `produce`/`claim`/`ship`/`launch`, counters on `PlayerState`).
- **Building owner.** `PlacedBuilding.Owner` is the player who placed it (`PlaceBuildingCommand.Player`), the starting
  player for the start prefabs' blocks (`Simulation.SettleStartingTile`), or the unit's owner for blocks put back by
  landing (`UnitAssembly.Deploy`). It need not equal `TileState.Owner`; placement needs presence, not ownership (see Territory rules
  below). Frame cells have no owner: they belong to whoever owns the tile.
- **Hit points.** `BuildingDef.Hp` (`hp` in `buildings.json`, validated > 0) is one block's full HP.
  `PlacedBuilding.Hp` and `UnitBlock.Hp` are the current value (both `internal set`), carried across lift-off
  (`UnitAssembly.Activate`) and landing (`UnitAssembly.Deploy`), hashed in `StateHash`. Nothing deals damage yet (F).
- **Players.** `GameSetup.Players` (default 1, max `GameSetup.MaxPlayers`) is saved with the setup. Player ids are
  0..Players-1. `Simulation.NewGame` settles each on `StartTileIds[p]`; `StartTileId` is player 0's.
  `Simulation.ChooseStarts` is a placeholder farthest-point spread over tile-graph steps (A replaces it, OQ-22). The
  local player is still `SimHost.LocalPlayer` = 0, and the new-game menu has no player count yet (G).
- **Saves** are not backward compatible (greenfield). Bump `SaveFile.CurrentVersion` when replay results change.
- **One core kind (WM-016, OQ-37).** `BuildingKind.Core` only (no unit core): variants `core` (2x2, the starting
  unit's) and `core_large` (3x3) in buildings.json, each with `cost` (never empty), `hp`, `powerSupply`,
  `supplyRadius` and `capacity` (per item, validated > 0, cores only). Runtime stock is `PlacedBuilding.Stock`
  (hashed in `MixEntity`, in `BuildingSnap.Stock`); `TileGrid.Cores` lists them in id order. There is no
  `TileState.Inventory`. Every unit has exactly one core; on a unit its stock is `Unit.Cargo`.
- **Start (WM-005, FB-021).** No free core: `Simulation.SettleStartingTile` stamps the seat's start prefabs (default `scout`: thruster, 2x2 core,
  thruster on a 4x2 frame, `game/data/prefabs/scout.json`) nearest the circle centre, its core holding the prefab's `items`.
- **Lift-off and landing (UD-010).** `UnitAssembly.Launch` drains the core's stock and every lifted building's items
  (`TileEntities.Drain(b, into)`) into `Unit.Cargo`; `Deploy` puts the whole cargo back into the landed core.
- **Elimination.** `Simulation.CoreCount` / `Victory.HasCore` count cores on tiles and units (every unit has one).
- **Cell layers (FB-022).** `game/data/layers.json` declares per-cell layers bottom to top (deposit, platform, frame,
  cable) with stacking rules; platform/wire/frame buildings name theirs (`BuildingDef.Layer`). `TileGrid` keeps a byte per
  cell per layer (`EntryAt`, `Has(kind, x, y)`, `CountOn`, `SetEntry`/`Lay`/`Clear`); hashing, snapshots, `Placement.Fits`/
  `CanLift` and `TileView.DrawLayer` loop over all layers, so a new layer or a second entry needs no plumbing. Behaviour
  finds its layer by kind (`grid.Layers.Of(BuildingKind.Wire)`). Deposits are the item layer (`Layers.Deposit`; RS-004
  adds patches beside it). The frame is `ContentDb.Frame` (its cost in buildings.json); cores by kind (`DefaultCore`).
  Wrecks stay their own store.
- **Rate mode** books `FlowKind.Core` per core (`Flow.PortId` = the core's id), held to its capacity.
- **Tests:** hand-built tiles get funds from a test-only `bank` core (1x1, no power, huge capacity) via
  `Harness.Tile`/`Bank`/`Fund`. `Simulation.AddCore` puts a core down unpaid for tests and scenes (`Harness.PutCore`).
- **Test setup goes through `Harness` helpers.** Tests stock funds (`Fund`, `Fund(sim, tile, item, n)`, `Stock`,
  `PutCore`) and build units (`LayUnit`, `LayScout`, `LiftOff`, `Scout`: thruster, 2x2 core, blocks on a two-deep frame)
  only through them, never by hand, so the next rule change is one helper edit, not a hunt through every file. Lay a
  custom frame by hand only when the layout is the test's subject (lift-off validation, blueprint shapes). When a rule
  changes, delete the tests whose subject was replaced and write fresh ones for the new rule; don't patch old-design
  tests into shape. A test that breaks only through setup gets a helper; one that needs more than that is deleted.

## Combat conventions (silo F)

- All damage goes through `Combat.DamageBuilding` / `Combat.DamageBlock`; removal by force through
  `Combat.DestroyBuilding` / `Combat.DestroyUnit` (wreck left, held items lost). Don't write `Hp` elsewhere.
- Wrecks are a separate `TileGrid` layer, not `PlacedBuilding`s: the entity sim, pathing and collisions never see
  them. Placement-like checks must also test `TileGrid.WreckFree`/`WreckAt` (done in `Placement.Check`, `CheckFrame`,
  `CheckDeploy`).
- A dead unit block (Hp 0) stays in `Unit.Blocks`; anything that reads block function must skip `Hp <= 0`, and
  `Unit.RecomputeStats` rebuilds engines/cargo/radius/locomotion after a block dies or is rebuilt.
- `Territory.DestroyBuilding` forwards to `Combat.DestroyBuilding`, so every forced removal leaves a wreck.
- Attack orders are `Unit.Order` with `OrderKind.Attack`/`AttackMove` (building target: `TargetId` -1, cell in
  TileId/X/Y). Combat steers them only through `UnitSim.Chase`/`Halt`/`Resume`/`MayChase`/`CanFire`.

## Territory rules (silo A)

- **Presence, not ownership, gates entering, landing and lift-off** (WM-015): `Territory.HasPresence(sim, player, tile)`
  (owns it, or has a building or unit there). Nobody builds or lands with a cell in an enemy-owned circle while a
  building not of theirs stands there (`Territory.EnemyCircleBlocks`). New checks should use these, not `tile.Owner`.
- **Build radius gates every building operation** (WM-015, OQ-58): each cell touched must lie inside one of the
  player's radii on that tile (`Territory.InBuildRadius`; `Placement.Check` → `PlaceResult.OutOfRange`). Sources: major
  buildings (`BuildingDef.BuildRadius` > 0, from the footprint) and flying units (`UnitStats.BuildRadius`, live blocks).
  Placing, frames, blueprints, removal, taking up layers and wreck salvage use it; new operations (FB-023 cut, paste,
  graft) must too. Only a unit factory inside its own radius, match-setup stamps and scenario loads pass
  `ignoreRadius`. Drivers read `GameQuery.BuildRadii`; the planner stays inside radii and `Planner.Reach` extends them.
- **Who pays (FB-019, OQ-38, OQ-39):** `World/Funds.cs`. Paying on tile T (buildings, frames, blueprints, repairs) comes
  only from the player's cores standing on T, in id order, all or nothing: `Funds.CanPay` (read-only, for UI, AI,
  `GameQuery.CanAfford`) and `Funds.TryPay`. Never from neighbours, cores on units or other players. Refunds and drained
  items go to `Funds.Refund` (the player's core on T nearest the building; none → lost; not held to capacity). Belts
  respect capacity through `Funds.Accepts`. HUD stock is `Funds.Total` (world map: `GameQuery.ResourceTotals`).
- **Claims** have no command: `Expansion.Step` (phase 2) runs per-tile circle timers (`TileState.ClaimTicks`) and
  `Expansion.Claim` destroys rivals' buildings in the circle through `Territory.DestroyBuilding`.
- **Expansion path:** each start is a landed scout (thruster, core, thruster on frame) in its circle; build a second
  core at home first (else home has no funds once it leaves), lift it off with stock in its core, move it to
  `(tile.CircleX, tile.CircleY)`, land it, wait the capture time; its cargo is the new tile's funds.
- **Contested:** `Territory.IsContested` / `ContestedTiles`, computed live from unit positions.
- **Unclaimed buildings (WM-019):** owner `TileState.Unowned` (-1) once their seat's last core is destroyed
  (`Territory.AfterCoreLost`, called from `Combat.DestroyBuilding`/`DestroyUnit`). Any new stepping, power, targeting
  or radius code must skip `Owner < 0` (they do nothing); `sim.Player(owner)` needs `owner >= 0`. Ownership changes
  only through `Territory.TakeOver` (claims: `ClaimNear` after placing or landing), which bumps `Revision`.


## Tick order inside `Simulation.Step`

`Simulation.Step` (`src/Wunderwaffe.Sim/Simulation.cs`) runs these phases in this order. Today only the phases marked
"exists" have code; each silo adds its call at its place in the list, as one line calling a static `Step` in its own
file. Don't add empty stubs ahead of time.

| # | Phase | Owner | Call today / where it goes |
|---|---|---|---|
| 1 | Commands | shared | exists: the `_pending` loop (`LoggedCommand` into `_log`, then `Command.Apply`) |
| 2 | Territory / claim timers | A | exists: `Expansion.Step(this);` |
| 3 | Tile entities, incl. power and boosters | B | exists: `foreach (var tile in _tiles) TileEntities.Step(this, tile);` (power/boosters go inside `TileEntities.Step` or right beside it) |
| 4 | Combat (weapons, damage, wrecks) | F | exists: `Combat.Step(this);` |
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
