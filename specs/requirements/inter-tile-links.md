# Inter-Tile Links

P3 · Tiles trade resources and units with their neighbours.

Where it stands (2026-10-10): Export and import ports in paired I/O zones move items between adjacent owned tiles with a 3 s transit, backing belts up instead of losing items. Units don't use ports. No long-range links or interdiction.

## Requirements

| ID | Feature | Priority | Status | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|
| [IO-001](#io-001-io-zones-and-ports) | I/O zones and ports | Must | Done | MVP | Functional | P3 | WM-003, FB-004 | Updated |  |
| [IO-002](#io-002-item-transfer-between-tiles) | Item transfer between tiles | Must | In Progress | MVP | Functional | P3 | IO-001 |  |  |
| [IO-003](#io-003-unit-transfer-between-tiles) | Unit transfer between tiles | Must | In Progress | MVP | Functional | P3 | IO-001, UC-001 | Updated |  |
| [IO-004](#io-004-long-range-logistics) | Long-range logistics | Should | Proposed | Alpha | Functional | P3 | IO-002, TT-001 |  |  |
| [IO-005](#io-005-logistics-network-view) | Logistics network view | Should | In Progress | Alpha | Functional | P3 | IO-002 |  |  |
| [IO-006](#io-006-interdiction) | Interdiction | Should | Proposed | Alpha | Functional | P3 | IO-002, CB-001 |  |  |

### IO-001 I/O zones and ports

**Requirement:** Each tile shall have one I/O zone per neighbouring tile, along their shared border, in which export and import ports can be placed.

**Acceptance criteria:** A port must stand wholly inside a zone; the zone, not the port's rotation, decides the linked tile; tile A's zone for B is paired with B's zone for A.

**As built:** Zones are the cells within 4 steps of the shared border (PortRules.ZoneDepth); zones never overlap. While a port is picked, the tile view shades each zone and labels the tile it links to.

**Notes:** Was: 'I/O ports on its edges ... placing an export port on edge X and an import port on the neighbour's matching edge creates a link'. Units don't use ports (see IO-003). Code issue: goals.json's 'link' step still tells players to use 'the same lane', which no longer applies.

### IO-002 Item transfer between tiles

**Requirement:** Items entering an export port shall appear at the linked import port after a transit delay based on distance/link type.

**Acceptance criteria:** Throughput and latency of each link are shown; items are never duplicated or lost.

**As built:** Fixed 3 s transit (180 ticks per tile of distance; only adjacent links exist). Items are never lost or duplicated (tested); an export refuses items when the link can't deliver, so belts back up. Throughput is set by the feeding belts. Latency and throughput aren't shown as numbers.

### IO-003 Unit transfer between tiles

**Requirement:** Units shall cross tile borders anywhere their locomotion allows; borders are defended by terrain, not gates.

**Acceptance criteria:** A unit ordered to another tile crosses at any border cell its locomotion can pass (UD-004); hard terrain on a border (mountains, seas) blocks units that can't cross it.

**As built:** Units cross any border anywhere: positions are world coordinates and a unit's tile is the one under its centre. There are no gates.

**Notes:** Was: 'Units shall move between tiles via edge ports/gates, appearing on the destination tile's local map'. Decide between gates (as written) and free border crossing (as built); see OQ-17. OQ-17 decided 2026-10-10. Crossing anywhere is built; terrain limits wait on UD-004.

### IO-004 Long-range logistics

**Requirement:** Higher tech shall unlock long-range links (rail, mass driver, teleporter) between non-adjacent owned tiles.

**Acceptance criteria:** Long-range link unlocks only after the relevant tech resource is acquired.

**As built:** Not built.

### IO-005 Logistics network view

**Requirement:** The world map shall display all inter-tile links with flow rates.

**Acceptance criteria:** Toggling the logistics overlay shows arrows with items/min per link.

**As built:** World map draws an arrow per linked zone pair with its port count and a dot for each item in transit. No items/min figure or toggle.

### IO-006 Interdiction

**Requirement:** Enemy units shall be able to disrupt inter-tile links (e.g. by attacking ports or routes).

**Acceptance criteria:** Destroying a port halts its link; link shows 'Disrupted' status.

**As built:** Not built.

**Notes:** No gates (OQ-17), so the targets are ports and routes.

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-17](#oq-17-should-units-pass-between-tiles-only-through-gates-or-cross-borders-anywhere-as-built) | Should units pass between tiles only through gates, or cross borders anywhere (as built)? | IO-001, IO-003, UC-005 | Decided | Owner | 2026-10-10 |

### OQ-17 Should units pass between tiles only through gates, or cross borders anywhere (as built)?

**Options / notes:** Gates make borders defensible and give interdiction (IO-006) a target; free crossing is simpler and already built.

**Decision:** Cross borders anywhere. Borders are defended by hard terrain on them (mountains, seas) that some units can't cross.
