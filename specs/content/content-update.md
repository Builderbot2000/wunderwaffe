# Content & balance update: draft

Status: **draft for discussion** (2026-10-18). Nothing here is in `game/data` yet. Once approved, the tables below
become the JSON, and the requirement rows (RS-003 review, WW-003, NR-006, WW-005, OQ-13 campaign) move into
`specs/requirements/`.

Decided with the owner, 2026-10-18:
- Progression stays **cost-gated only** (TT-001): no unlocks, in skirmish or campaign. The tier ladder of costs is the
  progression. A read-only codex shows it.
- Campaign: a **tutorial arc of 7 missions** (OQ-13), with light story.
- Scale: **medium**, about 25 items, 65 buildings, 32 recipes, 10 prefabs, 4 tiers. *Superseded by CD-008: counts are
  reported, not targets.*
- Setting: **far-future sci-fi**, post-collapse.

Also decided, 2026-10-18 (second round):
- **The item catalogue is being renamed by the owner.** The item tables below use placeholder names. The **slot table** in
  §2 is the agreed structure; names map onto it.
- **Relic components (R8)**: a tier 4 co-gate, scrapped from relic blocks the way wrecks are cleared (§2.1).
- **Balance yardstick**: raw value + machine time (§6.1).

The tables are also in workbooks in `specs/content/`, one per topic:
- **`resources.xlsx`**: the working balance model. Item values are formulas (§6.1), each recipe gets a tier check, and
  there is a column for the new names.
- `buildings.xlsx`: every building, with its cost value and tier.
- `units.xlsx`: the prefabs.
- `campaign.xlsx`: the missions.
- `content-design.xlsx`: an index of the workbooks, plus pacing and open questions.

All numbers are a first pass. `tools/balance.py` (below) is where they get tuned.

**2026-10-21:** the content design principles (`specs/requirements/content-design.md`, CD-001..009) supersede these
parts: the scale above (CD-008), the "Found on" column of the §2 tier table and §8 item 4, which put placement on
items (CD-007: placement is map design). Names were changed to pass CD-009 (OQ-62).

`catalogue.md` extends this draft. It adds fluids (water, crude oil, pipes), the item tech tree, the
vanilla faction templates and grand strategy pacing. It replaces the recipe table in §2 and the pacing table in §6. The
workbooks don't have those changes yet.

## 1. Setting

Decided with the owner, 2026-10-18 (third round):
- The premise and the Corps are as below.
- The Corps come from off-world, on charters.
- There is one campaign colony.
- Survivors are a **neutral guardian faction** at claimable ruins.
- **"Wunderwaffe" is the game's label only**: no character in the world uses the word, and no hull or document carries
  it.

Names marked *(wn)* are working names.

### 1.1 Premise

Centuries ago a united interstellar civilisation, the **Concord** *(wn)*, settled and terraformed dozens of worlds,
linked by relay gates. Its end came in an apocalyptic civil war, the **Sundering** *(wn)*. The war was fought at the
end with colony-scale war machines. It destroyed the relays and left every colony on its own.

3037: a few worlds have rebuilt relay links. A surviving authority sells **reclamation charters** for the dead
colonies. The players are **Reclamation Corps**, chartered companies that drop onto a ruined colony to claim its
surviving infrastructure and resources before their rivals do.

The campaign colony is **Halcyon** *(wn)*. Its terraforming was left half-finished: climates drift, its seas are
brine, its craters are glassed, and the Sundering's wrecks lie across it.

### 1.2 How the mechanics fit the lore

| Mechanic | In the world |
|---|---|
| Tiles | survey parcels of the Concord land registry |
| Victory circles, claim timers | registry beacons. The registry still honours claims, and the timer is the beacon's handshake with your charter. |
| Cores, funds, elimination | a Corps' charter nodes. When your last node goes, your charter lapses. |
| Ports, I/O zones | old transit conduits along the parcel borders, still running |
| The starting scout | the Corps' drop vessel, landed from orbit |
| Rare-earth ore (R7) | deep ore laid bare in the craters of the war machines' heaviest weapons. Rare, far from the starts. |
| Relic components (R8) | war-tech salvaged from relic hulks. The Corps can copy the machines but can't yet make their key parts. |
| Relic hulks | the Sundering's wrecked war machines and dead terraforming towers |
| Claimable ruins | surviving Concord installations, guarded by survivors (§1.3) |
| Depletion | a colony's reserves are finite, and the Corps strip them |
| Wunderwaffe | out of the world, the game's name for its biggest units. In the world, the war machines have no single name. Each hull has its own designation and its own fragments. |

