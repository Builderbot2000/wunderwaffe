# Wunderwaffe 3037

2D factory-builder RTS. Requirements: `Wunderwaffe_3037_Requirements.xlsx`.

## Setup

- Godot 4.7.2 .NET (`C:\Users\Public\Godot\Godot_v4.7.2-stable_mono_win64`)
- .NET 8 SDK
- VS Code with C# Dev Kit and godot-tools (recommended in `.vscode/extensions.json`)
- Git LFS (`git lfs install` once per machine)

## Layout

| Path | What |
|---|---|
| `src/Wunderwaffe.Sim` | Game rules and state. Pure C#, **no Godot references**, integer-only, fixed tick. |
| `tests/Wunderwaffe.Sim.Tests` | xUnit tests for the sim, run against the real `game/data` content. |
| `game/` | Godot project. Draws the sim and turns input into `Command`s. |
| `game/render/` | Drawing. One MultiMesh per layer; redraws only when the grid's `Revision` changes. |
| `game/input/` | Camera, build tool, default key bindings. |
| `game/ui/` | Title screen, HUD, pause menu, settings, saves. Built in code from `Ui` helpers, not in .tscn files. |
| `game/debug/` | Debug tooling: cheats, stress benchmark, scripted `--drive` and `--tour` runs. |
| `game/data/*.json` | Content: items, recipes, buildings, goals, terrain. |

## Playing

The game opens on the title screen (`scenes/title.tscn`): **New game** (seed and map size), **Continue** (newest save), **Load game**, **Settings** (fullscreen, V-Sync), **Quit**. A match starts on the world map; the HUD's objective panel (top right) walks through the demo loop in `goals.json`: mine, smelt, claim a tile with coal, ship it home through ports, make steel, lift off a unit. **Esc** drops the picked building, then clears the unit selection, then opens the pause menu (save, load, settings, quit). **F1** lists the controls, **F2** shows fps and sim cost. There is no tech tree (TT-001): every building and recipe is available from the start.

Saves are a `GameSetup` plus the command log (`SaveFile`), in `user://saves` (`%APPDATA%\Godot\app_userdata\Wunderwaffe 3037\saves`). Loading replays the log (`Replay`) behind a progress bar, about 1.5 s per hour of play for a mid-sized factory, and warns if the replay doesn't end at the saved state hash (a save from different rules or content).

Debug flags, after `--`: `--stress` (add `--headless` to skip rendering) fills the home tile with 2,000 buildings and prints the sim cost per tick (F3 does the same in-game). `--drive [shot.png]` (needs a window) plays a scripted unit session through the real input path. `--tour out_dir` (needs a window) clicks through title, new game, world map, tile, claim, pause, save, quit and continue, logs what happened and saves a screenshot per step; it deletes the saves it made. **F4** adds 1,000 of every item to the viewed tile.

In game, **Tab** switches between the world map and the last tile you viewed. The world map is a blurred picture of every tile's local map, refreshed every few switches (3 by default; **Settings** changes it). On the world map: click selects a tile, double-click or Enter opens one you own, **C** claims the selected unclaimed tile next to one of yours. Drag with the left mouse button from one of your tiles onto an adjacent one of yours to export an item to it (`[` `]` pick the item, `-` `=` set a per-minute limit, `\` switches back to unlimited); drag with the right button to stop.

## Rules of the codebase

- The sim only changes through `Command`s (lockstep multiplayer, replays — NF-004).
- No `float`/`double` in sim state; no Godot types in `src/`.
- Economy tuning not yet in JSON (starting items, claim cost, frame cost, refunds) is in `Balance.cs`.
- Every tile currently runs the full entity sim. Rate mode for unviewed tiles (FB-012/013/014) is not built yet: it will be derived from the belt graph (OQ-14) and checked against the full sim.

## World

The world is one global grid of cells (`WorldMap`), generated in phases (`World/Gen`), all integer and seeded (WM-009):

