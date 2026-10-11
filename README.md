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

The game opens on the title screen (`scenes/title.tscn`): **New game** (seed and map size), **Continue** (newest save), **Load game**, **Settings** (fullscreen, V-Sync), **Quit**. A match starts on the world map; the HUD's objective panel (top right) walks through the demo loop in `goals.json`: mine, smelt, lift off your starting scout, land it in the victory circle of a tile with coal and hold it until the tile is yours, ship coal home through ports, make steel, build a unit of your own. **Esc** drops the picked building, then clears the unit selection, then opens the pause menu (save, load, settings, quit). **F1** lists the controls, **F2** shows fps and sim cost. There is no tech tree (TT-001): every building and recipe is available from the start.

Saves are a `GameSetup` plus the command log (`SaveFile`), in `user://saves` (`%APPDATA%\Godot\app_userdata\Wunderwaffe 3037\saves`). Loading replays the log (`Replay`) behind a progress bar, about 1.5 s per hour of play for a mid-sized factory, and warns if the replay doesn't end at the saved state hash (a save from different rules or content).

Debug flags, after `--`: `--stress` (add `--headless` to skip rendering) fills the home tile with 2,000 buildings and prints the sim cost per tick (F3 does the same in-game). `--drive [shot.png]` (needs a window) plays a scripted unit session through the real input path. `--tour out_dir` (needs a window) clicks through title, new game, world map, tile, scout sent to the coal tile, pause, save, quit and continue, logs what happened and saves a screenshot per step; it deletes the saves it made. **F4** adds 1,000 of every item to the viewed tile.

