# Wunderwaffe 3037 · Requirements Specification

Factory-builder × RTS × world domination. Build megafactories on world-map tiles, design mega units block by block, conquer the map.

Audited against the codebase on 2026-10-10, and again on 2026-10-17 (Built column). Owner decisions on OQ-2..34 recorded the same day (OQ-25 is a proposal awaiting confirmation). Revised after playtesting on 2026-10-17 (OQ-35..39 decided the same day). A review for unspecified mechanics the same day added OQ-40..48, all decided, and CB-007, FB-020, FB-021, MP-006.

## How to use

- The Markdown in specs/requirements is the source: one file per major mechanic, then Misc (UI/UX, non-functional, project-wide questions) and the glossary. Each file has its requirements, then its decisions and open questions.
- specs/xlsx has one workbook per Markdown file, generated for viewing (python tools/specs.py build): overview.xlsx for this page, one per mechanic, glossary.xlsx. Edits made in a workbook are not lost: the next build stops, and specs.py diff lists them so they can be copied into the Markdown.
- IDs are two letters and three digits (WM-001); open questions are OQ-n. 'Depends On' lists IDs that must exist first. 'Pillar' links each row to the design pitch below. Priority uses MoSCoW.
- Status: Done = built and meets its acceptance criteria; In Progress = partly built; Approved = decided, not built; Proposed = not decided or built.
- Built: Yes / Partial / No, what the code does as of the date in each file's 'Where it stands' line (also on the As Built header in the workbooks). Build checks it agrees with Status (Done = Yes, In Progress = Partial, anything else = No), so re-audit both together.
- Audit Flag: Conflict = what's built contradicts the requirement (needs a decision); Review = built differently or worth re-checking; Updated = text rewritten to match a decided build (old text in Notes); New = added from something already built.
- The summary tables are generated from the requirement tables on every build, here and in overview.xlsx.

## Audit findings (2026-10-10)

- Not up to date before this audit: every row was still 'Proposed'. Statuses and the As Built column now reflect the code.
- Conflicts needing a decision: units are assembled in place on frame, not designed in an editor and built by factories (UD-001, UD-006; OQ-16); units cross borders anywhere instead of through gates (IO-003; OQ-17).
- Updated to match what was decided and built: irregular territories instead of a square/hex grid (WM-001; OQ-1 decided); I/O zones instead of edge-to-edge ports (IO-001).
- To review: regional scarcity is per tile, not per biome (RS-003); load time grows with match length (NF-002); determinism is only 'Should' though lockstep and saves depend on it (NF-004).
- New rows for built features that had no requirement: WM-011..013, UD-010..012, UC-008, UI-006, NF-007. New open questions OQ-16..21.
- Outside the sheet: goals.json's 'Link your tiles' step still says 'the same lane', which no longer applies now that ports pair by zone.

## Playtest revisions (2026-10-17)

- Continuous zoom: the tile view pulls back smoothly into the world map past a zoom threshold instead of switching instantly (WM-007; OQ-36).
- World-map orders: selected units are pointed at any spot on the world map; they switch to abstract travel by themselves on crossing the border and keep it until they stop, then render again when their tile is opened. Passing through the rendered tile renders them without changing course; meeting enemies interrupts travel for a skirmish, and the stance decides whether they keep course or stop (UC-005, UC-009, UC-004).
- Bigger tiles: nominal tile 256x256 instead of 96x96, on the scale of Mindustry maps (WM-001, FB-001; OQ-35). Only the tile the player looks at runs the full sim (OQ-36).
- One core: the tile core and the unit core become one building with several variants, none free; players start with a deployed starting unit; cores on units count against elimination (WM-005, WM-016, UD-010, MP-001; OQ-37).
- Core-held funds: building on a tile spends only what the player's landed cores on that tile hold, paid in core order, up to each variant's capacity; no paying from a neighbour (FB-019, FB-007; OQ-38, OQ-39).

## Audit findings (2026-10-17)

- Every row now has a Built value, checked against the code (sim, game, tests, data) on this date. Statuses and As Built were far behind: silos A-G built most of what was Proposed, so 49 rows moved to Done (24 to 73 of 111).
- Still not built: WM-010 neutral tiles, RS-004 depletion, IO-004 long-range links, UD-008 test range, UD-009 design sharing, WW-003 multi-tile supply chain, WW-005 and NR-006 balance tests, MP-004 diplomacy, NF-006 modding; fluids and salvage deferred.
- Partial: the five playtest changes, UI-004 tutorial, MP-003 (two-peer online only), NF-005 (no packaged build).
- RS-003 keeps its Review flag: only coal is regional, so the 'one per tier' criterion isn't met above tier 1.

