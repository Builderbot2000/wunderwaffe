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
| `game/data/*.json` | Content: items, recipes, buildings, tech, goals. |

## Playing

The game opens on the title screen (`scenes/title.tscn`): **New game** (seed and map size), **Continue** (newest save), **Load game**, **Settings** (fullscreen, V-Sync), **Quit**. A match starts on the world map; the HUD's objective panel (top right) walks through the demo loop in `goals.json`: mine, smelt, claim a tile with coal, ship it home through ports, make steel, lift off a unit. **Esc** drops the picked building, then clears the unit selection, then opens the pause menu (save, load, settings, quit). **F1** lists the controls, **T** opens the tech tree, **F2** shows fps and sim cost.

Saves are a `GameSetup` plus the command log (`SaveFile`), in `user://saves` (`%APPDATA%\Godot\app_userdata\Wunderwaffe 3037\saves`). Loading replays the log (`Replay`) behind a progress bar, about 1.5 s per hour of play for a mid-sized factory, and warns if the replay doesn't end at the saved state hash (a save from different rules or content).

Debug flags, after `--`: `--stress` (add `--headless` to skip rendering) fills the home tile with 2,000 buildings and prints the sim cost per tick (F3 does the same in-game). `--drive [shot.png]` (needs a window) plays a scripted unit session through the real input path. `--tour out_dir` (needs a window) clicks through title, new game, world map, tile, tech tree, claim, pause, save, quit and continue, logs what happened and saves a screenshot per step; it deletes the saves it made. **F4** unlocks all tech and adds 1,000 of every item to the viewed tile.

In game, **Tab** switches between the tile view and the world map. On the world map: click selects a tile, double-click or Enter opens one you own, **C** claims the selected unclaimed tile next to one of yours. Drag with the left mouse button from one of your tiles onto an adjacent one of yours to export an item to it (`[` `]` pick the item, `-` `=` set a per-minute limit, `\` switches back to unlimited); drag with the right button to stop.

## Rules of the codebase

- The sim only changes through `Command`s (lockstep multiplayer, replays — NF-004).
- No `float`/`double` in sim state; no Godot types in `src/`.
- Every tile currently runs the full entity sim. Rate mode for unviewed tiles (FB-012/013/014) is not built yet: it will be derived from the belt graph (OQ-14) and checked against the full sim.

## Tile entities

- **Belts** carry up to 4 items per cell at 4/128 of a cell per tick, spaced at least 32 apart, so a belt moves 7.5 items a second. Rotation 0 faces east, then clockwise. A belt hands its front item to whatever it faces, and takes items from behind and the sides, never from the cell it faces.
- **Machines** (drills, smelters, mills) buffer two batches of each input, run their recipe, and dump output into any adjacent building that accepts it, taking turns. The core accepts everything into the tile inventory. `MachineState.Status` says why one isn't running (starved or output full); the tile view marks machines stopped for 1.5 s with an amber or red dot (FB-006).
- **Ports** are 1x1 buildings placed within 3 cells of the edge they face (their rotation). An export port sends to the import ports in the same lane of the neighbouring tile's opposite edge, after `PortRules.TransitTicksPerTile` ticks per tile. It refuses items when the neighbour isn't yours, has no import port in that lane, or the link is full, so belts back up rather than losing items. Port throughput is set by the belts feeding them.
- Removing a building returns the items it holds to the tile inventory.

## Units

A unit is a group of buildings standing on **frame** (key `0` in the build palette; a floor layer that overlaps buildings and costs 2 iron ore a cell), lifted off the tile by a **unit core**. They are not defined in JSON.

- **Lift off:** `ActivateUnitCommand` on a unit core flood-fills the frame joined to it (up to 64 cells). Every building on those cells must lie wholly inside the frame, with no tile core, no second unit core and no ports, or it is refused. The buildings and frame leave the tile and become one unit; items they held go back to the tile inventory. In game, hover the core with no building picked and press **G**.
- **Body:** the unit's mass is the frame (2 a cell) plus each building's cost, so a heavier build is slower. Blocks are inert while the unit is active, and only add mass and thrust. Running machines inside a moving unit is planned.
- **Physics:** integer position, velocity, heading and spin (`Trig` is integer CORDIC trig). A **thruster** pushes the way it faces (rotation 0 faces east, as buildings do). Moving to a point, the unit turns its strongest engine toward the acceleration it needs, fires every engine in proportion to how well it lines up, and brakes into the target; turning rate is limited by its engines and its moment of inertia. Drag brings it to rest. Without a thruster it cannot move.
- **Collisions:** each cell is a circle. Units are pushed out of buildings (conveyors excepted) and out of each other, inelastically. Units path-find inside a tile (A* with clearance for the unit's size, straightened where there is a clear line, replanned when buildings change); with no way round they stop at the obstacle. Units collide only with things on their own tile.
- **Tiles:** `MoveUnitsCommand` finds a route of tiles by breadth-first search and flies straight across each edge; the unit hops tile when its centre crosses. Units can cross unowned and enemy tiles. At the edge of the world they stop at the border.
- **Landing:** `DeployUnitCommand` puts the blocks and frame back on the tile, turned to the nearest quarter turn of the unit's heading, if the cells are free, in bounds, and on a tile the player owns.
- **Controls** (no building picked): left-click or drag a box to select your units (Shift adds), right-click to move them, and right-click past the tile's edge to send them into the neighbouring tile. On the world map, right-click sends the selection to the middle of a tile. With a building picked, left places and right removes, as before.

## Content notes

- Item `tier`: 1 raw, 2 refined, 3 advanced, 4 exotic. A recipe can't output an item more than one tier above its best input (RS-001).
- Recipe `ticks` are at 60 ticks/second. A recipe with empty `inputs` is extraction.
- Items with `"regional": true` (coal) never appear on a starting tile and appear on 35% of other tiles, always including at least one neighbour of each start (RS-003).
- Tech and goal conditions: `produce` (an item, counted when a batch finishes, extraction included), `claim` (tiles owned), `ship` (items arriving through import ports), `launch` (units lifted off). Counters are per player, empire-wide (TT-005).
- A building or recipe listed in some tech node's `unlocks` is locked until that node completes (`PlaceResult.Locked`); anything not listed is starter content (TT-004). Each id may be unlocked by one node only.
- `goals.json` is the guided objective chain, completed in order. `DemoLoopTests` plays the whole chain with real content and no cheats, so a content change that breaks the loop fails the tests.
- Build-menu order (and hotkeys 1-9, 0) is `buildings.json` order; crafters with several recipes get one slot per recipe, and the unit frame is last.

## Commands

```
dotnet build Wunderwaffe.sln
dotnet test tests/Wunderwaffe.Sim.Tests
```

In VS Code, use the **Play** or **Open Editor** launch configurations.
