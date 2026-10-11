# Wunderwaffe 3037: guide for Claude

2D factory-builder RTS, proof-of-concept stage. Godot 4.7.2 .NET (C#, net8.0) for presentation over a pure-C#
deterministic simulation. Requirement ids in comments (WM-xxx, FB-xxx, NF-xxx, OQ-xx...) refer to
`Wunderwaffe_3037_Requirements.xlsx`. Player-facing behaviour and design notes are in `README.md`.

**Use the file index below before opening code.** It says where everything lives and what each file owns, so
read only the files a change touches. When you add, move, split or delete a file, or change what a file owns,
update `.claude/index.md` in the same change. Keep entries one line where possible.

@index.md

Agent-team work (silos A-G): also follow `.claude/team.md` (tick order, file ownership, shared-hotspot rules).

## Commands

```
dotnet build Wunderwaffe.sln                    # sim + game + tests; should be 0 warnings
dotnet test tests/Wunderwaffe.Sim.Tests         # ~140 xUnit tests, ~10 s, uses real game/data JSON
GODOT="/c/Users/Public/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
"$GODOT" --path game --headless -- --stress     # sim benchmark, prints ms/tick, quits (no window)
"$GODOT" --path game -- --tour <out_dir>        # UI smoke test: clicks through every screen, PNG per step (opens a window, moves the mouse)
"$GODOT" --path game -- --drive [shot.png]      # scripted unit lift-off and move via real input (opens a window)
```

Verify sim changes with `dotnet test`. Verify Godot-side changes with a build, then `--tour` and look at the
screenshots (`[tour] FAIL` lines mean a step broke). There are no automated tests for `game/`.

## Architecture

```
game/ (Godot, namespace Wunderwaffe.Game.*)            src/Wunderwaffe.Sim (pure C#, no Godot)
  SimHost autoload ── owns ──────────────────────────▶  Simulation
    fixed 60 Hz tick loop, view mode, selection           Step(): commands → TileEntities per tile
  render/  reads sim state, draws                                 → UnitPhysics → Progression → Tick++
  input/   turns mouse/keys into Commands ── Enqueue ──▶  Command.Apply → Simulation.Try*/UnitAssembly/UnitSim
  ui/      HUD + menus, reads sim, sends Commands         read-only checks (Placement.Check, Territory.HasPresence,
  debug/   cheats, benchmark, scripted runs                 UnitAssembly.Check*) shared by UI preview and Apply
game/data/*.json ── text ──▶ ContentDb.FromJson (validated) ◀── tests load the same files
```

- **Sim is authoritative and deterministic** (lockstep multiplayer and replay-based saves). It changes only through
  `Command`s queued with `Simulation.Enqueue` and applied at the start of the next `Step`. A save is
  `GameSetup` + the command log; loading replays it and compares `StateHash`.
- **World model**: one global cell grid (`WorldMap`) generated in phases (`World/Gen`), cut into irregular tiles.
  Each `TileState` has a local `TileGrid` = its bounding box; local (x,y) = world (OriginX+x, OriginY+y); cells of
  the box owned by other tiles are outside (`InBounds` false). Units live in world sub-cell coordinates
  (`Unit.CellScale` = 65536 per cell).
- **Entities**: buildings are `PlacedBuilding`, with per-kind runtime state (`BeltState`, `MachineState`,
  `ExportState`). Behaviour is chosen by `BuildingKind` from JSON. Import ports reuse `MachineState`'s output buffer.
- **Ports**: each tile has one I/O zone per neighbour (`TileGrid.ZoneAt` = neighbour id). A port's zone, not its
  rotation, decides its partner tile.
- **Godot side** reads sim state directly every frame (no copies) and only writes via commands. Views redraw
  MultiMeshes only when `TileGrid.Revision` changes. UI is built in code (`Ui` helpers), not .tscn.

## Rules

- No Godot types, `float` or `double` in `src/`. Integer maths only (`IntMath`, `Trig`, `Rng`, never `System.Random`).
- Anything iterated in the sim must have a deterministic order: `SortedDictionary` with `StringComparer.Ordinal`,
  id-ordered lists. No `HashSet`/`Dictionary` iteration that affects state.
- New sim state must be mixed into `Core/StateHash.cs`, or desyncs and broken saves go undetected.
- New command kinds: add a `[JsonDerivedType]` line with a new stable tag in `Command.cs`. Never rename existing tags
  (saves store them).
- Gameplay checks live in the sim as public read-only `Check` methods returning a result enum; the command's `Apply`
  calls the same check. The UI calls the check to preview. Don't duplicate rules in `game/`.
- Content goes in `game/data/*.json` and gets validated in `ContentDb.Validate`. Code-side tuning that isn't
  content yet goes in `Balance.cs`.
- POC stage: prefer simple and direct over defensive. Don't add null checks for things the scene always wires
  (`[Export]` fields are non-nullable `= null!`), speculative extension points or placeholder features.
- Style: file-scoped namespaces, primary constructors, collection expressions, `is {}` patterns, braces omitted for
  single-line bodies, `/// <summary>` on public members explaining why/units. Comments use British spelling.
- Godot: a `Node` subclass must be `partial` and live in a file named after the class. Scene-attached scripts are
  referenced by path in `scenes/*.tscn`; moving one means updating the .tscn and moving its `.cs.uid`.
  Get the host with `SimHost.Instance`; subscribe to view changes with `SimHost.WatchView`/`ShowOnlyIn` (they
  unsubscribe on tree exit).

## Recipes

- **New building with existing behaviour**: add to `buildings.json` with an `hp` (+ recipes in `recipes.json`), optional
  sprite `game/assets/sprites/<id>.png`. Build menu picks it up in file order.
- **New building behaviour**: add a `BuildingKind`, runtime state in `World/Entities.cs` + `PlacedBuilding`
  (`TileGrid.cs`), a kind list in `TileGrid.KindList`, stepping in `TileEntities.Step`/`TryInsert`, draining in
  `TileEntities.Drain`, hashing in `StateHash.MixEntity`, validation in `ContentDb.Validate`, hover text in
  `Hud.Hover.cs`.
- **New command**: record in `Core/Command.cs` + JSON tag; logic as a `Check` + internal apply in the owning
  `World/*` static class (or `Simulation.Try*`); a round-trip case in `SaveTests.EveryCommandKindRoundTrips`.
- **New goal condition type**: `UnlockCondition.Types`, `Progression.Progress`, a counter on `PlayerState`
  (hashed), `GameText.ConditionUnit`.
- **New HUD panel**: a new `Hud.<Panel>.cs` partial; build its controls in `Hud.cs` layout, refresh from `RefreshSlow`.
- **New view-layer node**: add to `scenes/main.tscn`; in `_Ready` call `SimHost.Instance.ShowOnlyIn(this, ViewMode.Tile)`.
- **New key**: constant + default in `input/InputActions.cs`, handle in the owning node's `_UnhandledInput`.
