# Multiplayer & Victory

P8 · Players compete for domination of the world map.

Where it stands (2026-10-10): Single player only. The sim is built for lockstep (commands, determinism, state hash), but there's no networking, AI or victory condition; the demo ends with the guided goal chain.

## Requirements

| ID | Feature | Priority | Status | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|
| [MP-001](#mp-001-domination-victory) | Domination victory | Must | Proposed | MVP | Functional | P8 | WM-004, WM-016 | Updated |  |
| [MP-002](#mp-002-ai-opponents) | AI opponents | Must | Proposed | MVP | Functional | P8 | UC-001, UD-001 |  |  |
| [MP-003](#mp-003-online-multiplayer) | Online multiplayer | Should | In Progress | Alpha | Functional | P8 | NF-004 |  |  |
| [MP-004](#mp-004-diplomacy) | Diplomacy | Could | Proposed | Beta | Functional | P8 | MP-003, IO-002 |  |  |
| [MP-005](#mp-005-match-settings) | Match settings | Should | In Progress | Alpha | Functional | P8 | MP-001 | Updated |  |

### MP-001 Domination victory

**Requirement:** A player wins by controlling a configurable share of world tiles or eliminating all opponents. A player is eliminated when their last core is destroyed (WM-016), at which point they hold no tiles.

**Acceptance criteria:** Victory triggers at threshold; end screen shows stats.

**As built:** Not built. The demo ends with the guided goal chain instead (UI-004).

**Notes:** Elimination decided 2026-10-10.

### MP-002 AI opponents

**Requirement:** The game shall provide AI opponents that build, expand, design units and attack.

**Acceptance criteria:** AI completes a full match on standard settings without stalling.

**As built:** Not built.

### MP-003 Online multiplayer

**Requirement:** The game shall support real-time online matches with multiple human players on one world map, all players acting simultaneously.

**Acceptance criteria:** 2-8 players can complete a match; desync detected and reported.

**As built:** Foundation only: every change is a serialisable command applied on a tick, the sim is integer and deterministic, and a state hash detects divergence. No networking.

**Notes:** Updated 2026-10-05: decision OQ-12 No pause in multiplayer (OQ-32); a pause vote may come later.

### MP-004 Diplomacy

**Requirement:** Players may form alliances, share vision and trade resources via inter-tile links.

**Acceptance criteria:** Allied players can link ports across borders.

**As built:** Not built.

### MP-005 Match settings

**Requirement:** Hosts shall configure map size, player count, capture time, victory threshold and resource richness.

**Acceptance criteria:** Settings saved with match and shown in lobby.

**As built:** New game sets seed and map size (Small 5x5, Medium 7x7, Large 9x9; 3-15 allowed). Saved with the match (GameSetup). No player count, protection, victory or richness settings; no lobby.

**Notes:** Was: '... protection duration ...'. No protection setting (OQ-4); capture time added (WM-006), 2026-10-10.

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-12](#oq-12-real-time-for-all-players-at-once-or-asyncturn-based-on-the-world-layer) | Real-time for all players at once, or async/turn-based on the world layer? | MP-003, NF-004 | Decided | Owner | 2026-10-05 |
| [OQ-32](#oq-32-how-do-pause-and-game-speed-work-in-a-multiplayer-match) | How do pause and game speed work in a multiplayer match? | MP-003, UI-006 | Decided | Owner | 2026-10-10 |
| [OQ-33](#oq-33-does-the-ai-play-through-ordinary-commands-so-it-replays-from-the-log-and-stays-in-sync) | Does the AI play through ordinary commands, so it replays from the log and stays in sync? | MP-002, NF-004 | Decided | Owner | 2026-10-10 |

### OQ-12 Real-time for all players at once, or async/turn-based on the world layer?

**Options / notes:** Major architecture decision; affects NF-004.

**Decision:** Real-time for all players simultaneously.

### OQ-32 How do pause and game speed work in a multiplayer match?

**Options / notes:** Today Esc stops the sim, which lockstep can't do: vote pause, host-only pause, no pause.

**Decision:** No pause in multiplayer for now; maybe a vote to pause later.

### OQ-33 Does the AI play through ordinary commands, so it replays from the log and stays in sync?

**Options / notes:** Commands from a deterministic AI (works with saves and lockstep) vs host-only AI.

**Decision:** One command interface for players, game AI and external drivers (e.g. a genAI test harness); the GUI is a layer over it (NF-009).
