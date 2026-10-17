# Combat & Protection

P8 / P6 · Real-time combat for tiles; No Rush comes from defence/offence balance, not a mechanic.

Where it stands (2026-10-17): Real-time combat with turrets, unit weapons, damage types and resistances, ammo supply, shields, repair, wrecks and circle captures. No Rush relies on defence balance (NR-006), which has no balance test yet.

## Requirements

| ID | Feature | Priority | Status | Built | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|---|
| [CB-001](#cb-001-real-time-combat) | Real-time combat | Must | Done | Yes | MVP | Functional | P8 | FB-001 |  |  |
| [CB-002](#cb-002-damage-types--armour) | Damage types & armour | Should | Done | Yes | Alpha | Functional | P8 | CB-001 |  |  |
| [CB-003](#cb-003-tile-capture) | Tile capture | Must | Done | Yes | MVP | Functional | P8 | CB-001, WM-006 | Updated |  |
| [CB-004](#cb-004-ammunition-supply) | Ammunition supply | Should | Done | Yes | Alpha | Functional | P8 | FB-010 | Updated |  |
| [CB-005](#cb-005-repair) | Repair | Should | Done | Yes | Alpha | Functional | P8 | CB-001 |  |  |
| [NR-001](#nr-001-protection-period) | Protection period | Must | Rejected | No | MVP | Functional | P6 | CB-003 |  |  |
| [NR-002](#nr-002-buffed-starting-defences) | Buffed starting defences | Must | Rejected | No | MVP | Functional | P6 | FB-010 |  |  |
| [NR-003](#nr-003-buff-decay) | Buff decay | Must | Rejected | No | MVP | Functional | P6 | NR-002 |  |  |
| [NR-004](#nr-004-protection-forfeit-on-aggression) | Protection forfeit on aggression | Should | Rejected | No | Alpha | Functional | P6 | NR-001 |  |  |
| [NR-005](#nr-005-expansion-tile-protection) | Expansion tile protection | Could | Rejected | No | Beta | Functional | P6 | WM-006 |  |  |
| [NR-006](#nr-006-defenceoffence-balance) | Defence/offence balance | Must | Proposed | No | MVP | Non-Functional | P6 | FB-010, CB-001 | New |  |
| [CB-006](#cb-006-wrecks) | Wrecks | Must | Done | Yes | MVP | Functional | P8 | CB-001 | New |  |

### CB-001 Real-time combat

**Requirement:** Combat shall resolve in real time on the tile local map between units and structures.

**Acceptance criteria:** Projectiles/beams resolve hits against block hitboxes; destroyed entities removed.

**As built:** World/Combat.cs (tick phase 4): turrets and unit weapons fire projectiles and beams that hit per-block hitboxes; destroyed blocks and buildings are removed and leave wrecks (tested, incl. a ~280-unit firefight within budget).

**Notes:** Per-block damage (OQ-27); full visibility, no fog of war (OQ-29); destroyed blocks leave wrecks (CB-006).

### CB-002 Damage types & armour

**Requirement:** Weapons shall have damage types and armour blocks shall have resistances.

**Acceptance criteria:** Damage table data-driven; damage dealt = base × resistance modifier.

**As built:** Weapons have a damage type; blocks have per-type resistances in buildings.json; damage = base x (100 - resist) / 100 (BuildingDef.DamageTaken).

### CB-003 Tile capture

**Requirement:** Enemy tiles shall be captured by the victory-circle rule (WM-006): destroy every enemy building in the circle, then hold a building there for the capture time.

**Acceptance criteria:** Only ownership changes; buildings elsewhere on the tile keep their owner; the world map redraws the control border.

**As built:** Capture is the circle claim (World/Expansion.cs): rivals' buildings in the circle are destroyed, the rest keep their owner, and the world map redraws the owner.

**Notes:** Was: 'Captured when its core/HQ is destroyed or taken while the attacker holds presence'. Tiles have no Contested state yet (WM-004, OQ-20). OQ-3 decided 2026-10-10.

### CB-004 Ammunition supply

**Requirement:** Weapons and turrets shall consume ammo items: turrets from logistics, units from onboard storage or a friendly resource radius (FB-018).

**Acceptance criteria:** A turret or unit without ammo stops firing and shows a warning; a unit back inside a resource radius refills.

**As built:** Gun turrets and unit guns need shells (munitions works, belts, ammo racks); without ammo they stop firing and show '!'; units refill inside a friendly resource radius (FB-018).

**Notes:** Was: 'Weapons and turrets shall consume ammo items supplied by logistics'. OQ-30 decided 2026-10-10.

### CB-005 Repair

**Requirement:** Damaged buildings and units shall be repairable by repair structures or units using resources.

**Acceptance criteria:** Repair restores HP and rebuilds destroyed blocks at defined cost.

**As built:** Repair bay heals and rebuilds destroyed blocks in range for a cost (tested).

### NR-001 Protection period

**Requirement:** Each player shall have a protection period at match start during which their tiles cannot be captured.

**Acceptance criteria:** Duration configurable in match settings; timer visible to all players.

**As built:** Not built.

**Notes:** Rejected 2026-10-10 (OQ-4/5): no protection mechanic; No Rush is a balance outcome (NR-006).

### NR-002 Buffed starting defences

**Requirement:** Starting tiles shall be equipped with heavily buffed defences (e.g. HQ turrets, shield) able to repel early attacks.

**Acceptance criteria:** Balance test: strongest unit army buildable in first N min loses to starting defences.

**As built:** Not built.

**Notes:** Needs defensive structures (FB-010). Rejected 2026-10-10 (OQ-4/5): no protection mechanic; No Rush is a balance outcome (NR-006).

### NR-003 Buff decay

**Requirement:** Protection buffs shall decay or end on a clear trigger (time, tech tier reached, or player opting out).

**Acceptance criteria:** Buff state and end condition shown in HUD; ends exactly at trigger.

**As built:** Not built.

**Notes:** Rejected 2026-10-10 (OQ-4/5): no protection mechanic; No Rush is a balance outcome (NR-006).

### NR-004 Protection forfeit on aggression

**Requirement:** A player who attacks another player's tiles shall forfeit their own remaining protection.

**Acceptance criteria:** First offensive order against a player tile ends attacker's protection with warning prompt.

**As built:** Not built.

**Notes:** Rejected 2026-10-10 (OQ-4/5): no protection mechanic; No Rush is a balance outcome (NR-006).

### NR-005 Expansion tile protection

**Requirement:** Newly claimed tiles shall receive a short, weaker protection window to prevent instant sniping.

**Acceptance criteria:** New tile gains configurable grace period shown on world map.

**As built:** Not built.

**Notes:** Rejected 2026-10-10 (OQ-4/5): no protection mechanic; No Rush is a balance outcome (NR-006).

### NR-006 Defence/offence balance

**Requirement:** Content shall be balanced so that defence is more cost-effective than offence early in a match, with the curves crossing as players reach higher tiers. This replaces a protection mechanic.

**Acceptance criteria:** Balance test: the strongest army buildable in the first N minutes loses to defences of equal cost; past a set tier an equal-cost army wins.

**As built:** Not built.

**Notes:** OQ-4 decided 2026-10-10. Takes over NR-002's balance test.

### CB-006 Wrecks

**Requirement:** Destroyed blocks and buildings shall leave dead blocks that don't block movement but do block building, and can be deconstructed for resources.

**Acceptance criteria:** A destroyed block leaves a wreck in its cells; units pass through it; placement on it is refused; deconstructing it returns a share of its cost.

**As built:** Wreck layer on TileGrid: destroyed blocks and buildings leave wrecks that block placement; units fly over them; deconstructing one returns 25% of its cost (Balance.WreckRefundPercent).

**Notes:** OQ-28 decided 2026-10-10. Confirmed: for now the player deconstructs wrecks like removing a building (salvage units, RS-005, come later for the field); items a destroyed block held are lost.

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-4](#oq-4-what-ends-the-protection-period-time-tech-tier-or-both) | What ends the protection period: time, tech tier, or both? | NR-001, NR-003 | Decided | Owner | 2026-10-10 |
| [OQ-5](#oq-5-does-protection-apply-only-to-the-start-tile-or-to-all-tiles-during-the-window) | Does protection apply only to the start tile or to all tiles during the window? | NR-001, NR-005 | Decided | Owner | 2026-10-10 |
| [OQ-20](#oq-20-when-exactly-is-a-tile-contested-and-what-cant-its-owner-do-while-it-is) | When exactly is a tile Contested, and what can't its owner do while it is? | WM-004, CB-003 | Decided | Owner | 2026-10-10 |
| [OQ-28](#oq-28-what-does-a-destroyed-building-or-unit-leave-behind) | What does a destroyed building or unit leave behind? | CB-001, RS-005 | Decided | Owner | 2026-10-10 |
| [OQ-29](#oq-29-how-do-sight-and-targeting-work-inside-a-tile) | How do sight and targeting work inside a tile? | CB-001, FB-010, WM-008 | Decided | Owner | 2026-10-10 |
| [OQ-30](#oq-30-how-are-units-resupplied-with-ammo-away-from-their-tiles) | How are units resupplied with ammo away from their tiles? | CB-004 | Decided | Owner | 2026-10-10 |
| [OQ-31](#oq-31-is-there-unit-upkeep) | Is there unit upkeep? | WW-005 | Decided | Owner | 2026-10-10 |

### OQ-4 What ends the protection period: time, tech tier, or both?

**Options / notes:** Time is predictable; tech-tier rewards fast builders but invites turtling.

**Decision:** No protection period. No Rush comes from where the cost-effectiveness curves of defence and offence cross: a content balance matter, not a mechanic (NR-006).

### OQ-5 Does protection apply only to the start tile or to all tiles during the window?

**Options / notes:** All-tile protection could enable risk-free land grabs.

**Decision:** Moot: no protection (OQ-4).

### OQ-20 When exactly is a tile Contested, and what can't its owner do while it is?

**Options / notes:** The flag was removed until combat exists; define it with CB-001/CB-003.

**Decision:** A tile is Contested while enemy units are on it. That forces it to render and full-sim, and materialises abstract units travelling through it (WM-004, FB-013, UC-009).

### OQ-28 What does a destroyed building or unit leave behind?

**Options / notes:** Items it held (lost / dropped / returned), refund, a wreck for later salvage (RS-005), frame.

**Decision:** Dead blocks (wrecks) that don't block movement but block building, and can be deconstructed for resources (CB-006).

### OQ-29 How do sight and targeting work inside a tile?

**Options / notes:** Only world fog exists (WM-008). Turret range, unit sight, local fog of war.

**Decision:** Full sight for everyone, no fog of war: everybody has satellites watching (WM-008 rejected).

### OQ-30 How are units resupplied with ammo away from their tiles?

**Options / notes:** Onboard storage only; resupply near own buildings; ammo-free weapons using power.

**Decision:** Resource radius, From the Depths style: units and stores share stock with friendlies within a radius; out of range a unit uses onboard storage (FB-018, CB-004).

### OQ-31 Is there unit upkeep?

**Options / notes:** WW-005 mentions upkeep but nothing defines it.

**Decision:** Yes, summed from the blocks inside the unit (UD-013).
