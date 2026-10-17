# Unit Command

P5 · Players command units and squads that act as a team.

Where it stands (2026-10-17): Selection, move, attack, attack-move, hold, patrol, retreat, control groups, squads with formations and stances, rally points, terrain-aware pathing and abstract travel are built. To do from playtesting: units switch to abstract travel at the border and keep it until they stop.

## Requirements

| ID | Feature | Priority | Status | Built | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|---|
| [UC-001](#uc-001-unit-selection--orders) | Unit selection & orders | Must | Done | Yes | MVP | Functional | P5 |  |  |  |
| [UC-002](#uc-002-squads) | Squads | Must | Done | Yes | MVP | Functional | P5 | UC-001 |  |  |
| [UC-003](#uc-003-formations) | Formations | Should | Done | Yes | Alpha | Functional | P5 | UC-002 |  |  |
| [UC-004](#uc-004-squad-behaviours--stances) | Squad behaviours / stances | Should | Done | Yes | Alpha | Functional | P5 | UC-002 |  |  |
| [UC-005](#uc-005-world-map-level-orders) | World-map level orders | Must | In Progress | Partial | MVP | Functional | P5 | UC-002, IO-003, WM-007, UC-009 | Updated |  |
| [UC-006](#uc-006-factory-rally--auto-assign) | Factory rally & auto-assign | Should | Done | Yes | Alpha | Functional | P5 | UC-002, UD-006 |  |  |
| [UC-007](#uc-007-pathfinding) | Pathfinding | Must | Done | Yes | MVP | Non-Functional | P5 | UD-004 |  |  |
| [UC-008](#uc-008-unit-collisions) | Unit collisions | Should | Done | Yes | MVP | Functional | P5 | UD-012 | New |  |
| [UC-009](#uc-009-abstract-travel) | Abstract travel | Must | In Progress | Partial | MVP | Functional | P3 | UC-001, UD-004, FB-013 | Updated |  |

### UC-001 Unit selection & orders

**Requirement:** The player shall select single or multiple units and issue move, attack, attack-move, hold, patrol and retreat orders.

**Acceptance criteria:** Box select, click select and control groups (1-9) work; orders execute with visible feedback.

**As built:** Click, box and Shift-add selection; right-click move (tile view and world map); attack, attack-move, H hold, patrol, X retreat; Ctrl/Alt+1-9 control groups (UI only); destination lines and status text.

### UC-002 Squads

**Requirement:** Units shall be groupable into named squads that act as a team (shared orders, formation, target focus).

**Acceptance criteria:** A squad keeps formation when moving and focuses fire on the squad target.

**As built:** World/Squads.cs: Q forms a named squad that shares order, formation and stance, and focuses fire on Squad.TargetId; squads can be renamed, reassigned and disbanded.

### UC-003 Formations

**Requirement:** Squads shall support formations (line, column, wedge, custom) adjusting to unit sizes.

**Acceptance criteria:** Changing formation repositions members without breaking the squad order.

**As built:** Box, line, column, wedge and custom formations (T); slots make room for big units (tested).

### UC-004 Squad behaviours / stances

**Requirement:** Squads shall have stances (aggressive, defensive, hold fire, escort) controlling autonomous behaviour.

**Acceptance criteria:** Defensive squad returns to anchor after chasing beyond leash range.

**As built:** Aggressive, defensive (returns to its anchor past the leash), hold fire and escort (E) stances; V cycles (UnitSim).

**Notes:** 2026-10-17: the stance also decides what a travelling unit does when it meets enemies (UC-009): keep its course through the fight or stop to fight. Which stance does which is still to set.

### UC-005 World-map level orders

**Requirement:** Units and squads shall be orderable to any point on the world map; the order takes them straight there, switching to abstract travel (UC-009) by themselves, with no separate trip to a border first.

**Acceptance criteria:** With units selected, right-click on the world map (at any zoom, WM-007) sends them to that point along a route their locomotion can travel (UD-004); they head straight for it and switch to abstract travel by themselves as they cross the border of the rendered tile (UC-009); no unit stops or waits at a border; no gates.

**As built:** Right-click on the world map sends the selection there along a terrain-legal route (UnitNav.Plan; unreachable targets get a notice), across any border, unowned tiles too. A unit is abstract while its tile runs in rate mode, not from a border crossing. No gates.

**Notes:** Was: 'Squads shall be orderable from the world map to move to/attack/garrison other tiles'. Acceptance mentions gates; follows the IO-003 decision (OQ-17). OQ-17 decided 2026-10-10. Rewritten after playtesting 2026-10-17: units had to be taken to the tile border and wait there before they travelled; waiting is obsolete, the switch to abstract travel is automatic. Viewed tiles while zoomed: OQ-36.

### UC-006 Factory rally & auto-assign

**Requirement:** Factory output shall be assignable to a rally point or automatically added to a squad.

**Acceptance criteria:** New unit joins configured squad on completion.

**As built:** Y sets a factory's rally point and squad; new units fly there and join it (UnitSim.OnUnitBuilt).

### UC-007 Pathfinding

**Requirement:** Units shall path around buildings and terrain according to their locomotion and size, including large mega units.

**Acceptance criteria:** No units stuck in standard test maps; mega units path on large-footprint navmesh.

**As built:** A* over the current and next tile with clearance for the unit's size and terrain by locomotion, line-of-sight straightening, replanning when buildings change; a region graph per (class, clearance) plans long routes for units of any size (UnitNav); a blocked start heads for the nearest free cell.

**Notes:** Terrain now matters for pathing (UD-004, OQ-17).

### UC-008 Unit collisions

**Requirement:** Units shall collide with buildings and with each other, including across tile borders.

**Acceptance criteria:** Units are pushed out of buildings (conveyors excepted) and apart from each other, inelastically and in proportion to mass.

**As built:** UnitPhysics.CollideBuildings / CollideUnits.

**Notes:** Added from what was built.

### UC-009 Abstract travel

**Requirement:** A unit ordered to a destination on another tile shall fly physically across its rendered tile and, on crossing the border, become an abstract node: an icon on the world map following a terrain-legal route at a speed from its stats, without physics. It stays abstract until it stops at its destination, except while it passes through the rendered tile or meets enemies; the player opens the destination tile to render it again.

**Acceptance criteria:** Crossing the rendered tile's border turns the unit into a node with no stop or wait; a unit ordered on a tile that isn't rendered is a node from the start. A node whose route enters the rendered tile materialises there and keeps its order, flying physically to the destination and turning back into a node when it leaves that tile. A node that meets enemy units interrupts its travel: the tile turns Contested and runs the full sim (FB-013), the unit materialises, and the skirmish plays out; by its stance (UC-004) it either keeps its course through the fight or stops to fight, and a unit that survives resumes its order, going abstract again once the tile is no longer Contested. When it stops it stays a node at its exact world position until its tile is opened, which spawns it there with its state. Travel time matches the full sim within a set tolerance.

**As built:** World/AbstractTravel.cs: a unit on a rate-mode tile is a node moving along planned waypoints at cruise speed with no physics; it materialises where it is, keeping its order, when its tile is viewed or Contested, and goes abstract again on a rate-mode tile (tested); travel time within the set tolerance; turrets can't see nodes. Units go abstract by tile mode, not by crossing a border, and no stance decides between keeping course and stopping to fight.

**Notes:** Was: 'A unit outside every rendered tile shall travel as an abstract node ... It materialises at its exact world position when its tile is opened or becomes Contested.' OQ-15 decided 2026-10-10. Unwatched skirmishes run on automatic control, so stances (UC-004) matter. Rewritten after playtesting 2026-10-17: units switch at the border, keep travelling abstractly until they stop, and waiting at borders is gone (UC-005, OQ-36). Owner, same day: passing through the rendered tile renders the unit but keeps its course; meeting enemies interrupts travel for a skirmish, with the stance deciding course or standstill.

## Decisions & open questions

None yet.