In game, **Tab** switches between the world map and the last tile you viewed. The world map is a blurred picture of every tile's local map, refreshed every few switches (3 by default; **Settings** changes it). On the world map: click selects a tile, double-click or Enter opens one you have presence on, **C** (or **Send units**) sends the selected units to the selected tile's victory circle, and **G** lands them where they are. Every tile's victory circle is drawn in its owner's colour, with an arc per player claiming it; Contested tiles are outlined red. Drag with the left mouse button from one of your tiles onto an adjacent one of yours to export an item to it (`[` `]` pick the item, `-` `=` set a per-minute limit, `\` switches back to unlimited); drag with the right button to stop.

## Rules of the codebase

- The sim only changes through `Command`s (lockstep multiplayer, replays — NF-004).
- No `float`/`double` in sim state; no Godot types in `src/`.
- Economy and territory tuning not yet in JSON (starting items, frame cost, refunds, circle radius and capture time) is in `Balance.cs`.
- Every tile currently runs the full entity sim. Rate mode for unviewed tiles (FB-012/013/014) is not built yet: it will be derived from the belt graph (OQ-14) and checked against the full sim.

## World

The world is one global grid of cells (`WorldMap`), generated in phases (`World/Gen`), all integer and seeded (WM-009):

1. **Terrain** (`TerrainGen`): domain-warped fractal noise for elevation, sunk toward the rim so land forms continents and islands; sea level and land height by rank, so every seed has 55% land and the same share of high ground; priority-flood drainage, which fills deep depressions as lakes and turns cells with a big enough catchment into rivers; moisture from noise and nearness to water. Each cell takes the first terrain in `terrain.json` whose elevation and moisture bands contain it.
2. **Tiles** (`Partition`): seeds thrown at random, no two closer than ~0.8 of a nominal tile on land and ~1.15 at sea, grow by cheapest path; climbing, coastlines and rivers cost extra, so borders follow ridges, coasts and rivers. Tiny regions merge into a neighbour. The result is RISK-like territories with 2-9 neighbours; ocean tiles are ordinary tiles, from coastal to all water. `GameSetup` width x height is the world size in nominal 96-cell tiles; the tile count is about that.
3. **I/O zones** (`Zones`): see Ports below.
4. **Deposits** (`TileGenerator`): patches on buildable ground, centred by each terrain's deposit weights, with the regional rule (RS-003) as before.
5. **Victory circles** (`Circles`, WM-014): one per tile, ocean tiles included, radius `Balance.CircleRadius` (12). It goes where the most deposit cells fall inside it, among centres that keep it `Balance.CircleClearance` (4) cells off the tile's border (just wholly inside if that leaves no deposit in reach; a tile too small for the circle gets its deepest cell).

**Starts** (WM-005, OQ-22): one player gets the land tile nearest the middle with room for a core and at least two land neighbours. With more players (`GameSetup.Players`, no menu option yet), `Balance.StartAttempts` seeded attempts each pick a first start (the middle one, then random ones) and add the rest by farthest-point sampling over tile-graph steps; the kept set has the most starts with a land neighbour to carry their regional deposit (so coal is one claim away), then the most even starting deposits, then the widest spacing. A start gets its core nearest its circle's centre, the starting items, and a landed **scout** (thruster, unit core, thruster on frame, a few rows above the core). Every building records the player who placed it (`PlacedBuilding.Owner`).

Two tiles are neighbours if they share at least 3 cell edges. A tile's local map is its bounding box in the world map: local (x, y) is world (OriginX + x, OriginY + y). Cells of the box that belong to other tiles are outside the tile; the tile view shows them dimmed. Water and peaks are unbuildable (`PlaceResult.BadTerrain`), except that a **floating platform** (FB-017; a building of kind `platform`, `onWater` in buildings.json, 2 iron ore) can be laid on water terrain. A platform is a floor layer, like frame: buildings, frames and landings can then stand on that cell. Removing on a cell with only a platform takes the platform up and refunds it. Tests and debugging can still hand-build square tiles with `AddTile(x, y, size)`.

A 5x5 world generates in about 0.3 s, 15x15 in about 2 s.

## Territory

- **Presence** (WM-015): you may build, lay platforms and land on a tile you own or where you have a building or unit (`Territory.HasPresence`); otherwise placement says `NoPresence`. Nobody builds or lands with any cell inside an enemy-owned victory circle while a building not of theirs stands in it (`EnemyCircle`). You can only remove your own buildings.
- **Paying:** a building is paid from the tile's own stock if you own the tile and it can afford it, otherwise from the first neighbouring tile you own that can (`Territory.Payer`). Unowned tiles have no stock, so building on a new tile is supplied from next door; refunds go back the same way.
- **Claiming** (WM-006, CB-003): every tick, each player with a building (any cell) in a tile's circle who doesn't own the tile counts up; with none there their count drops to zero. The first to reach the capture time (`Balance.CaptureSeconds`, 30 s) takes the tile (lowest id on a tie) and every other player's building in the circle is destroyed; buildings elsewhere keep their owner. Capturing an enemy tile is the same rule, once their buildings in the circle are gone. The usual first foothold is a unit: fly it into the circle (C on the world map) and land it (G).
- **Cores** (WM-016): buildable (free for now), and you can't remove your last one. A player with no core left is eliminated (the victory check). Until combat, `DebugDestroyCommand` destroys a building.
- **Contested** (WM-004): a tile with a unit of someone other than its owner on it (an unowned tile: units of two or more players), `Territory.IsContested`.

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
- **Tiles:** unit positions are world coordinates, so the tile a unit is on is just the tile of the cell under its centre, and crossing a border is no jump. `MoveUnitsCommand` finds a route of tiles by breadth-first search; for each border the unit aims for the cell of the next tile, along the shared border, that makes the shortest trip, and plans its path over both tiles' buildings. Units can cross unowned and enemy tiles. At the edge of the world they stop at the border. Units collide with units on either side of a border.
- **Landing:** `DeployUnitCommand` puts the blocks and frame back on the tile, turned to the nearest quarter turn of the unit's heading, if the cells are free, buildable (ground or platform) and inside the tile, on any tile (the unit is presence there), but not in an enemy-held circle (UD-011).
- **Controls** (no building picked): left-click or drag a box to select your units (Shift adds), right-click to move them, and right-click past the tile's border to send them into the tile there. On the world map, right-click sends the selection to the clicked spot. With a building picked, left places and right removes, as before.

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
