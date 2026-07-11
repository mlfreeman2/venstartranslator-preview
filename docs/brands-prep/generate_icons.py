#!/usr/bin/env python3
"""Generate placeholder brand icons for the home-assistant/brands PR.

Pure-stdlib PNG writer (no PIL). Draws a flat rounded-square badge with a
thermometer glyph; the emulator variant adds outgoing broadcast arcs, the
listener variant adds incoming arcs, so the two are distinguishable at a glance.

These are PLACEHOLDERS — a human should approve (or replace) them before any
brands PR is opened. Regenerate with: python3 generate_icons.py
"""
import math
import struct
import zlib
from pathlib import Path

BG = (0x2B, 0x4F, 0x81, 255)      # flat slate blue
FG = (255, 255, 255, 255)         # white glyph
TRANSPARENT = (0, 0, 0, 0)


def write_png(path: Path, size: int, pixels) -> None:
    raw = b"".join(
        b"\x00" + b"".join(struct.pack("4B", *pixels[y][x]) for x in range(size))
        for y in range(size)
    )

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )
    path.write_bytes(png)


def rounded_square(u: float, v: float, half: float, radius: float) -> bool:
    ax, ay = abs(u) - (half - radius), abs(v) - (half - radius)
    if ax <= 0 or ay <= 0:
        return abs(u) <= half and abs(v) <= half
    return ax * ax + ay * ay <= radius * radius


def draw(size: int, arcs: str):
    s = size / 256.0  # design space is 256
    pixels = [[TRANSPARENT] * size for _ in range(size)]

    stem_hw = 11 * s          # stem half-width
    stem_top = 52 * s
    stem_bot = 158 * s
    bulb_cy, bulb_r = 180 * s, 26 * s
    # glyph is offset left when arcs are drawn, centered otherwise
    gx = (108 if arcs else 128) * s
    arc_cx, arc_cy = (150 * s, 116 * s)

    for y in range(size):
        for x in range(size):
            u, v = x - size / 2.0, y - size / 2.0
            if not rounded_square(u, v, size * 0.5, size * 0.20):
                continue
            px = BG
            # thermometer stem (rounded ends)
            dx = x - gx
            if abs(dx) <= stem_hw and stem_top <= y <= stem_bot:
                px = FG
            if dx * dx + (y - stem_top) ** 2 <= stem_hw * stem_hw:
                px = FG
            # bulb
            if dx * dx + (y - bulb_cy) ** 2 <= bulb_r * bulb_r:
                px = FG
            # broadcast arcs (three concentric ring segments)
            if arcs:
                d = math.hypot(x - arc_cx, y - arc_cy)
                ang = math.degrees(math.atan2(y - arc_cy, x - arc_cx))
                in_sector = -55 <= ang <= 55
                for r in (30 * s, 52 * s, 74 * s):
                    if in_sector and abs(d - r) <= 6 * s:
                        px = FG
                # listener: add an arrowhead pointing IN at the innermost arc
                if arcs == "in":
                    tip_x, tip_y = arc_cx + 8 * s, arc_cy
                    if (
                        abs(y - tip_y) <= (x - tip_x) * 0.9
                        and 0 <= x - tip_x <= 14 * s
                    ):
                        px = FG
            pixels[y][x] = px
    return pixels


def main() -> None:
    here = Path(__file__).resolve().parent
    variants = {
        "venstar_acc_tsenwifi_emulator": "out",
        "venstar_acc_tsenwifi_listener": "in",
    }
    for domain, arcs in variants.items():
        folder = here / domain
        folder.mkdir(parents=True, exist_ok=True)
        write_png(folder / "icon.png", 256, draw(256, arcs))
        write_png(folder / "icon@2x.png", 512, draw(512, arcs))
        print(f"wrote {folder}/icon.png + icon@2x.png")


if __name__ == "__main__":
    main()
