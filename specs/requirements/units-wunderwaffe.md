# Units & Wunderwaffe

P4 · Units, up to mega 'Wunderwaffe' units, are built block by block.

Where it stands (2026-10-10): Units are assembled in place: lay frame, place a unit core and thrusters, press G to lift off; mass and thrust come from the blocks. There's no designer editor, design library, unit factory or Wunderwaffe content yet. Whether in-world assembly replaces the editor is the main open design conflict (OQ-16).

## Requirements

| ID | Feature | Priority | Status | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|
| [UD-001](#ud-001-record-unit-as-blueprint) | Record unit as blueprint | Must | Proposed | MVP | Functional | P4 |  | Updated |  |
| [UD-002](#ud-002-derived-unit-stats) | Derived unit stats | Must | In Progress | MVP | Functional | P4 | UD-001 |  |  |
| [UD-003](#ud-003-design-validation) | Design validation | Must | In Progress | MVP | Functional | P4 | UD-002 | Updated |  |
| [UD-004](#ud-004-locomotion-types) | Locomotion types | Must | Proposed | MVP | Functional | P4 | UD-001 | Updated |  |
| [UD-005](#ud-005-design-library) | Design library | Must | Rejected | MVP | Functional | P4 | UD-003 |  |  |
| [UD-006](#ud-006-unit-factory) | Unit factory | Must | Proposed | MVP | Functional | P4 | FB-009, FB-016, UD-010 | Updated |  |
| [UD-007](#ud-007-block-damage-model) | Block damage model | Must | Proposed | MVP | Functional | P4 | UD-002, CB-001 |  |  |
| [UD-008](#ud-008-test-range--simulator) | Test range / simulator | Could | Proposed | Beta | Functional | P4 | UD-003 |  |  |
| [UD-009](#ud-009-design-sharing) | Design sharing | Could | Proposed | Beta | Functional | P4 | UD-005 |  |  |
| [WW-001](#ww-001-mega-unit-class) | Mega unit class | Must | Proposed | MVP | Functional | P4 | UD-001 | Updated |  |
| [WW-002](#ww-002-megafactory-requirement) | Megafactory requirement | Must | Proposed | MVP | Functional | P2 | UD-006, FB-016 | Updated |  |
| [WW-003](#ww-003-multi-tile-supply-chain) | Multi-tile supply chain | Should | Proposed | Alpha | Functional | P3 | WW-002, IO-002, RS-003 |  |  |
| [WW-004](#ww-004-staged-assembly) | Staged assembly | Should | Proposed | Alpha | Functional | P4 | WW-002 |  |  |
| [WW-005](#ww-005-strategic-impact) | Strategic impact | Must | Proposed | MVP | Functional | P8 | WW-001, CB-001 |  |  |
| [WW-006](#ww-006-visibility--alerts) | Visibility & alerts | Could | Rejected | Beta | Functional | P8 | WW-002, WM-008 |  |  |
| [UD-010](#ud-010-in-world-unit-assembly) | In-world unit assembly | Must | Done | MVP | Functional | P4 | FB-002 | New |  |
| [UD-011](#ud-011-landing) | Landing | Should | Done | MVP | Functional | P4 | UD-010 | Review |  |
| [UD-012](#ud-012-unit-physics) | Unit physics | Must | Done | MVP | Functional | P4 | UD-002 | New |  |
| [UD-013](#ud-013-upkeep) | Upkeep | Should | Proposed | Alpha | Functional | P4 | UD-002, FB-018 | New |  |

### UD-001 Record unit as blueprint

**Requirement:** Units shall be designed by building them in place on a tile (UD-010); at the unit core the player can record the design as a blueprint in the blueprint library (FB-009). There is no separate editor.

**Acceptance criteria:** Recording at a unit core saves every block on its frame with rotation; loading the blueprint as a ghost and building it gives the same unit. To change a design, rebuild and re-record it (e.g. on a debug map).

**As built:** No designer editor. Units are assembled in place on a tile: lay frame, place ordinary buildings (unit core, thrusters) on it, then lift off (UD-010).

**Notes:** Was: 'Design units in a 2D top-down editor by placing blocks on a grid'. Updated 2026-10-05: decision OQ-8; Decide whether a block editor still comes (e.g. as blueprints for in-world assembly) or in-world assembly replaces it; see OQ-16. 2D top-down decided (OQ-8). OQ-16 decided 2026-10-10.

### UD-002 Derived unit stats

**Requirement:** Unit stats (HP, speed, mass, power draw, DPS, cost, build time) shall be computed from its blocks.

**Acceptance criteria:** Changing a block updates stats instantly; formulas are documented in design doc.

**As built:** Mass (frame 2 per cell + each building's cost), moment of inertia, thrust, top speed and turn rate are derived from the blocks. No HP, DPS, power draw, cost or build time.

**Notes:** Units power themselves with onboard batteries or generators, or draw power, fuel and ammo from a friendly resource radius (FB-018), decided 2026-10-10. HP is per block (UD-007); upkeep is summed from blocks (UD-013).

### UD-003 Design validation

**Requirement:** Lift-off shall validate a design: connectivity, a power source for its engines (onboard batteries or generators; supply from a resource radius, FB-018, also counts in the field), a locomotion class, and size within the limit (the factory's block cap when a factory builds it).

**Acceptance criteria:** Invalid designs cannot be saved for production; errors list the offending blocks.

**As built:** Lift-off checks: frame connected to the unit core, at most 64 cells, buildings wholly on frame, one unit core, no tile core, no ports. A unit without thrusters can lift off but can't move. Hovering the core shows the result.

**Notes:** Was: 'The editor shall validate designs: connectivity, power balance, movement capability, size limits'. Validation runs at lift-off, not in an editor. No editor (OQ-16).

### UD-004 Locomotion types

**Requirement:** Each unit shall have a locomotion class (ground, naval or air), decided by its blocks, which sets the terrain it can cross.

**Acceptance criteria:** Ground: land, shallow water and rivers, not deep water or peaks. Naval: deep and shallow water and rivers. Air: anywhere. Pathing, border crossing and abstract routes obey it.

**As built:** One locomotion: thruster-driven and hover-like (ignores terrain, collides with buildings). The unused 'unitPassable' terrain field was removed in the 2026-10-10 cleanup.

**Notes:** Was: 'Wheeled/tracked, legged, hover, naval, air (Should, Alpha)'. 2026-10-10: terrain is what defends borders (OQ-17), so this is now a Must. Terrain split is a proposal; confirm.

### UD-005 Design library

**Requirement:** Players shall save, name, version, duplicate and delete unit designs.

**Acceptance criteria:** Saved designs persist across sessions and are selectable in factories.

**As built:** Not built.

**Notes:** Depends on the UD-001 decision (OQ-16). Merged into FB-009 (one library for buildings and units), 2026-10-10.

### UD-006 Unit factory

**Requirement:** A factory shall project a blueprint from the library within its radius and, fed resources by belts, build its blocks into the world one by one; when the last block is placed it triggers the unit core so the blocks lift off as a unit. Its radius and block cap come from its boosters (FB-016).

**Acceptance criteria:** The factory refuses blueprints over its block cap or outside its radius; it shows required inputs, buffer and progress; each block appears as it is paid for; completion lifts the unit off automatically. Building blueprints are allowed but the result stays in place and blocks the factory until removed.

**As built:** No unit factory: the player places each block as a building, paying its cost, then lifts the group off.

**Notes:** Was: 'A unit factory builds a selected design by consuming the summed block costs delivered via logistics'. Built production is in-world assembly, not a factory consuming summed block costs; see OQ-16. OQ-9 and OQ-16 decided 2026-10-10. Proposed boosters: OQ-25.

### UD-007 Block damage model

**Requirement:** Units shall take damage per block; destroying key blocks disables the corresponding function.

**Acceptance criteria:** Destroying all weapon blocks leaves unit unable to fire; destroying engine reduces speed.

**As built:** Not built.

**Notes:** Raised to Must 2026-10-10 (OQ-27): combat is per-block damage from the start.

### UD-008 Test range / simulator

**Requirement:** The player shall test a design against dummy targets before committing to production.

**Acceptance criteria:** Simulator spawns the design at no cost in a sandbox and reports DPS/survival.

**As built:** Not built.

**Notes:** Designs are edited by re-recording on a debug map (OQ-16); one sandbox map could serve both.

### UD-009 Design sharing

**Requirement:** Players may export/import designs as text codes or files.

**Acceptance criteria:** Exported code reimports to an identical design.

**As built:** Not built.

**Notes:** The library is already shared across games (FB-009); this covers sharing between players.

### WW-001 Mega unit class

**Requirement:** A Wunderwaffe is an ordinary unit that is particularly large and expensive: same blocks and rules, but it needs a factory with a large block cap (WW-002).

**Acceptance criteria:** No separate code path; units over a set block count count as Wunderwaffe class for UI and alerts.

**As built:** Not built.

**Notes:** Was: 'Wunderwaffe-class mega units with much larger size limits and block catalogue'. OQ-10 decided 2026-10-10.

### WW-002 Megafactory requirement

**Requirement:** A megafactory is a factory (UD-006) with enough boosters for a large block cap; Wunderwaffe-size units need one.

**Acceptance criteria:** A factory's boosted block cap decides which blueprints it can build; there is no separate megafactory building.

**As built:** Not built.

**Notes:** Was: 'Wunderwaffe only buildable in a megafactory: a large, multi-stage assembly structure'. OQ-9 decided 2026-10-10.

### WW-003 Multi-tile supply chain

**Requirement:** Wunderwaffe components shall require resources that realistically need several specialised tiles feeding the megafactory.

**Acceptance criteria:** At least one component per mega unit requires exotic inputs from a different biome.

**As built:** Not built.

### WW-004 Staged assembly

**Requirement:** Mega unit assembly shall be split into visible stages (frame, systems, armament) that can each be stalled by supply shortage.

**Acceptance criteria:** Megafactory shows per-stage progress and missing inputs.

**As built:** Not built.

**Notes:** Unit frame (UD-010) could become the 'frame' stage. Factories already build block by block (UD-006), which may cover staging.

### WW-005 Strategic impact

**Requirement:** A completed Wunderwaffe shall be capable of tipping a tile battle on its own, balanced by cost, upkeep and counters.

**Acceptance criteria:** Balance tests: 1 mega unit beats N standard units of equal cost only in defined matchups.

**As built:** Not built.

### WW-006 Visibility & alerts

**Requirement:** Construction and deployment of a Wunderwaffe shall be detectable/announced to opponents (e.g. intel when scouted).

**Acceptance criteria:** Scouted megafactories show construction stage to enemies.

**As built:** Not built.

**Notes:** Rejected 2026-10-10 (OQ-29): everyone sees everything, so there's nothing to scout. Announcing a finished Wunderwaffe could become an alert (UI-003).

### UD-010 In-world unit assembly

**Requirement:** The player shall build a unit on a tile by laying frame (a floor that can sit under buildings), placing blocks on it and triggering a unit core, which lifts the framed group off as one unit.

**Acceptance criteria:** Frame costs 2 iron ore a cell, refunded on removal; G over a unit core lifts off up to 64 connected cells; items held by lifted buildings return to the tile inventory.

**As built:** UnitAssembly.cs. Unit core and thrusters unlock with the Propulsion tech.

**Notes:** Added from what was built.

### UD-011 Landing

**Requirement:** A unit shall be able to land, putting its blocks and frame back on the tile as buildings.

**Acceptance criteria:** Lands turned to the nearest quarter turn, only on free buildable cells of a tile its owner holds (G with units selected).

**As built:** UnitAssembly.Deploy.

**Notes:** Added from what was built. WM-015 (2026-10-10) lets players build on any tile where they have a building or unit; landing should follow the same rule.

### UD-012 Unit physics

**Requirement:** Units shall move as rigid bodies driven by their thrusters: heavier units are slower, and turning depends on engine thrust and moment of inertia.

**Acceptance criteria:** Ordered to a point, a unit turns its main engine, accelerates, brakes and stops within 1.5 cells; drag brings it to rest; integer and deterministic.

**As built:** UnitPhysics.cs, Trig.cs (integer CORDIC).

**Notes:** Added from what was built.

### UD-013 Upkeep

**Requirement:** A unit's upkeep shall be the sum of what its blocks declare.

**Acceptance criteria:** Each block's upkeep (items per minute) is data in buildings.json; a unit pays it from onboard storage or a resource radius (FB-018); what happens when upkeep isn't met is defined with it.

**As built:** Not built.

**Notes:** OQ-31 decided 2026-10-10.

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-8](#oq-8-is-the-designer-2d-top-down-or-3d-voxel) | Is the designer 2D (top-down) or 3D (voxel)? | UD-001 | Decided | Owner | 2026-10-05 |
| [OQ-9](#oq-9-size-limit-per-factory-tier-vs-a-single-global-limit) | Size limit per factory tier vs. a single global limit? | UD-003, WW-002 | Decided | Owner | 2026-10-10 |
| [OQ-10](#oq-10-can-a-wunderwaffe-move-between-tiles-or-is-it-tile-bound) | Can a Wunderwaffe move between tiles, or is it tile-bound? | WW-001, IO-003 | Decided | Owner | 2026-10-10 |
| [OQ-16](#oq-16-are-units-designed-in-an-editor-and-produced-by-factories-ud-001005006-or-assembled-in-place-on-frame-and-lifted-off-as-built) | Are units designed in an editor and produced by factories (UD-001/005/006), or assembled in place on frame and lifted off (as built)? | UD-001, UD-005, UD-006, UD-010 | Decided | Owner | 2026-10-10 |
| [OQ-27](#oq-27-does-mvp-combat-use-one-hp-pool-per-unit-or-per-block-damage-from-the-start) | Does MVP combat use one HP pool per unit, or per-block damage from the start? | CB-001, UD-007, UD-002 | Decided | Owner | 2026-10-10 |

### OQ-8 Is the designer 2D (top-down) or 3D (voxel)?

**Options / notes:** Heavily affects art, physics and pathing scope.

**Decision:** 2D top-down (whole game is 2D).

### OQ-9 Size limit per factory tier vs. a single global limit?

**Options / notes:** Tiered factories create clear progression to megafactory.

**Decision:** Neither: factories are modular. A factory's radius and block cap come from adjacent boosters (FB-016); it builds a library blueprint block by block from belt-fed resources and lifts it off when done (UD-006). A megafactory is a factory with enough boosters.

### OQ-10 Can a Wunderwaffe move between tiles, or is it tile-bound?

**Options / notes:** Moving mega units need large gates and long-range transit.; Today every unit, of any size, can cross any border (IO-003 conflict).

**Decision:** A Wunderwaffe is just a large, expensive unit, so all units can cross all borders.

### OQ-16 Are units designed in an editor and produced by factories (UD-001/005/006), or assembled in place on frame and lifted off (as built)?

**Options / notes:** Options: keep in-world assembly only; add an editor that saves blueprints for in-world assembly; or build the editor + factory as written and drop frames.

**Decision:** Build units in place on the tile map; the unit core can record the design to a blueprint library (shared by buildings and units, shared across games, manage-only) that loads as a ghost guide. Modelled on Mindustry schematics (FB-009, UD-001).

### OQ-27 Does MVP combat use one HP pool per unit, or per-block damage from the start?

**Options / notes:** Per-block (UD-007, Should) fits block design but costs more; a pool is quick and can become per-block later.

**Decision:** Per-block damage from the start (UD-007 raised to Must).
