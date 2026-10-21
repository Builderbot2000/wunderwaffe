# Glossary

Terms used across the requirements and the code.

### Wunderwaffe

German for 'wonder weapon'. A particularly large and expensive unit: same blocks and rules as any unit, built by a megafactory; the game's signature end-game asset.

### World Map

The strategic layer: a map of tiles that players claim, link and fight over.

### Tile

One territory of the world map: an irregular region of world cells (about 256x256 on average) with its own real-time local map for building and combat.

### Tile Local Map

The RTS/factory-builder map inside a tile, where buildings are placed and battles are fought. It covers the tile's bounding box; cells of neighbouring tiles show dimmed and can't be built on.

### I/O Zone / Port

An I/O zone is the strip of a tile's cells (4 deep) along its border with one neighbour. Export and import ports in paired zones move items between the two tiles. Units don't use ports: they cross borders anywhere their locomotion allows.

### Megafactory

A factory with enough boosters for a block cap big enough to build a Wunderwaffe.

### Block

The smallest component of a unit (armour, weapon, engine, power, etc.). Blocks are ordinary buildings standing on frame.

### Squad

A named group of units that shares orders, formation and targeting.

### Protection Period

Not a mechanic (OQ-4). No Rush comes from defence being more cost-effective than offence early in a match.

### Resource Tier

Level of an item in the production chain (raw → refined → advanced → exotic). Higher tiers unlock tech.

### MoSCoW

Prioritisation scheme: Must have, Should have, Could have, Won't have (this release).

### Nominal Tile

256x256 cells (was 96x96; OQ-35): the unit of map size. Generated tiles average about this area.

### Regional Resource

An item whose deposits appear only on some tiles and never on a starting tile (currently coal).

### Frame

A floor laid under buildings to mark them as one unit. Costs 2 iron ore a cell.

### Core

The one core building (WM-016). It stores the items that pay for building on its tile (FB-019), supplies power and a resource radius, and lifts the framed group around it off as a unit (on landing, it puts it back). Every unit has exactly one; a player with no core left is eliminated. Replaces the separate tile core and unit core.

### Thruster

A unit block that pushes the unit the way it faces while the unit is active.

### Rate Mode

Background simulation for tiles nobody is viewing and that aren't Contested: aggregated per-item rates instead of the full entity sim (FB-012).

### Command / Command Log

Every player action is a command applied on a tick. A save is the match setup plus the command log, replayed on load.

### State Hash

A hash of the whole simulation state, used to detect desyncs and check that a loaded save replayed correctly.

### Goal Chain

The guided objectives in goals.json that walk a new player through the demo loop.

### Victory Circle

A fixed-radius circle on each tile, placed near rich deposits. Keeping a building in it for the capture time claims or captures the tile.

### Contested

A tile with enemy units on it. It always renders and runs the full sim.

### Booster

A building that improves an adjacent building of a matching category. A factory's radius and block cap come from its boosters.

### Blueprint

A saved set of buildings or a unit design, in a library shared across games. Loads as a ghost guide; factories build unit blueprints automatically.

### Abstract Unit Node

A unit travelling outside rendered tiles: an icon on the world map with recorded stats, materialised when its tile is opened or Contested.

### Locomotion

Ground, naval or air. Decides which terrain a unit can cross.

### Cell Layer

One of the stacked per-cell maps of a tile (deposits, platforms, cables, frame, buildings), declared in content with rules for what may stand on what (FB-022).

### Patch

A group of connected deposit cells of one item, found at world generation. It holds one amount that all extractors on it share and that runs out (RS-004).

### Scenario

A map plus what stands on it at the start: the world (hand-authored, or generated and saved) and each seat's starting tiles, buildings, units and stock (WM-017).

### Faction

What a seat plays as: a template that highlights the parts of the hidden master tech tree (the shared catalogue of buildings and blocks) the seat can build, which variants replace shared entries, and which special rules apply (MP-009, MP-010).

### Neutral site

An installation of the neutral guardian faction, placed at world generation or in a scenario, guarding rich deposits and tech players can't build. Each site has its own neutral player; destroying all its cores leaves its buildings unclaimed (WM-010, WM-018).

### Relocation

Cutting a building you own and pasting it, intact, elsewhere on the same tile, neither refunded nor paid for again; it never pastes onto another tile, so to leave its tile it is grafted onto a landed unit and flown out (FB-023).

### Unclaimed building

A building whose seat lost its last core. It belongs to no one and does nothing until a player claims it by building or landing a unit near it (WM-019).
