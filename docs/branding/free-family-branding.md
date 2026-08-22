# Free family branding

FreeX, FreeW, and FreeP use one original app-icon construction: a rounded square split into a compact `FREE` header band and a large product letter below it. The construction, typography, spacing, and export sizes are shared; color and the product letter distinguish each app.

| App | Product | Accent | Dark surface | Soft surface |
| --- | --- | --- | --- | --- |
| FreeX | Spreadsheet | `#0F6D8C` | `#17324D` | `#E6F6FA` |
| FreeW | Document | `#A26714` | `#4B2F12` | `#FBF0DC` |
| FreeP | Presentation | `#A23B72` | `#4E213B` | `#F9E7F1` |

The Windows FreeX icon is the canonical original and is intentionally immutable. Its SHA-256 is:

`81D217EFA33A689EFDB2ED79E1DFAD99AC7BFFBD98C280BF629B171AE4EA41A7`

`tools/generate_brand_assets.py` verifies that hash before and after generation. It creates the cross-platform FreeX SVG and ICNS exports plus the complete FreeW and FreeP SVG, ICO, and ICNS families. It never rewrites `FreeX.ico`.

The locked icon also defines the shared layout: the `FREE` header is centered at `(128, 48.5)` at 60 px, while each 154 px product letter is centered vertically at 129 px. Sister-app exports use those exact typographic sizes; the P receives a four-pixel optical correction for its asymmetric side bearing so its visible glyph, rather than its font advance box, is horizontally centered.

Application chrome must consume `Free.Shared.Theme.BrandThemes`; it must not introduce independent brand-color literals. Packaging must consume the canonical files in `shared/Free.Shared.Shell/Resources`.

These marks are project-owned geometric word/letter marks. They do not use Microsoft logos, Office tiles, Windows flags, Microsoft type treatments, or Microsoft product names. This is an engineering/design safeguard, not a legal opinion.
