# Command Icon Symbol Guide

FreeX command icons use a 32x32 SVG canvas. Keep geometry on integer or half-pixel coordinates, use explicit strokes, and avoid scaled bitmap-like effects. The goal is crisp thin-line icons that stay readable at ribbon, QAT, context menu, and collapsed-section sizes.

## Base Style

- Canvas: `width="32" height="32" viewBox="0 0 32 32"`.
- Main ink: `#242424`, stroke width `1.4` for regular outlines.
- Accent blue: `#4472c4` for direction, movement, chart/data emphasis.
- Accent green: `#217346` for workbook/sheet positive actions.
- Accent red: `#c50f1f` only for destructive/remove/error marks.
- Accent yellow: `#ffb900` or `#ffdf80` for fill/highlight.
- Use rounded stroke caps and joins for organic marks; use square/miter only where the command is explicitly grid, page, or chart geometry.
- Do not use background discs or badges unless the command itself is about a filled shape, cell fill, or background. Small colored dots are not meaningful enough for command state.

## Standard Decorations

- Remove / clear: a red X only, two strokes, no circle. Use a 5x5 or 6x6 cross in the lower-right area, with rounded caps.
- Add / new: a green plus only, no filled circle. Keep it centered in its available badge area.
- Repeat / refresh: one circular arrow loop with a clean arrowhead, or paired arrows only when the command implies two-way repeat.
- Hide: an eye with one diagonal slash, both in main ink. Do not add colored dots.
- Show / unhide: an open eye, no slash, no colored dot.
- Advanced / options: sliders/tune marks or a small gear, not a generic blue dot.
- Fill: show an actual filled cell/shape, bucket, or fill bar. Color fill commands should use a filled cell plus a matching color line below.
- Expand / collapse: paired chevrons or plus/minus marks; keep the direction explicit and avoid cramped arrows.
- Size / scale: corner or edge arrows outside the object with visible spacing from the object.
- Rotate: a curved arrow around the object. Use a clear arrowhead and leave air between arrow and shape.
- Watch / inspect: a small window or formula object plus an eye or magnifier.

## Legibility Checks

- Lettered icons must keep readable spacing between letters and arrows.
- Formula/name icons should prefer one strong metaphor over multiple tiny glyphs.
- Collapsed-section icons should use most of the available 32x32 space, but the visual weight should match neighboring icons.
- At small sizes, any colored accent must still describe the command. If it reads as noise, remove it.
