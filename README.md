# Wunderwaffe 3037

2D factory-builder RTS. Requirements: `Wunderwaffe_3037_Requirements.xlsx`.

## Setup

- Godot 4.7.2 .NET (`C:\Users\Public\Godot\Godot_v4.7.2-stable_mono_win64`)
- .NET 8 SDK
- VS Code with C# Dev Kit and godot-tools (recommended in `.vscode/extensions.json`)
- Git LFS (`git lfs install` once per machine)

## Layout

| Path | What |
|---|---|
| `src/Wunderwaffe.Sim` | Game rules and state. Pure C#, **no Godot references**, integer-only, fixed tick. |
| `tests/Wunderwaffe.Sim.Tests` | xUnit tests for the sim, run against the real `game/data` content. |
| `game/` | Godot project. Draws the sim and turns input into `Command`s. |
| `game/render/` | Drawing. One MultiMesh per layer; redraws only when the grid's `Revision` changes. |
| `game/input/` | Camera, build tool, default key bindings. |
| `game/data/*.json` | Content: items, recipes, buildings, tech. |

Run `Godot ... --path game -- --stress` (add `--headless` to skip rendering) to fill the tile with 2,000 buildings and print the sim cost per tick. F3 does the same in-game.

## Rules of the codebase

- The sim only changes through `Command`s (lockstep multiplayer, replays — NF-004).
- No `float`/`double` in sim state; no Godot types in `src/`.
- Tiles not viewed by any player run in rate mode (FB-012); viewed or contested tiles run full sim (FB-013).

## Content notes

- Item `tier`: 1 raw, 2 refined, 3 advanced, 4 exotic. A recipe can't output an item more than one tier above its best input (RS-001).
- Recipe `ticks` are at 60 ticks/second. A recipe with empty `inputs` is extraction.
- Tech `condition.type` is currently only `produce`.

## Commands

```
dotnet build Wunderwaffe.sln
dotnet test tests/Wunderwaffe.Sim.Tests
```

In VS Code, use the **Play** or **Open Editor** launch configurations.
