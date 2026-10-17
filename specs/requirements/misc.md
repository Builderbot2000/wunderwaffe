# Misc

Cross-cutting UI/UX and non-functional requirements, project-wide open questions, and the glossary.

Where it stands (2026-10-17): Title, pause, settings and key-rebinding menus; HUD with alerts, armies and production panels; guided objectives; integer deterministic sim; saves as command logs with snapshots; JSON content; observation API, JSON driver and AI; automated tests and scripted runs. No packaged build or modding.

## Requirements

| ID | Feature | Priority | Status | Built | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|---|
| [UI-001](#ui-001-world-map-hud) | World map HUD | Must | Done | Yes | MVP | Functional | P1 | WM-001 |  |  |
| [UI-002](#ui-002-tile-hud) | Tile HUD | Must | Done | Yes | MVP | Functional | P2 | FB-001 |  |  |
| [UI-003](#ui-003-cross-tile-alerts) | Cross-tile alerts | Must | Done | Yes | MVP | Functional | P5 | UI-001 |  |  |
| [UI-004](#ui-004-tutorial--onboarding) | Tutorial / onboarding | Should | In Progress | Partial | Alpha | Functional |  |  |  |  |
| [UI-005](#ui-005-key-rebinding--accessibility) | Key rebinding & accessibility | Should | Done | Yes | Alpha | Non-Functional |  |  |  |  |
| [NF-001](#nf-001-simulation-performance) | Simulation performance | Must | Done | Yes | MVP | Non-Functional |  |  |  |  |
| [NF-002](#nf-002-save--load) | Save / load | Must | Done | Yes | MVP | Non-Functional |  |  | Review |  |
| [NF-003](#nf-003-data-driven-content) | Data-driven content | Should | Done | Yes | Alpha | Non-Functional |  | RS-002 |  |  |
| [NF-004](#nf-004-deterministic-simulation) | Deterministic simulation | Must | Done | Yes | MVP | Non-Functional |  |  | Review |  |
| [NF-005](#nf-005-target-platform) | Target platform | Must | In Progress | Partial | MVP | Non-Functional |  |  |  |  |
| [NF-006](#nf-006-modding-support) | Modding support | Could | Proposed | No | Beta | Non-Functional |  | NF-003 |  |  |
| [UI-006](#ui-006-title-pause--settings-menus) | Title, pause & settings menus | Must | Done | Yes | MVP | Functional |  |  | New |  |
| [NF-007](#nf-007-automated-verification) | Automated verification | Should | Done | Yes | MVP | Non-Functional |  |  | New |  |
| [NF-008](#nf-008-save-snapshots) | Save snapshots | Should | Done | Yes | Alpha | Non-Functional |  | NF-002, NF-004 | New |  |
| [NF-009](#nf-009-shared-command-interface) | Shared command interface | Must | Done | Yes | MVP | Non-Functional | P8 | NF-004 | New |  |

### UI-001 World map HUD

**Requirement:** World map HUD shall show owned tiles, armies, alerts and global resource summary.

**Acceptance criteria:** Clicking an alert jumps to the relevant tile/unit.

**As built:** World map: tile side panel, armies list (Hud.World), alert list with jump-to (Hud.Alerts), top bar totals over owned tiles.

### UI-002 Tile HUD

**Requirement:** Tile view HUD shall show tile inventory, power balance, build menu and selected entity info.

**Acceptance criteria:** All HUD elements update in real time.

**As built:** Top bar stock and power balance, tabbed build bar (costs, tooltips, hotkeys), placement message, hover panel, objective panel.

### UI-003 Cross-tile alerts

**Requirement:** The player shall be alerted when any owned tile is under attack, starved of inputs or a factory completes a unit.

**Acceptance criteria:** Alert appears within 1 s of event; configurable filter.

**As built:** Hud.Alerts.cs over GameEvents: alert list, filter toggles per group, combat toasts, click jumps to the tile or unit.

### UI-004 Tutorial / onboarding

**Requirement:** A guided tutorial shall teach building, logistics, unit design, squads and expansion.

**Acceptance criteria:** New player can complete tutorial in < 30 min.

**As built:** A 7-step guided chain (goals.json): mine, smelt, lift off the scout, claim the coal tile, link tiles, make steel, build a unit. Not a full tutorial: no factories, squads or combat.

**Notes:** A campaign is planned as the tutorial, deferred until core mechanics settle (OQ-13).

### UI-005 Key rebinding & accessibility

**Requirement:** Players shall rebind controls, scale UI and use colour-blind palettes.

**Acceptance criteria:** All actions rebindable; at least 2 colour-blind modes.

**As built:** Rebinding screen for every action (KeyBindingsPanel), UI scale, red-green and blue-yellow colour modes (GameSettings, Palette.ColourMode).

### NF-001 Simulation performance

**Requirement:** The game shall run full simulation for viewed/contested tiles and rate-mode simulation for all other owned tiles at target tick rate.

**Acceptance criteria:** ≥ 60 FPS / 60 UPS with 8 full-sim tiles × 2,000 buildings plus 200 rate-mode tiles on target hardware.

**As built:** StressScene: 8 viewed full-sim tiles x 2,000 buildings plus 200 rate-mode factories hold 60 UPS, under 4 ms/tick (StressTests, --stress); a ~280-unit firefight stays within budget. Measured on the dev machine with 96-cell tiles; frame rate not measured.

**Notes:** Updated 2026-10-05: decision OQ-11 No budget yet for units and projectiles in combat. 2026-10-17: a 256x256 nominal tile (OQ-35) has about 7x the cells of a 96 one, and continuous zoom may view several tiles at once (OQ-36); re-measure the stress scene and world-gen time at that size.

### NF-002 Save / load

**Requirement:** The full world state (all tiles, units, designs, tech) shall be savable and loadable.

**Acceptance criteria:** Load restores identical state; save < 5 s for large world.

**As built:** A save is the match setup plus the command log and the latest snapshot; loading starts from the snapshot when it hash-checks (NF-008), else replays everything, and checks the state hash. Saving is instant.

**Notes:** Load time grows with match length. Long matches will need periodic snapshots to keep loads short; see OQ-19. Snapshots approved 2026-10-10 (NF-008, OQ-19). No save compatibility across versions during greenfield development (OQ-34).

### NF-003 Data-driven content

**Requirement:** Buildings, items, recipes, blocks and tech nodes shall be defined in external data files to support balancing and modding.

**Acceptance criteria:** Designers can add content without recompiling.

**As built:** Items, recipes, buildings, goals and terrain are JSON in game/data, validated at load. New building behaviours still need code.

### NF-004 Deterministic simulation

**Requirement:** Simulation shall be deterministic to support lockstep multiplayer and replays.

**Acceptance criteria:** Same inputs reproduce identical state hash after 30 min, including rate-mode tiles and full-sim promotion/demotion.

**As built:** Integer-only sim, ordered iteration, seeded SplitMix64, integer CORDIC trig, FNV state hash; a 30-minute determinism test with rate mode and switching, replay tests and lockstep hash checks. Not checked across different machines.

**Notes:** Updated 2026-10-05: decision OQ-12; lockstep assumed.; Priority is Should, but lockstep (MP-003) and replay saves (NF-002) depend on it; see OQ-18. Raised to Must 2026-10-10 (OQ-18). Owner, 2026-10-17: 'replays' means replaying the command log for saves and checks; there is no replay viewer yet.

### NF-005 Target platform

**Requirement:** The game shall ship on PC (Windows) with mouse & keyboard as primary input.

**Acceptance criteria:** Builds pass on Windows 10/11 64-bit.

**As built:** Godot 4.7.2 .NET on Windows. No export presets or packaged builds yet.

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

**As built:** tests/Wunderwaffe.Sim.Tests (~258 tests incl. AI, JSON driver, lockstep, rate mode and snapshots); game/debug/ (--stress, --tour, --drive, --net-smoke).

**Notes:** Added from what was built.

### NF-008 Save snapshots

**Requirement:** Saves shall include periodic state snapshots so loading a long match doesn't replay the whole command log.

**Acceptance criteria:** Loading the latest snapshot plus the commands after it gives the same state hash as a full replay; load time stays under a set bound whatever the match length.

**As built:** Core/Snapshot.cs: snapshots every SnapshotIntervalTicks; a save carries the latest, loading restores it and replays the commands after it, falling back to a full replay if it doesn't hash-check (tested).

**Notes:** OQ-19 decided 2026-10-10.

### NF-009 Shared command interface

**Requirement:** Players, game AI and external drivers (scripted tests, genAI agents) shall act on the game through one interface: the same commands to act, and the same read-only checks and state queries to observe. The GUI is a layer over that interface and has no rules of its own.

**Acceptance criteria:** Every action the GUI offers is a command; every preview or status the GUI shows comes from a public sim check or query; an AI player and a headless external driver can play the demo loop end to end using only the interface.

**As built:** Api/GameQuery.cs (read-only observation for one seat) serves the GUI, Ai/AiPlayer.cs and Api/JsonDriver.cs (JSON lines over stdin/stdout, src/Wunderwaffe.Cli); every action is a Command (tested end to end).

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
