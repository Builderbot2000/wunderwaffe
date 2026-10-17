# World Map

P1 · The world is split into tiles; you start on one and expand.

Where it stands (2026-10-17): A seeded world (terrain with rivers and lakes) cut into irregular 96-cell tiles, each with a victory circle. Any number of players get fair starts; tiles are claimed by holding a building in the circle, and you build wherever you have presence. Tab switches instantly between the world map and a tile. To do from playtesting: continuous zoom, 256-cell tiles, one unified core.

## Requirements

| ID | Feature | Priority | Status | Built | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|---|
| [WM-001](#wm-001-tiled-world-map) | Tiled world map | Must | In Progress | Partial | MVP | Functional | P1 |  | Updated |  |
| [WM-002](#wm-002-tile-attributes) | Tile attributes | Must | Done | Yes | MVP | Functional | P1 | WM-001 |  |  |
| [WM-003](#wm-003-tile-adjacency) | Tile adjacency | Must | Done | Yes | MVP | Functional | P1 | WM-001 |  |  |
| [WM-004](#wm-004-tile-ownership) | Tile ownership | Must | Done | Yes | MVP | Functional | P1 | WM-001 | Updated |  |
| [WM-005](#wm-005-starting-tile) | Starting tile | Must | In Progress | Partial | MVP | Functional | P1 | WM-004 | Updated |  |
| [WM-006](#wm-006-tile-claim-by-victory-circle) | Tile claim by victory circle | Must | Done | Yes | MVP | Functional | P1 | WM-014, WM-015 | Updated |  |
| [WM-007](#wm-007-zoom-between-layers) | Zoom between layers | Must | Approved | No | MVP | Functional | P1 | WM-001, FB-001, FB-013 | Updated |  |
| [WM-008](#wm-008-fog-of-war-world) | Fog of war (world) | Won't | Rejected | No | Alpha | Functional | P1 | WM-004 |  |  |
| [WM-009](#wm-009-procedural-world-generation) | Procedural world generation | Should | Done | Yes | Alpha | Functional | P1 | WM-002 |  |  |
| [WM-010](#wm-010-neutral--environmental-tiles) | Neutral / environmental tiles | Could | Proposed | No | Beta | Functional | P1 | WM-006 |  |  |
| [WM-011](#wm-011-terrain--buildability) | Terrain & buildability | Must | Done | Yes | MVP | Functional | P1 | WM-002 | New |  |
| [WM-012](#wm-012-tile-names) | Tile names | Could | Done | Yes | MVP | Functional | P1 | WM-009 | New |  |
| [WM-013](#wm-013-world-map-overview) | World map overview | Should | Done | Yes | MVP | Functional | P1 | WM-007 | New |  |
| [WM-014](#wm-014-victory-circles) | Victory circles | Must | Done | Yes | MVP | Functional | P1 | WM-009 | New |  |
| [WM-015](#wm-015-building-presence) | Building presence | Must | Done | Yes | MVP | Functional | P1 | WM-014 | New |  |
| [WM-016](#wm-016-cores-and-elimination) | Cores and elimination | Must | In Progress | Partial | MVP | Functional | P8 | WM-005, CB-001, UD-010 | Updated |  |

### WM-001 Tiled world map

**Requirement:** The world map shall be divided into discrete, irregular territories (tiles), each representing a self-contained playable area.

**Acceptance criteria:** The world is one generated cell grid cut into tiles of varying shape and size (about one per nominal 256x256 cells (OQ-35), 2-9 neighbours each); each tile has a unique ID, a name and a bounding box.

**As built:** World/Gen/Partition.cs: RISK-like territories grown from seeds; borders follow ridges, coasts and rivers; ocean tiles are ordinary tiles. ~1 tile per nominal 96x96 cells, 2-9 neighbours.

**Notes:** Was: 'World map renders a grid (square or hex) of tiles'. Irregular tiles were built and documented in README (OQ-1). Nominal tile raised from 96x96 to 256x256 after playtesting, 2026-10-17 (OQ-35): tiles felt cramped next to Mindustry maps.

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

**As built:** Unowned / owned / Contested. Territory.IsContested: Contested while another player's unit is on the tile (an unowned tile: units of two players). The world map tints owners and outlines Contested tiles; Contested tiles run the full sim and materialise abstract units (RateMode, AbstractTravel). Claims and losses raise events (UI-003).

**Notes:** Re-add Contested with combat (CB-001, CB-003); see OQ-20. OQ-20 decided 2026-10-10. What ownership gives, and no cost for holding tiles: OQ-45.

### WM-005 Starting tile

**Requirement:** Each player shall start owning exactly one tile, with a starting unit already deployed (landed) inside that tile's victory circle (WM-014). Its core is the player's first core (WM-016): it is given at the start, not built.

**Acceptance criteria:** On new game each player owns 1 tile and has a landed starting unit whose core stands inside the victory circle and holds the starting items; it can lift off like any unit; with several players, starts are spread fairly (OQ-22).

**As built:** Fair starts for every player of the setup (ChooseStarts, OQ-22). Each start gets a free tile core placed by the sim nearest the circle centre with 300 copper ore + 150 iron ore (Balance.StartingItems), plus a separate landed scout (PlaceScout).

**Notes:** Was: 'Each player shall start owning exactly one tile, with a tile core as the first building inside that tile's victory circle'. Core placement changed by OQ-3 (2026-10-10). Fair starts: farthest-point selection with fairness scoring (OQ-22). Start as a deployed unit instead of a free core, 2026-10-17 (OQ-37).

### WM-006 Tile claim by victory circle

**Requirement:** A player shall claim a tile by keeping at least one of their buildings inside the tile's victory circle (WM-014) for a configurable time. Capturing an enemy tile uses the same rule (CB-003).

**Acceptance criteria:** Each player's timer runs while they have a building in the circle and resets to zero when they have none; no timer runs while the circle is empty. The first player to finish claims the tile, and every other player's buildings inside the circle are destroyed at that moment. Otherwise only ownership and the world-map control border change: buildings outside the circle keep their owner. Capture time is a config value.

**As built:** World/Expansion.cs: a timer per player per circle runs while they have a building in it (a footprint counts if any cell is inside) and resets when they have none; the first to finish claims the tile and rivals' buildings in the circle are destroyed. Capture time is a match setting (MP-005). The ore claim cost is gone.

**Notes:** Was: 'Claim an unclaimed adjacent tile by meeting a claim condition (e.g. outpost or constructor unit)'. Cost is a placeholder in Balance.cs until OQ-3 is settled. OQ-3 decided 2026-10-10: the ore claim cost (Balance.ClaimCost) goes away. Contested circles: OQ-23. OQ-23 decided 2026-10-10. Progress resets; rivals' buildings in the circle are destroyed on claim (owner, 2026-10-10).

### WM-007 Zoom between layers

**Requirement:** The tile view and the world map shall be one continuous zoom: zooming out from a tile pulls back smoothly over the world, and past a zoom threshold the view becomes the world map; zooming back in over a tile enters its local map.

**Acceptance criteria:** The mouse wheel moves between a tile and the world map with no cut or loading pause (Tab and Enter stay as shortcuts that animate the same zoom); around the threshold the tile rendering cross-fades into the world-map picture at the same camera position, so the point under the cursor stays put; zooming in over any tile the player has presence on (WM-015) enters it; the unit selection and the active tool survive the transition; which tiles count as viewed while zooming (full sim, FB-013) and where the threshold sits follow OQ-36.

**As built:** Tab toggles between the world map and the last tile; double-click or Enter opens a tile. Switching is instant; each view keeps its own camera (CameraController) and the world map draws from a separate downsampled picture (WorldSnapshot).

**Notes:** Was: 'The player shall switch seamlessly between the world-map view and an individual tile's RTS view' (acceptance: enter opens the local map in < 2 s). Rewritten after playtesting 2026-10-17: the instant switch is jarring. Ordering units at any zoom: UC-005. Threshold and viewed tiles: OQ-36.

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

**Notes:** Added from what was built. With continuous zoom (WM-007) this picture is what the camera shows past the threshold, so it has to stay cheap with 256-cell tiles (OQ-35).

### WM-014 Victory circles

**Requirement:** World generation shall place one fixed-radius victory circle on each tile, near concentrations of large resource deposits and away from the tile's borders.

**Acceptance criteria:** Every claimable tile has exactly one circle wholly inside it; it sits on the richest deposit cluster that fits while keeping clear of borders; the radius is one Balance constant sized to the nominal tile; same seed gives the same circles; the world map shows them.

**As built:** World/Gen/Circles.cs: one circle per tile, ocean tiles included, on the most deposit cells in the disc among centres at least Balance.CircleClearance from the border; radius Balance.CircleRadius = 12. Drawn on the world map with claim arcs.

**Notes:** OQ-3 decided 2026-10-10. Radius placeholder: 1/8 of a nominal tile (12 cells at 96, 32 at 256; OQ-35) until playtested. Ocean/unbuildable tiles: OQ-24. OQ-24 decided 2026-10-10: ocean and unbuildable tiles get circles too and can be claimed, which needs buildings that stand on water (FB-017).

### WM-015 Building presence

**Requirement:** A player may build anywhere on any tile where they already have a building or unit, except inside an enemy-owned tile's victory circle while any enemy building stands in that circle.

**Acceptance criteria:** Placement is refused on a tile with none of the player's buildings or units, and inside an enemy-owned circle that holds an enemy building; it is allowed again once those buildings are destroyed.

**As built:** Territory.HasPresence and EnemyCircleBlocks, used by Placement.Check, frames, blueprints and landing (tested). Building on a tile you don't own is paid from an owned neighbour (Territory.Payer; replaced by FB-019).

**Notes:** Decided 2026-10-10. Conflicts with today's rule that building and landing need an owned tile.

### WM-016 Cores and elimination

**Requirement:** There shall be one core building that does the jobs of today's tile core and unit core. A core stores items (FB-019), supplies power (FB-005) and a resource radius (FB-018), gives presence (WM-015), and lifts the framed group around it off as a unit (UD-010); when the unit lands, its core is a core building again. Every unit has exactly one core. The first core stands in the player's starting victory circle (WM-005). A player whose last core is destroyed, landed or on a unit, is eliminated.

**Acceptance criteria:** One core kind in buildings.json with several variants (OQ-37); every variant costs items to build, none is free; a core on frame lifts off with G and its stock goes with it as the unit's cargo; a landed unit's core holds that cargo and adds it to the tile's funds; a player stays in the match while any core of theirs exists, on a tile or on a unit; destroying the last one eliminates them (MP-001).

**As built:** Two buildings: core (3x3, free, its deliveries go to the shared tile inventory, power 20) and unit_core (1x1, 10 copper plate + 5 steel, power 10). The sim places a core on each start; cores are in the build menu; the last core can't be removed (Simulation.TryRemove). Lift-off needs one unit core and refuses a tile core on the frame. Elimination counts landed cores only (Victory.HasCore).

**Notes:** Was: 'The tile core is the first building in a player's starting victory circle and keeps their control of that tile. Players can build more cores; a player who loses their last core is eliminated.' Decided 2026-10-10. Core and unit core unified after playtesting 2026-10-17, as they do the same jobs. Footprint, cost and tiers: OQ-37.

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-1](#oq-1-square-or-hex-tiles-on-the-world-map) | Square or hex tiles on the world map? | WM-001, WM-003 | Decided | As built; confirm | 2026-10-10 |
| [OQ-2](#oq-2-how-big-is-a-tiles-local-map-and-is-it-fixed-or-varied-by-biome) | How big is a tile's local map, and is it fixed or varied by biome? | FB-001 | Decided | Owner | 2026-10-10 |
| [OQ-3](#oq-3-what-is-the-claim-condition-for-a-new-tile) | What is the claim condition for a new tile? | WM-006 | Decided | Owner | 2026-10-10 |
| [OQ-22](#oq-22-with-several-players-how-are-starting-tiles-chosen) | With several players, how are starting tiles chosen? | WM-005, MP-002, MP-003 | Decided | Owner | 2026-10-10 |
| [OQ-23](#oq-23-if-two-players-both-have-buildings-in-an-unclaimed-tiles-circle-whose-timer-runs) | If two players both have buildings in an unclaimed tile's circle, whose timer runs? | WM-006, WM-014 | Decided | Owner | 2026-10-10 |
| [OQ-24](#oq-24-do-ocean-and-other-unbuildable-tiles-get-a-victory-circle-and-can-they-be-claimed) | Do ocean and other unbuildable tiles get a victory circle, and can they be claimed? | WM-014, UD-004 | Decided | Owner | 2026-10-10 |
| [OQ-35](#oq-35-how-big-should-a-nominal-tile-be) | How big should a nominal tile be? | WM-001, FB-001, WM-014, NF-001 | Decided | Owner | 2026-10-17 |
| [OQ-36](#oq-36-with-continuous-zoom-where-is-the-threshold-and-which-tiles-count-as-viewed) | With continuous zoom, where is the threshold and which tiles count as viewed? | WM-007, FB-013, UC-005, NF-001 | Decided | Owner | 2026-10-17 |
| [OQ-37](#oq-37-what-does-the-unified-core-look-like) | What does the unified core look like? | WM-016, UD-010, UD-003, FB-005, WM-005 | Decided | Owner | 2026-10-17 |
| [OQ-45](#oq-45-does-holding-more-tiles-cost-anything-and-what-does-owning-a-tile-give) | Does holding more tiles cost anything, and what does owning a tile give? | WM-004, WM-006, MP-001, IO-001 | Decided | Owner | 2026-10-17 |

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

### OQ-35 How big should a nominal tile be?

**Options / notes:** Playtest 2026-10-17: tiles should be significantly bigger, on the scale of Mindustry maps. For reference, Mindustry's editor opens a new map at 200x200 and allows up to about 500 a side. Candidates: 192 (4x today's area), 256 (about 7x) or 320 (about 11x). Bigger tiles multiply world-gen time, memory, the world-map picture and the rate-model graph: a 9x9 world at 256 is 2304x2304 = 5.3M cells.

**Decision:** Nominal 256x256 (Simulation.DefaultTileSize); victory-circle radius stays 1/8 of a tile (32 cells); I/O zone depth stays 4; the title screen offers 3/5/7 nominal tiles a side. Re-measure NF-001 and world-gen time at that size before going bigger.

### OQ-36 With continuous zoom, where is the threshold and which tiles count as viewed?

**Options / notes:** Viewed = only the tile under the screen centre (cheap, but neighbours visible at mid zoom look frozen in rate mode); or every tile the viewport touches below the threshold (looks right; cost bounded by the threshold). Threshold: a fixed zoom, or the zoom at which a tile fits the screen.

**Decision:** Only the tile the player is looking at (the one under the screen centre) renders in full and runs the full sim; every other tile, neighbours included, stays in rate mode with abstract units, even where it shows at mid zoom. Past the threshold the player views no tile. Threshold (tunable): the zoom at which a nominal tile fits the screen height, with a short cross-fade band either side.

### OQ-37 What does the unified core look like?

**Options / notes:** One size for everything (e.g. 2x2, small enough for scouts within the 64-cell cap); or Mindustry-style tiers (Shard / Foundation / Nucleus) of one core kind differing in size, HP, storage and power. Today's tile core is free to build; the unit core costs 10 copper plate + 5 steel.

**Decision:** One core kind with several variants in buildings.json (size, HP, power, storage capacity, cost); exact variants are content work. Nothing is free to build, cores included. Instead of a free core, each player starts with a deployed starting unit (WM-005). Every unit carries exactly one core.

### OQ-45 Does holding more tiles cost anything, and what does owning a tile give?

**Options / notes:** No upkeep or rising cost for tiles; only units have upkeep (UD-013). Anti-snowball options: tile upkeep, rising claim time, catch-up.

**Decision:** No cost: owning more tiles means defending more tiles, and a tile gives little without infrastructure built in it. Owning a tile gives the owner's colour on the world map, counts towards domination (MP-001), and stops enemies building in its circle while the owner's buildings stand there (WM-015). After a capture the loser's buildings outside the circle stay. Port links don't need either tile to be owned.
