# Multiplayer & Victory

P8 · Players compete for domination of the world map.

Where it stands (2026-10-17): Victory by domination or elimination, AI opponents, ENet lockstep with a lobby and desync detection, match settings. Only two-peer online play is tested. No diplomacy.

## Requirements

| ID | Feature | Priority | Status | Built | Milestone | Type | Pillar | Depends On | Audit Flag | Owner |
|---|---|---|---|---|---|---|---|---|---|---|
| [MP-001](#mp-001-domination-victory) | Domination victory | Must | Done | Yes | MVP | Functional | P8 | WM-004, WM-016 | Updated |  |
| [MP-002](#mp-002-ai-opponents) | AI opponents | Must | Done | Yes | MVP | Functional | P8 | UC-001, UD-001 |  |  |
| [MP-003](#mp-003-online-multiplayer) | Online multiplayer | Should | In Progress | Partial | Alpha | Functional | P8 | NF-004 |  |  |
| [MP-004](#mp-004-diplomacy) | Diplomacy | Could | Proposed | No | Beta | Functional | P8 | MP-003, IO-002 |  |  |
| [MP-005](#mp-005-match-settings) | Match settings | Should | Done | Yes | Alpha | Functional | P8 | MP-001 | Updated |  |

### MP-001 Domination victory

**Requirement:** A player wins by controlling a configurable share of world tiles or eliminating all opponents. A player is eliminated when their last core is destroyed (WM-016), at which point they hold no tiles.

**Acceptance criteria:** Victory triggers at threshold; end screen shows stats.

**As built:** World/Victory.cs: a player with no landed core is eliminated; domination at the match's tile share; end screen with winner, reason and per-player stats (Hud.Match).

**Notes:** Elimination decided 2026-10-10. 2026-10-17: cores on units count too (WM-016), so a player whose cores are all in flight is still in the match.

### MP-002 AI opponents

**Requirement:** The game shall provide AI opponents that build, expand, design units and attack.

**Acceptance criteria:** AI completes a full match on standard settings without stalling.

**As built:** Ai/AiPlayer.cs plays through ordinary commands and GameQuery (economy, steel, units, expansion, outposts, attack); tests play the whole goal chain, replay two AIs from the log and run a long match without stalling.

### MP-003 Online multiplayer

**Requirement:** The game shall support real-time online matches with multiple human players on one world map, all players acting simultaneously.

**Acceptance criteria:** 2-8 players can complete a match; desync detected and reported.

**As built:** Net/Lockstep.cs over ENet (net/NetSession.cs): lobby, host/join, AI seats on the host, periodic hash checks report desyncs to everyone, seat spoofing and cheats refused. Tested with in-memory peers and two headless instances (--net-smoke); no 3-8 player match tested.

**Notes:** Updated 2026-10-05: decision OQ-12 No pause in multiplayer (OQ-32); a pause vote may come later.

### MP-004 Diplomacy

**Requirement:** Players may form alliances, share vision and trade resources via inter-tile links.

**Acceptance criteria:** Allied players can link ports across borders.

**As built:** Not built.

### MP-005 Match settings

**Requirement:** Hosts shall configure map size, player count, capture time, victory threshold and resource richness.

**Acceptance criteria:** Settings saved with match and shown in lobby.

**As built:** ui/MatchSettings.cs: seed, map size, AI count, capture time, victory share and richness go into GameSetup, saved with the match; the lobby shows them to clients.

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
