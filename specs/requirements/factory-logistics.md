# Factory & Logistics

P2 · Each tile is a real-time factory map where you build.

Where it stands (2026-10-10): Drills, conveyors, smelters, a steel mill and the tile core on terrain-aware tile maps. Machines push output into neighbours; belts carry 7.5 items/s. Every tile runs the full entity sim: rate mode is decided but not built. No power, fluids, storage, defences or builders yet.

## Requirements

| ID | Feature | Priority | Status | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|
| [FB-001](#fb-001-tile-local-map) | Tile local map | Must | Done | MVP | Functional | P2 | WM-002 |  |  |
| [FB-002](#fb-002-building-placement) | Building placement | Must | Done | MVP | Functional | P2 | FB-001 |  |  |
| [FB-003](#fb-003-extractors) | Extractors | Must | Done | MVP | Functional | P2 | FB-002, RS-001 | Updated |  |
| [FB-004](#fb-004-logistics-conveyors) | Logistics: conveyors | Must | In Progress | MVP | Functional | P2 | FB-002 |  |  |
| [FB-005](#fb-005-power) | Power | Must | Proposed | MVP | Functional | P2 | FB-002 | Updated |  |
| [FB-006](#fb-006-processing-buildings) | Processing buildings | Must | Done | MVP | Functional | P2 | RS-002 |  |  |
| [FB-007](#fb-007-storage) | Storage | Must | In Progress | MVP | Functional | P2 | FB-002 |  |  |
| [FB-008](#fb-008-construction-drones--builder) | Construction drones / builder | Should | Rejected | Alpha | Functional | P2 | FB-002 |  |  |
| [FB-009](#fb-009-blueprint-library) | Blueprint library | Must | Proposed | MVP | Functional | P2 | FB-002 | Updated |  |
| [FB-010](#fb-010-defensive-structures) | Defensive structures | Must | Proposed | MVP | Functional | P2 | FB-002, CB-001 |  |  |
| [FB-011](#fb-011-production-statistics) | Production statistics | Should | Proposed | Alpha | Functional | P2 | FB-006 |  |  |
| [FB-012](#fb-012-background-tile-simulation-rate-mode) | Background tile simulation (rate mode) | Must | Approved | MVP | Non-Functional | P2 | FB-001, NF-001 |  |  |
| [FB-013](#fb-013-full-sim-promotion--demotion) | Full-sim promotion / demotion | Must | Approved | MVP | Non-Functional | P2 | FB-012, CB-001 | Updated |  |
| [FB-014](#fb-014-rate-model-reacts-to-supply) | Rate model reacts to supply | Must | Approved | MVP | Functional | P2 | FB-012, IO-002 |  |  |
| [FB-015](#fb-015-fluids--pumps) | Fluids & pumps | Could | Deferred | Alpha | Functional | P2 | FB-005 | New |  |
| [FB-016](#fb-016-building-boosters) | Building boosters | Must | Proposed | MVP | Functional | P2 | FB-002 | New |  |
| [FB-017](#fb-017-buildings-on-water) | Buildings on water | Must | Proposed | MVP | Functional | P1 | WM-014, WM-011 | New |  |
| [FB-018](#fb-018-resource-radius) | Resource radius | Should | Proposed | Alpha | Functional | P3 | FB-007, CB-004 | New |  |

### FB-001 Tile local map

**Requirement:** Each tile shall contain its own real-time local map (grid-based, Mindustry-like) where buildings are placed.

**Acceptance criteria:** Entering a tile loads its local grid map with terrain and deposits matching the world tile.

**As built:** A tile's local map is its bounding box in the world map; cells of neighbouring tiles show dimmed and can't be built on. Terrain and deposits match the world.

### FB-002 Building placement

**Requirement:** The player shall place, rotate and remove buildings on the tile grid, subject to footprint and terrain rules.

**Acceptance criteria:** Invalid placement is previewed in red and rejected; removal refunds a defined % of cost.

**As built:** Place (left-click/drag), rotate (R), remove (right-click/drag). Green/red ghost uses the sim's own check. Refund is 100% (Balance.RefundPercent), and items held by the building go back to the tile inventory.

### FB-003 Extractors

**Requirement:** The game shall provide extractor buildings (drills) that harvest deposits under their footprint.

**Acceptance criteria:** Drill on ore deposit outputs ore at a defined rate; on no deposit it cannot be placed.

**As built:** Drill (2x2) mines whichever deposit covers most of its footprint at 1 item/s; refused off-deposit. No pumps (no fluids yet).

**Notes:** Was: 'Extractor buildings (drills, pumps)'. Pumps moved to FB-015 with fluids (OQ-21, 2026-10-10).

### FB-004 Logistics: conveyors

**Requirement:** The game shall provide conveyors, junctions, routers, sorters and bridges to move items between buildings.

**Acceptance criteria:** Items travel on belts at defined speed; sorters filter by item type.

**As built:** Conveyors only: up to 4 items per cell, 7.5 items/s, side-loading, never feeds back into the cell it faces. No junctions, routers, sorters or bridges.

### FB-005 Power

**Requirement:** The game shall provide power: each tile core supplies a limited amount of free power with no input; generators burn fuel to make more; placeable conductor (wire) blocks power whatever they touch, joining sources and consumers into networks. Starting production buildings draw little power, so the core alone can run enough of them to build proper power infrastructure.

**Acceptance criteria:** A new player can run their first drills, smelters and a fuel line on core power alone; generators turn fuel into power; buildings stop or slow when their network falls short; UI shows each network's balance; the power value in buildings.json is the draw.

**As built:** Not built. buildings.json carries a 'power' value (steel mill 30) that nothing reads yet.

**Notes:** Was: 'Pipes for fluids and a power network (generators, nodes, batteries)'. See OQ-21. OQ-21 decided 2026-10-10. Fluids split to FB-015. Long-range power nodes may come later as a separate building. Units carry their own power (UD-002). Core power and fuel generators decided 2026-10-10. Content needed (A silo): a fuel item and recipe, a fuel generator, and a battery block usable on tiles and on units (UD-003).

### FB-006 Processing buildings

**Requirement:** The game shall provide crafting buildings (smelters, refineries, assemblers) that convert input items into output items via recipes.

**Acceptance criteria:** Each recipe has defined inputs, outputs and time; building idles with indicator when starved.

**As built:** Smelter (recipe chosen per placement) and steel mill, from recipes.json. Machines buffer 2 batches and push output into any neighbour in turn. Amber/red dot after 1.5 s starved or blocked; hover panel says why.

### FB-007 Storage

**Requirement:** The game shall provide storage buildings and a tile core with a shared inventory.

**Acceptance criteria:** Items delivered to core are added to tile inventory and usable for construction.

**As built:** Tile core with a shared inventory that pays for building. No separate storage buildings.

### FB-008 Construction drones / builder

**Requirement:** Building shall be performed by builder units/drones that consume resources over time rather than instant placement.

**Acceptance criteria:** Placed blueprints show as ghosts until builder delivers resources; build progress visible.

**As built:** Not built: placement is instant.

**Notes:** Rejected 2026-10-10: placing by hand stays instant. Factories (UD-006) are the gradual builders.

### FB-009 Blueprint library

**Requirement:** The player shall save buildings and unit designs as blueprints in one library, shared across games, and load a blueprint as a ghost preview on the tile map to build from.

**Acceptance criteria:** Save by area selection or from a unit core (UD-001); the library supports rename, duplicate, tag and delete, but not editing blocks; a loaded blueprint shows as rotatable, flippable ghosts and is placed in one action, paying its total cost (refused if any block can't be placed or afforded); the library lives in user data and persists across matches.

**As built:** Not built.

**Notes:** Was: 'Save a selection of buildings as a blueprint and paste it on any owned tile (Should, Alpha)'. OQ-16 decided 2026-10-10; absorbs UD-005. Modelled on Mindustry schematics. Ghost guide vs one-click placement: OQ-26. OQ-26 decided 2026-10-10: one-action placement for now.

### FB-010 Defensive structures

**Requirement:** The game shall provide turrets, walls and shields buildable on a tile.

**Acceptance criteria:** Turrets auto-target enemies in range and consume ammo/power.

**As built:** Not built.

**Notes:** No Rush comes from defence being cost-effective early (NR-006).

### FB-011 Production statistics

**Requirement:** Each tile shall track production/consumption rates per item over time.

**Acceptance criteria:** Stats panel shows per-minute rates and a 10-minute history graph.

**As built:** Not built.

**Notes:** Rate-mode tiles report their aggregated rates into the same history (OQ-11).

### FB-012 Background tile simulation (rate mode)

**Requirement:** Tiles not being viewed by any player shall not run a full entity simulation; instead they advance as aggregated per-item production, consumption and I/O rates.

**Acceptance criteria:** Leaving a stable tile for 5 min yields the same net output (±5%) as watching it for 5 min.

**As built:** Not built: every tile runs the full entity sim. Rates will be computed from the belt graph and checked against the full sim (OQ-14).

**Notes:** Updated 2026-10-05: decision OQ-11

### FB-013 Full-sim promotion / demotion

**Requirement:** A tile shall switch to full simulation when any player views it or it is Contested (enemy units on it, WM-004), and back to rate mode when neither is true.

**Acceptance criteria:** Enemy units entering an unviewed tile trigger full sim before the first shot and materialise abstract units on it at their world positions (UC-009); leaving returns it to rate mode with no item loss/duplication.

**As built:** Not built.

**Notes:** Updated 2026-10-05: decision OQ-11 Contested defined by OQ-20 (2026-10-10).

### FB-014 Rate model reacts to supply

**Requirement:** Rate-mode output shall scale down when imports, power or deposits fall short, and propagate the shortfall to dependent tiles.

**Acceptance criteria:** Cutting an import link to a rate-mode tile reduces its outputs within 1 tick-batch; restoring it recovers them.

**As built:** Not built.

**Notes:** Updated 2026-10-05: decision OQ-11

### FB-015 Fluids & pumps

**Requirement:** The game shall provide pumps and pipes for fluids.

**Acceptance criteria:** Pump on a fluid source outputs fluid; pipes carry it to consumers.

**As built:** Not built.

**Notes:** Split from FB-003/FB-005 on 2026-10-10 (OQ-21): wanted for content variety, not MVP.

### FB-016 Building boosters

**Requirement:** Any building may connect to adjacent booster buildings that improve how it works. Each booster only works with certain building categories and under placement rules, so arranging them is a packing puzzle.

**Acceptance criteria:** Boosters, their categories, adjacency rules, effects and stacking are data-driven and validated; the hover panel shows a building's boosted stats; removing a booster reverts its effect at once.

**As built:** Not built.

**Notes:** OQ-9 decided 2026-10-10. Factories (UD-006) get their radius and block cap from boosters. First boosters and rules: OQ-25. Starter booster set proposed in OQ-25.

### FB-017 Buildings on water

**Requirement:** Some buildings shall be placeable on water, so ocean tiles can be built on and their victory circles held.

**Acceptance criteria:** At least one cheap building (e.g. a floating platform that other buildings can stand on) is placeable on deep and shallow water; terrain.json and building data say which buildings may stand on water.

**As built:** Not built.

**Notes:** Follows from OQ-24 (2026-10-10). The platform design is a proposal; confirm.

### FB-018 Resource radius

**Requirement:** Storage buildings, cores and units with storage or power blocks shall each have a circular resource radius within which they transmit power, fuel and ammo to friendly units and buildings that need them (modelled on From the Depths).

**Acceptance criteria:** A unit inside a friendly radius receives the power, fuel and ammo it uses and pays its upkeep from that source, with no logistics needed; units supply each other the same way; the radius shows when a store or unit is selected; outside every radius a unit lives off its onboard batteries, generators and storage.

**As built:** Not built.

**Notes:** OQ-30 decided 2026-10-10. Radius sizes and sharing order to be set when built.

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-11](#oq-11-are-unvisited-tiles-fully-simulated-or-abstracted-to-rates) | Are unvisited tiles fully simulated or abstracted to rates? | FB-012, NF-001 | Decided | Owner | 2026-10-05 |
| [OQ-14](#oq-14-how-are-a-tiles-background-rates-derived-measured-from-the-last-n-seconds-of-full-sim-or-computed-from-the-building-graph) | How are a tile's background rates derived: measured from the last N seconds of full sim, or computed from the building graph? | FB-012, FB-014 | Decided | As built; confirm | 2026-10-10 |
| [OQ-15](#oq-15-how-do-units-travel-across-tiles-nobody-is-viewing) | How do units travel across tiles nobody is viewing? | UC-005, IO-003, FB-013 | Decided | Owner | 2026-10-10 |
| [OQ-21](#oq-21-is-power-still-in-scope-for-mvp-and-does-the-power-value-in-buildingsjson-mean-draw-per-second) | Is power still in scope for MVP, and does the 'power' value in buildings.json mean draw per second? | FB-005 | Decided | Owner | 2026-10-10 |
| [OQ-25](#oq-25-what-are-the-first-boosters-and-what-are-their-rules) | What are the first boosters, and what are their rules? | FB-016, UD-006 | Provisional | Proposal; confirm | 2026-10-10 |
| [OQ-26](#oq-26-is-a-loaded-blueprint-only-a-ghost-guide-for-building-by-hand-or-can-it-be-placed-in-one-action-paying-the-whole-cost) | Is a loaded blueprint only a ghost guide for building by hand, or can it be placed in one action, paying the whole cost? | FB-009 | Decided | Owner | 2026-10-10 |

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
