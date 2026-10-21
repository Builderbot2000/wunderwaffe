# Factory & Logistics

P2 · Each tile is a real-time factory map where you build.

Where it stands (2026-10-17): Drills, belts, junctions, routers, sorters, bridges, storage, crafters, power networks, boosters, platforms on water, turrets and walls, blueprints, production stats and resource radii are built. Unviewed tiles run in rate mode. Each core holds its own stock, which funds building on its tile. Per-cell layers (deposits, platforms, frame, cables) are declared in content (FB-022). Tiles are 256 cells (OQ-35). Fluids deferred.

## Requirements

| ID | Feature | Priority | Status | Built | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|---|
| [FB-001](#fb-001-tile-local-map) | Tile local map | Must | Done | Yes | MVP | Functional | P2 | WM-002 | Updated |  |
| [FB-002](#fb-002-building-placement) | Building placement | Must | Done | Yes | MVP | Functional | P2 | FB-001 |  |  |
| [FB-003](#fb-003-extractors) | Extractors | Must | Done | Yes | MVP | Functional | P2 | FB-002, RS-001 | Updated |  |
| [FB-004](#fb-004-logistics-conveyors) | Logistics: conveyors | Must | Done | Yes | MVP | Functional | P2 | FB-002 |  |  |
| [FB-005](#fb-005-power) | Power | Must | Done | Yes | MVP | Functional | P2 | FB-002 | Updated |  |
| [FB-006](#fb-006-processing-buildings) | Processing buildings | Must | Done | Yes | MVP | Functional | P2 | RS-002 |  |  |
| [FB-007](#fb-007-storage) | Storage | Must | Done | Yes | MVP | Functional | P2 | FB-002 | Review |  |
| [FB-008](#fb-008-construction-drones--builder) | Construction drones / builder | Should | Rejected | No | Alpha | Functional | P2 | FB-002 |  |  |
| [FB-009](#fb-009-blueprint-library) | Blueprint library | Must | Done | Yes | MVP | Functional | P2 | FB-002 | Updated |  |
| [FB-010](#fb-010-defensive-structures) | Defensive structures | Must | Done | Yes | MVP | Functional | P2 | FB-002, CB-001 |  |  |
| [FB-011](#fb-011-production-statistics) | Production statistics | Should | Done | Yes | Alpha | Functional | P2 | FB-006 |  |  |
| [FB-012](#fb-012-background-tile-simulation-rate-mode) | Background tile simulation (rate mode) | Must | Done | Yes | MVP | Non-Functional | P2 | FB-001, NF-001 |  |  |
| [FB-013](#fb-013-full-sim-promotion--demotion) | Full-sim promotion / demotion | Must | Done | Yes | MVP | Non-Functional | P2 | FB-012, CB-001 | Updated |  |
| [FB-014](#fb-014-rate-model-reacts-to-supply) | Rate model reacts to supply | Must | Done | Yes | MVP | Functional | P2 | FB-012, IO-002 |  |  |
| [FB-015](#fb-015-fluids--pumps) | Fluids & pumps | Could | Deferred | No | Alpha | Functional | P2 | FB-005 | New |  |
| [FB-016](#fb-016-building-boosters) | Building boosters | Must | Done | Yes | MVP | Functional | P2 | FB-002 | New |  |
| [FB-017](#fb-017-buildings-on-water) | Buildings on water | Must | Done | Yes | MVP | Functional | P1 | WM-014, WM-011 | New |  |
| [FB-018](#fb-018-resource-radius) | Resource radius | Should | Done | Yes | Alpha | Functional | P3 | FB-007, CB-004 | New |  |
| [FB-019](#fb-019-build-funds-from-cores) | Build funds from cores | Must | Done | Yes | MVP | Functional | P2 | WM-016, FB-007 | Review |  |
| [FB-020](#fb-020-power-layer-nodes-and-cross-tile-power) | Power layer, nodes and cross-tile power | Must | Done | Yes | MVP | Functional | P3 | FB-005, IO-002 | New |  |
| [FB-021](#fb-021-prefab-blueprints) | Prefab blueprints | Must | Done | Yes | MVP | Functional | P2 | FB-009, WM-005 | New |  |
| [FB-022](#fb-022-generic-cell-layers) | Generic cell layers | Should | Done | Yes | Alpha | Non-Functional | P2 | FB-001, FB-017, FB-020, UD-010 |  |  |
| [FB-023](#fb-023-building-relocation) | Building relocation | Should | Approved | No | Alpha | Functional | P2 | FB-002, UD-010, WM-019 | New |  |

### FB-001 Tile local map

**Requirement:** Each tile shall contain its own real-time local map (grid-based, Mindustry-like) where buildings are placed.

**Acceptance criteria:** Entering a tile loads its local grid map with terrain and deposits matching the world tile; a nominal tile is 256x256 cells (OQ-35), the scale of a mid-size Mindustry map.

**As built:** A tile's local map is its bounding box in the world map; cells of neighbouring tiles show dimmed and can't be built on. Terrain and deposits match the world. Nominal tile is 256x256 (Simulation.DefaultTileSize, OQ-35); deposit patches scaled to 16 per item per nominal tile, radius 4-10. Entering a tile centres the camera on its victory circle at 0.75 zoom.

**Notes:** Size raised from a 96x96 nominal tile after playtesting 2026-10-17 (OQ-35).

### FB-002 Building placement

**Requirement:** The player shall place, rotate and remove buildings on the tile grid, subject to footprint and terrain rules.

**Acceptance criteria:** Invalid placement is previewed in red and rejected; removal refunds 100% of cost (wrecks 25%, CB-006).

**As built:** Place (left-click/drag), rotate (R), remove (right-click/drag). Green/red ghost uses the sim's own check. Refund is 100% (Balance.RefundPercent), and items held by the building go back to the tile inventory.

**Notes:** Owner, 2026-10-17: 100% back on an intact block or building, 25% on a wreck (CB-006).

### FB-003 Extractors

**Requirement:** The game shall provide extractor buildings (drills) that harvest deposits under their footprint.

**Acceptance criteria:** Drill on ore deposit outputs ore at a defined rate; on no deposit it cannot be placed.

**As built:** Drill (2x2) mines whichever deposit covers most of its footprint at 1 item/s; refused off-deposit. No pumps (no fluids yet).

**Notes:** Was: 'Extractor buildings (drills, pumps)'. Pumps moved to FB-015 with fluids (OQ-21, 2026-10-10).

### FB-004 Logistics: conveyors

**Requirement:** The game shall provide conveyors, junctions, routers, sorters and bridges to move items between buildings.

**Acceptance criteria:** Items travel on belts at defined speed; sorters filter by item type.

**As built:** Conveyors (4 items per cell, 7.5 items/s, side-loading, never feed the cell they face), junction, router, sorter (filter set by command, Logistics.CheckFilter) and bridge (Logistics.BridgeLink); stepping in TileEntities (tested).

### FB-005 Power

**Requirement:** The game shall provide power: each tile core supplies a limited amount of free power with no input; generators burn fuel to make more; placeable conductor (wire) blocks power whatever they touch, joining sources and consumers into networks. Starting production buildings draw little power, so the core alone can run enough of them to build proper power infrastructure.

**Acceptance criteria:** A new player can run their first drills, smelters and a fuel line on core power alone; generators turn fuel into power; buildings stop or slow when their network falls short; UI shows each network's balance; the power value in buildings.json is the draw.

**As built:** World/PowerNet.cs: cores (20) and landed unit cores (10) supply power; cables (a layer under buildings, FB-020) join what they touch into networks; the fuel generator burns fuel from the fuel press; batteries store surplus; a short network slows every consumer by the same share; the top bar shows each network's balance. 'power' in buildings.json is the draw.

**Notes:** Was: 'Pipes for fluids and a power network (generators, nodes, batteries)'. See OQ-21. OQ-21 decided 2026-10-10. Fluids split to FB-015. Power nodes, a separate cable layer and power over port links: FB-020 (OQ-42). Units carry their own power (UD-002). Core power and fuel generators decided 2026-10-10. Content needed (A silo): a fuel item and recipe, a fuel generator, and a battery block usable on tiles and on units (UD-003).

### FB-006 Processing buildings

**Requirement:** The game shall provide crafting buildings (smelters, refineries, assemblers) that convert input items into output items via recipes.

**Acceptance criteria:** Each recipe has defined inputs, outputs and time; building idles with indicator when starved.

**As built:** Smelter (recipe chosen per placement), steel mill, munitions works and fuel press, from recipes.json. Machines buffer 2 batches and push output into any neighbour in turn. Amber/red dot after 1.5 s starved or blocked; the hover panel says why.

### FB-007 Storage

**Requirement:** The game shall provide storage buildings and cores; each core holds its own stock.

**Acceptance criteria:** Items delivered into a core are added to that core's stock, up to its variant's capacity (OQ-38), and count towards its owner's build funds on that tile (FB-019).

**As built:** Each core has its own stock (PlacedBuilding.Stock), filled by belts and machines up to its variant's per-item capacity (Funds.Accepts: a full core refuses, so belts back up; rate mode holds to it too), and counted in its owner's funds on its tile (FB-019). Storage buildings: container, supply depot and ammo rack (load from belts, unload onto belts, give items back when removed).

**Notes:** Was: 'storage buildings and a tile core with a shared inventory' / 'Items delivered to core are added to tile inventory and usable for construction'. Per-core stock after playtesting 2026-10-17 (FB-019, WM-016).

### FB-008 Construction drones / builder

**Requirement:** Building shall be performed by builder units/drones that consume resources over time rather than instant placement.

**Acceptance criteria:** Placed blueprints show as ghosts until builder delivers resources; build progress visible.

**As built:** Not built: placement is instant.

**Notes:** Rejected 2026-10-10: placing by hand stays instant. Factories (UD-006) are the gradual builders. Owner, 2026-10-17: no automatic rebuilding of destroyed buildings for now; drone-based construction may replace instant placement later, and rebuilding would be settled then.

### FB-009 Blueprint library

**Requirement:** The player shall save buildings and unit designs as blueprints in one library, shared across games, and load a blueprint as a ghost preview on the tile map to build from.

**Acceptance criteria:** Save by area selection or from a unit core (UD-001); the library supports rename, duplicate, tag and delete, but not editing blocks; a loaded blueprint shows as rotatable, flippable ghosts and is placed in one action, paying its total cost (refused if any block can't be placed or afforded); the library lives in user data and persists across matches.

**As built:** ui/BlueprintLibrary.cs (user://blueprints.json: rename, tags, duplicate, delete); World/Blueprints.cs (record an area, or a valid unit core with K; flip then turn; all-or-nothing Check; one-action Place paying the total); input/BlueprintTool.cs (ghosts, R turns, F mirrors).

**Notes:** Was: 'Save a selection of buildings as a blueprint and paste it on any owned tile (Should, Alpha)'. OQ-16 decided 2026-10-10; absorbs UD-005. Modelled on Mindustry schematics. Ghost guide vs one-click placement: OQ-26. OQ-26 decided 2026-10-10: one-action placement for now.

### FB-010 Defensive structures

**Requirement:** The game shall provide turrets, walls and shields buildable on a tile.

**Acceptance criteria:** Turrets auto-target enemies in range and consume ammo/power.

**As built:** Gun turret (shells from the munitions works), laser turret (power, no ammo), wall and shield generator; turrets target enemies in range (World/Combat.cs).

**Notes:** No Rush comes from defence being cost-effective early (NR-006).

### FB-011 Production statistics

**Requirement:** Each tile shall track production/consumption rates per item over time.

**Acceptance criteria:** Stats panel shows per-minute rates and a 10-minute history graph.

**As built:** World/ProductionStats.cs: 10 s buckets over 10 minutes per tile, rate mode included. O opens the production panel (Hud.Stats): made/used per minute per item and a 10-minute HistoryGraph.

**Notes:** Rate-mode tiles report their aggregated rates into the same history (OQ-11).

### FB-012 Background tile simulation (rate mode)

**Requirement:** Tiles not being viewed by any player shall not run a full entity simulation; instead they advance as aggregated per-item production, consumption and I/O rates.

**Acceptance criteria:** Leaving a stable tile for 5 min yields the same net output (±5%) as watching it for 5 min.

**As built:** World/RateMode.cs + RateModel.cs: tiles nobody views and that aren't Contested step at per-item rates solved from the building/belt graph; tested within 5% of the full sim over 5 minutes.

**Notes:** Updated 2026-10-05: decision OQ-11

### FB-013 Full-sim promotion / demotion

**Requirement:** A tile shall switch to full simulation when any player views it or it is Contested (enemy units on it, WM-004), and back to rate mode when neither is true.

**Acceptance criteria:** Enemy units entering an unviewed tile trigger full sim before the first shot and materialise abstract units on it at their world positions (UC-009); leaving returns it to rate mode with no item loss/duplication.

**As built:** RateMode picks each tile's mode every tick: full when any player views it (view_tile) or it is Contested, else rate mode. Switching neither loses nor duplicates items (tested); abstract units materialise on promotion.

**Notes:** Updated 2026-10-05: decision OQ-11 Contested defined by OQ-20 (2026-10-10).

### FB-014 Rate model reacts to supply

**Requirement:** Rate-mode output shall scale down when imports, power or deposits fall short, and propagate the shortfall to dependent tiles.

**Acceptance criteria:** Cutting an import link to a rate-mode tile reduces its outputs within 1 tick-batch; restoring it recovers them.

**As built:** RateModel iterates per group of port-linked tiles until imports and exports agree, so cutting an import reduces what depends on it along a chain and restoring it recovers; power shortfalls and boosters are modelled (tested).

**Notes:** Updated 2026-10-05: decision OQ-11

### FB-015 Fluids & pumps

**Requirement:** The game shall provide pumps and pipes for fluids.

**Acceptance criteria:** Pump on a fluid source outputs fluid; pipes carry it to consumers.

**As built:** Not built.

**Notes:** Split from FB-003/FB-005 on 2026-10-10 (OQ-21): wanted for content variety, not MVP.

### FB-016 Building boosters

**Requirement:** Any building may connect to adjacent booster buildings that improve how it works. Each booster only works with certain building categories and under placement rules, so arranging them is a packing puzzle.

**Acceptance criteria:** Boosters, their categories, adjacency rules, effects and stacking are data-driven and validated; the hover panel shows a building's boosted stats; removing a booster reverts its effect at once.

**As built:** World/Boosters.cs with the OQ-25 starter set in buildings.json (overdrive coil, yield sieve, heat exchanger, assembly arm, gantry rail, feeder hub, servo bank, coolant tower, precision jig): touch rule, per-type caps, straight gantry chains, removal reverts at once. The hover panel lists boosted stats and their boosters.

**Notes:** OQ-9 decided 2026-10-10. Factories (UD-006) get their radius and block cap from boosters. First boosters and rules: OQ-25. Starter booster set proposed in OQ-25.

### FB-017 Buildings on water

**Requirement:** Some buildings shall be placeable on water, so ocean tiles can be built on and their victory circles held.

**Acceptance criteria:** At least one cheap building (e.g. a floating platform that other buildings can stand on) is placeable on deep and shallow water; terrain.json and building data say which buildings may stand on water.

**As built:** Platform (2 iron ore) goes only on water and is buildable floor for other buildings (TileGrid platforms, BuildingDef.OnWater); tested.

**Notes:** Follows from OQ-24 (2026-10-10). The platform design is a proposal; confirm.

### FB-018 Resource radius

**Requirement:** Storage buildings, cores and units with storage or power blocks shall each have a circular resource radius within which they transmit power, fuel and ammo to friendly units and buildings that need them (modelled on From the Depths).

**Acceptance criteria:** A unit inside a friendly radius receives the power, fuel and ammo it uses and pays its upkeep from that source, with no logistics needed; units supply each other the same way; the radius shows when a store or unit is selected; outside every radius a unit lives off its onboard batteries, generators and storage.

**As built:** World/ResourceRadius.cs: cores, storage buildings and units with storage supply power, fuel and ammo within their radius every 30 ticks; units supply each other; upkeep is paid from the supplier in reach. CombatView draws the radius of selected units and hovered suppliers.

**Notes:** OQ-30 decided 2026-10-10. Radius sizes and sharing order to be set when built.

### FB-019 Build funds from cores

**Requirement:** What a player can spend on a tile shall be the sum of the items held by their cores standing on that tile as buildings. Cores on units don't count, and other tiles' stocks can't be spent there.

**Acceptance criteria:** Placing buildings, frames and blueprints and lifting off on tile T are paid only from the player's landed cores on T (no paying from neighbouring tiles); with no core on T the player can't pay for anything there; the HUD stock shows that sum for the viewed tile (world map: all landed cores); landing a unit adds its core's cargo to the sum and lifting off takes it away; starting items go into the starting unit's core; payment order and capacity follow OQ-38, refunds and lost cores OQ-39.

**As built:** World/Funds.cs. Buildings, frames, blueprints and repairs on a tile are paid only from the player's cores standing there, lowest id first, all or nothing (CanPay/TryPay, OQ-38); never from neighbours, other players or units. Refunds, held items of removed buildings and wreck refunds go to the player's nearest core on the tile, else are lost (Refund, OQ-39). HUD stock is Funds.Total for the viewed tile (world map: GameQuery.ResourceTotals over every standing core). Lift-off moves a core's stock into Unit.Cargo and landing back; starting items are in the starting unit's core. There is no tile inventory. Tested (TerritoryTests).

**Notes:** Added after playtesting 2026-10-17. Replaces paying from an owned neighbour: expanding means landing a unit, whose core brings its cargo as the new tile's funds. Rate mode books core deliveries per player per core (TileRates, FlowKind.Core).

### FB-020 Power layer, nodes and cross-tile power

**Requirement:** Power cables shall lie on their own layer, under buildings, like frame. A power node building shall join into one network every generator, battery and other node within its radius, as in Mindustry. A linked export/import port pair shall carry power between the two tiles, joining their networks.

**Acceptance criteria:** A cable can be laid under a building or belt, and a building placed over a cable, without either blocking the other; cables still join what they touch. A node shows its radius while placed and joins every generator, battery and node inside it into its network; removing it splits the network again. With a linked port pair, a generator on one tile powers consumers on the other, and both tiles' power panels show the shared network. Rate mode uses the joined network's satisfaction.

**As built:** World/PowerNet.cs, the cable layer of the generic cell layers (FB-022: hashed, in snapshots). The 'wire' building (named Cable) is laid on the cable layer: it only clashes with another cable, buildings and belts stand on it, and it joins every power building on or edge-beside its run; removing on a cell takes the building first, then the cable, then a platform. Power Node (power_node, powerRadius 8): joins every generator (anything with powerSupply), battery and node whose footprint is within its radius (straight-line between nearest cells), not consumers; removing it splits the network again; the build tool shades its radius and lines to what it would join. A linked port pair (export port in the partner's zone, import port on the partner in this tile's zone, whoever owns them) joins the parts the two ports touch, with no carry limit: networks are world-wide objects (PowerWorld), each tile's power panel lists those with members on it and names the tiles it is shared with. A shared network balances once per tick, when its first full-sim tile steps. Rate mode (RateModel.Rebalance) sums supply and demand over every port-linked tile's graph on the network. Tested (PowerLayerTests).

**Notes:** Owner, 2026-10-17 (OQ-42). Built 2026-10-17 (silo B): nodes do not join consumers and port links have no carry limit; revisit either if playtests ask. Decided while building: A node joins only generators, batteries and nodes by radius; consumers join by touching or cable. A port link carries any amount of power and works whoever owns the two tiles (OQ-45). Ports conduct: they join what they touch or a cable beside them.

### FB-021 Prefab blueprints

**Requirement:** The blueprint library shall come with prefab blueprints (buildings and unit designs) that the player can pick from like their own, and the same prefabs shall set up what stands on the map before the match starts, such as each player's starting unit.

**Acceptance criteria:** Prefabs are content: blueprint files shipped with the game data, loaded and validated like the other content (every block id exists, rotations and frame cells valid) and refused with an error otherwise. They show in the library alongside the player's own, marked as prefabs; they can be placed, projected by a factory and duplicated like any blueprint, but not renamed, retagged or deleted. Match setup places each player's starting layout from prefabs named in the setup (with the items their cores start with), instead of code; a map or setup can name extra prefabs for a seat, which is how an AI gets a head start (OQ-47). The AI picks its designs from the prefabs through the same queries as everyone else.

**As built:** game/data/prefabs/*.json, one PrefabDef each (id, name, tags, blocks, frames, items for the first core), loaded by ContentDb.FromJson and validated (Prefabs.Problems: block ids, recipes, rotations 0..3, overlaps, repeated frame cells, grid from (0, 0), unit blocks on frame, items need a core); errors refuse to load. GameSetup.StartPrefabs names prefab ids per seat (default "scout"; extras placed beside the first are an AI head start); Simulation.SettleStartingTile stamps them (Blueprints.Stamp), replacing the code start and Balance.StartingItems. One shipped prefab, scout.json, equals the old start with 600 copper ore + 300 iron ore. The library lists prefabs after the player's own, marked PREFAB, placeable, projectable, duplicable, not renamable/retaggable/deletable. GameQuery.Prefabs/Prefab list them (PrefabTests). The AI picks its unit design from GameQuery.Prefabs (AiDesigns.Pick, MP-006). No menu option sets StartPrefabs yet.

**Notes:** Owner, 2026-10-17: define the system first; which prefabs ship is a later content and balance update. Saves already store the setup, so a save replays with the prefabs it was started with only while content doesn't change (OQ-34).

### FB-022 Generic cell layers

**Requirement:** A tile's per-cell layers (buildings, deposits, frame, platforms, cables, and any added later) shall be declared in content and stored, hashed, snapshotted, checked and drawn by one generic mechanism, instead of each layer having its own hand-written code path.

**Acceptance criteria:** Each layer is declared in content with an id and stacking rules (what it must sit on, what may stand on it, whether it blocks buildings); each layer-placed building names its layer in buildings.json. Every cell of a layer stores which definition it holds (0 = empty), with a side table only for layers needing per-cell state (owner, HP). Set/has/count, clearing, StateHash, Snapshot, the placement check (from the stacking rules) and the tile renderer each loop over the layers once, with no per-layer code. Behaviour stays in code keyed by kind and reads the generic layer: cables joining power networks, platforms as buildable floor, frame flood-filled at lift-off. Frame becomes a building entry on the frame layer with its cost in buildings.json (no Balance.FrameCost); deposits become a layer whose cells point at their patch (RS-004); scenarios save and load layers through one serialiser (WM-017). Two definitions on the same layer (say two cable tiers) need no code change. Wrecks stay their own store: they carry entity data and aren't placed by players.

**As built:** game/data/layers.json declares the layers bottom to top (deposit, platform, frame, cable), each with sitsOn (ground, water or a layer below), floor, underBuildings, blocksBuildings, holds (buildings or items) and draw (tiled, icon, links); Content/Layers.cs resolves them (LayerSet: by kind, the deposit layer). Platform, wire and frame buildings name their layer in buildings.json (validated: 1x1, every building of a kind on one layer). TileGrid stores one byte per cell per layer (entry index, 0 = empty) with per-entry counts; EntryAt/Has/CountOn/SetEntry, ClearForRestore, StateHash.MixLayers, Snapshot LayerSnap (runs per layer, deposits included), Placement.Fits/CanLift (stacking rules) and TileView.DrawLayer each loop over the layers. Removing on a cell with no building takes up the topmost layer entry unless something on it needs it. Deposits are the item layer (no patches yet: RS-004). No per-cell side table yet (no layer needs one). Frame, cable and platform behaviour reads the layer by kind. A second cable tier is content only (LayerTests).

**Notes:** Owner, 2026-10-18 (OQ-52). Do before deposit depletion (RS-004) and scenarios (WM-017), alongside MP-007.

### FB-023 Building relocation

**Requirement:** The player shall be able to cut a building they own and paste it, intact, elsewhere on a tile they own, instead of removing it for a refund and building it again. To take a building off its tile, they graft it onto a vehicle: paste it onto one of their landed units and fly it out. This covers buildings they claimed (WM-019), including buildings and unit blocks their faction cannot build (MP-009).

**Acceptance criteria:** Cut and paste keeps the building's type, HP, recipe, settings and held items; it is neither refunded nor paid for again. Pasting on the ground needs a tile the player owns (WM-004); on any other tile a building they own can only be grafted onto one of their landed units there. A graft is pasted onto the unit's frame as a block, and the unit must still pass lift-off validation (UD-003) to fly it out; after landing it can be cut off again. The paste spot is checked by the placement rules (presence, bounds, overlap, layers, terrain, port zones; not cost or the faction's catalogue) and previewed from the same check; a refused paste leaves the building where it was. A building the player's faction cannot build still works where it is pasted, on the ground or as a unit block; a blueprint holding it can be recorded, but only placed where one they own is pasted. Cut and paste are commands, so they replay and stay in sync (NF-004).

**As built:** Not built.

**Notes:** Owner, 2026-10-20: lets players take buildings and blocks from defeated neutral factions instead of only salvaging them for resources (CB-006, RS-005), since they may hold tech that can't be built otherwise. Also the way a capture-to-build faction gets its first copy of a building (MP-010). Cut and paste; leaving a tile means grafting onto a vehicle (OQ-53).

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-11](#oq-11-are-unvisited-tiles-fully-simulated-or-abstracted-to-rates) | Are unvisited tiles fully simulated or abstracted to rates? | FB-012, NF-001 | Decided | Owner | 2026-10-05 |
| [OQ-14](#oq-14-how-are-a-tiles-background-rates-derived-measured-from-the-last-n-seconds-of-full-sim-or-computed-from-the-building-graph) | How are a tile's background rates derived: measured from the last N seconds of full sim, or computed from the building graph? | FB-012, FB-014 | Decided | As built; confirm | 2026-10-10 |
| [OQ-15](#oq-15-how-do-units-travel-across-tiles-nobody-is-viewing) | How do units travel across tiles nobody is viewing? | UC-005, IO-003, FB-013 | Decided | Owner | 2026-10-10 |
| [OQ-21](#oq-21-is-power-still-in-scope-for-mvp-and-does-the-power-value-in-buildingsjson-mean-draw-per-second) | Is power still in scope for MVP, and does the 'power' value in buildings.json mean draw per second? | FB-005 | Decided | Owner | 2026-10-10 |
| [OQ-25](#oq-25-what-are-the-first-boosters-and-what-are-their-rules) | What are the first boosters, and what are their rules? | FB-016, UD-006 | Provisional | Proposal; confirm | 2026-10-10 |
| [OQ-26](#oq-26-is-a-loaded-blueprint-only-a-ghost-guide-for-building-by-hand-or-can-it-be-placed-in-one-action-paying-the-whole-cost) | Is a loaded blueprint only a ghost guide for building by hand, or can it be placed in one action, paying the whole cost? | FB-009 | Decided | Owner | 2026-10-10 |
| [OQ-38](#oq-38-with-several-cores-on-a-tile-which-pays-and-how-much-can-a-core-hold) | With several cores on a tile, which pays, and how much can a core hold? | FB-019, WM-016, FB-007 | Decided | Owner | 2026-10-17 |
| [OQ-39](#oq-39-where-do-refunds-go-and-what-happens-to-a-lost-cores-items) | Where do refunds go, and what happens to a lost core's items? | FB-019, WM-016, FB-002 | Decided | Owner | 2026-10-17 |
| [OQ-42](#oq-42-can-power-cross-tiles-and-do-cables-share-the-building-layer) | Can power cross tiles, and do cables share the building layer? | FB-005, FB-020, IO-002 | Decided | Owner | 2026-10-17 |
| [OQ-52](#oq-52-are-cell-layers-hand-coded-or-generic) | Are cell layers hand-coded or generic? | FB-022, FB-017, FB-020, RS-004, WM-017 | Decided | Owner | 2026-10-18 |
| [OQ-53](#oq-53-how-does-a-moved-building-get-to-its-new-place) | How does a moved building get to its new place? | FB-023, UD-010, IO-002, MP-010 | Decided | Owner | 2026-10-20 |

### OQ-11 Are unvisited tiles fully simulated or abstracted to rates?

**Options / notes:** Rate abstraction is cheaper; full sim needed for combat.

**Decision:** Rate mode in background; full sim only when viewed by any player or combat present (FB-012..014).

### OQ-14 How are a tile's background rates derived: measured from the last N seconds of full sim, or computed from the building graph?

**Options / notes:** Measured is simple but wrong for tiles left before steady state; computed is robust but must model belt/power limits.

**Decision:** Computed from the belt/building graph and checked against the full sim (README).

### OQ-15 How do units travel across tiles nobody is viewing?

**Options / notes:** Abstract to travel time between gates, materialising only on full-sim tiles.; Today units are always fully simulated in world coordinates.

**Decision:** Outside rendered tiles a unit is an abstract node: an icon on the world map with recorded stats, not rendered or physically simulated. It follows a terrain-legal route. Units render (and skirmish, under auto or player control) when enemy units share the tile or the tile is opened (UC-009).

### OQ-21 Is power still in scope for MVP, and does the 'power' value in buildings.json mean draw per second?

**Options / notes:** The field exists (steel mill 30) but nothing reads it.

**Decision:** In scope. Conductor (wire) blocks power whatever they touch; power nodes may come later as their own building. Units need onboard power covering their engines. Fluids are still wanted but not in MVP (FB-015).

### OQ-25 What are the first boosters, and what are their rules?

**Options / notes:** Per booster: categories it works with, adjacency/shape rule, effect (speed, radius, block cap...), stacking limits, cost.

**Decision:** Starter set. Rules: a booster must touch its host; it boosts every matching host it touches (sharing a booster is part of the puzzle); per-type caps per host; all values in buildings.json. Base factory 3x3, radius 6, block cap 16. FACTORY: Assembly Arm 2x2 (+16 block cap); Gantry Rail 1x3 (+4 radius, only counts in a straight chain running outward from the factory); Feeder Hub 1x2 (an extra input side and double input buffer); Servo Bank 2x2 (+50% build speed, draws power, half effect unless it also touches a Coolant Tower); Coolant Tower 2x2 (lets up to 2 touching Servo Banks run at full rate); Precision Jig 2x2 (needed to build tier-3+ and weapon blocks). PRODUCTION: Overdrive Coil 1x1 (+25% speed for drills, smelters and mills, +power, max 2 per host); Yield Sieve 1x2 (drills only, +20% output); Heat Exchanger 2x2 (crafters only, -20% power for each host it touches). A megafactory is a factory with many Arms and long Gantry chains.

### OQ-26 Is a loaded blueprint only a ghost guide for building by hand, or can it be placed in one action, paying the whole cost?

**Options / notes:** Mindustry pastes plans that builders fill in; here hand placement is instant, so one-click placement is possible.

**Decision:** Placed in one action for now, paying the total cost (FB-009).

### OQ-38 With several cores on a tile, which pays, and how much can a core hold?

**Options / notes:** Pay in id order, nearest first, or largest first; unlimited stock or a capacity per core.

**Decision:** Pay from the player's landed cores on the tile in id order, all or nothing; this may become more elaborate later. Each core has a storage capacity set by its variant (OQ-37).

### OQ-39 Where do refunds go, and what happens to a lost core's items?

**Options / notes:** Where refunds and items drained from removed buildings go; what happens to a destroyed core's items; what a full core does with deliveries; whether capacity is per item or in total; whether storage next to a core adds to it (Mindustry links adjacent vaults to the core).

**Decision:** Refunds and drained items go to the player's core on that tile nearest the building; with no core there, removal still works and the items are lost; a destroyed core's items are lost (salvage later, RS-005); capacity is per item, as in Mindustry, and a full core refuses deliveries so belts back up; storage linking to cores deferred.

### OQ-42 Can power cross tiles, and do cables share the building layer?

**Options / notes:** Built: wire is a building block, and networks stay inside one tile.

**Decision:** Port links carry power as well as items. Cables go on their own layer. Add a power node building that joins all generators, batteries and other nodes within its radius, like Mindustry (FB-020).

### OQ-52 Are cell layers hand-coded or generic?

**Options / notes:** Built: each layer (frame, platform, cable, deposits, wrecks) has its own array and repeats the same code across hashing, snapshots, placement and rendering, in about 15 files; platform and cable store only a bool. Common practice: content-declared layers (Mindustry's floor, overlay and block per cell; Factorio's tiles and entities) with shared plumbing and behaviour keyed by kind.

**Decision:** Generic layers declared in content with stacking rules, one store per layer by definition index, shared plumbing, behaviour in code by kind; frame and deposits move onto it, wrecks stay separate (FB-022).

### OQ-53 How does a moved building get to its new place?

**Options / notes:** (a) Pick up and put down on the same tile only: simplest, but a site's tech could never leave its tile. (b) Pack it: picking a building up turns it into a packed item (one per building type, carrying its HP) held by the nearest own core on the tile or the unit standing on it; it travels like any item, in unit cargo (UD-010) or over port links (IO-002), and is put down from the build menu for free. Reuses inventories, cargo, links, hashing and snapshots; the held items drain as on removal (OQ-39). (c) A unit carries it physically, as a slung load adding mass: the most visible, the most work (physics, UD-003 validation, a new attachment rule). Also to decide: whether a move takes time or a fee, whether HP is kept, and whether a building can be picked up under fire or inside an enemy-held circle. Leaning: (b), picking up needs presence (WM-015) and is instant, no fee, HP kept.

**Decision:** Cut and paste. Once a player owns a building (built it, or claimed it unclaimed, WM-019), they can cut it and paste it elsewhere on a tile they own. Taking it off a tile means grafting it onto a vehicle: pasted onto a landed unit, which then flies it out (FB-023).
