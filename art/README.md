# Art pipeline

Sprites in `game/assets/sprites/` are generated with PixelLab from `jobs.json`:

    python tools/pixellab_gen.py art/jobs.json game/assets/sprites

Existing PNGs are skipped, so delete one to regenerate it. Needs `PIXELLAB_API_KEY` in the repo-root `.env`.

- Buildings start from a hand-drawn flat blockout in `blockouts/` (`init_image`, strength ~100-125, `no_background: false`). Without one, PixelLab draws them at an angle instead of top-down.
- The API's minimum canvas is 32x32, so 1-cell sprites (conveyor, items) are drawn at 32 px and scaled to the 16 px cell.
- `palette.png` is an optional shared palette (`color_image`). It dulls glowing parts, so the steel mill doesn't use it.
- `preview_mock.png` / `preview_sheet.png` show the set on the grid and individually.
- Sprites face east at rotation 0; the game turns them clockwise per quarter turn (conveyor.png was rotated to match).
- `ingame_*.png` are real Godot frames (`--write-movie`), the stress one cropped and scaled 4x.
