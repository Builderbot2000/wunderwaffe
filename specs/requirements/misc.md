# Misc

Cross-cutting UI/UX and non-functional requirements, project-wide open questions, and the glossary.

Where it stands (2026-10-10): Title, pause and settings menus; HUD; guided objectives; integer deterministic sim; saves as replayable command logs; JSON content; automated tests and scripted runs.

## Requirements

| ID | Feature | Priority | Status | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|
| [UI-001](#ui-001-world-map-hud) | World map HUD | Must | In Progress | MVP | Functional | P1 | WM-001 |  |  |
| [UI-002](#ui-002-tile-hud) | Tile HUD | Must | In Progress | MVP | Functional | P2 | FB-001 |  |  |
| [UI-003](#ui-003-cross-tile-alerts) | Cross-tile alerts | Must | Proposed | MVP | Functional | P5 | UI-001 |  |  |
| [UI-004](#ui-004-tutorial--onboarding) | Tutorial / onboarding | Should | In Progress | Alpha | Functional |  |  |  |  |
| [UI-005](#ui-005-key-rebinding--accessibility) | Key rebinding & accessibility | Should | In Progress | Alpha | Non-Functional |  |  |  |  |
| [NF-001](#nf-001-simulation-performance) | Simulation performance | Must | In Progress | MVP | Non-Functional |  |  |  |  |
| [NF-002](#nf-002-save--load) | Save / load | Must | Done | MVP | Non-Functional |  |  | Review |  |
| [NF-003](#nf-003-data-driven-content) | Data-driven content | Should | Done | Alpha | Non-Functional |  | RS-002 |  |  |
| [NF-004](#nf-004-deterministic-simulation) | Deterministic simulation | Must | In Progress | MVP | Non-Functional |  |  | Review |  |
| [NF-005](#nf-005-target-platform) | Target platform | Must | In Progress | MVP | Non-Functional |  |  |  |  |
| [NF-006](#nf-006-modding-support) | Modding support | Could | Proposed | Beta | Non-Functional |  | NF-003 |  |  |
| [UI-006](#ui-006-title-pause--settings-menus) | Title, pause & settings menus | Must | Done | MVP | Functional |  |  | New |  |
| [NF-007](#nf-007-automated-verification) | Automated verification | Should | Done | MVP | Non-Functional |  |  | New |  |
| [NF-008](#nf-008-save-snapshots) | Save snapshots | Should | Approved | Alpha | Non-Functional |  | NF-002, NF-004 | New |  |
| [NF-009](#nf-009-shared-command-interface) | Shared command interface | Must | In Progress | MVP | Non-Functional | P8 | NF-004 | New |  |

### UI-001 World map HUD

**Requirement:** World map HUD shall show owned tiles, armies, alerts and global resource summary.

**Acceptance criteria:** Clicking an alert jumps to the relevant tile/unit.

**As built:** Selected-tile panel (profile, deposits, claim cost and payer, ports), objective panel, clock. No armies list, alerts or global resource summary.

### UI-002 Tile HUD

**Requirement:** Tile view HUD shall show tile inventory, power balance, build menu and selected entity info.

**Acceptance criteria:** All HUD elements update in real time.

**As built:** Inventory, build bar (costs, locks, tooltips), placement line, hover info, objective panel. No power balance (no power yet).

### UI-003 Cross-tile alerts

**Requirement:** The player shall be alerted when any owned tile is under attack, starved of inputs or a factory completes a unit.

**Acceptance criteria:** Alert appears within 1 s of event; configurable filter.

**As built:** Not built. Toasts exist for tech unlocks and completed goals only.

### UI-004 Tutorial / onboarding

**Requirement:** A guided tutorial shall teach building, logistics, unit design, squads and expansion.

**Acceptance criteria:** New player can complete tutorial in < 30 min.

**As built:** A 6-step guided objective chain (goals.json) walks through the demo loop: mine, smelt, claim coal, ship, make steel, lift off. Not a full tutorial (no unit design or squads yet).

**Notes:** A campaign is planned as the tutorial, deferred until core mechanics settle (OQ-13).

### UI-005 Key rebinding & accessibility

**Requirement:** Players shall rebind controls, scale UI and use colour-blind palettes.

**Acceptance criteria:** All actions rebindable; at least 2 colour-blind modes.

**As built:** Every key is a named input action with defaults set in code, ready for rebinding. No rebinding UI, UI scale or colour-blind modes.

### NF-001 Simulation performance

**Requirement:** The game shall run full simulation for viewed/contested tiles and rate-mode simulation for all other owned tiles at target tick rate.

**Acceptance criteria:** ≥ 60 FPS / 60 UPS with 8 full-sim tiles × 2,000 buildings plus 200 rate-mode tiles on target hardware.

**As built:** Every tile runs the full entity sim. Tests hold 8 tiles x 2,000 buildings under 1 ms/tick; the headless stress run measured ~0.07 ms/tick for ~1,000 buildings. Rate mode not built.

**Notes:** Updated 2026-10-05: decision OQ-11 No budget yet for units and projectiles in combat.

### NF-002 Save / load

**Requirement:** The full world state (all tiles, units, designs, tech) shall be savable and loadable.

**Acceptance criteria:** Load restores identical state; save < 5 s for large world.

**As built:** A save is the match setup plus the command log; loading replays it (~1.5 s per hour of play) and checks the state hash. Saving is instant.

**Notes:** Load time grows with match length. Long matches will need periodic snapshots to keep loads short; see OQ-19. Snapshots approved 2026-10-10 (NF-008, OQ-19). No save compatibility across versions during greenfield development (OQ-34).

### NF-003 Data-driven content

**Requirement:** Buildings, items, recipes, blocks and tech nodes shall be defined in external data files to support balancing and modding.

**Acceptance criteria:** Designers can add content without recompiling.

**As built:** Items, recipes, buildings, tech, goals and terrain are JSON in game/data, validated at load. New building behaviours still need code.

### NF-004 Deterministic simulation

**Requirement:** Simulation shall be deterministic to support lockstep multiplayer and replays.

**Acceptance criteria:** Same inputs reproduce identical state hash after 30 min, including rate-mode tiles and full-sim promotion/demotion.

**As built:** Integer-only sim, ordered iteration, seeded SplitMix64, integer CORDIC trig, FNV state hash; determinism and replay tests. Not yet checked across machines or over 30 min; rate mode doesn't exist.

**Notes:** Updated 2026-10-05: decision OQ-12; lockstep assumed.; Priority is Should, but lockstep (MP-003) and replay saves (NF-002) depend on it; see OQ-18. Raised to Must 2026-10-10 (OQ-18).

### NF-005 Target platform

**Requirement:** The game shall ship on PC (Windows) with mouse & keyboard as primary input.

**Acceptance criteria:** Builds pass on Windows 10/11 64-bit.

**As built:** Godot 4.7.2 .NET on Windows. No packaged builds yet.

### NF-006 Modding support

**Requirement:** The game may expose a modding API for content and scripts.

**Acceptance criteria:** Example mod adding a new block loads successfully.

**As built:** Not built.

### UI-006 Title, pause & settings menus

**Requirement:** The game shall open on a title screen offering new game (seed, map size), continue, load, settings and quit, with an in-game pause menu.

**Acceptance criteria:** Esc pauses the match (the sim stops); the pause menu saves, loads, opens settings and quits; settings persist (fullscreen, V-Sync, world map refresh).

**As built:** ui/TitleScreen.cs, PauseMenu.cs, GameSettings.cs, SaveSlots.cs.

**Notes:** Added from what was built. Esc pausing applies to single player only (OQ-32).

### NF-007 Automated verification

**Requirement:** The project shall have automated checks for the simulation and scripted runs of the game.

**Acceptance criteria:** xUnit suite on the real content, including a no-cheat run of the whole goal chain and tick-budget tests; headless --stress benchmark; --tour and --drive scripted runs with screenshots.

**As built:** tests/Wunderwaffe.Sim.Tests (136 tests), game/debug/.

**Notes:** Added from what was built.

### NF-008 Save snapshots

**Requirement:** Saves shall include periodic state snapshots so loading a long match doesn't replay the whole command log.

**Acceptance criteria:** Loading the latest snapshot plus the commands after it gives the same state hash as a full replay; load time stays under a set bound whatever the match length.

**As built:** Not built.

**Notes:** OQ-19 decided 2026-10-10.

### NF-009 Shared command interface

**Requirement:** Players, game AI and external drivers (scripted tests, genAI agents) shall act on the game through one interface: the same commands to act, and the same read-only checks and state queries to observe. The GUI is a layer over that interface and has no rules of its own.

**Acceptance criteria:** Every action the GUI offers is a command; every preview or status the GUI shows comes from a public sim check or query; an AI player and a headless external driver can play the demo loop end to end using only the interface.

**As built:** Every sim change is already a Command and the UI uses the sim's Check methods, but the GUI reads sim state directly, there's no formal query/observation API and no external driver.

**Notes:** OQ-33 decided 2026-10-10. MP-002 (AI) and automated play-testing build on it.

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-13](#oq-13-is-a-single-player-campaign-or-story-planned-the-3037-setting) | Is a single-player campaign or story planned (the '3037' setting)? |  | Decided | Owner | 2026-10-10 |
| [OQ-18](#oq-18-raise-deterministic-simulation-nf-004-from-should-to-must) | Raise deterministic simulation (NF-004) from Should to Must? | NF-004, MP-003, NF-002 | Decided | Owner | 2026-10-10 |
| [OQ-19](#oq-19-do-saves-need-periodic-state-snapshots-so-loads-stay-short-in-long-matches) | Do saves need periodic state snapshots so loads stay short in long matches? | NF-002 | Decided | Owner | 2026-10-10 |
| [OQ-34](#oq-34-how-do-saves-survive-content-changes) | How do saves survive content changes? | NF-002, NF-008, NF-003 | Decided | Owner | 2026-10-10 |

### OQ-13 Is a single-player campaign or story planned (the '3037' setting)?

**Options / notes:** Not covered by current requirements.

**Decision:** Planned, mainly as the tutorial. Deferred: it's content work and core mechanics are still open.

### OQ-18 Raise deterministic simulation (NF-004) from Should to Must?

**Options / notes:** Lockstep multiplayer and replay-based saves both depend on it, and it's already built that way.

**Decision:** Yes: NF-004 is a Must.

### OQ-19 Do saves need periodic state snapshots so loads stay short in long matches?

**Options / notes:** Replaying the command log takes ~1.5 s per hour of play; snapshots would bound load time but must hash-match the replay.

**Decision:** Yes (NF-008).

### OQ-34 How do saves survive content changes?

**Options / notes:** A save replays commands against today's JSON, so any balance change can break old saves. Options: store a content hash/version and refuse or warn; snapshots (NF-008) ease it but don't fix it.

**Decision:** None needed: greenfield development, no save compatibility for now.
