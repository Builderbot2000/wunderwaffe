# File index

One line per file: what it owns, key members, gotchas. Paths from repo root. Keep in sync with the code.

## src/Wunderwaffe.Sim (namespace `Wunderwaffe.Sim.*`)

| File | Owns |
|---|---|
| `Simulation.cs` | Core state: tiles, map, units, players, command log. `NewGame` (settles every player of the setup), `Step` (tick order; see `team.md`), `Enqueue`, lookups (`TryGetTile`, `TileAtCell`, `ResolveCell`, `Neighbors`, `UnitById`, `PlayerOrNull`), `StartTileId`/`StartTileIds`, `StateHash()`. Command handlers `TryPlace` (pays the plan's payer; platforms) / `TryRemove` (own buildings, not the last core; platforms), `CoreCount`. `TicksPerSecond`=60, `CoreBuildingId`. |
| `Simulation.Combat.cs` | Partial: `Projectiles` (spent ones linger a tick for drawing), projectile ids, alert throttle. |
| `Simulation.World.cs` | Partial: world building. `GenerateWorld` (runs Gen phases, `ChooseStarts` = fair starts OQ-22: seeded farthest-point attempts scored on regional reach, deposit evenness, spacing; regional deposits RS-003; circles), `AddTile`/`AddStartingTile` (hand-built square tiles for tests), `SettleStartingTile`, `CreateTile`, `RebuildTopology` (adjacency + zones), `CoreSpot` (nearest the circle centre)/`PlaceCore`, `PlaceScout` (starting landed scout), `FreeSpot`. `DefaultTileSize`=96. |
| `Balance.cs` | Code-side tuning: `StartingItems`, `CircleRadius`, `CircleClearance`, `CaptureSeconds`, `StartAttempts`, `FrameCost`, `RefundPercent`, `RegionalChancePercent`, land-share thresholds. |
| `GameSetup.cs` | Record (Width, Height, Seed, Players=1) in nominal tiles; `Default`, `ParseSeed` (text → FNV hash). |
| `Content/Defs.cs` | JSON records: `ItemDef`, `RecipeDef` (empty inputs = extraction), `BuildingDef` (`Hp` per block, +`Footprint(rotation)`), `BuildingKind` enum (`Platform` = water floor layer), `OnWater`, combat sub-records `WeaponDef`/`ShieldDef`/`RepairDef` + `Resist`/`SupplyRadius`/`Storage` (`DamageTaken`), `UnlockCondition` (goal conditions: `produce`/`claim`/`ship`/`launch`), `GoalDef`, `TerrainDef` (bands, deposit weights, `Ground`/`Naval` passability), `BuildingDef.Locomotion` (locomotor blocks). |
| `Content/ContentDb.cs` | Loads + validates all JSON (`FromJson`, throws `ContentException` with every error). Lookups: `Items`, `Recipes`, `Buildings`, `BuildingList` (menu order), `Goals`, `TerrainList`, `RecipesByBuilding`, `DefaultTerrain`, `RiverTerrain`, `Classify(elev, moist)`. |
| `Core/Command.cs` | Every player action as a JSON-polymorphic record with `Apply(sim)`: place/remove building, place/remove frame, activate/deploy/move units, attack/attack_building/attack_move, debug grant/damage/destroy, `view_tile` (rate mode, lockstep input). Claims have no command (circle timers). No tech tree, so no unlock command. `LoggedCommand`. |
| `Core/SaveFile.cs` | `SaveFile` (setup + log + hash, `CurrentVersion`), `Replay` (sliced re-simulation, `Matches`; starts from the save's `Snapshot` when it hash-checks, else full replay, `FromSnapshot`), `SaveException`. |
| `Core/StateHash.cs` | FNV-1a over tiles, buildings, entity state, players, units. Must cover all sim state. |
| `Core/Inventory.cs` | Sorted item counts (long); `Add`, `Has`, `TryTake` (all or nothing). Throws on underflow. |
| `Core/Rng.cs` | SplitMix64, seeded; `Next`, `Range`. |
| `Core/IntMath.cs` | `FloorDiv` (int/long), `ISqrt`. |
| `World/TileState.cs` | A territory: id, name, origin, centre, neighbours, biome, `Owner` (`Unowned`=-1), `Inventory`, `Grid`, `ToGlobal`, victory circle `CircleX`/`CircleY` + `InCircle`, `ClaimTicks` (per player). |
| `World/TileGrid.cs` | `PlacedBuilding` (def id, kind, `Owner` = placing player, `Hp`, footprint, rotation, recipe, runtime state incl. `Combat`). `TileGrid`: cell → building, deposits, mask/buildable/terrain, frames, platforms (count as buildable), zones, `Blank`, kind lists (`Belts`, `Machines`, `ExportPorts`, `ImportPorts`, `Factories`, `CombatBuildings`, `Suppliers`), wreck layer (`WreckAt`, `WreckFree`, `Wrecks`), `BuildingById`, `Revision` (bumped on every change). `InRect` = in box, `InBounds` = in tile. |
| `World/Entities.cs` | Runtime state: `BeltState` (4 items/cell, positions 0..128), `MachineState` (+`MachineStatus`), `ExportState` + `Shipment`. |
| `World/TileEntities.cs` | Per-tick tile sim: machines work and dump to neighbours, belts advance/hand off, export ports send/deliver. `TryInsert` = who accepts what. `Drain` returns a removed building's items. |
| `World/Placement.cs` | `Placement.Check` → `PlaceResult` + `PlacementPlan` (+`Payer`): presence and enemy circles (WM-015), bounds, overlap, terrain, port zones, recipe resolution (extractors pick by deposit), cost. |
| `World/PortRules.cs` | `ZoneDepth`, `TransitTicksPerTile`, `ZoneOf(footprint)`, `PartnerOf(port)`. |
| `World/Expansion.cs` | Circle claims (WM-006): `CaptureTicks`, `CircleOccupants` (cached by `Revision`), `Progress`, `Step` (tick phase 2), `Claim` (wipes rivals in the circle). |
| `World/Territory.cs` | `HasPresence` (WM-015), `EnemyCircleBlocks`, `Payer`/`RefundTo` (own tile else owned neighbour), `IsContested`/`ContestedTiles` (WM-004), `DestroyBuilding` (stand-in for combat's). |
| `World/Progression.cs` | Goal chain progress: `Progress`, `IsMet`, `CurrentGoal`, `Step` (end of tick). No tech tree (TT-001). |
| `World/PlayerState.cs` | Per-player counters (`Produced`, `Shipped`, `Launched`), goal completion ticks, `ViewedTile` (set by `SetViewedTileCommand` "view_tile", lockstep input; drives rate mode). |
| `World/WorldMap.cs` | Global grid: terrain, elevation, tile id per cell; adjacency (`MinBorderCells`=3), `BorderCells(from,to)`, `Grow` for hand-built worlds. |
| `World/TileGenerator.cs` | Deposit patches on a tile, weighted by terrain (`GenerateDeposits`, `exclude` for regional items, `richnessPercent`). |
| `World/Direction.cs` | N/E/S/W, `Dx`/`Dy`, `FromRotation` (0=east, clockwise), `Opposite`. |
| `World/Units.cs` | `Unit` rigid body (mass, inertia, radius, thrusters, max speed, `Locomotion`, cargo room, supply radius from live blocks via `RecomputeStats`), route/path state, `Order`, `Stance`, anchor, escort, `SquadId`, `BlockCombat`, `CoreBlock`, `Cargo`; `Stance` enum, `UnitBlock` (carries the building's `Hp`), `UnitCell`, `Thruster`. |
| `World/UnitAssembly.cs` | Frames (`CheckFrame`, place/remove), lift-off (`CheckActivate` flood-fills frame from a unit core and validates it (UD-003: power, locomotion seam, cap; lists `offending` blocks) → `Launch` spawns unit, ends in the `Built` hook), landing (`CheckDeploy` → `Deploy`; any tile, not in an enemy-held circle). Frames and lift-off need presence (WM-015) and pay via `Territory.Payer`. Result enums. `MaxCells`=64. |
| `World/UnitSim.cs` | Orders (`OrderKind`, `UnitOrder`, `Issue`/`Order`, hold/patrol/retreat, `Arrived`), formation `Slots`, stances (`Step`, `MayChase`/`Chase`/`Halt`/`Resume`/`CanFire`, `LeashCells`), squad ops + checks, rally (`CheckRally`, `OnUnitBuilt`), `AbstractWaypoints`/`CruiseSpeed` (UC-009). |
| `World/UnitPhysics.cs` | Per-tick flight controller (starts with `UnitSim.Step`), border portals (by route region), path following, tile tracking, collisions with buildings, impassable terrain and units. |
| `World/UnitPath.cs` | A* over current + next tile with clearance, terrain by locomotion (`UnitNav` regions), line-of-sight straightening; a blocked start heads for the nearest free cell. |
| `World/Trig.cs` | Integer CORDIC sin/cos/atan2; angles in 1/65536 turns, `One`=4096. |
| `World/Gen/TerrainGen.cs` | Phase 1: elevation (warped noise, rank-normalised), drainage → lakes/rivers, moisture, classification. `SeaLevel`=400. |
| `World/Gen/Partition.cs` | Phase 2: seeds by dart throwing, Dijkstra growth along terrain costs, merge tiny regions, renumber by position. |
| `World/Gen/Zones.cs` | Phase 3: per-tile I/O zones by multi-source BFS from borders. |
| `World/Gen/Noise.cs` | Integer value noise + fractal octaves. |
| `World/Gen/TileNames.cs` | Seeded made-up-language tile names, unique per world. |
| `World/Combat.cs` | Combat (tick phase 4): `Step` (recharge, resupply, `Attack`/`AttackMove` orders on `Unit.Order` via `UnitSim` chase/halt/resume, fire with squad focus `Squad.TargetId`, repair, shots), damage API (`DamageBuilding`, `DestroyBuilding`, `DamageBlock`, `DestroyUnit`, `ClearWreck`), queries (`Dps`, `StatusOf`, `UnitWeaponStatus`, `WeaponRange`, `LinkDisrupted`, `BlockWorld`). `CombatPower` seam (power, silo B). Alert seam in `Attacked`. |
| `World/CombatState.cs` | `WeaponStatus`, `CombatState` (ammo, cooldown, shield charge, target, repair credit), `Target`, `Wreck`, `Projectile`. |
| `World/ResourceRadius.cs` | FB-018: `SupplierFor(sim, unit)`, `Covers`, `Centre`, `Step` (every 30 ticks: magazines, cargo, turrets). |
| `World/Gen/Circles.cs` | Phase 4: victory circle per tile (WM-014): most deposit cells in the disc among centres clear of the border (Chebyshev depth). |
| `World/Blueprint.cs` | `Blueprint` (blocks + frame cells, top-left at 0,0), `BlueprintBlock`, `BlueprintCell`: plain data the library stores and commands carry (FB-009). |
| `World/Blueprints.cs` | Recording (`FromArea`, `FromUnitCore` only for valid designs, `FromUnit`), `Transform` (flip then quarter turns), `Check` → `BlueprintResult` + `BlueprintPlan` (per-block `Problems`, total `Cost`, paid via `Territory.Payer`), one-action `Place`. |
| `World/UnitStats.cs` | Derived unit stats (UD-002): cells, mass, HP, power draw/supply, cost, build ticks, DPS, upkeep, `IsWunderwaffe`; for a `Unit`, a `Blueprint` or a block list. Seam: `OnboardSupply` (B); DPS from `Combat.Dps`. |
| `World/UnitFactory.cs` | Unit factory (UD-006): `FactoryState` (projection, steps frame→systems→armament, buffer, status), `CheckProject` → `ProjectResult`, `Radius`/`BlockCap`/`BuildSpeed` (booster seam `BoosterBonus`), `Step` (after tile entities), `TryInsert` (belts), `Missing`, `StageProgress`, `Drain`. |
| `World/UnitUpkeep.cs` | Upkeep (UD-013): each minute a unit pays its blocks' upkeep from its cargo if that holds it all, else `ResourceRadius.SupplierFor`; unpaid → `Unit.Unsupplied`, orders dropped, retried each second. |
| `World/Locomotion.cs` | UD-004: `LocomotionClass` (ground/naval/air), `Locomotion.Check`/`Of` (class from block ids; rotor > hull > wheels/thrusters; hull+wheels conflict), `CanCross` (terrain). |
| `World/UnitNav.cs` | Terrain regions per (class, clearance), cached per sim, not hashed: `Regions`, `FindRoute` (region BFS → tiles + regions, goal snapping), `Portal` (border cell), `Plan` → `TravelPlan` (route + ETA for abstract travel). |
| `World/Squads.cs` | `Squad` (name, members in slot order, `Formation`, custom slots, stance, order, `TargetId` for combat), `Rally`, `Squads` registry on `Simulation.Squads` (hashed; `Clone`/`RestoreFrom` for snapshots). |
| `World/PowerNet.cs` | FB-005: `PowerNetwork` (members, live supply/demand updated by machines via `Report`, battery store, `Satisfaction` permille), `TilePower` on `TileGrid.Power` (rebuilt on `Revision`; cores, landed unit cores, wire and power blocks join what they touch), `PowerNet` queries (`Networks`, `Satisfaction`, `IsPowered`, `OnboardSupply` for units, `SpeedPermille`, `DrawOf`/`SupplyOf`), `Step` (before machines). Hashed. |
| `World/Boosters.cs` | FB-016 / OQ-25: `BoostStats`, `Boosters.StatsOf`/`Bonus(sim, tile, host, stat)`/`Of`; a booster boosts every matching host it touches, per-type caps from buildings.json, gantry chains count only in a straight line outward. |
| `World/Logistics.cs` | FB-004: sorter filter (`CheckFilter` → `FilterResult`, `SetFilter` for the filter command), `BridgeLink`. Junction/router/sorter/bridge/storage stepping lives in `TileEntities`. |
| `World/ProductionStats.cs` | FB-011 / IO-005: `TileState.Stats`, 10 s buckets over 10 minutes of made/used per item and sent per partner tile; `MadePerMinute`/`UsedPerMinute`/`SentPerMinute`, `MadeHistory`/`UsedHistory`, totals. Hashed. |
| `Simulation.Scaling.cs` | Partial (C): `RateModeEnabled` (on for `NewGame`, off for hand-built test sims), per-tile `TileRateState`, abstract nodes (`Abstract`, `IsAbstract`), `SnapshotIntervalTicks`/`LatestSnapshot` (taken at tick start), snapshot restore helpers. |
| `Core/Snapshot.cs` | NF-008 state snapshot (`Capture`, `Restore`): tiles, buildings + entity state, rate accumulators, players, units, abstract nodes. Every new piece of sim state needs a field here too, or snapshot loads fall back to full replay. |
| `World/RateMode.cs` | FB-012..014 tick phase 6: steps rate-mode tiles at their rates (stalls when an import buffer or export link can't cover a tick), runs `AbstractTravel`, then picks each tile's mode for the next tick (viewed by any player or `ContestedTiles` → full). `IsFullSim`, `RatesOf`. Belts/machines freeze in rate mode (never touches `Working`/`IdleTicks`), so switching is lossless. Records made/used/sent in `ProductionStats`. |
| `World/RateModel.cs` | Solves per-item rates from the building/belt graph (OQ-14), per group of port-linked tiles, iterating until imports/exports agree, so shortfalls propagate (FB-014). Machines at boosted speed/output and their power network's steady-state satisfaction (`Rebalance`; generators by fuel burnt, batteries ignored). Fixed point: `One` = 1 item/tick (`Shift`=24). |
| `World/TileRates.cs` | `FlowKind` (Core, Produced, Draw, Export, Used), `Flow` (sorted, hashed, saved), `TileRates` (`Flows`, `RateOf`, `Total`). |
| `World/AbstractTravel.cs` | UC-009: units on rate-mode tiles become `AbstractNode`s moving along planned waypoints at cruise speed with no physics; materialise on promotion. `Eta`, `TolerancePercent`. |
| `World/StressScene.cs` | NF-001 benchmark world (8 viewed full-sim tiles x 2,000 buildings + 200 rate-mode factories), shared by `StressTests` and `--stress`; `Fill`. |

## game/ (Godot project; namespaces `Wunderwaffe.Game[.Input/.Render/.Screens/.Debug]`)

| File | Owns |
|---|---|
| `project.godot` | Main scene `scenes/title.tscn`, autoload `SimHost`, 1920x1080 canvas_items stretch. |
| `scenes/title.tscn` | Root Control with `TitleScreen`. |
| `scenes/main.tscn` | Match scene; node order matters for input (PauseMenu first sees Esc last). Wires `[Export]`s: UnitTool→BuildTool, Camera→WorldMapView, Hud→BuildTool/WorldMapView/UnitTool/PauseMenu/BlueprintTool, BlueprintTool→BuildTool. |
| `autoload/SimHost.cs` | `SimHost.Instance`. Owns `Content`, `Sim`, tick loop (`_Process`, max 8 ticks/frame), `ViewMode` (Tile/World) + `ViewedTile`, `Selected` units, `WatchView`/`ShowOnlyIn` helpers, new/save/load (`BeginLoad` replays over frames), quit, CLI flags (`--tour`, `--stress`, `--drive`), F3/F4/Tab. `SyncView` sends `view_tile` on every view change; `StartWith` opens a prebuilt sim. |
| `input/InputActions.cs` | Action names + default key bindings (registered in code). |
| `input/BuildTool.cs` | Build palette (`Tools`, one per building or per recipe for multi-recipe crafters, + frame), selection, `BuildRotation`, placement/removal on mouse, ghost and port-zone drawing, `Preview`, `HoveredZone`, `PickedIsPort`, `HoveredCell`. |
| `input/UnitTool.cs` | Unit selection (click/box, Shift adds), move/patrol orders (tile and world map), H hold, X retreat, Q squad, T formation, V stance, E escort, Y rally, Ctrl/Alt+1-9 control groups (UI-only), G = lift off / land (land also on the world map), `Status` text, `Notice` (unreachable moves, via `UnitNav.Plan`). Active when no building is picked. |
| `input/CameraController.cs` | Pan/zoom; keeps separate camera per view; fits the world map. |
| `render/Palette.cs` | `CellPx`=16, placeholder colours (spread by id), terrain colours, `Zone`, `Owner`. |
| `render/Sprites.cs` | `res://assets/sprites/{id}.png` lookup with cache; null if missing (callers fall back to colours). |
| `render/TileView.cs` | Tile layer: terrain image, MultiMesh layers (ground, deposits, frames, buildings, belt items), outline/zones. Redraws on `Revision`. `Rect` (quad transform), `CellAt` (canvas pos → cell). |
| `render/TileShapes.cs` | Cached per-tile geometry: border outline, zone runs/outlines/label points. |
| `render/MachineStatusView.cs` | Pulsing dots on stopped machines (FB-006). |
| `render/CombatView.cs` | Wrecks, dead unit blocks, HP bars, shots/beams, shields, no-ammo "!", resource radius of selected units / hovered supplier. |
| `render/UnitView.cs` | Units on the tile view (blocks rotated by heading, selection, destination line). `PixelPos`, `InView`. |
| `render/WorldMapView.cs` | World layer: snapshot texture, owner tint, borders, labels, victory circles with claim arcs, Contested outlines, port links, unit dots; select/enter input, C sends units to the circle. `CellPx`=2, `CellAt`, `Center`, `Fit`. |
| `render/WorldSnapshot.cs` | Downsampled picture of every tile's contents for the world map; `Refresh` repaints changed tiles. |
| `ui/Ui.cs` | Theme, colours, font sizes, control builders (`Label`, `Button`, `Panel`, `Row`, `Column`, `Icon`, `Items`, `Pin`, `Overlay`, `Clear`, `Clock`). |
| `ui/GameText.cs` | Player-facing wording: `ItemName`, `Cost`, `Recipe`, `ConditionUnit`, `TerrainName`, `Plural`. |
| `ui/Hud.cs` | HUD layout, help/complete overlays, per-frame update, `RefreshSlow` (5 Hz). |
| `ui/Hud.Objectives.cs` | Goal panel, toasts (`Toast`), goal notifications. |
| `ui/Hud.SidePanel.cs` | Left panel: tile inventory; world-map tile profile/deposits/victory circle progress/ports; Send units button. Cached by `_sideKey`. |
| `ui/Hud.BuildBar.cs` | Build buttons, afford tinting, tooltips, placement message per `PlaceResult`. |
| `ui/Hud.Hover.cs` | Hover panel: building/deposit under cursor, machine status, port problems. |
| `ui/TitleScreen.cs` | Title menu, new game form, load progress bar, handles `SimHost.PendingLoad`. |
| `ui/PauseMenu.cs` | Esc menu (pauses tree), save/load/settings/quit; `LoadMenu` (save list, shared with title). |
| `ui/GameSettings.cs` | `user://settings.cfg`: fullscreen, vsync, world-map snapshot interval; settings panel. |
| `ui/SaveSlots.cs` | `user://saves/*.json` listing (`SaveInfo`), new path, delete. |
| `debug/DebugTools.cs` | `Cheat` (F4, items only), `StressFill` (F3, the viewed tile), `StressWorld` (`--stress`, opens `StressScene`). |
| `debug/StressBench.cs` | Node that prints ms/tick over 600 ticks after a stress fill; quits if headless. |
| `debug/DebugDriver.cs` | `--drive`: scripted unit session through injected input, screenshot. |
| `debug/UiTour.cs` | `--tour`: scripted click-through of all screens with screenshots; cleans up its saves and its blueprint. |
| `input/BlueprintTool.cs` | Blueprint mouse modes (`BlueprintMode`): capture area, place ghosts (R turn, F mirror), project from the selected factory; K records the hovered unit core; factory selection; outlines blocks that stop a lift-off. `Notice` event for toasts. After UnitTool in main.tscn so it sees clicks first. |
| `ui/BlueprintLibrary.cs` | `user://blueprints.json`, shared across matches: `BlueprintEntry` (name, tags, blueprint), `Add`, `Rename`, `SetTags`, `Duplicate`, `Delete`, `Revision`. |
| `ui/Hud.Blueprints.cs` | Library panel (B): entries with stats, rename/tags/duplicate/delete/place/build in factory. Factory panel: status, per-stage progress (WW-004), buffer, missing. |
| `data/*.json` | Content: items, recipes, buildings (order = build menu), goals (order = chain), terrain (order = classification; supports `//` comments). |
| `ui/Hud.Stats.cs` | O: production panel for the viewed tile (FB-011): per item made/used per minute + `HistoryGraph`. |
| `ui/HistoryGraph.cs` | Small line chart of one item's 10-minute made/used history. |

## tests/Wunderwaffe.Sim.Tests (xUnit, real `game/data` content)

| File | Covers |
|---|---|
| `Harness.cs` | `NewSim` (empty sim, real content), `Tile` (hand-built, stocked), `Open`, `Place`, `Ore`, `Belt`, `Run`. Players `Me`=0, `Them`=1. |
| `TestContent.cs` | Loads the real JSON from the output dir; `Load(edit)` with buildings.json edited. |
| `TimingCollection.cs` | Non-parallel collection for wall-time tests; `Timing.PreferFastCores` (opts the test host out of Windows power throttling, which parked it on E-cores at 2-2.5x the tick time). |
| `ContentTests.cs` | Content loads; validation errors. |
| `PlacementTests.cs` | Bounds, overlap, deposits, recipe choice, rotation, terrain. |
| `EntityTests.cs` | Machines, belts (speed, throughput, corners, merging, spacing), dumping, removal returns items. |
| `PortTests.cs` | Zones, pairing, transit, backpressure. |
| `ProgressionTests.cs` | Everything available from the start, goal chain, counters, goal validation. |
| `WorldTests.cs` | Hand-built tile topology, `ResolveCell`, claiming, hash. |
| `WorldGenTests.cs` | Generation determinism, irregular tiles, regional deposits, names, multi-player starts, timing. |
| `UnitTests.cs` | Frames, lift-off, physics, pathing, borders, collisions, landing. |
| `SaveTests.cs` | Save/replay round trip, every command kind serialises, divergence detection. |
| `SimulationTests.cs` | Determinism, starting tile. |
| `StressTests.cs` | NF-001 tick budget with thousands of buildings, and the `StressScene` (8 full + 200 rate-mode tiles) under 4 ms/tick. `Timing.PreferFastCores`, warm-up, best of several windows. |
| `DemoLoopTests.cs` | Plays the whole `goals.json` chain with no cheats (scout lift-off, landing in the coal circle): content changes that break the loop fail here. |
| `TerritoryTests.cs` | Presence, paying from a neighbour (buildings, frames, blueprints), circle claims and wipes, enemy circles, landing and lifting off again from an unowned tile, Contested, cores, platforms, circle placement, fair starts, starting scout vs UD-003/UD-004, unreachable circles. |
| `BlueprintTests.cs` | Recording, identical rebuild (UD-001), transforms, all-or-nothing placement, lift-off validation, stats, Wunderwaffe class, upkeep. |
| `FactoryTests.cs` | Factory builds block by block from belts and lifts off, refusals (cap, radius, blocked), megafactory needs boosters, building blueprints block it, determinism. Uses `TestContent.Load(edit)` for ore-priced unit parts. |
| `MovementTests.cs` | Locomotion classes, terrain-aware routes on a generated world, hold/patrol/retreat, squads, formations, stances, rally, travel plan ETA. |
| `CombatTests.cs` | Turrets, ammo/logistics, lasers, shields, per-block damage, wrecks, attack orders, resource radius, repair, interdiction, determinism. |
| `CombatStressTests.cs` | ~280 gunships and turrets in a firefight within a tick budget. |
| `PowerTests.cs` | Core power, wire networks, fuel generator, batteries, shortfall slows machines, unit onboard supply. |
| `BoosterTests.cs` | OQ-25 boosters: touch rule, multi-host, caps, gantry straight chains, factory radius/cap query, Precision Jig gate. |
| `LogisticsTests.cs` | Junction, router, sorter filter, bridge, storage. |
| `RateModeTests.cs` | Rate mode vs full sim within 5% over 5 min, rates from the graph, promotion/demotion (view, Contested) loses nothing, import shortfalls propagate along a chain, power shortfall, boosters, fuel generator and stats. |
| `AbstractTravelTests.cs` | Abstract nodes, travel time vs physics, materialising when viewed or Contested. |
| `SnapshotTests.cs` | Snapshot restore matches, latest snapshot + later commands match a full replay, 30-minute determinism (`Category=Long`, a few seconds). |

## Other

`README.md` (player-facing behaviour, design notes), `.claude/team.md` (agent-team conventions: tick order, silo file ownership, rules for shared hotspots), `tools/pixellab_gen.py` (sprite generation via PixelLab API, `PIXELLAB_API_KEY` from env or `.env`), `art/` (sprite pipeline: `jobs.json`, palette, mockups; see `art/README.md`), `.vscode/` (Play/Open Editor launch configs, build/test tasks).