1. **Terrain** (`TerrainGen`): domain-warped fractal noise for elevation, sunk toward the rim so land forms continents and islands; sea level and land height by rank, so every seed has 55% land and the same share of high ground; priority-flood drainage, which fills deep depressions as lakes and turns cells with a big enough catchment into rivers; moisture from noise and nearness to water. Each cell takes the first terrain in `terrain.json` whose elevation and moisture bands contain it.
2. **Tiles** (`Partition`): seeds thrown at random, no two closer than ~0.8 of a nominal tile on land and ~1.15 at sea, grow by cheapest path; climbing, coastlines and rivers cost extra, so borders follow ridges, coasts and rivers. Tiny regions merge into a neighbour. The result is RISK-like territories with 2-9 neighbours; ocean tiles are ordinary tiles, from coastal to all water. `GameSetup` width x height is the world size in nominal 96-cell tiles; the tile count is about that.
3. **I/O zones** (`Zones`): see Ports below.
4. **Deposits** (`TileGenerator`): patches on buildable ground, centred by each terrain's deposit weights, with the regional rule (RS-003) as before. Player 0's start is the land tile nearest the middle with room for a core and at least two land neighbours. With more players (`GameSetup.Players`, no menu option yet), each next start is the eligible tile farthest in tile-graph steps from those already picked: a placeholder until the fair-start algorithm (OQ-22). Every building records the player who placed it (`PlacedBuilding.Owner`).

Two tiles are neighbours if they share at least 3 cell edges. A tile's local map is its bounding box in the world map: local (x, y) is world (OriginX + x, OriginY + y). Cells of the box that belong to other tiles are outside the tile; the tile view shows them dimmed. Water and peaks are unbuildable (`PlaceResult.BadTerrain`). Tests and debugging can still hand-build square tiles with `AddTile(x, y, size)`.

A 5x5 world generates in about 0.3 s, 15x15 in about 2 s.

## Tile entities

- **Belts** carry up to 4 items per cell at 4/128 of a cell per tick, spaced at least 32 apart, so a belt moves 7.5 items a second. Rotation 0 faces east, then clockwise. A belt hands its front item to whatever it faces, and takes items from behind and the sides, never from the cell it faces.
- **Machines** (drills, smelters, mills) buffer two batches of each input, run their recipe, and dump output into any adjacent building that accepts it, taking turns. The core accepts everything into the tile inventory. `MachineState.Status` says why one isn't running (starved or output full); the tile view marks machines stopped for 1.5 s with an amber or red dot (FB-006).
- **Ports** are 1x1 buildings placed in an **I/O zone**. Each tile has one zone per neighbour: the cells within `PortRules.ZoneDepth` (4) steps of the shared border, on the side facing that neighbour. A breadth-first search from all border cells gives each cell to the neighbour that reaches it first (lower id on ties), so zones never overlap. A port's zone, not its rotation, decides the tile it links to. Tile A's zone for B is paired with B's zone for A: an export port in one sends to the import ports in the other, taking turns, and items flow both ways, after `PortRules.TransitTicksPerTile` ticks. An export refuses items when the partner isn't yours, has no import port in the paired zone, or the link is full, so belts back up rather than losing items. Port throughput is set by the belts feeding them. With a port picked, the tile view shades every zone in its own colour and labels the tile it links to. The zone pair is the link a future rate mode (FB-012) would aggregate on.
- Removing a building returns the items it holds to the tile inventory.

## Units

A unit is a group of buildings standing on **frame** (key `0` in the build palette; a floor layer that overlaps buildings and costs 2 iron ore a cell), lifted off the tile by a **unit core**. They are not defined in JSON.

