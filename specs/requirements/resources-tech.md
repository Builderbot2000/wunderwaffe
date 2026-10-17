# Resources & Tech

P7 · Progression is gated by producing higher-tier resources, not by research.

Where it stands (2026-10-17): Eight items over three tiers; coal is regional. The tech tree is gone: everything is available from the start and costs higher-tier items.

## Requirements

| ID | Feature | Priority | Status | Built | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|---|
| [RS-001](#rs-001-resource-tiers) | Resource tiers | Must | Done | Yes | MVP | Functional | P7 |  |  |  |
| [RS-002](#rs-002-recipe-graph) | Recipe graph | Must | Done | Yes | MVP | Non-Functional | P7 | RS-001 |  |  |
| [RS-003](#rs-003-regional-scarcity) | Regional scarcity | Must | Done | Yes | MVP | Functional | P1 | WM-002, RS-001 | Review |  |
| [RS-004](#rs-004-deposit-depletion) | Deposit depletion | Could | Proposed | No | Beta | Functional | P2 | FB-003 |  |  |
| [TT-001](#tt-001-resource-gated-progression) | Resource-gated progression | Must | Done | Yes | MVP | Functional | P7 | RS-001 | Updated |  |
| [TT-002](#tt-002-unlock-conditions) | Unlock conditions | Must | Rejected | No | MVP | Functional | P7 | TT-001 |  |  |
| [TT-003](#tt-003-tech-tree-viewer) | Tech tree viewer | Must | Rejected | No | MVP | Functional | P7 | TT-002 |  |  |
| [TT-004](#tt-004-unlock-scope) | Unlock scope | Must | Rejected | No | MVP | Functional | P7 | TT-002 |  |  |
| [TT-005](#tt-005-global-vs-per-tile-unlocks) | Global vs per-tile unlocks | Should | Rejected | No | Alpha | Functional | P7 | TT-001 |  |  |
| [RS-005](#rs-005-salvage) | Salvage | Could | Deferred | No | Beta | Functional | P7 | CB-001 | New |  |

### RS-001 Resource tiers

**Requirement:** Resources shall be organised into tiers (raw → refined → advanced → exotic), each tier requiring the previous tier to produce.

**Acceptance criteria:** Resource catalogue lists tier for each item; no tier N item is craftable without tier N-1 inputs.

**As built:** Tier 1-4 per item in items.json; content validation rejects a recipe whose output is more than one tier above its best input. Items: copper/iron ore, coal (1); copper/iron plate, shell, fuel (2); steel (3). Nothing is tier 4 yet.

### RS-002 Recipe graph

**Requirement:** All items shall be defined in a data-driven recipe graph (inputs, outputs, building, time).

**Acceptance criteria:** Recipes load from data files; adding a recipe requires no code change.

**As built:** recipes.json (inputs, outputs, building, ticks); empty inputs = extraction. New recipes need no code.

### RS-003 Regional scarcity

**Requirement:** Some resources shall exist only on certain biomes/tiles, forcing expansion or trade between tiles.

**Acceptance criteria:** At least one higher-tier input per tier is unavailable on the starting biome.

**As built:** Per item, not per biome: items flagged 'regional' (coal) never spawn on a starting tile, appear on 35% of other tiles, and always on at least one neighbour of each start.

**Notes:** Built scarcity is per tile, not per biome, and only coal (tier 1) is regional. Confirm the acceptance criterion or reword it.

### RS-004 Deposit depletion

**Requirement:** Deposits may be finite or have diminishing yield to encourage expansion.

**Acceptance criteria:** Configurable per deposit type; UI shows remaining amount.

**As built:** Not built: deposits are infinite.

### TT-001 Resource-gated progression

**Requirement:** Progression shall be gated only by resources: content can be built once the player can pay its cost in higher-tier items. There are no tech nodes, unlocks or research points.

**Acceptance criteria:** Every building is available from the start; higher-tier buildings cost higher-tier items; no tech tree data or UI remains.

**As built:** No tech tree: tech.json, unlocks and the T overlay are gone, and every building and recipe is available from the start (tested). The goal chain (goals.json) stays as guidance.

**Notes:** Was: 'Tech unlocks shall be triggered by acquiring/producing higher-level resources, not by spending science points'. Tech tree removed 2026-10-10 (OQ-7 answer). To do: remove tech.json, Progression unlocks and the T overlay; goals.json still uses the same condition types.

### TT-002 Unlock conditions

**Requirement:** Each tech node shall define its unlock condition (e.g. first produce X, deliver N of X to core, or hold a tile with deposit Y).

**Acceptance criteria:** Data file defines conditions; meeting condition unlocks node immediately with notification.

**As built:** Built once (tech.json conditions), removed with the tech tree (TT-001). Goal conditions reuse the same types.

**Notes:** 'Produce X' conditions must count rate-mode production too (OQ-11). Rejected 2026-10-10: no tech tree (TT-001).

### TT-003 Tech tree viewer

**Requirement:** The player shall view the tech tree showing locked/unlocked nodes, unlock conditions and what each node grants.

**Acceptance criteria:** Tree UI shows progress toward each condition (e.g. 34/100 Plasteel).

**As built:** Built once (T overlay), removed with the tech tree (TT-001).

**Notes:** Rejected 2026-10-10: no tech tree (TT-001).

### TT-004 Unlock scope

**Requirement:** Tech nodes shall unlock buildings, recipes, unit blocks, logistics link types and Wunderwaffe parts.

**Acceptance criteria:** Every buildable content item references exactly one unlocking node (or is starter content).

**As built:** Built once, removed with the tech tree (TT-001).

**Notes:** Rejected 2026-10-10: no tech tree (TT-001).

### TT-005 Global vs per-tile unlocks

**Requirement:** Unlocks shall apply to the player's whole empire once achieved on any tile.

**Acceptance criteria:** Unlocking on tile A makes content available on tile B immediately.

**As built:** Built once (empire-wide), removed with the tech tree (TT-001).

**Notes:** Decided OQ-6. Rejected 2026-10-10: no tech tree (TT-001).

### RS-005 Salvage

**Requirement:** Units equipped with special salvage components shall deconstruct destroyed units for resources.

**Acceptance criteria:** A salvage unit next to a wreck turns it into items over time.

**As built:** Not built. Wrecks can be deconstructed for a share of their cost (CB-006), without a salvage unit.

**Notes:** Planned later (OQ-7 answer, 2026-10-10).

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-6](#oq-6-is-tech-per-player-empire-wide-or-per-tile) | Is tech per player (empire-wide) or per tile? | TT-005 | Decided | Owner | 2026-10-10 |
| [OQ-7](#oq-7-can-captured-enemy-tilesresources-grant-tech-unlocks) | Can captured enemy tiles/resources grant tech unlocks? | TT-002 | Decided | Owner | 2026-10-10 |

### OQ-6 Is tech per player (empire-wide) or per tile?

**Options / notes:** Empire-wide assumed in TT-005.

**Decision:** Moot: there is no tech tree (TT-001).

### OQ-7 Can captured enemy tiles/resources grant tech unlocks?

**Options / notes:** Would reward aggression and enable catch-up.

**Decision:** Defunct: there are no tech unlocks. Later, salvage units may take apart destroyed units for resources (RS-005).
