# Unit Command

P5 · Players command units and squads that act as a team.

Where it stands (2026-10-17): Selection, move, attack, attack-move, hold, patrol, retreat, control groups, squads with formations and stances, rally points, terrain-aware pathing and abstract travel are built. To do from playtesting: units switch to abstract travel at the border and keep it until they stop; aggressive stance seeks out enemies across the tile and a new passive stance keeps course.

## Requirements

| ID | Feature | Priority | Status | Built | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|---|
| [UC-001](#uc-001-unit-selection--orders) | Unit selection & orders | Must | Done | Yes | MVP | Functional | P5 |  |  |  |
| [UC-002](#uc-002-squads) | Squads | Must | Done | Yes | MVP | Functional | P5 | UC-001 |  |  |
| [UC-003](#uc-003-formations) | Formations | Should | Done | Yes | Alpha | Functional | P5 | UC-002 |  |  |
| [UC-004](#uc-004-squad-behaviours--stances) | Squad behaviours / stances | Should | Done | Yes | Alpha | Functional | P5 | UC-002 | Updated |  |
| [UC-005](#uc-005-world-map-level-orders) | World-map level orders | Must | Done | Yes | MVP | Functional | P5 | UC-002, IO-003, WM-007, UC-009 | Updated |  |
| [UC-006](#uc-006-factory-rally--auto-assign) | Factory rally & auto-assign | Should | Done | Yes | Alpha | Functional | P5 | UC-002, UD-006 |  |  |
| [UC-007](#uc-007-pathfinding) | Pathfinding | Must | Done | Yes | MVP | Non-Functional | P5 | UD-004 |  |  |
| [UC-008](#uc-008-unit-collisions) | Unit collisions | Should | Done | Yes | MVP | Functional | P5 | UD-012 | New |  |
| [UC-009](#uc-009-abstract-travel) | Abstract travel | Must | Done | Yes | MVP | Functional | P3 | UC-001, UD-004, FB-013 | Updated |  |
| [UC-010](#uc-010-follow-cursor-mode) | Follow cursor mode | Should | Approved | No | Alpha | Functional | P5 | UC-001, UC-003, NF-004 | New |  |

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

**Requirement:** Units and squads shall have one of four stances controlling autonomous behaviour. Aggressive: seeks out any enemy on its tile and engages it. Defensive: engages enemies in range and may chase a little beyond it, but always returns (to its anchor, or to its course when it has an order) and never pursues indefinitely. Passive: keeps its course and order and opens fire only on enemies within weapon range; never chases. Hold fire: does nothing on its own, neither firing nor chasing, and only carries out its orders.

**Acceptance criteria:** An aggressive unit with no order heads for and attacks the nearest enemy anywhere on its tile, not only those in weapon range. A defensive unit chases a target out of range at most the leash distance (UnitSim.LeashCells) from its anchor or course, then goes back and resumes its order. A passive unit on a move order keeps moving at the same speed and path while fired on, shoots enemies in range, and never leaves its path to chase. A hold-fire unit never fires or moves on its own, even when attacked. Travelling units that meet enemies (UC-009): aggressive stops to fight; defensive fights, chases within its leash and then resumes its course; passive and hold fire keep their course (passive shooting what comes into range). There is no escort stance.

**As built:** UnitSim: four stances, V cycles them (squads share theirs). Aggressive (default): with no order, or once stopped to fight, goes for the nearest enemy unit anywhere on its tile (an idle one then enemy buildings too); on a move it stops for enemy units in weapon range, fights them, then resumes. Defensive: goes for enemy units it can reach within UnitSim.LeashCells of its anchor (where it stood, or where it left its course), never past the leash, then returns or resumes its order. Passive: keeps course and speed, fires at what is in range, never chases. Hold fire: never fires or moves on its own. Orders are carried out whatever the stance: attack and attack-move fire and chase even on hold fire. No escort stance. Travelling nodes that meet enemy units stop or keep course by UnitSim.StopsToFight (UC-009). All tested (StanceTests).

**Notes:** Was: 'Squads shall have stances (aggressive, defensive, hold fire, escort) controlling autonomous behaviour.' 2026-10-17: the stance also decides what a travelling unit does when it meets enemies (UC-009): keep its course through the fight or stop to fight. Owner, same day: aggressive seeks out any enemy in the tile and engages; passive keeps course, fights back only when an enemy comes into range and doesn't pursue when it leaves. So aggressive stops to fight and passive keeps course. OQ-40 decided the same day: four stances (aggressive, defensive, passive, hold fire); defensive chases a little and always returns; hold fire does nothing on its own; no escort stance.

### UC-005 World-map level orders

**Requirement:** Units and squads shall be orderable to any point on the world map; the order takes them straight there, switching to abstract travel (UC-009) by themselves, with no separate trip to a border first.

**Acceptance criteria:** With units selected, right-click on the world map (at any zoom, WM-007) sends them to that point along a route their locomotion can travel (UD-004); they head straight for it and switch to abstract travel by themselves as they cross the border of the rendered tile (UC-009); no unit stops or waits at a border; no gates.

**As built:** Right-click on the world map sends the selection there along a terrain-legal route (UnitNav.Plan; unreachable targets get a notice), across any border, unowned tiles too. Crossing the rendered tile's border turns the unit into a node that same tick, still moving (AbstractTravel.Crossed, from UnitPhysics; tested); a node with no route from where it is falls back to the straight line rather than stopping. No gates.

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

**Acceptance criteria:** Crossing the rendered tile's border turns the unit into a node with no stop or wait; a unit ordered on a tile that isn't rendered is a node from the start. A node whose route enters the rendered tile materialises there and keeps its order, flying physically to the destination and turning back into a node when it leaves that tile. A node that meets enemy units interrupts its travel: the tile turns Contested and runs the full sim (FB-013), the unit materialises, and the skirmish plays out; by its stance (UC-004) it either keeps its course through the fight (passive) or stops to fight (aggressive), and a unit that survives resumes its order, going abstract again once the tile is no longer Contested. When it stops it stays a node at its exact world position until its tile is opened, which spawns it there with its state. Travel time matches the full sim within a set tolerance.

**As built:** World/AbstractTravel.cs: rendered = viewed by a player or Contested (OQ-36). A physical unit becomes a node the tick it crosses onto a tile that isn't rendered (UnitPhysics hook), a unit ordered off the rendered tile is one from the start; nodes move along planned waypoints at cruise speed with no physics and materialise where they are, keeping their order, on a rendered tile, going abstract again on leaving it. A travelling node that materialises on a Contested tile with enemy units on it halts if UnitSim.StopsToFight (aggressive, defensive), keeping its order, and resumes once no enemy unit is left on the tile (Simulation.InterruptedUnits, hashed and snapshotted); passive and hold fire keep their course. A stopped node stays exactly where it is until its tile is opened. All tested; turrets can't see nodes. Full-sim travel time matches the abstract estimate within AbstractTravel.TolerancePercent (tested; the steering no longer weaves, so a scout flies within about 1% of it).

**Notes:** Was: 'A unit outside every rendered tile shall travel as an abstract node ... It materialises at its exact world position when its tile is opened or becomes Contested.' OQ-15 decided 2026-10-10. Unwatched skirmishes run on automatic control, so stances (UC-004) matter. Rewritten after playtesting 2026-10-17: units switch at the border, keep travelling abstractly until they stop, and waiting at borders is gone (UC-005, OQ-36). Owner, same day: passing through the rendered tile renders the unit but keeps its course; meeting enemies interrupts travel for a skirmish, with the stance deciding course or standstill.

### UC-010 Follow cursor mode

**Requirement:** The player shall be able to switch the selected vehicles into a mode where they keep moving towards the mouse cursor until the mode ends.

**Acceptance criteria:** A key (rebindable, UI-005) toggles the mode for the selection; while it is on, the selection heads for the cell under the cursor and follows it as it moves, and squads keep their formation (UC-003). It ends on the key again, on any other order, or when the selection is cleared. Target updates are ordinary commands, sent only when the cursor has moved and at most a few times a second, so lockstep and replays work (NF-004) and the command log stays small. Works on the tile view and the world map.

**As built:** Not built.

**Notes:** Owner, 2026-10-20.

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-40](#oq-40-what-do-defensive-hold-fire-and-escort-do-while-travelling-and-does-passive-replace-defensive) | What do defensive, hold fire and escort do while travelling, and does passive replace defensive? | UC-004, UC-009 | Decided | Owner | 2026-10-17 |

### OQ-40 What do defensive, hold fire and escort do while travelling, and does passive replace defensive?

**Options / notes:** Aggressive stops to fight and passive keeps course (owner, 2026-10-17). Defensive (chase within a leash of its anchor) overlaps with passive once the anchor is the unit's path. Options: drop defensive; keep it as 'stop, fight within the leash, then resume'; hold fire keeps course without firing; escort follows its charge's choice.

**Decision:** Four stances. Aggressive seeks out and engages any enemy on the tile (stops to fight when travelling). Defensive engages and may chase a little out of range but always returns and never pursues indefinitely (resumes its course when travelling). Passive opens fire only within weapon range and keeps its course. Hold fire does nothing on its own and keeps its course. Escort is not a stance (UC-004).
