from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "native" / "Assets"
MASTER_SIZE = 1024
SUPERSAMPLE = 4


def _rounded_polygon(
    draw: ImageDraw.ImageDraw,
    points: list[tuple[float, float]],
    radius: float,
    fill: str,
) -> None:
    scale = SUPERSAMPLE
    vertices = [(x * scale, y * scale) for x, y in points]
    corner_radius = radius * scale
    outline: list[tuple[float, float]] = []

    for index, vertex in enumerate(vertices):
        previous = vertices[index - 1]
        following = vertices[(index + 1) % len(vertices)]
        incoming = math.dist(previous, vertex)
        outgoing = math.dist(vertex, following)
        inset = min(corner_radius, incoming * 0.24, outgoing * 0.24)

        start = (
            vertex[0] + (previous[0] - vertex[0]) * inset / incoming,
            vertex[1] + (previous[1] - vertex[1]) * inset / incoming,
        )
        end = (
            vertex[0] + (following[0] - vertex[0]) * inset / outgoing,
            vertex[1] + (following[1] - vertex[1]) * inset / outgoing,
        )
        outline.append(start)
        for step in range(1, 9):
            t = step / 8
            inverse = 1 - t
            outline.append(
                (
                    inverse * inverse * start[0]
                    + 2 * inverse * t * vertex[0]
                    + t * t * end[0],
                    inverse * inverse * start[1]
                    + 2 * inverse * t * vertex[1]
                    + t * t * end[1],
                )
            )

    draw.polygon(outline, fill=fill)


def render_master() -> Image.Image:
    canvas = Image.new(
        "RGBA",
        (MASTER_SIZE * SUPERSAMPLE, MASTER_SIZE * SUPERSAMPLE),
        (0, 0, 0, 0),
    )
    draw = ImageDraw.Draw(canvas)

    _rounded_polygon(
        draw,
        [(196, 168), (480, 280), (480, 510), (440, 550), (196, 435)],
        radius=24,
        fill="#24262A",
    )
    _rounded_polygon(
        draw,
        [(816, 168), (610, 250), (530, 335), (530, 510), (570, 550), (816, 435)],
        radius=24,
        fill="#303238",
    )
    _rounded_polygon(
        draw,
        [(510, 552), (642, 622), (642, 762), (382, 856), (379, 635)],
        radius=22,
        fill="#3A3C41",
    )

    # One flat secondary plane is enough to communicate “fold” at medium sizes.
    _rounded_polygon(
        draw,
        [(530, 335), (610, 250), (610, 306), (595, 321)],
        radius=8,
        fill="#B7AEA3",
    )

    return canvas.resize((MASTER_SIZE, MASTER_SIZE), Image.Resampling.LANCZOS)


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    master = render_master()
    png_path = ASSETS / "YitDesktopFold-icon-flat.png"
    ico_path = ASSETS / "YitDesktopFold-icon-flat.ico"
    master.save(png_path, optimize=True)
    master.save(
        ico_path,
        format="ICO",
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print(png_path)
    print(ico_path)


if __name__ == "__main__":
    main()
