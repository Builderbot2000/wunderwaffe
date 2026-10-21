# Content Design

P7 · Every item, building and block has a distinct purpose and fits the rest of the content. These principles apply to all content: items, recipes, buildings, unit blocks, prefabs, factions and maps.

Where it stands (2026-10-21): Principles agreed with the owner. The shipped content (eight items) predates them and hasn't been audited against them, except for names (CD-009). The content and balance update (specs/content) applies them first.

## Requirements

| ID | Feature | Priority | Status | Built | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|---|
| [CD-001](#cd-001-tiers-add-verbs) | Tiers add verbs | Must | Approved | No | Alpha | Non-Functional | P7 | RS-001 | New |  |
| [CD-002](#cd-002-one-role-per-item) | One role per item | Must | Approved | No | Alpha | Non-Functional | P7 | RS-002 | New |  |
| [CD-003](#cd-003-hubs-and-consumables) | Hubs and consumables | Must | Approved | No | Alpha | Non-Functional | P7 | RS-002 | New |  |
| [CD-004](#cd-004-lower-tiers-stay-relevant) | Lower tiers stay relevant | Must | Approved | No | Alpha | Non-Functional | P7 | RS-001 | New |  |
| [CD-005](#cd-005-material-identity-in-blocks) | Material identity in blocks | Must | Approved | No | Alpha | Non-Functional | P4 | UD-007, CB-002 | New |  |
| [CD-006](#cd-006-physical-state-is-logistics) | Physical state is logistics | Must | Approved | No | Alpha | Non-Functional | P2 | FB-015 | New |  |
| [CD-007](#cd-007-placement-is-map-design) | Placement is map design | Must | Approved | No | Alpha | Non-Functional | P1 | RS-003, WM-017 | New |  |
| [CD-008](#cd-008-content-count-follows-purpose) | Content count follows purpose | Must | Approved | No | Alpha | Non-Functional | P7 | NF-003 | New |  |
| [CD-009](#cd-009-names-say-what-things-are) | Names say what things are | Must | Approved | No | Alpha | Non-Functional | P7 |  | New |  |

### CD-001 Tiers add verbs

**Requirement:** Each resource tier shall let the player do something new, not only make bigger numbers: tier 1 build (extract and smelt), tier 2 reach (units fly, ports link tiles, turrets hold ground), tier 3 wage war (factories make armies, and armies beat defences, NR-006), tier 4 decide (the Wunderwaffe ends a stalemate).

**Acceptance criteria:** For each tier, the content design names the verb and the buildings or blocks that first need that tier's items to do it. No tier is only a stronger copy of the one below.

**As built:** Not audited.

**Notes:** Owner, 2026-10-21.

### CD-002 One role per item

**Requirement:** Every item shall have one physical job (bear load, conduct, burn, explode, cool...) that no other item of its tier does.

**Acceptance criteria:** The resource catalogue gives each item a role. Within a tier, no two items share a role: two that do are merged, or their roles are split. A player can tell an item's role from its name and icon.

**As built:** Not audited. Known overlaps in the draft: two kinetic ammo items in reach of each other, two burnt power fuels (OQ-60).

**Notes:** Owner, 2026-10-21.

### CD-003 Hubs and consumables

**Requirement:** Every item shall be either a hub, with two or more consumers in different chains, or a consumable that play uses up (ammo, fuel). An intermediate with a single consumer is just a slower recipe: it gets a second consumer or is cut.

**Acceptance criteria:** Counting recipe inputs and building and block costs, every item that isn't a consumable has at least two consumers in different chains. Content tooling (tools/balance.py) lists the ones that don't.

**As built:** Not audited.

**Notes:** Owner, 2026-10-21. Raw resources count too: a raw resource with one consumer is a hub candidate that failed.

### CD-004 Lower tiers stay relevant

**Requirement:** Higher tiers shall build on lower ones rather than make them obsolete. Lower-tier items stay in demand to the end of a match.

**Acceptance criteria:** Every raw resource feeds at least two tiers. Tier 1 and 2 items are in the costs or recipes of tier 3 and 4 content in bulk, not only through one intermediate.

**As built:** Not audited.

**Notes:** Owner, 2026-10-21.

### CD-005 Material identity in blocks

**Requirement:** A material's identity shall live in the physical properties of the blocks made from it (HP, resistances, mass), because combat is a full simulation in which a shot hits a block and the block's properties decide the outcome. There are no unit-level stats that combat reads.

**Acceptance criteria:** The content design has a material table: for each structural material, the HP, resistance and mass profile of the blocks made mainly of it. Blocks follow their main material's profile, and content tooling flags those that stray. Unit stats (UD-002) are read-outs of the blocks for players and the AI, never inputs to the simulation.

**As built:** Not audited. A block's mass today comes from how many items it costs, whatever they are (UnitAssembly), so a heavy block is an expensive one (OQ-59).

**Notes:** Owner, 2026-10-21. The material chosen should shape the unit through the simulation, without rules: a cheap but very heavy material ends up in defences because it is too heavy to fly.

### CD-006 Physical state is logistics

**Requirement:** Solid and fluid resources shall differ only in their physical state and the infrastructure that moves and stores them: belts and cargo for solids, pipes, tanks and fluid ports for fluids. In every other way a fluid is an item like any other.

**Acceptance criteria:** Fluids can be produced, consumed, stored, shipped between tiles, carried by units and paid in costs, each through infrastructure built for fluids. No rule ties fluids to a place or bars them from anything solids can do.

**As built:** No fluids yet (FB-015).

**Notes:** Owner, 2026-10-21. How units carry fluids is OQ-61.

### CD-007 Placement is map design

**Requirement:** Where resources appear shall be set by the map (generation settings or a scenario), not by the items. An item's tier carries no geography.

**Acceptance criteria:** Item content has no placement rules: regional chances and distance rules belong to map generation settings and scenarios. Vanilla maps cluster higher-tier raw resources away from the player spawns, so climbing tiers means expanding.

**As built:** Placement is per item today (items.json "regional", Balance.RegionalChancePercent).

**Notes:** Owner, 2026-10-21. Answers RS-003's review: scarcity is a map decision.

### CD-008 Content count follows purpose

**Requirement:** There shall be no fixed budget of items, buildings or blocks. Content is added or cut as purpose and balance need.

**Acceptance criteria:** Content counts in the design are reported, not targets. An addition is justified by a role (CD-002) and its consumers (CD-003), and a cut by a role that is empty or duplicated.

**As built:** Not applicable.

**Notes:** Owner, 2026-10-21. Replaces the content draft's 'medium scale' targets (about 25 items, 65 buildings).

### CD-009 Names say what things are

**Requirement:** Every item, building and block name shall tell the player what the thing is without the lore. Names come from real engineering or from serious science fiction (fusion reactors, railguns, lasers, superconductors, shield generators). There are no buzzwords (flux, quantum, aegis) and no made-up terms (an invented mineral or element).

**Acceptance criteria:** Each name in the content passes two questions: would a player know what this is from the name alone, and does the idea come from real engineering or serious science fiction? A physics stretch is allowed when the name is real technology. Lore proper nouns (the Concord, the Keepers, faction names) name people and places, not content, and are exempt.

**As built:** The shipped names pass. The content draft's failures were renamed (OQ-62).

**Notes:** Owner, 2026-10-21. Realistic chemistry is welcome but not the bar: the Ion Thruster stays, although ion thrust couldn't lift a unit in an atmosphere.

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-59](#oq-59-should-items-carry-a-mass-so-a-blocks-mass-comes-from-its-materials) | Should items carry a mass, so a block's mass comes from its materials? | CD-005, UD-002, UD-007 | Open |  |  |
| [OQ-60](#oq-60-which-item-roles-does-the-content-need) | Which item roles does the content need? | CD-002, CD-003, CD-004, RS-001 | Open |  |  |
| [OQ-61](#oq-61-how-do-units-carry-fluids) | How do units carry fluids? | CD-006, FB-015, UD-010 | Open |  |  |
| [OQ-62](#oq-62-which-draft-names-fail-cd-009-and-what-replaces-them) | Which draft names fail CD-009, and what replaces them? | CD-009, CD-002 | Decided | Owner | 2026-10-21 |
| [OQ-63](#oq-63-does-a-raw-resource-with-one-refined-product-pass-cd-003) | Does a raw resource with one refined product pass CD-003? | CD-003 | Open |  |  |

### OQ-59 Should items carry a mass, so a block's mass comes from its materials?

**Options / notes:** Today a block's mass is the number of items in its cost spread over its cells, plus the frame. Option: a `mass` per item in items.json, with a block's mass the sum of its cost items times their mass, so a light material makes a light block at the same cost. Thrust, inertia and collisions then reward light materials through the simulation, which is what CD-005 asks. Leaning: per-item mass.

**Decision:**

### OQ-60 Which item roles does the content need?

**Options / notes:** Proposed role table (specs/content/catalogue.md, second round, with the OQ-62 names): bulk structure (iron plate, steel), static mass (concrete: cheap HP, too heavy to fly), light structure (titanium plate, composite), conduction (copper plate), optics (glass: circuits and lenses), logic (circuit), superconduction (superconductor: fusion magnets, railguns, maglev), seals and insulation (polymer: battery cells, fast belts, tanks, cargo pods), carbon (coke), combustion fuel (fuel), fusion fuel (deuterium), heat removal, energy storage (battery cell), salvage (relic components), and one ammo per damage type: shell (kinetic), missile (explosive), and power for energy weapons (lasers, particle beam), so no energy ammo item. Rail slugs merge into shells, with the railgun using power for velocity.

Chemistry options that also serve the principles (review 2026-10-21, leaning yes): (1) the coking oven makes **coke**, not graphite; graphite only comes back if it gets a role of its own (CD-008). (2) **Titanium plate takes brine** (the real Kroll route chlorinates the ore with chlorine from brine), which makes brine a hub (CD-003). (3) **Cut coolant**: power-hungry buildings (lasers, fusion, superconductor works) consume water as evaporative cooling, which makes water a hub and removes an item with one consumer. (4) **Composite = steel + silicon carbide**, a real armour ceramic made from sand + coke, which makes sand a hub and gives CD-005 a hard, brittle material. (5) **Fusion burns deuterium + lithium** (D-T fusion breeds tritium from lithium); deuterium can then come from any water at a high power cost, and lithium (from brine) stays the regional gate.

**Decision:**

### OQ-61 How do units carry fluids?

**Options / notes:** Under CD-006 units may carry fluids, through infrastructure for fluids. Option: a tank block holding one fluid at a time, next to cargo for solids; lift-off and landing move fluids between the core and the unit's tanks as they do cargo (UD-010). Leaning: a tank block.

**Decision:**

### OQ-62 Which draft names fail CD-009, and what replaces them?

**Options / notes:** Review of the content draft, 2026-10-21. Pass: Fusion Reactor, Deuterium, Railgun, Laser Turret, Missile, Maglev Conveyor, Capacitor Bank, Solar Array, Electrolyser, Arc Furnace, Battery Cell, Unit Factory, Shield Generator, Ion Thruster (a stretch in an atmosphere, kept). Fail: Xenite (made up), Xenite Lattice, Lattice Forge, Flux Core, Flux Accelerator, Quantum Foundry (buzzwords), Relic Element (reads as a new chemical element), Aegis Projector (a brand name for a bigger shield), Plasma Cell and Plasma Lance (a space-opera trope; the cell needs explaining), Plasteel (a vague trope word).

**Decision:** Owner, 2026-10-21. Xenite → Rare-earth Ore; Xenite Lattice → Superconductor; Lattice Forge → Superconductor Works; Flux Core → Reactor Core (the compact fusion core the Corps can't build without salvage); Quantum Foundry → Precision Works; Relic Element → Relic Components (salvaged Concord parts); Aegis Projector → Large Shield Generator; Plasma Lance → Particle Beam, running on power, and the Plasma Cell is dropped; Plasteel → Composite. The Flux Accelerator gets a new name when it is next reworked, under CD-009.

### OQ-63 Does a raw resource with one refined product pass CD-003?

**Options / notes:** CD-003's note fails a raw resource with one consumer, which fails iron, copper and titanium ore and crude oil: realistically an ore has one job, being refined. Option: a raw resource whose single refined product is a hub passes; sand, rare-earth ore and relic components still have to find a second consumer. Leaning: yes, amend the note.

**Decision:**
