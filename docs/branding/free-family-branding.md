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

Each product letter has a two-pixel keyline in its app's dark surface color. The keyline is visible where the letter overlaps the accent header and blends into the lower field, matching the readability treatment of the preserved FreeX icon.

Application chrome must consume `Free.Shared.Theme.BrandThemes`; it must not introduce independent brand-color literals. Packaging must consume the canonical files in `shared/Free.Shared.Shell/Resources`.

## Theme configuration

Branding has one platform-neutral runtime owner. Each `Theme` contains:

- semantic chrome colors, including title bar, ribbon, status bar, Backstage rail, hover, selection, separator, and link roles;
- typography and layout metrics;
- `ThemeVisualAssets`, which identifies the product glyph and the canonical Windows ICO, scalable SVG, and macOS ICNS files.

WPF and Avalonia both materialize those same tokens. Window icons, title-bar badges, taskbar icons, and Backstage colors are selected from the active theme rather than from product-specific literals in views. `FREEX_THEME`, `FREEW_THEME`, and `FREEP_THEME` select the optional `midnight` variants at startup. Each midnight variant retains its product's accent and icon family.

The build-time counterpart is `shared/Free.Shared.Shell/BrandAssets.props`. Every WPF and Avalonia application declares only its `FreeBrand` identity (`FreeX`, `FreeW`, or `FreeP`); the shared props file resolves and validates the ICO, SVG, and ICNS paths used by Windows builds and cross-platform publish/package outputs. Changing a product identity requires updating that app's startup theme and platform bundle metadata together, so the runtime and packaged artwork cannot drift.

To add a theme, define its semantic roles and `ThemeVisualAssets` in `BrandThemes`, then route it through the product's startup descriptor. To add a new export, update the canonical generator and the shared build mapping instead of adding a local copy to an app project.

These marks are project-owned geometric word/letter marks. They do not use Microsoft logos, Office tiles, Windows flags, Microsoft product names, or a copied Microsoft application-icon silhouette. This is an engineering/design safeguard, not a legal opinion.
