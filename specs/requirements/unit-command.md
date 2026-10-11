# Unit Command

P5 · Players command units and squads that act as a team.

Where it stands (2026-10-10): Select by click or box and move with right-click, on a tile or the world map. Units path around buildings, cross borders anywhere and collide. No squads, stances or attack orders.

## Requirements

| ID | Feature | Priority | Status | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|
| [UC-001](#uc-001-unit-selection--orders) | Unit selection & orders | Must | In Progress | MVP | Functional | P5 |  |  |  |
| [UC-002](#uc-002-squads) | Squads | Must | Proposed | MVP | Functional | P5 | UC-001 |  |  |
| [UC-003](#uc-003-formations) | Formations | Should | Proposed | Alpha | Functional | P5 | UC-002 |  |  |
| [UC-004](#uc-004-squad-behaviours--stances) | Squad behaviours / stances | Should | Proposed | Alpha | Functional | P5 | UC-002 |  |  |
| [UC-005](#uc-005-world-map-level-orders) | World-map level orders | Must | In Progress | MVP | Functional | P5 | UC-002, IO-003 | Updated |  |
| [UC-006](#uc-006-factory-rally--auto-assign) | Factory rally & auto-assign | Should | Proposed | Alpha | Functional | P5 | UC-002, UD-006 |  |  |
| [UC-007](#uc-007-pathfinding) | Pathfinding | Must | In Progress | MVP | Non-Functional | P5 | UD-004 |  |  |
| [UC-008](#uc-008-unit-collisions) | Unit collisions | Should | Done | MVP | Functional | P5 | UD-012 | New |  |
| [UC-009](#uc-009-abstract-travel) | Abstract travel | Must | Proposed | MVP | Functional | P3 | UC-001, UD-004, FB-013 | New |  |

### UC-001 Unit selection & orders

**Requirement:** The player shall select single or multiple units and issue move, attack, attack-move, hold, patrol and retreat orders.

**Acceptance criteria:** Box select, click select and control groups (1-9) work; orders execute with visible feedback.

**As built:** Click, box and Shift-add selection; right-click moves (tile view and world map); Esc clears. No attack, attack-move, hold, patrol or retreat; no control groups.

### UC-002 Squads

**Requirement:** Units shall be groupable into named squads that act as a team (shared orders, formation, target focus).

**Acceptance criteria:** A squad keeps formation when moving and focuses fire on the squad target.

**As built:** Not built.

### UC-003 Formations

**Requirement:** Squads shall support formations (line, column, wedge, custom) adjusting to unit sizes.

**Acceptance criteria:** Changing formation repositions members without breaking the squad order.

**As built:** Not built as squads. Units sent to one point spread on a 4-column grid, 2 cells apart.

### UC-004 Squad behaviours / stances

**Requirement:** Squads shall have stances (aggressive, defensive, hold fire, escort) controlling autonomous behaviour.

**Acceptance criteria:** Defensive squad returns to anchor after chasing beyond leash range.

**As built:** Not built.

### UC-005 World-map level orders

**Requirement:** Squads shall be orderable from the world map to move to/attack/garrison other tiles.

**Acceptance criteria:** An order from the world map routes the squad across tiles along a route its locomotion can travel (UD-004); no gates.

**As built:** Right-click on the world map sends the selection there. The tile route is a breadth-first search; units fly across each border at the crossing cell that makes the shortest trip, through unowned tiles too. No gates.

**Notes:** Acceptance mentions gates; follows the IO-003 decision (OQ-17). OQ-17 decided 2026-10-10.

### UC-006 Factory rally & auto-assign

**Requirement:** Factory output shall be assignable to a rally point or automatically added to a squad.

**Acceptance criteria:** New unit joins configured squad on completion.

**As built:** Not built.

### UC-007 Pathfinding

**Requirement:** Units shall path around buildings and terrain according to their locomotion and size, including large mega units.

**Acceptance criteria:** No units stuck in standard test maps; mega units path on large-footprint navmesh.

**As built:** A* over the current and next tile with clearance for the unit's size, path straightening, replanning when buildings change; conveyors are passable. Terrain is ignored (one locomotion); no large-footprint navmesh.

**Notes:** Terrain now matters for pathing (UD-004, OQ-17).

### UC-008 Unit collisions

**Requirement:** Units shall collide with buildings and with each other, including across tile borders.

**Acceptance criteria:** Units are pushed out of buildings (conveyors excepted) and apart from each other, inelastically and in proportion to mass.

**As built:** UnitPhysics.CollideBuildings / CollideUnits.

**Notes:** Added from what was built.

### UC-009 Abstract travel

**Requirement:** A unit outside every rendered tile shall travel as an abstract node: an icon on the world map following a terrain-legal route at a speed from its stats, without physics. It materialises at its exact world position when its tile is opened or becomes Contested.

**Acceptance criteria:** Leaving a rendered tile turns the unit into a node; its route is checked against the terrain of the tiles it crosses (loading their grids without simulating them); opening the tile, or an enemy unit entering, spawns it at the right position and state; travel time matches the full sim within a set tolerance.

**As built:** Units are always fully simulated in world coordinates.

**Notes:** OQ-15 decided 2026-10-10. Unwatched skirmishes run on automatic control, so stances (UC-004) matter.

## Decisions & open questions

None yet.