### 1.3 Survivors: the guardian faction

Descendants of the colonists who stayed, called the **Keepers** *(wn)* by the Corps. They don't expand, attack or
claim. They **guard the claimable ruins**, the working Concord installations, against anyone who comes too close.

- **On the map:** a neutral seat that owns the defences at each ruin. It never wins or loses and isn't counted in
  victory. Its turrets fire on any Corps unit in range.
- **Claimable ruins:** a ruin is a pre-built installation, such as an old foundry with its boosters, a power plant, a
  relay mast or a vault of stock. Beat its guardians and hold its beacon, and the ruin's buildings become yours.
  These are the "surviving infrastructure" the Corps fight over.
- **Show, don't tell:** the Keepers never speak in cutscenes. You learn about them from what's around the ruins and
  from their own fragments: shrines built from hull plates, maintenance rotas centuries long, a child's drawing of a
  relay gate.

### 1.4 Narrative delivery: fragments

Lore reaches the player in fragments, never by exposition. The rules:

- **Every fragment is an object inside the world**, with an author, a date and a purpose: a log, an order, a catalogue
  entry, a transmission, an inscription.
- **There's no narrator**, no opening crawl and no lore dump at loading.
- **Voices disagree.** Corps marketing, Concord records, soldiers' logs and the Keepers can contradict each other, and
  the game never says who's right.
- **The codex lists fragments in the order found**, not in history's order, so the player assembles the timeline
  themselves.

| Channel | Example | Code needed |
|---|---|---|
| Hulk fragments | Clearing a relic hulk also recovers a fragment: a last transmission, a crew list, a maintenance order. | fragment id on each hulk, an event, a codex entry |
| Ruin fragments | Claiming a ruin recovers the installation's records and Keeper traces. | as above, on ruins |
| Codex flavour | One line per item, building and prefab in the voice of the Corps' field catalogue. | text fields in JSON |
| Tile names | Colonial names worn down by centuries, e.g. "Halden's Reach" → "Haldreach". | a rule in `TileNames` |
| Campaign | Briefings as Corps memos with redactions, debriefs as internal reports, rival transmissions intercepted mid-mission. | campaign shell |
| The map | Glassed craters at rare-earth sites, dead terraforming towers as hulks, a hulk lying in a victory circle. | world-gen placement |
| Goal text | Operational orders in the Corps' voice, not tutorial prose. | rewrite `goals.json` |

### 1.5 Corps

All seats are mechanically the same. Corps names are cosmetic: a name and colours per seat, picked or assigned. The
campaign's player Corps and its rival **Kaskade Recovery** *(wn)* have voices of their own in briefings and intercepted
transmissions.

Item and building names stay readable, with sci-fi only where it helps. The owner is renaming the catalogue (§2).

## 2. Material ladder

Every raw item is tier 1, because extraction can only produce tier 1 (RS-001 validation). Scarcity comes from **where**
the raw items are: each tier above 1 needs at least one regional raw item, which satisfies RS-003 as written, and tier 4
needs one from far away (WW-003).

| Tier | Gate (regional input) | Found on |
|---|---|---|
| 1 | none | iron and copper ore and sand are on every start |
| 2 | **coal** (graphite, fuel) and **titanium ore** | coal: forest, grassland, hills. Titanium: mountain. |
| 3 | **brine** (lithium; batteries, missiles via fuel) | shallow water, pumped from a floating platform |
| 4 | **rare-earth ore** and the **relic components** | rare-earth ore: rare, mountain and desert, never within 2 tiles of a start. Relic components: relic hulks (§2.1). |

### Slots (the agreed structure; names to come)