- **Lift off:** `ActivateUnitCommand` on a unit core flood-fills the frame joined to it (up to 64 cells). Every building on those cells must lie wholly inside the frame, with no tile core, no second unit core and no ports, or it is refused. The buildings and frame leave the tile and become one unit; items they held go back to the tile inventory. In game, hover the core with no building picked and press **G**.
- **Body:** the unit's mass is the frame (2 a cell) plus each building's cost, so a heavier build is slower. Blocks are inert while the unit is active, and only add mass and thrust. Running machines inside a moving unit is planned.
- **Physics:** integer position, velocity, heading and spin (`Trig` is integer CORDIC trig). A **thruster** pushes the way it faces (rotation 0 faces east, as buildings do). Moving to a point, the unit turns its strongest engine toward the acceleration it needs, fires every engine in proportion to how well it lines up, and brakes into the target; turning rate is limited by its engines and its moment of inertia. Drag brings it to rest. Without a thruster it cannot move.
- **Collisions:** each cell is a circle. Units are pushed out of buildings (conveyors excepted) on any tile and out of each other, inelastically. Units path-find over the tile they are on and the next one (A* with clearance for the unit's size, straightened where there is a clear line, replanned when buildings change); with no way round they stop at the obstacle.
- **Locomotion (UD-004):** every unit is **ground**, **naval** or **air**, from its blocks: any **rotor** makes it air; otherwise a **hull** makes it naval (a hull with **wheels** as well can't be decided and is refused at lift-off); otherwise it is ground (wheels, or just thrusters). Ground crosses land, shallow water and rivers but not deep water or peaks; naval crosses deep and shallow water and rivers; air crosses anything. Terrain sets this through `ground`/`naval` in `terrain.json`. Pathing, border crossings and tile routes all obey it, and units are pushed back out of terrain they can't cross. A unit whose centre stands on such terrain (a boat built on land) can drive off it, and plans from the nearest cell within 8 it can use.
- **Tiles:** unit positions are world coordinates, so the tile a unit is on is just the tile of the cell under its centre, and crossing a border is no jump. A move finds a route of tiles through the terrain regions the unit's locomotion and size allow (`UnitNav`: per tile, the connected areas its centre can stand on, linked across borders), so a unit never needs to cross terrain it can't; a goal it can't reach moves to the nearest cell of the tile it can, and a tile it can't reach at all refuses the order. For each border the unit aims for a cell of the next tile, along the shared border and in the region its route enters, that makes the shortest trip, and plans its path over both tiles' buildings and terrain. Units can cross unowned and enemy tiles. At the edge of the world they stop at the border. Units collide with units on either side of a border.
- **Landing:** `DeployUnitCommand` puts the blocks and frame back on the tile, turned to the nearest quarter turn of the unit's heading, if the cells are free, buildable, inside the tile, and on a tile the player owns.
- **Orders (UC-001):** move, **hold** (stop and stay; fires but doesn't chase), **patrol** (back and forth between where the unit was and the target), **retreat** (to the owned tile it can reach in the fewest crossings, without firing). Attack and attack-move are combat's. A finished move leaves the unit anchored where it stopped.
- **Squads (UC-002, UC-003):** named groups (`CreateSquadCommand`) that share orders, formation and stance; ordering all of a squad's units moves them in the squad's formation. Formations: **box** (up to four columns, square to the map; loose units use it), **line**, **column**, **wedge** (point toward the goal) and **custom** (cell offsets forward/right), spaced by the units' sizes and turned to the direction of travel. Changing formation on the move re-forms the members still on the squad's order without breaking it. Squads are sim state.
- **Stances (UC-004):** **aggressive** (default), **defensive** (chases only within 12 cells of its anchor, then walks back), **hold fire** (never fires on its own), **escort** (follows another unit and fights only near it). Combat asks `UnitSim.MayChase`/`CanFire`.
- **Rally (UC-006):** a factory (today, a unit core) can have a rally point and a squad; a unit it finishes joins the squad and heads for the point. A unit core's setting is used up at lift-off.
- **Controls** (no building picked): left-click or drag a box to select your units (Shift adds), right-click to move them, and right-click past the tile's border to send them into the tile there. On the world map, right-click sends the selection to the clicked spot. **H** hold, **P** then right-click patrol, **X** retreat, **Q** make the selection a squad, **F** cycle its formation, **V** cycle stance (aggressive, defensive, hold fire), **E** escort the unit under the cursor, **Ctrl+1-9** store a control group, **Alt+1-9** recall it, **Y** over a unit core then right-click to set its rally point (and squad, if the selection is one squad). With a building picked, left places and right removes, as before.

## Content notes

- `terrain.json` lists terrains in classification order: a generated cell gets the first whose `elevation` and `moisture` bands (0-1000, inclusive) contain it; sea level is 400. `buildable` gates placement and frames; `water` marks coasts and ocean; `river` marks the terrain used for river cells (buildable for now, so rivers don't cut tiles up); `deposits` weights where each ore's patches are centred. Content validation checks every elevation and moisture is covered.

- Item `tier`: 1 raw, 2 refined, 3 advanced, 4 exotic. A recipe can't output an item more than one tier above its best input (RS-001).
- Recipe `ticks` are at 60 ticks/second. A recipe with empty `inputs` is extraction.
- Items with `"regional": true` (coal) never appear on a starting tile and appear on 35% of other tiles, always including at least one neighbour of each start (RS-003).
- Building `hp`: hit points of one placed block, kept while it rides on a unit. Must be > 0. Nothing deals damage yet.
- Goal conditions: `produce` (an item, counted when a batch finishes, extraction included), `claim` (tiles owned), `ship` (items arriving through import ports), `launch` (units lifted off). Counters are per player, empire-wide (TT-005).
- `goals.json` is the guided objective chain, completed in order. `DemoLoopTests` plays the whole chain with real content and no cheats, so a content change that breaks the loop fails the tests.
- Build-menu order (and hotkeys 1-9, 0) is `buildings.json` order; crafters with several recipes get one slot per recipe, and the unit frame is last.

## Commands

```
dotnet build Wunderwaffe.sln
dotnet test tests/Wunderwaffe.Sim.Tests
```

In VS Code, use the **Play** or **Open Editor** launch configurations.
