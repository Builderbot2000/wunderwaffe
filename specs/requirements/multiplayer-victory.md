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
| [MP-006](#mp-006-ai-framework) | AI framework | Must | In Progress | Partial | MVP | Non-Functional | P8 | MP-002, NF-009, FB-021 | New |  |

### MP-001 Domination victory

**Requirement:** A player wins by controlling a configurable share of world tiles or eliminating all opponents. A player is eliminated when their last core is destroyed (WM-016), at which point they hold no tiles.

**Acceptance criteria:** Victory triggers at threshold; end screen shows stats.

**As built:** World/Victory.cs: a player with no landed core is eliminated; domination at the match's tile share; end screen with winner, reason and per-player stats (Hud.Match).

**Notes:** Elimination decided 2026-10-10. 2026-10-17: cores on units count too (WM-016), so a player whose cores are all in flight is still in the match.

### MP-002 AI opponents

**Requirement:** The game shall provide AI opponents that build, expand, design units and attack.

**Acceptance criteria:** AI completes a full match on standard settings without stalling.

**As built:** Ai/AiPlayer.cs plays through ordinary commands and GameQuery (economy, steel, units, expansion, outposts, attack); tests play the whole goal chain, replay two AIs from the log and run a long match without stalling.

**Notes:** Owner, 2026-10-17: no difficulty levels; difficulty comes from giving the AI a head start on certain maps (OQ-47). There is no written design for the AI's behaviour beyond the priority list in the code.

### MP-003 Online multiplayer

**Requirement:** The game shall support real-time online matches with multiple human players on one world map, all players acting simultaneously.

**Acceptance criteria:** 2-8 players can complete a match; desync detected and reported; a player who leaves is replaced by an AI seat that carries on their side (OQ-46).

**As built:** Net/Lockstep.cs over ENet (net/NetSession.cs): lobby, host/join, AI seats on the host, periodic hash checks report desyncs to everyone, seat spoofing and cheats refused. Tested with in-memory peers and two headless instances (--net-smoke); no 3-8 player match tested. A leaver's side stays where it is, with nobody playing it.

**Notes:** Updated 2026-10-05: decision OQ-12. Owner, 2026-10-17: leavers are replaced by AI (OQ-46). No pause in multiplayer (OQ-32); a pause vote may come later.

### MP-004 Diplomacy

**Requirement:** Players may form alliances, share vision and trade resources via inter-tile links.

**Acceptance criteria:** Allied players can link ports across borders.

**As built:** Not built.

### MP-005 Match settings

**Requirement:** Hosts shall configure map size, player count, capture time, victory threshold and resource richness.

**Acceptance criteria:** Settings saved with match and shown in lobby.

**As built:** ui/MatchSettings.cs: seed, map size, AI count, capture time, victory share and richness go into GameSetup, saved with the match; the lobby shows them to clients.

**Notes:** Was: '... protection duration ...'. No protection setting (OQ-4); capture time added (WM-006), 2026-10-10.

### MP-006 AI framework

**Requirement:** Every AI seat shall run on one framework that its behaviour plugs into: an initialisation step that works from any game state, then a fixed think loop that observes through the shared query interface (NF-009), has behaviour modules propose scored actions, and issues the best of them as ordinary commands. Decisions are split into a strategic layer on the world map (where to expand, defend and attack) and a tactical layer inside each tile (layout, production, squad placement, stance and targeting mode). AI logic is refined by adding or tuning modules and scores, not by changing the framework.

**Acceptance criteria:** Initialise: runs when a seat gets an AI, whether at match start, on load, or taking over a leaver mid-match (OQ-46); it reads the match through GameQuery, picks its unit and building designs from the prefab library (FB-021), and builds whatever working memory it needs from the current state alone. Loop: every think interval (staggered per seat), observe (GameQuery), run the strategic layer, then ask each module for proposals; each proposal carries an integer score and has passed the sim's own checks; the framework spends a per-think command budget on the highest scores (ties broken by module order, then a stable key) and issues them through the same path as a player's commands (host only in multiplayer; replays use the logged commands and never run the AI). Strategic layer: an influence map over the tile graph (own and enemy strength, tile value from deposits and circles, threat) sets the seat's goals for the tiles, such as expand to, hold, reinforce or attack, which module scores read. Tactical layer: modules act within one tile at a time towards those goals. Modules: each owns one concern (for example economy, expansion, production, defence, attack), is a separate unit of code with its own tests, and can be switched off or re-weighted per seat; a module with nothing to propose never blocks the others. Units: the AI commands squads through orders, stances (UC-004) and targeting modes (CB-007) and leaves moment-to-moment fighting to them; it does not steer single units each think. Memory: anything the AI remembers between thinks can be rebuilt from game state, so saving, loading and takeover change nothing about its play; its decisions are deterministic for a given state (integer scores, seeded Rng only). Observable: a debug view or log shows each seat's tile goals, the proposals with their scores, and what it chose, for tuning.

**As built:** Partly: Ai/AiPlayer.cs thinks every half second (staggered by seat) through GameQuery and returns ordinary commands, keeps no memory between thinks, and does the first thing it can along a fixed priority list (Clean, Economy, Steel, Units, Expand, Outposts, Attack); AiDirector runs the seats on the host only. The steps are methods of one class, not separate modules; there are no scores, no influence map or strategic layer, no initialisation step, command budget, takeover or debug view, and its unit design is a constant.

**Notes:** Owner, 2026-10-17: set up the system the AI initialises and loops on before refining its logic. MP-002 is what the AI plays; this is how it is built. Design follows OQ-48. References: UAlbertaBot (D. Churchill; one manager per concern), 0 A.D.'s Petra AI (headquarters, attack, defence and queue managers), Beyond All Reason / Spring skirmish AIs (factory-driven economies), Ontanon et al. 2013, 'A Survey of Real-Time Strategy Game AI Research and Competition in StarCraft' (strategy / tactics / reactive control), D. Mark and K. Dill, 'Improving AI Decision Modeling Through Utility Theory' (GDC 2010), and Game AI Pro (gameaipro.com) on utility systems and influence maps.

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-12](#oq-12-real-time-for-all-players-at-once-or-asyncturn-based-on-the-world-layer) | Real-time for all players at once, or async/turn-based on the world layer? | MP-003, NF-004 | Decided | Owner | 2026-10-05 |
| [OQ-32](#oq-32-how-do-pause-and-game-speed-work-in-a-multiplayer-match) | How do pause and game speed work in a multiplayer match? | MP-003, UI-006 | Decided | Owner | 2026-10-10 |
| [OQ-33](#oq-33-does-the-ai-play-through-ordinary-commands-so-it-replays-from-the-log-and-stays-in-sync) | Does the AI play through ordinary commands, so it replays from the log and stays in sync? | MP-002, NF-004 | Decided | Owner | 2026-10-10 |
| [OQ-46](#oq-46-what-happens-when-a-player-leaves-an-online-match) | What happens when a player leaves an online match? | MP-003, MP-002 | Decided | Owner | 2026-10-17 |
| [OQ-47](#oq-47-how-is-ai-difficulty-set) | How is AI difficulty set? | MP-002, MP-005 | Decided | Owner | 2026-10-17 |
| [OQ-48](#oq-48-how-should-the-ai-choose-what-to-do) | How should the AI choose what to do? | MP-006, MP-002 | Decided | Owner | 2026-10-17 |

### OQ-12 Real-time for all players at once, or async/turn-based on the world layer?

**Options / notes:** Major architecture decision; affects NF-004.

**Decision:** Real-time for all players simultaneously.

### OQ-32 How do pause and game speed work in a multiplayer match?

**Options / notes:** Today Esc stops the sim, which lockstep can't do: vote pause, host-only pause, no pause.

**Decision:** No pause in multiplayer for now; maybe a vote to pause later. Owner, 2026-10-17: game speed is fixed, in single player too.

### OQ-33 Does the AI play through ordinary commands, so it replays from the log and stays in sync?

**Options / notes:** Commands from a deterministic AI (works with saves and lockstep) vs host-only AI.

**Decision:** One command interface for players, game AI and external drivers (e.g. a genAI test harness); the GUI is a layer over it (NF-009).

### OQ-46 What happens when a player leaves an online match?

**Options / notes:** Built: their side stays where it is, unplayed. Options: freeze, AI takeover, eliminate.

**Decision:** An AI takes over their seat.

### OQ-47 How is AI difficulty set?

**Options / notes:** Options: difficulty levels (smarter play, resource bonuses), handicaps per map.

**Decision:** No difficulty levels for now; difficulty is tuned by giving the AI a head start on certain maps.

### OQ-48 How should the AI choose what to do?

**Options / notes:** Built: the first possible step on a fixed priority list. Options: priority list, behaviour trees, utility scoring, planners (GOAP, HTN). Common RTS bot practice (UAlbertaBot, Petra) is one manager per concern; utility scoring weighs competing goals better than a fixed order.

**Decision:** Per-concern modules proposing integer-scored actions, best scores first within a command budget; a strategic layer with an influence map over the tile graph sets tile goals, and a tactical layer acts inside tiles; units are commanded as squads through orders, stances and targeting modes (MP-006).