| Slot | Tier | Source | Role | Placeholder |
|---|---|---|---|---|
| R1 | 1 | common | structural metal ore | iron ore |
| R2 | 1 | common | conductive metal ore | copper ore |
| R3 | 1 | common | silicate | sand |
| R4 | 1 | regional | carbon | coal |
| R5 | 1 | regional | light-metal ore | titanium ore |
| R6 | 1 | regional, water | fluid feedstock | brine |
| R7 | 1 | rare, far | magnet metals | rare-earth ore |
| R8 | 1 | scrapped relic blocks, finite per world | relic components | (new) |
| P1-P3 | 2 | R1, R2, R3 | structural plate, conductor plate, glass | iron/copper plate, glass |
| P4 | 2 | R4 | refined carbon | graphite |
| P5 | 2 | R5 + P4 | light alloy plate | titanium plate |
| P6, P7 | 2 | R6 | energy-storage salt, fusion isotope | lithium, deuterium |
| P8 | 2 | R4 | liquid fuel | fuel |
| A1 | 2 | P1 | basic ammo (kinetic) | shell |
| C1 | 3 | P1 + P4 | structural alloy | steel |
| C2 | 3 | P2 + P3 | logic component | circuit |
| C3 | 3 | C1 + P5 | composite armour | composite |
| C4 | 3 | P6 + P2 | energy storage | battery cell |
| A2, A3 | 3 | C1 + P5; C1 + P8 + C2 | penetrator ammo, guided ammo | rail slug, missile |
| X1 | 4 | R7 + C2 | lossless conductor (magnets) | superconductor |
| X2 | 4 | X1 + **R8** + C2 + C3 | Wunderwaffe power core | reactor core |

Value fixes held until naming is done: composite armour 1 + 1 (value 21 → 14); superconductor 6 rare-earth ore (28 → 40);
a name for the liquid fuel that isn't another "cell". (The energy ammo is dropped, OQ-62.)

### 2.1 Relic components (R8)

- **Source:** relic hulks. These are wrecks (CB-006) of the Sundering's war machines and terraforming towers, seeded by world generation and by
  scenarios.
- **Recovery:** clearing a hulk yields R8 into the nearest own core, the same way clearing a wreck yields a share of its
  cost. Nothing regenerates, so the world's R8 is fixed at generation, and ruins become late-game objectives to fight
  over.
- **Use:** tier 4 co-gate. The Wunderwaffe system component (X2) and Wunderwaffe-only blocks (the Particle Beam, the
  Large Shield Generator) need R8 as well as the rare-earth ore chain (X1). A Wunderwaffe therefore needs both a rare-earth tile
  and ruins, and these are usually different places (WW-003).
- **Generation (first pass):** hulks on about 25% of non-start tiles, more of them farther from the starts. Each hulk is
  a multi-cell wreck worth 20-60 R8. Never on a start tile. A scenario places them by hand.
- **Scarcity weight:** 8, the highest of any item, because the supply is finite.

### Items (placeholder names)

| Id | Name | Tier | Regional | Notes |
|---|---|---|---|---|
| `iron_ore` | Iron Ore | 1 | | |
| `copper_ore` | Copper Ore | 1 | | |
| `sand` | Silica Sand | 1 | | desert, beach, plains |
| `coal` | Coal | 1 | yes, 35% | as today |
| `titanium_ore` | Titanium Ore | 1 | yes, 30% | needs a Heavy Drill |
| `brine` | Brine | 1 | yes, 40% of tiles with shallow water | deposit on water cells (code, §8) |
| `rare_earth_ore` | Rare-earth Ore | 1 | yes, 10%, at least 2 tiles from any start | deposit `perCell` 80 (scarce) |
| `iron_plate` | Iron Plate | 2 | | |
| `copper_plate` | Copper Plate | 2 | | |
| `glass` | Glass | 2 | | |
| `graphite` | Graphite | 2 | | from coal |
| `titanium_plate` | Titanium Plate | 2 | | |
| `lithium` | Lithium | 2 | | from brine |
| `deuterium` | Deuterium | 2 | | from brine, fusion fuel |
| `fuel` | Fuel Cell | 2 | | as today |
| `shell` | Shell | 2 | | ammo, kinetic |
| `steel` | Steel | 3 | | now needs graphite, not coal |
| `circuit` | Circuit | 3 | | |
| `composite` | Composite | 3 | | armour, heavy frames |
| `battery_cell` | Battery Cell | 3 | | |
| `missile` | Missile | 3 | | ammo, explosive |
| `rail_slug` | Rail Slug | 3 | | ammo, kinetic, high damage |
| `superconductor` | Superconductor | 4 | | |
| `reactor_core` | Reactor Core | 4 | | Wunderwaffe systems |

### Recipes

Ticks are at 60 per second.

