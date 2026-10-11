"""Flat top-down blockouts drawn in code (same palette as the hand-drawn ones). Writes art/blockouts/<id>.png.

    python art/make_blockouts.py
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parent / "blockouts"
INK = (22, 22, 26)
BODY = (84, 86, 94)
LIGHT = (104, 106, 114)
SHADE = (78, 80, 88)
DARK = (50, 50, 56)
RIVET = (150, 152, 160)
COPPER = (150, 100, 60)
ORANGE = (230, 120, 30)
GLOW = (255, 225, 110)
CYAN = (110, 200, 240)
CYAN_D = (40, 90, 130)
YELLOW = (220, 180, 40)
RED = (220, 60, 60)
GREEN = (90, 190, 90)
BRASS = (200, 160, 70)
VIOLET = (170, 110, 230)


def plate(w, h, body=BODY):
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, w - 1, h - 1], fill=INK)
    d.rectangle([2, 2, w - 3, h - 3], fill=body)
    d.line([2, 2, w - 3, 2], fill=LIGHT)
    d.line([2, 2, 2, h - 3], fill=LIGHT)
    d.line([2, h - 3, w - 3, h - 3], fill=SHADE)
    d.line([w - 3, 2, w - 3, h - 3], fill=SHADE)
    for x, y in [(4, 4), (w - 5, 4), (4, h - 5), (w - 5, h - 5)]:
        d.point((x, y), fill=RIVET)
    return im, d


def disc(d, cx, cy, r, fill, outline=INK):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill, outline=outline)


def hazard(d, x0, y0, x1, y1):
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            if x in (x0, x0 + 1, x1, x1 - 1) or y in (y0, y0 + 1, y1, y1 - 1):
                d.point((x, y), fill=YELLOW if ((x + y) // 2) % 2 == 0 else INK)


def munitions_works():
    im, d = plate(32, 32)
    d.rectangle([5, 5, 26, 14], fill=DARK, outline=INK)
    for x in range(7, 25, 4):
        d.rectangle([x, 7, x + 2, 12], fill=BRASS, outline=INK)
        d.point((x + 1, 7), fill=COPPER)
    d.rectangle([5, 18, 26, 26], fill=SHADE, outline=INK)
    d.rectangle([13, 16, 18, 28], fill=DARK, outline=INK)
    d.rectangle([15, 19, 16, 25], fill=ORANGE)
    return im


def gun_turret():
    im, d = plate(32, 32)
    disc(d, 14, 16, 9, LIGHT)
    disc(d, 14, 16, 5, DARK)
    d.rectangle([16, 12, 29, 14], fill=DARK, outline=INK)
    d.rectangle([16, 18, 29, 20], fill=DARK, outline=INK)
    d.rectangle([28, 12, 29, 20], fill=INK)
    return im


def laser_turret():
    im, d = plate(32, 32)
    disc(d, 14, 16, 9, LIGHT)
    disc(d, 14, 16, 4, RED)
    d.point((13, 15), fill=GLOW)
    d.rectangle([18, 14, 28, 18], fill=DARK, outline=INK)
    d.rectangle([27, 15, 29, 17], fill=RED)
    return im


def wall():
    im, d = plate(32, 32, (120, 122, 128))
    d.rectangle([5, 5, 26, 26], fill=(104, 106, 114), outline=INK)
    d.line([5, 16, 26, 16], fill=INK)
    d.line([16, 5, 16, 16], fill=INK)
    d.line([10, 16, 10, 26], fill=INK)
    d.line([22, 16, 22, 26], fill=INK)
    return im


def shield_generator():
    im, d = plate(32, 32)
    disc(d, 16, 16, 12, (60, 50, 110))
    disc(d, 16, 16, 9, (120, 90, 200))
    disc(d, 16, 16, 5, (190, 170, 255))
    d.point((14, 14), fill=(240, 235, 255))
    return im


def repair_bay():
    im, d = plate(32, 32)
    d.rectangle([5, 5, 26, 26], fill=DARK, outline=INK)
    d.rectangle([13, 8, 18, 23], fill=GREEN, outline=INK)
    d.rectangle([8, 13, 23, 18], fill=GREEN, outline=INK)
    d.rectangle([14, 9, 17, 22], fill=GREEN)
    return im


def supply_depot():
    im, d = plate(32, 32)
    crate = (110, 120, 70)
    for x, y in [(5, 5), (17, 5), (5, 17), (17, 17)]:
        d.rectangle([x, y, x + 9, y + 9], fill=crate, outline=INK)
        d.line([x, y, x + 9, y + 9], fill=(80, 90, 50))
        d.line([x + 9, y, x, y + 9], fill=(80, 90, 50))
    d.rectangle([14, 14, 17, 17], fill=YELLOW)
    return im


def ammo_rack():
    im, d = plate(32, 32)
    d.rectangle([5, 5, 26, 26], fill=DARK, outline=INK)
    for y in (7, 17):
        for x in range(7, 25, 4):
            d.rectangle([x, y, x + 2, y + 7], fill=BRASS, outline=INK)
            d.point((x + 1, y), fill=COPPER)
    return im


def unit_factory():
    im, d = plate(48, 48)
    d.rectangle([6, 6, 41, 41], fill=DARK)
    hazard(d, 6, 6, 41, 41)
    d.rectangle([12, 22, 35, 25], fill=YELLOW, outline=INK)
    d.rectangle([20, 12, 27, 35], fill=SHADE, outline=INK)
    disc(d, 23, 23, 3, CYAN)
    return im


def wheels():
    im, d = plate(32, 32)
    d.rectangle([14, 6, 17, 25], fill=DARK, outline=INK)
    for y in (4, 20):
        d.rectangle([6, y, 25, y + 7], fill=(30, 30, 34), outline=INK)
        for x in range(8, 25, 3):
            d.line([x, y + 1, x, y + 6], fill=(60, 60, 66))
    return im


def hull():
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    pts = [(1, 3), (22, 3), (30, 16), (22, 28), (1, 28)]
    d.polygon(pts, fill=(70, 90, 110), outline=INK)
    d.polygon([(4, 6), (20, 6), (26, 16), (20, 25), (4, 25)], fill=(90, 110, 130))
    d.line([4, 16, 26, 16], fill=(50, 65, 80))
    for x in (8, 14, 20):
        d.point((x, 9), fill=RIVET)
        d.point((x, 22), fill=RIVET)
    return im


def rotor():
    im, d = plate(32, 32)
    disc(d, 16, 16, 13, DARK)
    d.polygon([(15, 3), (17, 3), (17, 29), (15, 29)], fill=(40, 40, 44), outline=INK)
    d.polygon([(3, 15), (29, 15), (29, 17), (3, 17)], fill=(40, 40, 44), outline=INK)
    disc(d, 16, 16, 3, LIGHT)
    return im


def platform():
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    deck = (120, 95, 65)
    d.rectangle([0, 0, 31, 31], fill=deck)
    for y in range(0, 32, 6):
        d.line([0, y, 31, y], fill=(80, 60, 40))
    for y in range(3, 32, 6):
        d.point((4 + (y % 12), y), fill=(90, 70, 48))
    d.rectangle([0, 0, 31, 31], outline=(60, 50, 40))
    return im


def wire():
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 14, 31, 17], fill=COPPER, outline=INK)
    d.rectangle([14, 0, 17, 31], fill=COPPER, outline=INK)
    d.rectangle([10, 10, 21, 21], fill=DARK, outline=INK)
    d.rectangle([13, 13, 18, 18], fill=ORANGE)
    return im


def fuel_press():
    im, d = plate(32, 32)
    disc(d, 11, 11, 6, (60, 80, 40))
    disc(d, 11, 11, 3, (200, 170, 50))
    d.rectangle([18, 5, 26, 26], fill=DARK, outline=INK)
    d.rectangle([20, 8, 24, 18], fill=LIGHT, outline=INK)
    d.rectangle([5, 20, 15, 26], fill=SHADE, outline=INK)
    return im


def fuel_generator():
    im, d = plate(32, 32)
    disc(d, 13, 16, 9, DARK)
    for i in range(4):
        d.line([13, 16, 13 + [8, 0, -8, 0][i], 16 + [0, 8, 0, -8][i]], fill=LIGHT)
    disc(d, 13, 16, 3, YELLOW)
    d.rectangle([24, 6, 27, 25], fill=DARK, outline=INK)
    d.polygon([(25, 10), (23, 16), (26, 16), (24, 22)], outline=YELLOW)
    return im


def battery():
    im, d = plate(32, 32)
    d.rectangle([5, 9, 25, 22], fill=DARK, outline=INK)
    d.rectangle([25, 13, 28, 18], fill=LIGHT, outline=INK)
    for x in (7, 12, 17):
        d.rectangle([x, 11, x + 3, 20], fill=GREEN)
    return im


def container():
    im, d = plate(32, 32, (70, 85, 105))
    d.rectangle([5, 5, 26, 26], fill=(80, 98, 120), outline=INK)
    for x in range(8, 26, 4):
        d.line([x, 6, x, 25], fill=(60, 75, 95))
    d.rectangle([5, 14, 26, 17], fill=DARK, outline=INK)
    return im


def belt(d, horizontal, a, b):
    if horizontal:
        d.rectangle([0, a, 31, b], fill=(40, 44, 60), outline=INK)
    else:
        d.rectangle([a, 0, b, 31], fill=(40, 44, 60), outline=INK)


def junction():
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    belt(d, True, 9, 22)
    belt(d, False, 9, 22)
    d.rectangle([9, 9, 22, 22], fill=BODY, outline=INK)
    d.rectangle([13, 13, 18, 18], fill=DARK)
    return im


def arrow(d, cx, cy, dx, dy, colour):
    pts = {(1, 0): [(cx - 2, cy - 3), (cx + 2, cy), (cx - 2, cy + 3)],
           (-1, 0): [(cx + 2, cy - 3), (cx - 2, cy), (cx + 2, cy + 3)],
           (0, 1): [(cx - 3, cy - 2), (cx, cy + 2), (cx + 3, cy - 2)],
           (0, -1): [(cx - 3, cy + 2), (cx, cy - 2), (cx + 3, cy + 2)]}[(dx, dy)]
    d.polygon(pts, fill=colour, outline=INK)


def router():
    im, d = plate(32, 32)
    d.rectangle([9, 9, 22, 22], fill=DARK, outline=INK)
    arrow(d, 27, 16, 1, 0, YELLOW)
    arrow(d, 16, 27, 0, 1, YELLOW)
    arrow(d, 16, 5, 0, -1, YELLOW)
    disc(d, 15, 15, 3, YELLOW)
    return im


def sorter():
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    belt(d, True, 9, 22)
    d.rectangle([10, 3, 21, 28], fill=BODY, outline=INK)
    d.rectangle([13, 7, 18, 24], fill=DARK)
    for y in range(8, 24, 3):
        d.line([13, y, 18, y], fill=VIOLET)
    arrow(d, 27, 16, 1, 0, VIOLET)
    return im


def bridge():
    im, d = plate(32, 32)
    d.rectangle([5, 8, 22, 23], fill=(40, 44, 60), outline=INK)
    d.rectangle([20, 6, 27, 25], fill=DARK, outline=INK)
    d.rectangle([22, 9, 27, 22], fill=INK)
    arrow(d, 12, 16, 1, 0, YELLOW)
    return im


def overdrive_coil():
    im, d = plate(32, 32)
    d.rectangle([8, 6, 23, 25], fill=DARK, outline=INK)
    for y in range(8, 25, 3):
        d.line([9, y, 22, y], fill=ORANGE)
    disc(d, 16, 16, 3, VIOLET)
    d.point((16, 16), fill=(240, 220, 255))
    return im


def yield_sieve():
    im, d = plate(32, 64)
    d.rectangle([6, 6, 25, 57], fill=DARK, outline=INK)
    for y in range(8, 57, 4):
        d.line([7, y, 24, y], fill=SHADE)
    for x in range(10, 24, 4):
        d.line([x, 7, x, 56], fill=SHADE)
    for x, y in [(12, 14), (18, 26), (10, 38), (20, 48), (15, 52)]:
        d.rectangle([x, y, x + 1, y + 1], fill=COPPER)
    return im


def heat_exchanger():
    im, d = plate(32, 32)
    cols = [(220, 80, 50), (200, 100, 80), (150, 120, 140), (90, 140, 200), (60, 160, 230)]
    for i, y in enumerate(range(7, 26, 4)):
        d.rectangle([6, y, 25, y + 2], fill=cols[i], outline=INK)
    for i, y in enumerate(range(7, 22, 4)):
        x = 25 if i % 2 == 0 else 6
        d.rectangle([x - 1, y, x + 1, y + 6], fill=cols[i], outline=INK)
    return im


def assembly_arm():
    im, d = plate(32, 32)
    disc(d, 9, 23, 5, DARK)
    d.line([9, 23, 18, 12], fill=INK, width=5)
    d.line([9, 23, 18, 12], fill=YELLOW, width=3)
    d.line([18, 12, 25, 18], fill=INK, width=5)
    d.line([18, 12, 25, 18], fill=YELLOW, width=3)
    disc(d, 18, 12, 2, LIGHT)
    d.rectangle([24, 18, 27, 22], fill=LIGHT, outline=INK)
    return im


def gantry_rail():
    im, d = plate(32, 96)
    d.rectangle([6, 4, 9, 91], fill=RIVET, outline=INK)
    d.rectangle([22, 4, 25, 91], fill=RIVET, outline=INK)
    for y in range(10, 90, 8):
        d.line([10, y, 21, y], fill=DARK)
    d.rectangle([4, 40, 27, 55], fill=YELLOW, outline=INK)
    d.rectangle([12, 44, 19, 51], fill=DARK, outline=INK)
    return im


def feeder_hub():
    im, d = plate(32, 64)
    for cy in (17, 46):
        d.polygon([(6, cy - 10), (25, cy - 10), (19, cy + 4), (12, cy + 4)], fill=DARK, outline=INK)
        d.rectangle([13, cy - 3, 18, cy + 8], fill=SHADE, outline=INK)
        for x, y in [(10, cy - 8), (15, cy - 7), (20, cy - 8), (13, cy - 5), (18, cy - 5)]:
            d.point((x, y), fill=COPPER)
    return im


def servo_bank():
    im, d = plate(32, 32)
    for cx, cy in [(10, 10), (21, 10), (10, 21), (21, 21)]:
        disc(d, cx, cy, 5, DARK)
        disc(d, cx, cy, 2, ORANGE)
        for dx, dy in [(0, -5), (5, 0), (0, 5), (-5, 0)]:
            d.point((cx + dx, cy + dy), fill=LIGHT)
    return im


def coolant_tower():
    im, d = plate(32, 32)
    disc(d, 16, 16, 12, (120, 124, 132))
    disc(d, 16, 16, 9, CYAN_D)
    disc(d, 16, 16, 6, CYAN, CYAN_D)
    d.point((14, 14), fill=(220, 245, 255))
    return im


def precision_jig():
    im, d = plate(32, 32)
    d.rectangle([5, 5, 26, 26], fill=DARK, outline=INK)
    for x, y in [(5, 5), (22, 5), (5, 22), (22, 22)]:
        d.rectangle([x, y, x + 4, y + 4], fill=YELLOW, outline=INK)
    d.line([16, 7, 16, 24], fill=CYAN)
    d.line([7, 16, 24, 16], fill=CYAN)
    d.ellipse([11, 11, 21, 21], outline=CYAN)
    for i in range(8, 24, 3):
        d.point((i, 7), fill=RIVET)
    return im


BUILDERS = {f.__name__: f for f in [
    munitions_works, gun_turret, laser_turret, wall, shield_generator, repair_bay, supply_depot, ammo_rack,
    unit_factory, wheels, hull, rotor, platform, wire, fuel_press, fuel_generator, battery, container, junction,
    router, sorter, bridge, overdrive_coil, yield_sieve, heat_exchanger, assembly_arm, gantry_rail, feeder_hub,
    servo_bank, coolant_tower, precision_jig]}

if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for name, f in BUILDERS.items():
        f().save(OUT / f"{name}.png")
    print(len(BUILDERS), "blockouts")