## Mechanics

- [World Map](world-map.md)
- [Factory & Logistics](factory-logistics.md)
- [Resources & Tech](resources-tech.md)
- [Inter-Tile Links](inter-tile-links.md)
- [Units & Wunderwaffe](units-wunderwaffe.md)
- [Unit Command](unit-command.md)
- [Combat & Protection](combat-protection.md)
- [Multiplayer & Victory](multiplayer-victory.md)
- [Misc](misc.md)

See also the [glossary](glossary.md).

## Summary

<!-- BEGIN generated summary: tools/specs.py build rewrites this block -->

### Requirements by mechanic

| Mechanic | Must | Should | Could | Won't | Total | Done | In Progress | % Done | Conflict | Review |
|---|---|---|---|---|---|---|---|---|---|---|
| [World Map](world-map.md) | 11 | 2 | 2 | 1 | 16 | 13 | 0 | 81% | 0 | 2 |
| [Factory & Logistics](factory-logistics.md) | 17 | 3 | 1 | 0 | 21 | 17 | 0 | 81% | 0 | 2 |
| [Resources & Tech](resources-tech.md) | 7 | 1 | 2 | 0 | 10 | 4 | 0 | 40% | 0 | 1 |
| [Inter-Tile Links](inter-tile-links.md) | 3 | 3 | 0 | 0 | 6 | 5 | 0 | 83% | 0 | 0 |
| [Units & Wunderwaffe](units-wunderwaffe.md) | 12 | 4 | 3 | 0 | 19 | 12 | 1 | 63% | 0 | 2 |
| [Unit Command](unit-command.md) | 5 | 4 | 0 | 0 | 9 | 7 | 2 | 78% | 0 | 0 |
| [Combat & Protection](combat-protection.md) | 7 | 5 | 1 | 0 | 13 | 6 | 0 | 46% | 0 | 0 |
| [Multiplayer & Victory](multiplayer-victory.md) | 3 | 2 | 1 | 0 | 6 | 3 | 2 | 50% | 0 | 0 |
| [Misc](misc.md) | 9 | 5 | 1 | 0 | 15 | 12 | 2 | 80% | 0 | 2 |
| **Total** | 74 | 29 | 11 | 1 | 115 | 79 | 7 | 69% | 0 | 9 |

### Requirements by status

| Status | Count | % of Total |
|---|---|---|
| Done | 79 | 69% |
| In Progress | 7 | 6% |
| Approved | 4 | 3% |
| Proposed | 10 | 9% |
| Deferred | 2 | 2% |
| Rejected | 13 | 11% |

### Audit flags

| Flag | Count |
|---|---|
| Conflict | 0 |
| Review | 9 |
| Updated | 25 |
| New | 23 |

### Design pillar coverage

| Pillar | Name | Reqs | Must | Done |
|---|---|---|---|---|
| P1 | World map of tiles | 18 | 13 | 15 |
| P2 | Tile = RTS factory map | 21 | 17 | 17 |
| P3 | Inter-tile I/O | 10 | 5 | 6 |
| P4 | Block-by-block unit design | 15 | 10 | 11 |
| P5 | Squad command | 10 | 5 | 8 |
| P6 | No Rush | 6 | 4 | 0 |
| P7 | Resource-gated tech | 8 | 6 | 3 |
| P8 | World domination | 16 | 9 | 11 |

<!-- END generated summary -->

## Design pillars

| Pillar | Name | Pitch Statement |
|---|---|---|
| P1 | World map of tiles | The game world map is separated into tiles; you start on one tile you occupy and expand. |
| P2 | Tile = RTS factory map | Each tile is a real-time strategy map in its own right (like Mindustry) where you build megafactories. |
| P3 | Inter-tile I/O | Tiles interact with the world through I/O of resources and units to other tiles. |
| P4 | Block-by-block unit design | Units, including mega 'Wunderwaffe' units, are customised block by block and built in factories. |
| P5 | Squad command | Players command units or squadrons of units that act as a team. |
| P6 | No Rush | Early build-up is protected by heavily buffed defences. |
| P7 | Resource-gated tech | Tech tree advances by acquiring higher-level resources, not science points. |
| P8 | World domination | Players compete for world map domination. |