| Building | Recipe | Inputs → outputs | Ticks |
|---|---|---|---|
| Drill | mine iron, copper, sand, coal | → 1 | 60 |
| Heavy Drill | mine iron, copper, sand, coal | → 1 | 30 |
| Heavy Drill | mine titanium | → 1 titanium ore | 60 |
| Brine Pump | pump brine | → 2 brine | 60 |
| Rare-earth Bore | mine rare-earth ore | → 1 rare-earth ore | 180 |
| Smelter | copper plate, iron plate, glass | 2 ore or sand → 1 | 120 |
| Coking Oven | graphite | 2 coal → 1 | 120 |
| Fuel Press | fuel (coal) / fuel (copper, the poor fallback) | 1 coal → 2, or 2 copper ore → 1 | 60, or 120 |
| Arc Furnace | titanium plate | 2 titanium ore + 1 graphite → 1 | 180 |
| Arc Furnace | fast iron plate / fast copper plate | 2 ore → 1 | 60 |
| Electrolyser | lithium | 4 brine → 1 | 180 |
| Electrolyser | deuterium | 8 brine → 1 | 300 |
| Steel Mill | steel | 2 iron plate + 1 graphite → 1 | 240 |
| Assembler | circuit | 1 copper plate + 1 glass → 1 | 120 |
| Assembler | battery cell | 2 lithium + 1 copper plate → 1 | 240 |
| Alloy Forge | composite | 1 steel + 2 titanium plate → 1 | 300 |
| Munitions Works | shells | 1 iron plate → 5 | 60 |
| Munitions Works | rail slugs | 1 titanium plate + 1 steel → 4 | 180 |
| Munitions Works | missiles | 1 steel + 1 fuel + 1 circuit → 2 | 240 |
| Superconductor Works | superconductor | 4 rare-earth ore + 1 circuit → 1 | 360 |
| Precision Works | reactor core | 1 superconductor + 2 circuit + 1 composite → 1 | 600 |
| Fuel Generator | burn fuel | 1 fuel | 240 |
| Fusion Reactor | burn deuterium | 1 deuterium | 1200 |

Rough raw-ore value per item: tier 2 ≈ 2-3, tier 3 ≈ 6-10, tier 4 ≈ 40-60. A Wunderwaffe should cost around 25-40x a
tier 2 gunship.

## 3. Buildings (≈ 66; 41 today)

Tier = the highest tier in the building's cost. **New** = a new building, and it reuses an existing `kind` unless §8
says otherwise.

### Production (15; 5 today)

| Building | Size | Tier | Power | Cost (indicative) | Notes |
|---|---|---|---|---|---|
| Drill | 2x2 | 1 | 2 | 10 copper ore | as today, plus sand |
| Heavy Drill **new** | 3x3 | 3 | 12 | 20 steel, 20 iron plate | 2x speed; the only drill for titanium |
| Brine Pump **new** | 2x2 | 2 | 4 | 15 iron plate, 10 copper plate | on a platform over a brine deposit |
| Rare-earth Bore **new** | 3x3 | 3 | 40 | 30 composite, 20 circuit | slow; the T4 gate |
| Smelter | 2x2 | 1 | 3 | as today | plus glass |
| Coking Oven **new** | 2x2 | 1 | 2 | 20 iron ore, 10 sand | |
| Fuel Press | 2x2 | 1 | 2 | as today | |
| Arc Furnace **new** | 3x3 | 3 | 30 | 30 steel, 20 copper plate | titanium and fast plates |
| Electrolyser **new** | 2x2 | 2 | 15 | 20 titanium plate, 10 glass | |
| Steel Mill | 3x3 | 2 | 20 | as today | |
| Assembler **new** | 2x2 | 2 | 8 | 20 iron plate, 15 copper plate, 10 glass | circuits, battery cells |
| Alloy Forge **new** | 3x3 | 3 | 25 | 30 steel, 20 titanium plate | |
| Munitions Works | 2x2 | 2 | 4 | as today | plus slugs and missiles |
| Superconductor Works **new** | 3x3 | 3 | 50 | 40 composite, 30 circuit | |
| Precision Works **new** | 4x4 | 4 | 80 | 20 superconductor, 40 composite | |

### Power (8; 4 today)

