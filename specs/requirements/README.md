# Wunderwaffe 3037 · Requirements Specification

Factory-builder × RTS × world domination. Build megafactories on world-map tiles, design mega units block by block, conquer the map.

Audited against the codebase on 2026-10-10. Owner decisions on OQ-2..34 recorded the same day (OQ-25 is a proposal awaiting confirmation).

## How to use

- The Markdown in specs/requirements is the source: one file per major mechanic, then Misc (UI/UX, non-functional, project-wide questions) and the glossary. Each file has its requirements, then its decisions and open questions.
- specs/xlsx has one workbook per Markdown file, generated for viewing (python tools/specs.py build): overview.xlsx for this page, one per mechanic, glossary.xlsx. Edits made in a workbook are not lost: the next build stops, and specs.py diff lists them so they can be copied into the Markdown.
- IDs are two letters and three digits (WM-001); open questions are OQ-n. 'Depends On' lists IDs that must exist first. 'Pillar' links each row to the design pitch below. Priority uses MoSCoW.
- Status: Done = built and meets its acceptance criteria; In Progress = partly built; Approved = decided, not built; Proposed = not decided or built.
- Audit Flag: Conflict = what's built contradicts the requirement (needs a decision); Review = built differently or worth re-checking; Updated = text rewritten to match a decided build (old text in Notes); New = added from something already built.
- The summary tables are generated from the requirement tables on every build, here and in overview.xlsx.

## Audit findings (2026-10-10)

- Not up to date before this audit: every row was still 'Proposed'. Statuses and the As Built column now reflect the code.
- Conflicts needing a decision: units are assembled in place on frame, not designed in an editor and built by factories (UD-001, UD-006; OQ-16); units cross borders anywhere instead of through gates (IO-003; OQ-17).
- Updated to match what was decided and built: irregular territories instead of a square/hex grid (WM-001; OQ-1 decided); I/O zones instead of edge-to-edge ports (IO-001).
- To review: regional scarcity is per tile, not per biome (RS-003); load time grows with match length (NF-002); determinism is only 'Should' though lockstep and saves depend on it (NF-004).
- New rows for built features that had no requirement: WM-011..013, UD-010..012, UC-008, UI-006, NF-007. New open questions OQ-16..21.
- Outside the sheet: goals.json's 'Link your tiles' step still says 'the same lane', which no longer applies now that ports pair by zone.

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
| [World Map](world-map.md) | 11 | 2 | 2 | 1 | 16 | 8 | 2 | 50% | 0 | 0 |
| [Factory & Logistics](factory-logistics.md) | 14 | 3 | 1 | 0 | 18 | 4 | 2 | 22% | 0 | 0 |
| [Resources & Tech](resources-tech.md) | 7 | 1 | 2 | 0 | 10 | 3 | 1 | 30% | 0 | 1 |
| [Inter-Tile Links](inter-tile-links.md) | 3 | 3 | 0 | 0 | 6 | 1 | 3 | 17% | 0 | 0 |
| [Units & Wunderwaffe](units-wunderwaffe.md) | 12 | 4 | 3 | 0 | 19 | 3 | 2 | 16% | 0 | 1 |
| [Unit Command](unit-command.md) | 5 | 4 | 0 | 0 | 9 | 1 | 3 | 11% | 0 | 0 |
| [Combat & Protection](combat-protection.md) | 7 | 4 | 1 | 0 | 12 | 0 | 0 | 0% | 0 | 0 |
| [Multiplayer & Victory](multiplayer-victory.md) | 2 | 2 | 1 | 0 | 5 | 0 | 2 | 0% | 0 | 0 |
| [Misc](misc.md) | 9 | 5 | 1 | 0 | 15 | 4 | 8 | 27% | 0 | 2 |
| **Total** | 70 | 28 | 11 | 1 | 110 | 24 | 23 | 22% | 0 | 4 |

### Requirements by status

| Status | Count | % of Total |
|---|---|---|
| Done | 24 | 22% |
| In Progress | 23 | 21% |
| Approved | 4 | 4% |
| Proposed | 44 | 40% |
| Deferred | 2 | 2% |
| Rejected | 13 | 12% |

### Audit flags

| Flag | Count |
|---|---|
| Conflict | 0 |
| Review | 4 |
| Updated | 22 |
| New | 22 |

### Design pillar coverage

| Pillar | Name | Reqs | Must | Done |
|---|---|---|---|---|
| P1 | World map of tiles | 18 | 13 | 9 |
| P2 | Tile = RTS factory map | 19 | 15 | 4 |
| P3 | Inter-tile I/O | 9 | 4 | 1 |
| P4 | Block-by-block unit design | 15 | 10 | 3 |
| P5 | Squad command | 9 | 5 | 1 |
| P6 | No Rush | 6 | 4 | 0 |
| P7 | Resource-gated tech | 8 | 6 | 2 |
| P8 | World domination | 15 | 8 | 0 |

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
