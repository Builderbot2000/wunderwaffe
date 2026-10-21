# World Map

P1 · The world is split into tiles; you start on one and expand.

Where it stands (2026-10-17): A seeded world (terrain with rivers and lakes) cut into irregular 256-cell tiles (3, 5 or 7 a side), each with a victory circle. Any number of players get fair starts; tiles are claimed by holding a building in the circle, and you build wherever you have presence. Tab switches instantly between the world map and a tile. One core kind: each player starts with a landed unit whose core holds the starting items. To do from playtesting: continuous zoom.

## Requirements

| ID | Feature | Priority | Status | Built | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|---|
| [WM-001](#wm-001-tiled-world-map) | Tiled world map | Must | Done | Yes | MVP | Functional | P1 |  | Updated |  |
| [WM-002](#wm-002-tile-attributes) | Tile attributes | Must | Done | Yes | MVP | Functional | P1 | WM-001 |  |  |
| [WM-003](#wm-003-tile-adjacency) | Tile adjacency | Must | Done | Yes | MVP | Functional | P1 | WM-001 |  |  |
| [WM-004](#wm-004-tile-ownership) | Tile ownership | Must | Done | Yes | MVP | Functional | P1 | WM-001 | Updated |  |
| [WM-005](#wm-005-starting-tile) | Starting tile | Must | In Progress | Partial | MVP | Functional | P1 | WM-004, FB-021 | Updated |  |
| [WM-006](#wm-006-tile-claim-by-victory-circle) | Tile claim by victory circle | Must | Done | Yes | MVP | Functional | P1 | WM-014, WM-015 | Updated |  |
| [WM-007](#wm-007-zoom-between-layers) | Zoom between layers | Must | Done | Yes | MVP | Functional | P1 | WM-001, FB-001, FB-013 | Updated |  |
| [WM-008](#wm-008-fog-of-war-world) | Fog of war (world) | Won't | Rejected | No | Alpha | Functional | P1 | WM-004 |  |  |
| [WM-009](#wm-009-procedural-world-generation) | Procedural world generation | Should | Done | Yes | Alpha | Functional | P1 | WM-002 |  |  |
| [WM-010](#wm-010-neutral-guardian-faction) | Neutral guardian faction | Should | Approved | No | Alpha | Functional | P1 | WM-006, CB-001, MP-009, WM-019 | Updated |  |
| [WM-011](#wm-011-terrain--buildability) | Terrain & buildability | Must | Done | Yes | MVP | Functional | P1 | WM-002 | New |  |
| [WM-012](#wm-012-tile-names) | Tile names | Could | Done | Yes | MVP | Functional | P1 | WM-009 | New |  |
| [WM-013](#wm-013-world-map-overview) | World map overview | Should | Done | Yes | MVP | Functional | P1 | WM-007 | New |  |
| [WM-014](#wm-014-victory-circles) | Victory circles | Must | Done | Yes | MVP | Functional | P1 | WM-009 | New |  |
| [WM-015](#wm-015-build-radius) | Build radius | Must | In Progress | Partial | MVP | Functional | P1 | WM-014, UD-010 | Updated |  |
| [WM-016](#wm-016-cores-and-elimination) | Cores and elimination | Must | Done | Yes | MVP | Functional | P8 | WM-005, CB-001, UD-010 | Review |  |
| [WM-017](#wm-017-scenarios) | Scenarios | Should | Done | Yes | Alpha | Functional | P1 | WM-009, FB-021, NF-002 |  |  |
| [WM-018](#wm-018-neutral-site-generation) | Neutral site generation | Should | Approved | No | Alpha | Functional | P1 | WM-009, WM-010, WM-014, FB-021 | New |  |
| [WM-019](#wm-019-unclaimed-buildings) | Unclaimed buildings | Should | Approved | No | Alpha | Functional | P8 | WM-016, MP-001 | New |  |

### WM-001 Tiled world map

**Requirement:** The world map shall be divided into discrete, irregular territories (tiles), each representing a self-contained playable area.

**Acceptance criteria:** The world is one generated cell grid cut into tiles of varying shape and size (about one per nominal 256x256 cells (OQ-35), 2-9 neighbours each); each tile has a unique ID, a name and a bounding box.

**As built:** World/Gen/Partition.cs: RISK-like territories grown from seeds; borders follow ridges, coasts and rivers; ocean tiles are ordinary tiles. ~1 tile per nominal 256x256 cells (Simulation.DefaultTileSize), 2-9 neighbours. The title screen offers 3, 5 or 7 nominal tiles a side (GameSetup.MaxSize = 7); generation takes about 0.4 / 1.1 / 2.3 s on the dev machine.

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

**Requirement:** Each player shall start owning exactly one tile, with two vehicles: a big starting vehicle already landed inside that tile's victory circle (WM-014), whose core is the player's first core (WM-016), and a small starting vehicle beside it, ready for orders. Both are given at the start, not built.

**Acceptance criteria:** On new game each player owns 1 tile; their big vehicle stands landed with its core inside the victory circle, holding the starting items, and can lift off like any unit; their small vehicle is a unit beside it with its own core (so it counts for elimination, WM-016) and a build radius of its own (WM-015). Both come from start prefabs named in content (FB-021), set per faction (MP-009) and by scenarios (WM-017). With several players, starts are spread fairly (OQ-22).

**As built:** Fair starts for every player of the setup (ChooseStarts, OQ-22). No free core: each start gets a landed starting unit nearest the circle centre (the setup's start prefabs, FB-021; default game/data/prefabs/scout.json: thruster, 2x2 core, thruster on a 4x2 frame), whose core holds the prefab's items, 600 copper ore + 300 iron ore (doubled for 256-cell tiles, OQ-35); it passes lift-off validation. Tested (SimulationTests, TerritoryTests). No small starting vehicle yet.

**Notes:** Was: 'Each player shall start owning exactly one tile, with a starting unit already deployed (landed) inside that tile's victory circle (WM-014). Its core is the player's first core (WM-016): it is given at the start, not built.' / 'On new game each player owns 1 tile and has a landed starting unit whose core stands inside the victory circle and holds the starting items; it can lift off like any unit; with several players, starts are spread fairly (OQ-22).'. Was: 'Each player shall start owning exactly one tile, with a tile core as the first building inside that tile's victory circle'. Core placement changed by OQ-3 (2026-10-10). Fair starts: farthest-point selection with fairness scoring (OQ-22). Start as a deployed unit instead of a free core, 2026-10-17 (OQ-37). Owner, 2026-10-20: add a small starting vehicle for every player beside the landed big one.

### WM-006 Tile claim by victory circle

**Requirement:** A player shall claim a tile by keeping at least one of their buildings inside the tile's victory circle (WM-014) for a configurable time. Capturing an enemy tile uses the same rule (CB-003).

**Acceptance criteria:** Each player's timer runs while they have a building in the circle and resets to zero when they have none; no timer runs while the circle is empty. The first player to finish claims the tile, and every other player's buildings inside the circle are destroyed at that moment. Otherwise only ownership and the world-map control border change: buildings outside the circle keep their owner. Capture time is a config value.

**As built:** World/Expansion.cs: a timer per player per circle runs while they have a building in it (a footprint counts if any cell is inside) and resets when they have none; the first to finish claims the tile and rivals' buildings in the circle are destroyed. Capture time is a match setting (MP-005). The ore claim cost is gone.

**Notes:** Was: 'Claim an unclaimed adjacent tile by meeting a claim condition (e.g. outpost or constructor unit)'. Cost is a placeholder in Balance.cs until OQ-3 is settled. OQ-3 decided 2026-10-10: the ore claim cost (Balance.ClaimCost) goes away. Contested circles: OQ-23. OQ-23 decided 2026-10-10. Progress resets; rivals' buildings in the circle are destroyed on claim (owner, 2026-10-10).

### WM-007 Zoom between layers

**Requirement:** The tile view and the world map shall be one continuous zoom: zooming out from a tile pulls back smoothly over the world, and past a zoom threshold the view becomes the world map; zooming back in over a tile enters its local map.

**Acceptance criteria:** The mouse wheel moves between a tile and the world map with no cut or loading pause (Tab and Enter stay as shortcuts that animate the same zoom); around the threshold the tile rendering cross-fades into the world-map picture at the same camera position, so the point under the cursor stays put; zooming in over any tile the player has presence on (WM-015) enters it; the unit selection and the active tool survive the transition; which tiles count as viewed while zooming (full sim, FB-013) and where the threshold sits follow OQ-36.

**As built:** One camera (CameraController) holds a centre in world cells and a scale in screen pixels per cell, expressed each frame in whichever layer's canvas is up, so switching layers moves nothing on screen. The threshold is the scale at which a 256-cell nominal tile fits the screen height (`ThresholdFit`, tunable); the cross-fade runs from threshold / 1.35 to threshold x 1.35 (`FadeBand`): the tile view (and the units, combat and tool layers over it) fades over the world map, which is drawn beneath it scaled into the tile's canvas, and on the world-map side the tile view shows the tile a zoom would open. The wheel zooms toward the cursor; past the threshold it opens the tile under the screen centre, else the one under the cursor, if the player has presence there, and otherwise stops short of the threshold. Panning a tile view onto another tile with presence makes that the viewed tile. Tab, Enter, double-click and Open tile animate the same zoom (0.45 s); Tab returns to where each layer was left. Unit selection and the picked building or blueprint survive; the blueprint tool resets only when another tile opens. World-map labels stop growing past twice their size. Viewed tile for the sim (`view_tile`) is the open tile, none on the world map.

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

### WM-010 Neutral guardian faction

**Requirement:** The world shall hold neutral sites: installations of a neutral guardian faction that defends them but never expands, attacks beyond its sites, claims or wins. Each site guards something worth taking: rich or rare deposits, and buildings or blocks from the catalogue that player factions can't build (MP-009).

**Acceptance criteria:** Each site is held by its own neutral player of the guardian faction, with one or more cores; sites never share a neutral player. Neutral players are seats outside victory (MP-001) and the AI director; their turrets and guard units fire on any player's units and buildings in range and don't leave their site. A site is won by destroying all of its neutral player's cores (OQ-54): its buildings become unclaimed and players take them by claiming (WM-019), to use in place or move (FB-023). Sites come from world generation (WM-018) and scenarios (WM-017). Neutral state is hashed and snapshotted like any seat's.

**As built:** Not built.

**Notes:** Was: 'The world may contain neutral hazards or NPC-held tiles (e.g. derelict factories) that must be cleared before claim' / 'At least one neutral tile type exists with defenders that block claim until destroyed'. Owner, 2026-10-20: neutral factions hold sites within tiles, as treasure guardians; a site is won by destroying its neutral player's cores (OQ-54). Matches the Keepers and claimable ruins of the content draft (specs/content/content-update.md §1.3, item 8), except that a site is taken through its cores, not a beacon.

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

**As built:** World/Gen/Circles.cs: one circle per tile, ocean tiles included, on the most deposit cells in the disc among centres at least Balance.CircleClearance from the border; radius Balance.CircleRadius = DefaultTileSize / 8 = 32, clearance Balance.CircleClearance = DefaultTileSize / 32 = 8. Drawn on the world map with claim arcs.

**Notes:** OQ-3 decided 2026-10-10. Radius placeholder: 1/8 of a nominal tile (12 cells at 96, 32 at 256; OQ-35) until playtested. Ocean/unbuildable tiles: OQ-24. OQ-24 decided 2026-10-10: ocean and unbuildable tiles get circles too and can be claimed, which needs buildings that stand on water (FB-017).

### WM-015 Build radius

**Requirement:** A player may build only within the build radius of one of their major placed buildings (factories, cores, defences; not minor ones such as conveyors and junctions) or vehicles, flying or landed, and never inside an enemy-owned tile's victory circle while any enemy building stands in that circle.

**Acceptance criteria:** Every building operation (placing a building, frame or blueprint, removing, and cutting, pasting or grafting, FB-023) is refused unless every cell it touches lies within the build radius of one of the player's major buildings or vehicles on that tile (OQ-58); the enemy-circle refusal stays as it is. A building's radius depends on what building it is (a build radius per building in buildings.json; minor buildings have none) and takes effect the moment it is placed, so placing a major building extends where its owner can build. A vehicle's radius comes from a formula on its live block count (Balance), so lost blocks shrink it. A radius never reaches past its own tile's border. Unclaimed buildings give none (WM-019). One sim check (Placement.Check) decides it for the command, the UI preview and the AI. Radii show when a building or vehicle is selected and while placing. The AI's planner searches only spots inside a radius, and the AI extends its radius by building or moves a vehicle to where it wants to build (MP-006, MP-007).

**As built:** Territory.HasPresence and EnemyCircleBlocks, used by Placement.Check, frames, blueprints and landing (tested). Building on a tile you don't own is paid from an owned neighbour (Territory.Payer; replaced by FB-019). No build radius yet: any building or unit on the tile gives presence.

**Notes:** Was: 'A player may build anywhere on any tile where they already have a building or unit, except inside an enemy-owned tile's victory circle while any enemy building stands in that circle.' / 'Placement is refused on a tile with none of the player's buildings or units, and inside an enemy-owned circle that holds an enemy building; it is allowed again once those buildings are destroyed.'. Owner, 2026-10-20: building only within the radius of a vehicle, then of major buildings too, by building type and, for vehicles, by block count (OQ-58). Decided 2026-10-10. Conflicts with today's rule that building and landing need an owned tile.

### WM-016 Cores and elimination

**Requirement:** There shall be one core building that does the jobs of today's tile core and unit core. A core stores items (FB-019), supplies power (FB-005) and a resource radius (FB-018), gives presence (WM-015), and lifts the framed group around it off as a unit (UD-010); when the unit lands, its core is a core building again. Every unit has exactly one core. The first core stands in the player's starting victory circle (WM-005). A player whose last core is destroyed, landed or on a unit, is eliminated.

**Acceptance criteria:** One core kind in buildings.json with several variants (OQ-37); every variant costs items to build, none is free; a core on frame lifts off with G and its stock goes with it as the unit's cargo; a landed unit's core holds that cargo and adds it to the tile's funds; a player stays in the match while any core of theirs exists, on a tile or on a unit; destroying the last one eliminates them (MP-001).

**As built:** BuildingKind.Core is the only core kind, with two variants in buildings.json: core (2x2, 10 copper ore + 5 iron ore, power 20, capacity 1000 of each item, supply radius 16) and core_large (3x3, 60 iron plate + 40 copper plate, capacity 5000). Every variant has a cost and a capacity (validated in ContentDb). Each core holds its own stock (PlacedBuilding.Stock, FB-019). Lift-off needs exactly one core on the frame and takes its stock as cargo; landing puts it back (UD-010). The last core can't be removed (Simulation.TryRemove). Elimination counts cores on tiles and on units (Simulation.CoreCount, Victory.HasCore). Tested (TerritoryTests, UnitTests).

**Notes:** Was: 'The tile core is the first building in a player's starting victory circle and keeps their control of that tile. Players can build more cores; a player who loses their last core is eliminated.' Decided 2026-10-10. Core and unit core unified after playtesting 2026-10-17, as they do the same jobs. Footprint, cost and tiers: OQ-37.

### WM-017 Scenarios

**Requirement:** A match shall be able to start from a scenario: a map plus what stands on it at the start. A map may be hand-authored or generated; a generated map can be saved as a scenario and then changed in its data.

**Acceptance criteria:** A scenario is a content file holding the world (terrain, tile shapes and names, zones, deposits and patch amounts, victory circles) and per-seat starting state (owned tiles, buildings, units, core stock), validated on load like other content. A generated map saved as a scenario reloads to the same world, and editing the file (terrain, deposits, tile ownership, placed assets) changes that world. A match setup names either a seed (generated, as today) or a scenario; the save records which, with the scenario's content hash, so it replays the same. AI seats take over their seat's scenario assets at the start (MP-008).

**As built:** Content/Scenarios.cs, Simulation.Scenario.cs: a scenario is game/data/scenarios/*.json (or user://scenarios, from the Esc menu's Save as scenario; Simulation.ToScenario is the API call). It holds the world size, terrain and tile map as run-length rows per cell row with a legend, elevation (deflated, optional), per tile its name, circle, zones, every cell layer through one generic serialiser (FB-022: deposits, platforms, frames, cables) and its deposit patches with amounts left (RS-004; left out, they are found from the deposits), and per seat its owned tiles, buildings (with core stock and damage) and units (at rest, with cargo). Loading validates it by building its world (placement rules for buildings). A generated map saved at tick 0 reloads to the same state hash; editing terrain, deposits, ownership or buildings changes the world (ScenarioTests). GameSetup names a seed or a scenario plus its content hash; a save with a changed or missing scenario won't load. The new-game menu and lobby offer every scenario (its seats fix the player count, seats nobody takes are AI); one ships (ai_head_start).

**Notes:** Owner, 2026-10-18 (OQ-51).

### WM-018 Neutral site generation

**Requirement:** World generation shall place neutral sites (WM-010), each guarding valuable deposits and tech, with guards matched to what the site holds, while keeping starts fair.

**Acceptance criteria:** A generation phase after victory circles: the same seed gives the same sites; site density is a match setting, none included (MP-005), about one site per three tiles by default; none on or beside a start tile, and none inside a victory circle; each site has its own neutral player with at least one core, sits on a rich or rare deposit patch (RS-003, RS-004) and holds at least one catalogue entry no player faction can build, or a stock of valuable items; the value held and the guards' strength rise with distance from the starts, guard strength worked out from content (item values, UnitStats, Combat.Dps), never ids (MP-007); fair starts (OQ-22) score each start's access to sites. Sites are built from content templates (prefab-like: buildings, guards, deposits, items; FB-021), validated with the rest. Saving the map as a scenario keeps its sites.

**As built:** Not built.

**Notes:** Owner, 2026-10-20 (OQ-57).

### WM-019 Unclaimed buildings

**Requirement:** When a seat's last core is destroyed, whether a neutral player's or an active player's, every building it still has becomes unclaimed. A player claims an unclaimed building by placing one of their own buildings, or landing one of their units, near it; it is then theirs, to use where it stands or move (FB-023).

**Acceptance criteria:** The last core counts cores on units as well as on tiles (WM-016). Unclaimed buildings belong to no seat: they keep their HP and held items, can still be damaged, but don't run or fire, give no build radius (WM-015) and don't count towards victory circles (WM-006) or domination (MP-001). Placing a building or landing a unit within the claim radius (a Balance constant, tuned in playtesting) of unclaimed buildings claims every one in range; when two players claim the same building on one tick, the first command applied wins. A claim raises an event (UI-003) and counts as a capture for capture to build (MP-010). Unclaimed state and owners are sim state (hashed, snapshotted) and readable through GameQuery, so the AI can go after them. Placing and landing show the claim radius and what it would claim.

**As built:** Not built.

**Notes:** Owner, 2026-10-20 (OQ-53, OQ-54): one mechanic for neutral sites and eliminated players alike.

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
| [OQ-51](#oq-51-are-maps-hand-authored-or-generated) | Are maps hand-authored or generated? | WM-017, WM-009, MP-008 | Decided | Owner | 2026-10-18 |
| [OQ-54](#oq-54-when-does-a-neutral-sites-equipment-become-a-players) | When does a neutral site's equipment become a player's? | WM-010, FB-023, MP-010, WM-006 | Decided | Owner | 2026-10-20 |
| [OQ-57](#oq-57-where-do-neutral-sites-spawn-and-how-strong-are-their-guards) | Where do neutral sites spawn, and how strong are their guards? | WM-018, WM-010, WM-005, MP-005 | Decided | Owner | 2026-10-20 |
| [OQ-58](#oq-58-what-sets-a-vehicles-build-radius-and-what-needs-one) | What sets a vehicle's build radius, and what needs one? | WM-015, UD-002, UD-006, FB-023, WM-019 | Decided | Owner | 2026-10-20 |

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

### OQ-51 Are maps hand-authored or generated?

**Options / notes:** A seed plus an overlay of placements anchored to generated features is small; a full map file format (terrain, shapes, deposits, circles saved instead of generated) is larger but lets maps be edited.

**Decision:** Both, through scenarios with a full map file: maps can be hand-authored or generated, a generated map can be saved as a scenario and edited in its data, and scenarios carry per-seat starting assets that an AI seat takes over (WM-017, MP-008).

### OQ-54 When does a neutral site's equipment become a player's?

**Options / notes:** Options: (a) when every guard (turrets and units) is destroyed, the rest goes to the player with the most presence there; (b) a site beacon held on a timer like a victory circle (WM-006), as in the content draft; (c) building by building, each taken when picked up (FB-023). Also: whether a site inside a tile's victory circle blocks claiming that tile until cleared (the old WM-010 criterion), and whether guards ever rebuild. Leaning: (b), a beacon with the circle's timer rules, so contested sites work like contested circles; a site in the circle blocks the claim; no rebuilding.

**Decision:** A neutral site is won by destroying all of its neutral player's cores. As with any seat losing its last core, its buildings become unclaimed, and players take them by claiming (WM-019).

### OQ-57 Where do neutral sites spawn, and how strong are their guards?

**Options / notes:** Placement: a share of tiles, more of them far from the starts; never on a start tile, perhaps not on its neighbours; inside or outside the victory circle; on ocean tiles (FB-017). Strength: guard value from the value held, rising with distance from the starts, so the nearest sites are early-game targets and the far ones need an army. Treasure: richer patches of the tile's deposits, regional or rare items (RS-003), neutral-only buildings and blocks, stock. Leaning: one site per three tiles, none on or beside a start, outside the circle, value and guards rising with distance.

**Decision:** As proposed: about one site per three tiles, none on or beside a start, outside the victory circle, the value held and the guards rising with distance from the starts (WM-018).

### OQ-58 What sets a vehicle's build radius, and what needs one?

**Options / notes:** Size: one Balance constant for every vehicle; a `buildRadius` on core variants in buildings.json (a vehicle's radius is its largest core's); or a dedicated constructor block whose radius adds to the unit's stats (UD-002), like a power node's radius. Measured from the core's footprint (nearest cells, as power nodes are) or the unit's centre. What needs it: placing buildings, frames, blueprints, pastes and grafts; perhaps removal and cutting too; unit factories projecting a design inside their own radius (UD-006) could be exempt. Claiming an unclaimed building by building near it (WM-019) then needs a vehicle nearby as well. Leaning: `buildRadius` on core variants, measured from the core's footprint like a power node; flying and landed vehicles both count; everything placed, cut or removed needs it; factories exempt inside their own radius.

**Decision:** Major placed buildings (factories, cores, defences) and vehicles exert a build radius; minor buildings (conveyors, junctions and the like) don't. A building's radius depends on what building it is (content) and takes effect the moment it is placed; a vehicle's comes from a formula on its block count. Every building operation needs the inside of a radius: placing, removing, cutting, pasting and grafting. A vehicle's radius follows its live block count, so lost blocks shrink it. Radii stay within their tile.