| Building | Tier | Supply | Notes |
|---|---|---|---|
| Cable, Power Node, Battery, Fuel Generator | as today | | |
| Solar Array **new** | 3 | 15, constant | 3x3, glass and circuits; no fuel, weak |
| Substation **new** | 3 | | `power_node`, radius 20 |
| Capacitor Bank **new** | 3 | | `battery`, holds 6000, battery cells |
| Fusion Reactor **new** | 4 | 400 | 4x4, burns deuterium; reactor core in its cost |

### Logistics and cores (16; 13 today)

| Building | Tier | Notes |
|---|---|---|
| Conveyor | 1 | speed 4 |
| Fast Conveyor **new** | 2 | speed 8, iron plate + graphite |
| Maglev Conveyor **new** | 3 | speed 16, titanium plate + circuit |
| Junction, Router, Sorter, Bridge | 1 | as today |
| Long Bridge **new** | 3 | `bridge`, range 10 |
| Export Port, Import Port | 1 | as today |
| Container | 2 | as today |
| Vault **new** | 3 | `storage` 2000, 3x3 |
| Floating Platform | 1 | as today |
| Core, Large Core | as today | |
| Bastion Core **new** | 3 | 4x4, capacity 20000, power supply 60, HP 4000; the late-game capital |

### Combat (15; 7 today)

The damage types are **kinetic** (shells, slugs), **explosive** (missiles) and **energy** (lasers, particle beam; they run on power, no ammo). Each tier's
armour resists the weapons of the tier below it, which is how the defence/offence crossover (NR-006) is built in.

| Building | Tier | Type | Range | Role |
|---|---|---|---|---|
| Gun Turret | 2 | kinetic | 10 | cheap early defence (as today) |
| Autocannon **new** | 2 | kinetic | 9 | fast fire, chews up light units |
| Laser Turret | 3 | energy | 12 | no ammo, power-hungry (as today) |
| Missile Battery **new** | 3 | explosive | 18 | outranges T2 turrets: the offence tool |
| Railgun **new** | 3 | kinetic | 22 | slow, high damage, anti-armour |
| Particle Beam **new** | 4 | energy | 14 | huge damage per shot; Wunderwaffe main gun |
| Wall | 2 | | | as today |
| Titanium Wall **new** | 2 | | | 1500 HP, kinetic 50 |
| Composite Wall **new** | 3 | | | 3000 HP, kinetic 60, explosive 40 |
| Shield Generator | 3 | | | as today |
| Large Shield Generator **new** | 4 | | | `shield`, radius 14, capacity 4000 |
| Repair Bay, Supply Depot, Ammo Rack | | | | as today |

Every weapon can also go on a unit, as today (UD-002).

### Unit parts (12; 7 today)

| Block | Tier | Notes |
|---|---|---|
| Unit Frame, Core, Thruster, Wheels, Hull, Rotor, Unit Factory | as today | |
| Ion Thruster **new** | 3 | thrust 150 at the same mass; titanium plate + circuit |
| Tracks **new** | 3 | `locomotor` ground; heavy, high HP |
| Armour Plate **new** | 2 | light 1x1 block: 300 HP, kinetic 30; titanium |
| Heavy Armour **new** | 3 | 1x1: 800 HP, kinetic 50, explosive 30; composite |
| Cargo Pod **new** | 2 | adds cargo room (verify how cargo room is derived today, §8) |

### Boosters (9; 8 today)

The current eight stay. One is new: **Flux Accelerator** (tier 4, 2x2, +50% speed for crafters with a reactor core in
their cost), a late-game reason to keep making reactor cores. Its name fails CD-009 and is to be replaced.

## 4. Prefabs (10; 2 today)

These are reference designs for each role. The AI picks from them by role and tag (MP-007), and players get them in the
library.

| Id | Role | Tier | Size (cells) | Make-up |
|---|---|---|---|---|
| `scout` | start, expansion | 2 | 8 | as today |
| `depot` | head start | | | as today |
| `rover` | early raider | 2 | 6 | wheels, core, gun turret |
| `hauler` | cargo | 2 | 10 | wheels, core, 2 cargo pods |
| `gunship` | air skirmisher | 3 | 12 | thrusters, core, 2 autocannons |
| `patrol_boat` | naval | 2 | 10 | hull, core, gun turret, armour |
| `lancer` | air anti-armour | 3 | 14 | rotor, core, missile battery, armour |
| `bulwark` | ground tank | 3 | 24 | tracks, core, railgun, heavy armour |
| `leviathan` | **Wunderwaffe** | 4 | 56 | tracks, core, 2 particle beams, large shield generator, heavy armour, fusion reactor |
| `smelting_line` | factory layout | 1 | | drill, belts, smelter: a starter layout prefab (not a unit) |

