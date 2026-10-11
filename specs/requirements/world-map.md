# World Map

P1 · The world is split into tiles; you start on one and expand.

Where it stands (2026-10-10): One seeded world (terrain with rivers and lakes) cut into irregular tiles. A single player starts on a central land tile and claims neighbours by paying ore. Tab switches between the world map and a tile.

## Requirements

| ID | Feature | Priority | Status | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|
| [WM-001](#wm-001-tiled-world-map) | Tiled world map | Must | Done | MVP | Functional | P1 |  | Updated |  |
| [WM-002](#wm-002-tile-attributes) | Tile attributes | Must | Done | MVP | Functional | P1 | WM-001 |  |  |
| [WM-003](#wm-003-tile-adjacency) | Tile adjacency | Must | Done | MVP | Functional | P1 | WM-001 |  |  |
| [WM-004](#wm-004-tile-ownership) | Tile ownership | Must | In Progress | MVP | Functional | P1 | WM-001 | Updated |  |
| [WM-005](#wm-005-starting-tile) | Starting tile | Must | In Progress | MVP | Functional | P1 | WM-004 | Updated |  |
| [WM-006](#wm-006-tile-claim-by-victory-circle) | Tile claim by victory circle | Must | Proposed | MVP | Functional | P1 | WM-014, WM-015 | Updated |  |
| [WM-007](#wm-007-zoom-between-layers) | Zoom between layers | Must | Done | MVP | Functional | P1 | WM-001, FB-001 |  |  |
| [WM-008](#wm-008-fog-of-war-world) | Fog of war (world) | Won't | Rejected | Alpha | Functional | P1 | WM-004 |  |  |
| [WM-009](#wm-009-procedural-world-generation) | Procedural world generation | Should | Done | Alpha | Functional | P1 | WM-002 |  |  |
| [WM-010](#wm-010-neutral--environmental-tiles) | Neutral / environmental tiles | Could | Proposed | Beta | Functional | P1 | WM-006 |  |  |
| [WM-011](#wm-011-terrain--buildability) | Terrain & buildability | Must | Done | MVP | Functional | P1 | WM-002 | New |  |
| [WM-012](#wm-012-tile-names) | Tile names | Could | Done | MVP | Functional | P1 | WM-009 | New |  |
| [WM-013](#wm-013-world-map-overview) | World map overview | Should | Done | MVP | Functional | P1 | WM-007 | New |  |
| [WM-014](#wm-014-victory-circles) | Victory circles | Must | Proposed | MVP | Functional | P1 | WM-009 | New |  |
| [WM-015](#wm-015-building-presence) | Building presence | Must | Proposed | MVP | Functional | P1 | WM-014 | New |  |
| [WM-016](#wm-016-tile-cores-and-elimination) | Tile cores and elimination | Must | Proposed | MVP | Functional | P8 | WM-005, CB-001 | New |  |

### WM-001 Tiled world map

**Requirement:** The world map shall be divided into discrete, irregular territories (tiles), each representing a self-contained playable area.

**Acceptance criteria:** The world is one generated cell grid cut into tiles of varying shape and size (about one per nominal 96x96 cells, 2-9 neighbours each); each tile has a unique ID, a name and a bounding box.

**As built:** World/Gen/Partition.cs: RISK-like territories grown from seeds; borders follow ridges, coasts and rivers; ocean tiles are ordinary tiles. ~1 tile per nominal 96x96 cells, 2-9 neighbours.

**Notes:** Was: 'World map renders a grid (square or hex) of tiles'. Irregular tiles were built and documented in README (OQ-1).

### WM-002 Tile attributes

**Requirement:** Each tile shall have a biome/terrain type and a set of natural resource deposits that determine what can be extracted there.

**Acceptance criteria:** Inspecting a tile on the world map shows biome, deposits and owner.

**As built:** Each cell has a terrain from terrain.json (11 kinds incl. water, peaks, rivers); a tile's biome is its most common terrain. World-map side panel shows name, owner, terrain, size, buildable %, neighbours, ground mix and deposits.

### WM-003 Tile adjacency

**Requirement:** The system shall define adjacency between tiles; adjacency governs I/O links, unit movement and expansion.

**Acceptance criteria:** For any tile, the game can list its neighbours; non-adjacent tiles cannot be linked directly.

**As built:** Neighbours share at least 3 cell edges (corners don't count). Ports, claims and unit routes all use this adjacency.

### WM-004 Tile ownership

**Requirement:** Each tile shall have an ownership state: Unclaimed, Owned (player X) or Contested. A tile is Contested while units of any player other than its owner are on it.

**Acceptance criteria:** Ownership is shown on the world map by colour; a tile turns Contested the tick an enemy unit enters and stops when none remain; a Contested tile runs the full sim and materialises abstract units on it (FB-013, UC-009).

**As built:** Unclaimed / Owned only, shown as an owner tint on the world map. 'Contested' was removed in the 2026-10-10 cleanup because nothing could set it before combat exists. Transitions aren't logged separately (every claim is in the command log).

**Notes:** Re-add Contested with combat (CB-001, CB-003); see OQ-20. OQ-20 decided 2026-10-10.

### WM-005 Starting tile

**Requirement:** Each player shall start owning exactly one tile, with a tile core as the first building inside that tile's victory circle (WM-014).

**Acceptance criteria:** On new game each player owns 1 tile and has a core inside its victory circle; with several players, starts are spread fairly (OQ-22).

**As built:** Assigned: the land tile nearest the map centre with room for a core and 2+ land neighbours. Gets a core plus 300 copper ore and 150 iron ore (Balance.cs). Single player only.

**Notes:** Was: 'Player owns 1 tile and spawns with a starter core/HQ on it'. Core placement changed by OQ-3 (2026-10-10). Fair starts: farthest-point selection with fairness scoring (OQ-22).

### WM-006 Tile claim by victory circle

**Requirement:** A player shall claim a tile by keeping at least one of their buildings inside the tile's victory circle (WM-014) for a configurable time. Capturing an enemy tile uses the same rule (CB-003).

**Acceptance criteria:** Each player's timer runs while they have a building in the circle and resets to zero when they have none; no timer runs while the circle is empty. The first player to finish claims the tile, and every other player's buildings inside the circle are destroyed at that moment. Otherwise only ownership and the world-map control border change: buildings outside the circle keep their owner. Capture time is a config value.

**As built:** Claim = pay 100 copper ore + 50 iron ore from an adjacent owned tile (C key or Claim button); the new tile gets a core and 120 copper ore. Claimable tiles are outlined on the world map.

**Notes:** Was: 'Claim an unclaimed adjacent tile by meeting a claim condition (e.g. outpost or constructor unit)'. Cost is a placeholder in Balance.cs until OQ-3 is settled. OQ-3 decided 2026-10-10: the ore claim cost (Balance.ClaimCost) goes away. Contested circles: OQ-23. OQ-23 decided 2026-10-10. Progress resets; rivals' buildings in the circle are destroyed on claim (owner, 2026-10-10).

### WM-007 Zoom between layers

**Requirement:** The player shall switch seamlessly between the world-map view and an individual tile's RTS view.

**Acceptance criteria:** Selecting an owned tile and pressing enter/zoom opens its local map in < 2 s.

**As built:** Tab toggles between the world map and the last tile; double-click or Enter opens an owned tile. Switching is instant; each view keeps its own camera.

### WM-008 Fog of war (world)

**Requirement:** Tiles not owned or adjacent to owned tiles shall be hidden or partially hidden on the world map.

**Acceptance criteria:** Unscouted tiles hide owner and deposits; scouting reveals them.

**As built:** Not built.

**Notes:** Rejected 2026-10-10 (OQ-29): full sight for everyone, no fog of war.

### WM-009 Procedural world generation

**Requirement:** The world map shall be procedurally generated from a seed with configurable size and resource distribution.

**Acceptance criteria:** Same seed + settings produces identical map.

**As built:** Seeded, integer-only generation in phases (terrain with drainage, rivers and lakes; partition; I/O zones; deposits). Same seed + size gives an identical world (tested). Size 3-15 nominal tiles a side (title offers 5/7/9).

**Notes:** Resource distribution follows terrain deposit weights in terrain.json; per-match richness belongs to MP-005.

### WM-010 Neutral / environmental tiles

**Requirement:** The world may contain neutral hazards or NPC-held tiles (e.g. derelict factories) that must be cleared before claim.

**Acceptance criteria:** At least one neutral tile type exists with defenders that block claim until destroyed.

**As built:** Not built.

### WM-011 Terrain & buildability

**Requirement:** Each cell shall have a terrain kind that decides whether buildings and frame can stand on it; terrain kinds and their rules shall be data-driven.

**Acceptance criteria:** Water and peaks reject placement (BadTerrain); terrain.json defines elevation/moisture bands, colour, buildability and deposit weights; validation rejects gaps in coverage.

**As built:** 11 terrains incl. deep/shallow water, rivers (buildable for now), beach, peaks; lakes and rivers come from drainage.

**Notes:** Added from what was built.

### WM-012 Tile names

**Requirement:** Each tile shall have a generated place name, unique within its world.

**Acceptance criteria:** Names come from a made-up language picked by the seed; unique per world; no numbers or terrain words; same seed gives the same names.

**As built:** World/Gen/TileNames.cs.

**Notes:** Added from what was built.

### WM-013 World map overview

**Requirement:** The world map shall show a zoomed-out picture of every tile's terrain and buildings, tinted by owner, with labels, claimable outlines and port links.

**Acceptance criteria:** The picture refreshes every N view switches (setting, default 3); labels show name, owner and building count.

**As built:** Deliberately a slightly stale overview, for performance.

**Notes:** Added from what was built.

### WM-014 Victory circles

**Requirement:** World generation shall place one fixed-radius victory circle on each tile, near concentrations of large resource deposits and away from the tile's borders.

**Acceptance criteria:** Every claimable tile has exactly one circle wholly inside it; it sits on the richest deposit cluster that fits while keeping clear of borders; the radius is one Balance constant sized to the nominal tile; same seed gives the same circles; the world map shows them.

**As built:** Not built.

**Notes:** OQ-3 decided 2026-10-10. Radius placeholder: 12 cells (1/8 of a nominal 96-cell tile) until playtested. Ocean/unbuildable tiles: OQ-24. OQ-24 decided 2026-10-10: ocean and unbuildable tiles get circles too and can be claimed, which needs buildings that stand on water (FB-017).

### WM-015 Building presence

**Requirement:** A player may build anywhere on any tile where they already have a building or unit, except inside an enemy-owned tile's victory circle while any enemy building stands in that circle.

**Acceptance criteria:** Placement is refused on a tile with none of the player's buildings or units, and inside an enemy-owned circle that holds an enemy building; it is allowed again once those buildings are destroyed.

**As built:** Building (Placement.Check) and landing (UnitAssembly.CheckDeploy) need a tile the player owns.

**Notes:** Decided 2026-10-10. Conflicts with today's rule that building and landing need an owned tile.

### WM-016 Tile cores and elimination

**Requirement:** The tile core is the first building in a player's starting victory circle and keeps their control of that tile. Players can build more cores; a player who loses their last core is eliminated.

**Acceptance criteria:** Cores are buildable; destroying a player's last core eliminates them (MP-001).

**As built:** One core per tile, placed by the sim on start and on claim; cores can't be built or destroyed.

**Notes:** Decided 2026-10-10.

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-1](#oq-1-square-or-hex-tiles-on-the-world-map) | Square or hex tiles on the world map? | WM-001, WM-003 | Decided | As built; confirm | 2026-10-10 |
| [OQ-2](#oq-2-how-big-is-a-tiles-local-map-and-is-it-fixed-or-varied-by-biome) | How big is a tile's local map, and is it fixed or varied by biome? | FB-001 | Decided | Owner | 2026-10-10 |
| [OQ-3](#oq-3-what-is-the-claim-condition-for-a-new-tile) | What is the claim condition for a new tile? | WM-006 | Decided | Owner | 2026-10-10 |
| [OQ-22](#oq-22-with-several-players-how-are-starting-tiles-chosen) | With several players, how are starting tiles chosen? | WM-005, MP-002, MP-003 | Decided | Owner | 2026-10-10 |
| [OQ-23](#oq-23-if-two-players-both-have-buildings-in-an-unclaimed-tiles-circle-whose-timer-runs) | If two players both have buildings in an unclaimed tile's circle, whose timer runs? | WM-006, WM-014 | Decided | Owner | 2026-10-10 |
| [OQ-24](#oq-24-do-ocean-and-other-unbuildable-tiles-get-a-victory-circle-and-can-they-be-claimed) | Do ocean and other unbuildable tiles get a victory circle, and can they be claimed? | WM-014, UD-004 | Decided | Owner | 2026-10-10 |

### OQ-1 Square or hex tiles on the world map?

**Options / notes:** Hex gives 6 neighbours and cleaner borders; square aligns with tile-local grid.

**Decision:** Neither: irregular RISK-like territories, about one per nominal 96x96 cells, 2-9 neighbours (WM-001).

### OQ-2 How big is a tile's local map, and is it fixed or varied by biome?

**Options / notes:** Fixed size simplifies balance and edge ports; varied adds identity.

**Decision:** Varies with how the world is partitioned. The global map is the real map; a tile's local map exists only for rendering and simulation performance, so huge worlds needn't be fully loaded or simulated.

### OQ-3 What is the claim condition for a new tile?

**Options / notes:** Outpost building, constructor unit, or resource cost.

**Decision:** Victory circle: a building of yours inside a tile's circle for a set time (config) claims it; capture works the same way. You can't build in an enemy-owned circle while enemy buildings stand in it. See WM-006, WM-014..016.

### OQ-22 With several players, how are starting tiles chosen?

**Options / notes:** E.g. maximise distance between starts, equal deposits and a regional resource next door for each; or player-picked.

**Decision:** Use a standard fair-start algorithm: among eligible land tiles, pick starts by farthest-point sampling over tile-graph distance (maximise the minimum distance between starts), then over several seeded attempts keep the set with the most even deposits and regional resources within 2 hops.

### OQ-23 If two players both have buildings in an unclaimed tile's circle, whose timer runs?

**Options / notes:** Timer pauses while shared; first to arrive keeps it; most buildings wins. The building ban only covers enemy-OWNED circles.

**Decision:** Each player's timer runs while they have a building in the circle; with no building in the circle no timer runs. First to finish claims (WM-006).

### OQ-24 Do ocean and other unbuildable tiles get a victory circle, and can they be claimed?

**Options / notes:** Circle needs buildable land; naval platforms as buildings; ocean tiles are unclaimable.

**Decision:** Yes: every tile has a circle and can be claimed (FB-017 for building on water).
