# Glossary

Terms used across the requirements and the code.

### Wunderwaffe

German for 'wonder weapon'. A particularly large and expensive unit: same blocks and rules as any unit, built by a megafactory; the game's signature end-game asset.

### World Map

The strategic layer: a map of tiles that players claim, link and fight over.

### Tile

One territory of the world map: an irregular region of world cells (about 96x96 on average) with its own real-time local map for building and combat.

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

96x96 cells: the unit of map size. Generated tiles average about this area.

### Regional Resource

An item whose deposits appear only on some tiles and never on a starting tile (currently coal).

### Frame

A floor laid under buildings to mark them as one unit. Costs 2 iron ore a cell.

### Unit Core

The block that lifts the framed group around it off the tile as a unit (and, on landing, puts it back).

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