## 5. Campaign: "Halcyon" *(wn)* (7 missions)

Each mission is a scenario (WM-017) with its own goal chain, a win condition and a lose condition. There are no build
restrictions (TT-001). What a mission teaches comes from the map, the starting stock and the deposits.

| # | Mission | Map | Teaches | Win | Lose |
|---|---|---|---|---|---|
| 1 | **Reboot** | 1 tile | core, drill, belts, smelting | 100 iron plate made | none |
| 2 | **Beacon** | 3 tiles | lift-off, world map, claiming, ports, coal → graphite, fuel power | claim the coal tile, ship 30 | none |
| 3 | **Foundry** | 3 tiles | steel, glass, circuits, a hand-built unit, the unit factory | factory builds 2 units | none |
| 4 | **Hold the Line** | 4 tiles, scripted attacker | turrets, walls, ammo logistics, shields, repair | survive 12 min | lose the home core |
| 5 | **First Strike** | 5 tiles, an outpost AI | blueprints, prefabs, squads, stances, missiles vs turrets | destroy the outpost's cores | all units and cores lost |
| 6 | **Deep Reserves** | 9 tiles, water | titanium, platforms, brine, boats, a multi-tile chain, depletion, solar | 20 composite + 20 battery cells made | eliminated |
| 7 | **Wunderwaffe** | 12 tiles, 2 AIs | rare-earth ore far away, megafactory boosters, fusion, the Leviathan | eliminate both rivals or hold 75% of tiles | eliminated |

Story is told in fragments (§1.4): the briefing is a Corps memo, the debrief an internal report. The rival Corps,
**Kaskade Recovery**, is heard in intercepted transmissions in missions 4, 5 and 7. All missions are on Halcyon, going
deeper into its ruins as the campaign goes on. Mission titles are in the Corps' voice. Mission 7's working title,
"Wunderwaffe", is the game's label, not an in-world word. After the campaign, skirmish keeps a general goal chain as guidance, extended to tier 4.

## 6. Balance targets

**Pacing** for skirmish on a normal map at normal richness, played well:

| Milestone | Minute |
|---|---|
| tier 2 production running | 2-4 |
| first expansion claimed | 6-9 |
| tier 3 (steel + circuits) | 12-18 |
| first factory-built unit | 15-20 |
| tier 4 (superconductor) | 30-40 |
| first Wunderwaffe | 50-70 |
| typical match end | 60-90 |

**NR-006 (No Rush):**
- At tiers 1-2, defence of cost X beats any army of cost X. Gun turrets and walls are cheap, and thrusters already cost
  steel.
- From tier 3 an equal-cost army wins. Missile Batteries outrange tier 2 turrets, and heavy armour shrugs off shells.
- Turtles still have to climb: Composite Walls and Railguns answer missiles, at tier 3 cost.

