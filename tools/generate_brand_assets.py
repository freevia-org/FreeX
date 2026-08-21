#!/usr/bin/env python3
"""Generate the owned FreeFamily SVG, ICO, and ICNS application icon assets.

FreeX.ico is the locked Windows reference and is intentionally never rewritten.
The sister-app marks share its two-band FREE + product-letter construction while
using their canonical theme accent and dark colors.
"""

from __future__ import annotations

import hashlib
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
RESOURCE_DIRECTORY = REPOSITORY_ROOT / "shared" / "Free.Shared.Shell" / "Resources"
LOCKED_FREEX_ICO_SHA256 = "81d217efa33a689efdb2ed79e1dfad99ac7bffbd98c280bf629b171ae4ea41a7"
WINDOWS_ICON_SIZES = (16, 24, 32, 48, 64, 128, 256)


@dataclass(frozen=True)
class BrandIcon:
    product: str
    letter: str
    accent: str
    dark: str


BRANDS = (
    BrandIcon("FreeX", "X", "#0F6D8C", "#17324D"),
    BrandIcon("FreeW", "W", "#A26714", "#4B2F12"),
    BrandIcon("FreeP", "P", "#A23B72", "#4E213B"),
)


def verify_locked_freex_icon() -> None:
    path = RESOURCE_DIRECTORY / "FreeX.ico"
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if digest != LOCKED_FREEX_ICO_SHA256:
        raise RuntimeError(
            f"Refusing to generate sister assets: {path} no longer matches the locked FreeX Windows icon. "
            f"Expected {LOCKED_FREEX_ICO_SHA256}, got {digest}."
        )


def find_font(size: int) -> ImageFont.FreeTypeFont:
    for name in ("DejaVuSans-Bold.ttf", "arialbd.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    raise RuntimeError("A bold DejaVu Sans or Arial font is required to generate brand assets.")


def draw_centered_text(draw: ImageDraw.ImageDraw, text: str, center_x: int, center_y: int, font: ImageFont.FreeTypeFont) -> None:
    box = draw.textbbox((0, 0), text, font=font, stroke_width=0)
    width = box[2] - box[0]
    height = box[3] - box[1]
    draw.text(
        (center_x - width / 2 - box[0], center_y - height / 2 - box[1]),
        text,
        font=font,
        fill="#FFFFFF",
    )


def render_master(brand: BrandIcon, size: int = 1024) -> Image.Image:
    scale = size / 256
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    outer_mask = Image.new("L", (size, size), 0)
    mask_draw = ImageDraw.Draw(outer_mask)
    bounds = (0, 0, size - 1, size - 1)
    mask_draw.rounded_rectangle(bounds, radius=round(24 * scale), fill=255)

    color_layer = Image.new("RGBA", (size, size), brand.dark)
    layer_draw = ImageDraw.Draw(color_layer)
    layer_draw.rectangle(
        (0, 0, size - 1, round(92 * scale)),
        fill=brand.accent,
    )
    canvas.paste(color_layer, (0, 0), outer_mask)

    draw = ImageDraw.Draw(canvas)
    draw_centered_text(draw, "FREE", size // 2, round(56 * scale), find_font(round(48 * scale)))
    draw_centered_text(draw, brand.letter, size // 2, round(158 * scale), find_font(round(128 * scale)))
    return canvas


def svg_text(brand: BrandIcon) -> str:
    return f'''<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256" role="img" aria-label="{brand.product} application icon">
  <defs>
    <clipPath id="brandTile">
      <rect width="256" height="256" rx="24"/>
    </clipPath>
  </defs>
  <g clip-path="url(#brandTile)">
    <rect width="256" height="256" fill="{brand.dark}"/>
    <rect width="256" height="92" fill="{brand.accent}"/>
  </g>
  <g fill="#ffffff" font-family="DejaVu Sans, Arial, sans-serif" font-weight="700" text-anchor="middle">
    <text x="128" y="74" font-size="48">FREE</text>
    <text x="128" y="206" font-size="128">{brand.letter}</text>
  </g>
</svg>
'''


def write_svg(brand: BrandIcon) -> None:
    (RESOURCE_DIRECTORY / f"{brand.product}.svg").write_text(svg_text(brand), encoding="utf-8", newline="\n")


def write_ico(brand: BrandIcon) -> None:
    master = render_master(brand)
    frames = [master.resize((size, size), Image.Resampling.LANCZOS) for size in WINDOWS_ICON_SIZES]
    frames[-1].save(
        RESOURCE_DIRECTORY / f"{brand.product}.ico",
        format="ICO",
        sizes=[(size, size) for size in WINDOWS_ICON_SIZES],
        append_images=frames[:-1],
    )


def write_icns(brand: BrandIcon) -> None:
    master = render_master(brand)
    master.save(RESOURCE_DIRECTORY / f"{brand.product}.icns", format="ICNS")


def main() -> None:
    verify_locked_freex_icon()
    RESOURCE_DIRECTORY.mkdir(parents=True, exist_ok=True)

    for brand in BRANDS:
        write_svg(brand)
        if brand.product != "FreeX":
            write_ico(brand)
        write_icns(brand)

    verify_locked_freex_icon()


if __name__ == "__main__":
    main()
