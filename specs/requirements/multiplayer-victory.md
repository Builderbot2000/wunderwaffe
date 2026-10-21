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
| [MP-006](#mp-006-ai-framework) | AI framework | Must | Done | Yes | MVP | Non-Functional | P8 | MP-002, NF-009, FB-021 | New |  |
| [MP-007](#mp-007-content-agnostic-ai) | Content-agnostic AI | Must | Done | Yes | MVP | Non-Functional | P8 | MP-006, RS-002 |  |  |
| [MP-008](#mp-008-ai-head-starts) | AI head starts | Should | Done | Yes | Alpha | Functional | P8 | FB-021, MP-005, WM-017 |  |  |
| [MP-009](#mp-009-faction-templates) | Faction templates | Should | Done | Yes | Alpha | Functional | P8 | MP-005, MP-007, FB-021 | New |  |
| [MP-010](#mp-010-faction-rules) | Faction rules | Should | In Progress | Partial | Alpha | Functional | P8 | MP-009, FB-023, WM-019 | New |  |

### MP-001 Domination victory

**Requirement:** A player wins by controlling a configurable share of world tiles or eliminating all opponents. A player is eliminated when their last core is destroyed (WM-016), at which point they hold no tiles.

**Acceptance criteria:** Victory triggers at threshold; end screen shows stats.

**As built:** World/Victory.cs: a player with no core, on a tile or on a unit, is eliminated; domination at the match's tile share; end screen with winner, reason and per-player stats (Hud.Match).

**Notes:** Elimination decided 2026-10-10. 2026-10-17: cores on units count too (WM-016), so a player whose cores are all in flight is still in the match.

### MP-002 AI opponents

**Requirement:** The game shall provide AI opponents that build, expand, design units and attack.

**Acceptance criteria:** AI completes a full match on standard settings without stalling.

**As built:** Ai/ (MP-006 framework) plays through ordinary commands and GameQuery (economy, steel, units from the cheapest unit prefab, expansion, defence, attack in squads); tests play the whole goal chain, replay two AIs from the log and run a long match without stalling.

**Notes:** Owner, 2026-10-17: no difficulty levels; difficulty comes from giving the AI a head start on certain maps (OQ-47). The AI's behaviour is the scored modules of MP-006; there is no written design beyond them.

### MP-003 Online multiplayer

**Requirement:** The game shall support real-time online matches with multiple human players on one world map, all players acting simultaneously.

**Acceptance criteria:** 2-8 players can complete a match; desync detected and reported; a player who leaves is replaced by an AI seat that carries on their side (OQ-46).

**As built:** Net/Lockstep.cs over ENet (net/NetSession.cs): lobby, host/join, AI seats on the host, periodic hash checks report desyncs to everyone, seat spoofing and cheats refused. A client who leaves is dropped (Lockstep.Drop raises SeatLeft) and the host's AiDirector takes over the seat from the current state (OQ-46); a left seat's input is refused. Tested with in-memory peers (5 seats with a leaver taken over by the AI, LockstepTests) and two headless instances (--net-smoke); no 3-8 player match over ENet tested.

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

**As built:** Ai/: AiPlayer.Init runs at match start, on load (first think) and on takeover (AiDirector.TakeOver), picks the unit design from the prefabs (AiDesigns.Pick: the cheapest moving unit prefab) and drops its caches. Think every half second, staggered by seat: AiContext observes through GameQuery, InfluenceMap (own/enemy unit strength, threat from neighbours, tile value) sets Expand/Hold/Reinforce/Attack goals per tile, then the modules (Economy, Expansion, Production, Defence, Attack; one file each under Ai/Modules, IAiModule) propose integer-scored Proposals whose commands passed the sim's checks; the best weighted scores go out within AiConfig.Budget, ties by module order then key, skipping proposals whose locks (a tile's layout, a unit) are taken or whose tile funds are spent. AiConfig weights per seat (0 switches a module off). The only memory is a cache keyed by tile revision, so a load or takeover plays identically (tested). Attack forms squads and sets their stance before attack-moving. Debug: AiPlayer.Last (ThinkReport.Describe: goals, proposals with scores and outcomes) and the Log hook. Attack squads also set a targeting mode (AttackModule.TargetingFor, most weapons; CB-007). Limits: no in-game debug view (log only), tactical layer is the modules' own layout choices, influence counts units only (not turrets).

**Notes:** Owner, 2026-10-17: set up the system the AI initialises and loops on before refining its logic. MP-002 is what the AI plays; this is how it is built. Design follows OQ-48. References: UAlbertaBot (D. Churchill; one manager per concern), 0 A.D.'s Petra AI (headquarters, attack, defence and queue managers), Beyond All Reason / Spring skirmish AIs (factory-driven economies), Ontanon et al. 2013, 'A Survey of Real-Time Strategy Game AI Research and Competition in StarCraft' (strategy / tactics / reactive control), D. Mark and K. Dill, 'Improving AI Decision Modeling Through Utility Theory' (GDC 2010), and Game AI Pro (gameaipro.com) on utility systems and influence maps.

### MP-007 Content-agnostic AI

**Requirement:** The AI shall know no item, recipe or building by id. It shall work out what to build from the content itself, so a content or balance update needs no AI code change.

**Acceptance criteria:** Buildings are found by role (BuildingKind and recipe: the cheapest conveyor, an extractor for an item, a crafter for a recipe), not by id. Demand comes from what the AI wants to build (its chosen unit design, turrets, the next goal); a chain planner expands each needed item backwards through the recipe graph (recipe, building, inputs) down to extraction and deposits. An input with no deposit on an owned tile makes tiles holding it expansion targets, replacing any fixed 'coal first' rule. Unit and turret value comes from stats (UnitStats, Combat.Dps), as today. Content may add optional AI hints (such as a priority) where the derived plan is wrong; none are required. Test: with the item, recipe and building ids renamed in the test content, the AI still plays the goal chain (AiTests).

**As built:** Ai/* and Api/Planner.cs name no item, recipe or building id. Api/Roles.cs finds buildings by role: the cheapest building of a kind (conveyor, wire, ports), the cheapest machine with an extraction recipe for an item, the crafting recipes making an item; the core is ContentDb.DefaultCore. Ai/AiPlan.cs is the chain planner: demand starts from the chosen design's cost (AiDesigns.Pick) and the current produce goal's item and grows by the cost of every building the plan needs; each item is chained backwards (recipe, machine, inputs) to deposits, preferring fewest missing deposits, then fewest steps. EconomyModule runs a line per one-line home item into the home core (count from demand weight) and raw lines into outposts; ProductionModule places machines of several inputs beside an import port and feeds each input (local lines, or export lines on an owned neighbour); ExpansionModule targets home neighbours holding a missing deposit (replaces coal first), and the first scout's carry is sized for that tile's export run. Units from the design as before. No turret demand yet (the AI builds no turrets) and no content hints were needed. Test: AiTests plays the goal chain with every id renamed (TestContent.Renamed).

**Notes:** Owner, 2026-10-18: do this before the content and balance update (OQ-50).

### MP-008 AI head starts

**Requirement:** An AI seat's head start (OQ-47) shall be set in one of two ways: as part of a scenario, where AI assets already placed on the map are handed to the AI at the start (WM-017), or as a generic head start of prefabs placed beside its starting core.

**Acceptance criteria:** A scenario's per-seat assets (buildings, units, owned tiles, core stock) belong to that seat, and an AI seat runs them from the first think like anything else it owns. Generic head starts are named presets in content, each a list of prefabs (FB-021) with optional extra items, stamped beside the seat's first start prefab; the match settings let the host pick a preset per AI seat (MP-005). Both are part of the setup, so saves replay them.

**As built:** game/data/head_starts.json presets (second scout, stockpile with a depot prefab, both), validated on load. GameSetup.AiHeadStarts picks one per AI seat (new-game form and lobby: one picker per AI seat); Simulation.GiveHeadStart stamps its prefabs beside the seat's first core and adds its items to it, on generated maps and scenarios alike. Scenario seat assets belong to the seat and an AI seat runs them from its first think (ScenarioTests: a fresh AI carries on a mid-game scenario). Both are in the setup, so saves replay them.

**Notes:** Owner, 2026-10-18.

### MP-009 Faction templates

**Requirement:** Every seat, human or AI, shall play a faction made from a template over one shared catalogue of buildings and blocks (buildings.json, Age of Empires style). The catalogue is a hidden master tech tree; a template highlights the parts of it its faction can build (its subset), may swap some entries for variants (the faction's own version of a shared building), and lists the rules it plays by, buffs included (MP-010). Factions are content, not code.

**Acceptance criteria:** Entries of the master tree are gated behind one another by resource: an entry's prerequisites are the buildings that make the items it costs, back to extraction (from the recipe graph, RS-002), so content validation refuses a subset that highlights an entry without its prerequisites. The tree stays hidden: players see their faction's build menu, not a tree (TT-003 stays rejected). Templates are content (a factions file), validated with the rest: unknown ids, a variant of something not in the catalogue, a faction with no core, conveyor or extractor its start needs. Each seat's faction is picked in the lobby before the match (MP-005), where Random is one of the choices, or fixed by a scenario (WM-017, e.g. campaign missions), and stored in the GameSetup, so saves, replays and lockstep use the same factions; its start prefabs and head starts fit it (FB-021, MP-008). Blueprints are shared across factions (FB-009); players filter them with their own free-form tags. Placement, blueprints and factories refuse what the seat's faction can't build through a sim check, which the build menu previews, not by hiding it in the UI only. The AI plays any faction with no code change (MP-007): roles and the chain planner search only its faction's subset. A default faction with the whole catalogue plays as today.

**As built:** game/data/factions.json (ContentDb.Factions, first = default; content without the file gets one faction with everything). FactionDef: allow list (buildings) or deny list (except), variants (base → own building of the same kind, footprint and layer; a variant belongs only to the factions naming it), rules, optional startPrefabs, hidden. Validation: unknown ids, bad variants and rules, no core/conveyor/extractor buildable without capture, start prefabs it can't build, and the resource gating derived from recipes and costs (FactionDef.Producible: items the subset's recipes make back to extraction; every entry's cost must be in it, so a subset missing a prerequisite is refused). GameSetup.Factions per seat (id, "random" resolved from seed, scenario hash and seat, or default), ScenarioSeat.Faction fixes it; saved with the setup. Factions.Check gates Placement.Check (NotInFaction, NotCaptured), blueprints (Factions.Substitute maps bases to the seat's variants and back), factory projection, the build menu (other factions' entries hidden, capture-locked ones greyed with the reason), GameQuery (BuildingTypes, Faction, CheckFaction, ForFaction, CheckBlueprintFaction) and Roles/planner/AI (a buildable filter; the AI's design is the cheapest unit prefab its faction can build). The new-game form and lobby pick per seat, Random included; the blueprint library marks entries that don't work for your faction. Shipped: Charter Company (default, everything), Nomad Clans, Foundry Guild (placeholder values). The neutral guardian template waits for WM-010.

**Notes:** Owner, 2026-10-20 (OQ-55). The neutral guardian faction (WM-010) is a template too, holding entries no player faction can build. Fits the content draft's Reclamation Corps (specs/content/content-update.md §1): rival charter companies can differ in kit.

### MP-010 Faction rules

**Requirement:** A faction template may attach special rules that change what its seat can build, or how. The first two: capture to build ('nomad'), where the faction may build a building only once it has owned one it didn't build, and buff, a simple modifier.

**Acceptance criteria:** Rules come from a fixed list the sim knows, named in the template with their parameters, and validated. Capture to build: placing a building the seat has never owned is refused with its own result, which the build menu shows as locked with the reason; owning one, which for a building it can't yet build means claiming an unclaimed one (WM-019), unlocks that exact building for good (variants and their bases count separately). Buff: a percentage change to one stat (such as cost, HP or speed) for the buildings it names or a building kind. Per-seat capture state is sim state: hashed, snapshotted, and readable through GameQuery, so the AI can value capture targets (MP-007). Each rule has a read-only check that the command, the UI preview and the AI share.

**As built:** Rules capture_to_build and buff (FactionRule: buildings and/or kind covered; buff stat speed, power or build_speed, percent -90 to 400), validated. Capture: PlayerState.Acquired (hashed, snapshotted, GameQuery.Faction().Acquired/Locked), recorded by Factions.Acquired(sim, player, defId); placing an unacquired covered building is NotCaptured, and once acquired it stays unlocked (variants and bases separate). Buff: added to the owner's booster stats (Boosters.StatsOf, the machines' Boost), so full sim and rate mode both see it. Not yet: nothing calls Factions.Acquired until claiming unclaimed buildings (WM-019, Territory.TakeOver) and moving them (FB-023) land; the AI doesn't value capture targets yet.

**Notes:** Owner, 2026-10-20. A narrow exception to TT-001: buildings are gated by capture, never research, and only for factions whose template says so. Capture to build is one rule among many; the others aren't added yet (OQ-56). Buffs are a faction rule, not a separate template part (OQ-55).

## Decisions & open questions

| # | Question | Related IDs | State | Decided By | Date |
|---|---|---|---|---|---|
| [OQ-12](#oq-12-real-time-for-all-players-at-once-or-asyncturn-based-on-the-world-layer) | Real-time for all players at once, or async/turn-based on the world layer? | MP-003, NF-004 | Decided | Owner | 2026-10-05 |
| [OQ-32](#oq-32-how-do-pause-and-game-speed-work-in-a-multiplayer-match) | How do pause and game speed work in a multiplayer match? | MP-003, UI-006 | Decided | Owner | 2026-10-10 |
| [OQ-33](#oq-33-does-the-ai-play-through-ordinary-commands-so-it-replays-from-the-log-and-stays-in-sync) | Does the AI play through ordinary commands, so it replays from the log and stays in sync? | MP-002, NF-004 | Decided | Owner | 2026-10-10 |
| [OQ-46](#oq-46-what-happens-when-a-player-leaves-an-online-match) | What happens when a player leaves an online match? | MP-003, MP-002 | Decided | Owner | 2026-10-17 |
| [OQ-47](#oq-47-how-is-ai-difficulty-set) | How is AI difficulty set? | MP-002, MP-005 | Decided | Owner | 2026-10-17 |
| [OQ-48](#oq-48-how-should-the-ai-choose-what-to-do) | How should the AI choose what to do? | MP-006, MP-002 | Decided | Owner | 2026-10-17 |
| [OQ-50](#oq-50-how-does-the-ai-cope-with-content-changes) | How does the AI cope with content changes? | MP-007, MP-006, RS-002 | Decided | Owner | 2026-10-18 |
| [OQ-55](#oq-55-what-can-a-faction-template-change-and-how-is-a-faction-chosen) | What can a faction template change, and how is a faction chosen? | MP-009, MP-010, MP-005, MP-007, FB-021 | Decided | Owner | 2026-10-20 |
| [OQ-56](#oq-56-under-capture-to-build-what-counts-as-capturing-a-building) | Under capture to build, what counts as capturing a building? | MP-010, FB-023, WM-010 | Decided | Owner | 2026-10-20 |

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

**Decision:** No difficulty levels for now; difficulty is tuned by giving the AI a head start on certain maps. 2026-10-18: the head start comes from a scenario or from a generic preset (MP-008).

### OQ-48 How should the AI choose what to do?

**Options / notes:** Built: the first possible step on a fixed priority list. Options: priority list, behaviour trees, utility scoring, planners (GOAP, HTN). Common RTS bot practice (UAlbertaBot, Petra) is one manager per concern; utility scoring weighs competing goals better than a fixed order.

**Decision:** Per-concern modules proposing integer-scored actions, best scores first within a command budget; a strategic layer with an influence map over the tile graph sets tile goals, and a tactical layer acts inside tiles; units are commanded as squads through orders, stances and targeting modes (MP-006).

### OQ-50 How does the AI cope with content changes?

**Options / notes:** Built: modules name items, recipes and buildings by id, so every content change means AI code changes. Common practice in data-driven strategy games: find buildings by role, plan production backwards through the recipe graph from what is wanted, and let content carry optional AI weights (Paradox, Civilization).

**Decision:** Roles instead of ids, demand from the wanted designs and goals, backward chaining over the recipe graph, expansion towards missing deposits, value from stats, optional hints in content; the module framework stays (MP-007).

### OQ-55 What can a faction template change, and how is a faction chosen?

**Options / notes:** Can change: the subset (allow list, or deny list from the whole catalogue); variants (a variant is its own building in the catalogue that replaces a named base for that faction, so blueprints and the AI's roles map the base to the variant); rules (MP-010); start prefabs and head starts per faction; flat percentage modifiers (cost, HP, speed) per kind. Choosing: per seat in the lobby, random, or fixed by a scenario; the AI picks for its seat or is given one. A blueprint recorded by one faction and placed by another needs a rule for missing entries (refuse, or substitute variants). Leaning: subset, variants and rules first; modifiers later; per-seat choice with a random option; blueprints substitute variants and refuse the rest.

**Decision:** Subsets, variants and rules. A subset is the highlighted parts of a hidden master tech tree, whose entries are gated behind one another by resource, so a high-tier entry can't be highlighted without its prerequisites (MP-009). Buffs and modifiers are faction rules: a simple buff rule (MP-010). A faction is picked in the lobby before the match, Random included, or by the scenario (e.g. campaign). Blueprints are shared across factions; players add their own tags to filter out ones that don't work for their faction.

### OQ-56 Under capture to build, what counts as capturing a building?

**Options / notes:** Options: owning a standing one (lost again when it's destroyed or taken back), or having ever owned one (stays unlocked); taken by clearing a neutral site, by claiming a tile with enemy buildings left standing (OQ-45), or by moving one (FB-023); does a captured variant unlock its base, and does capturing a unit block count. Leaning: having ever owned one, by any of those, unlocks that exact building for good; variants and bases are separate.

**Decision:** Having ever owned one unlocks that exact building for good; variants and their bases are separate. Capture to build is only one rule among many; the others aren't added yet (MP-010).