**WW-005 (Wunderwaffe impact):**
- A Leviathan beats equal-cost tier 3 ground and naval units and tier 3 static defences.
- It loses to an equal cost of Lancers (air with missiles, which it can't chase) and to Railgun lines behind a Large
  Shield Generator.
- Upkeep (UD-013) is about 5% of its cost each minute, so you can't keep two.

### 6.1 Value yardstick (decided)

`value(item) = (sum of input values + machine-seconds × (m + power × p)) / outputs`

- Raw items start at their scarcity weight: common 1, coal 1.5, titanium 2, brine 0.5, rare-earth ore 6, R8 8.
- First-pass constants: `m` = 1/20 per machine-second, `p` = 1/2000 per power-second.
- Targets: each tier roughly doubles the one below. Bands are about 2 / 4-7 / 4-14 / 20-60.

**Economy rules:**
- Each tier's buildings mostly cost the tier below.
- No recipe nets a loss of raw value.
- Each regional item is on 30-40% of tiles, except rare-earth ore at 10%.
- Every start has a neighbour with coal (as today) and a tile with titanium within 2 tiles.

## 7. Tooling and tests

- **`tools/balance.py`**: reads `game/data`, then prints:
  - raw value per item;
  - the machine counts for a target rate;
  - the cost per tier of every building;
  - cost efficiency (HP × DPS / cost) for every combat building and prefab.
  It also checks the economy rules in §6. It runs alongside `specs.py`.
- **Sim tests:**
  - `BalanceTests`: NR-006 fights of an equal-cost army against defences, early and at tier 3, and WW-005 matchups,
    all set up through Harness.
  - `CampaignTests`: every mission loads, its goals can be completed, and the win and lose conditions fire.
  - `DemoLoopTests` and `AiTests` move to the new chain. The AI plays skirmish and its tier timings are checked against
    §6, within a margin.
- **Content validation:** a check per §6 rule (regional items present, the tier gate holds).

## 8. Code work this needs

The content is mostly JSON, but these need code:

1. **Per-scenario goals and mission outcome**: `ScenarioDef.Goals` (or a goal-chain id), and win/lose conditions
   evaluated in `Victory`.
2. **New goal condition types**: `survive` (ticks), `destroy` (enemy cores/buildings of a seat), `eliminate`.
3. **Campaign shell** (game side): `campaign.json` (mission order, scenario, briefing, debrief), progress in
   `user://campaign.cfg`, mission select, briefing and debrief screens on the title screen.
4. *Superseded by CD-007: these settings belong to map generation and scenarios, not items.* **Regional settings per
   item**: `regional: { chance, minStartDistance }` in items.json, replacing the global
   `Balance.RegionalChancePercent`. The rare-earth ore distance rule.
5. **Deposits on water**: deposit generation counts only buildable cells today. Brine needs water cells, and an
   extractor on a platform needs to find the deposit under the platform.
6. **Relic hulks**: seeded wrecks at world generation and in scenarios (a `relics` field per scenario tile), clearing
   them yields R8 (a wreck yield item), hashed and snapshotted like wrecks.
7. **Codex** (UI): a read-only browser of items, recipes and buildings by tier, which is how cost-gated progression
   shows the ladder. It also holds the **fragments** found, in the order found: a per-player list in sim state, hashed.
8. **Guardian faction and claimable ruins** (§1.3):
   - a neutral seat (never wins or loses, ignored by victory and the AI director's expansion);
   - ruin sites placed at world generation and in scenarios: pre-built buildings owned by the neutral seat, plus a
     beacon;
   - claiming a ruin transfers its buildings to the claimer.
9. **Fragments**: a `fragments.json` (id, author, date, text); hulks and ruins name a fragment; recovering it raises an
   event.
10. **Check**: how cargo room is derived (cargo pod), and the "reactor core in cost" host test for the Flux Accelerator,
   which could be a booster `category` instead.
11. **Sprites**: about 35 new building sprites through the art pipeline (`art/jobs.json`). Colour fallbacks work
   until then.

## 9. Open questions

| # | Question | Leaning |
|---|---|---|
| C-1 | Rename `coal` to `carbon` for the setting? | keep `coal`: readable, and ids are already opaque to the AI |
| C-2 | Should a Wunderwaffe have a build cap per player (one at a time) on top of upkeep? | no, upkeep is enough |
| C-3 | Campaign difficulty settings? | one difficulty for the tutorial arc |
| C-4 | Fluids as items (brine on belts) or a pipe layer (FB-022 makes one cheap)? | pipe layer, see `catalogue.md` §1.1 and K-2 |
| C-5 | Should mission 7's Leviathan be pre-built as the enemy's, so the player sees one before building their own? | yes, Kaskade fields one at minute 30 |
| C-6 | Split the work into silos as before (content JSON, campaign code, balance tooling/tests, art)? | yes, after this draft is approved |
| L-1 | Final names for the Concord, the Sundering, Halcyon, the Keepers and Kaskade Recovery? | owner, with the catalogue renaming |
| L-2 | Narrative spine: do the fragments slowly show that the Corps, by rebuilding the war machines, are repeating the Sundering? | proposed; unconfirmed |
| L-3 | Keepers: only hostile at their ruins, or can a Corps ever trade with them or leave them in peace? | hostile at ruins only, for now |
| L-4 | Do claimed ruins count as tiles for victory, or are they sites inside a tile? | sites inside a tile, each with its own beacon |
| L-5 | Is the calendar the Concord's (3037 = years since founding) or the Corps'? | Concord calendar; the Sundering c. 2790-2810 |
