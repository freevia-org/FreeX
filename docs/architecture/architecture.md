# Architecture

FreeX is a free, native desktop spreadsheet application with a command-driven workbook engine and explicit `.xlsx` fidelity boundaries. The primary shell targets Windows via WPF (`FreeX.App.Host`). A cross-platform Avalonia shell (`FreeX.App.Avalonia`) targets macOS and other platforms via the shared `FreeX.App.Services` session layer. FreeX shares a portable, app-neutral `shared/` project tier (`Free.Shared.*`) with its sibling desktop apps, FreeW (word processor, `freew/`) and FreeP (presentations, `freep/`); see [Shared Tier](#shared-tier-shared) below. Current outstanding work is tracked in [planning/outstanding-build.md](../planning/outstanding-build.md), with command-level scope in [parity/command-surface.md](../parity/command-surface.md) and file-format scope in [formats/fidelity-contract.md](../formats/fidelity-contract.md).

## Layered Architecture

```
App.Host (WPF composition root — Windows only, net10.0-windows)
  └── App.UI (WPF controls — GridView, dialogs)
       └── App.Services (WorkbookSession, WorkbookSessionFactory — shared session layer)
            └── Core.Commands (command bus, undo/redo, find/replace service)
            └── Core.Calc (dependency graph, recalc engine, viewport service)
                 └── Core.Formula (lexer, parser, AST, evaluator, built-in functions)
                      └── Core.Model (pure data types — Workbook, Sheet, Cell, ScalarValue, CellStyle)
            └── Core.IO (file adapters — XLSX via ClosedXML, CSV/text, XML Spreadsheet 2003, native JSON)
                 └── Core.Model

App.Avalonia (Avalonia cross-platform shell — net10.0; macOS/Linux/Windows)
  └── App.Services (shared session layer — same as App.Host path above)
```

**Dependency rule**: No `Core.*` project may reference any `App.*` project. This is enforced by project references. `App.Services` may not reference `App.Host` or `App.Avalonia`.

## Shared Tier (`shared/`)

FreeX, FreeW, and FreeP (plus their Avalonia ports) are extracting common infrastructure out of app-specific code and
into a portable `shared/` tier of `Free.Shared.*` class libraries, so the same tested code backs all three apps
instead of being copy-pasted per app. This extraction is an active, ongoing refactor (see commit history for
"Extract Free.Shared.* ..." and "Establish shared/ tier"), not a finished layer — some projects (for example `Free.Shared.Commands`, which currently exposes only a
document-agnostic `UndoRedoStack<TCommand, TPayload>` engine extracted from FreeX's command bus) carry a narrow
slice of an eventual shared surface; others (for example `Free.Shared.Drawing`, `Free.Shared.Ribbon`) are already
load-bearing for every app.

Most `Free.Shared.*` projects come in a platform-neutral form plus optional `.Wpf` / `.Avalonia` companions that add
framework-specific rendering or platform APIs on top of the neutral model. As of this writing the tier has 19
projects:

| Project | Purpose | Consumers |
|---|---|---|
| `Free.Shared.Drawing` | Shared drawing/geometry primitives | FreeX (`Core.Model`, `Core.Calc`, `Core.Commands`, `Core.IO`, `App.*`), FreeP (`Core.Model`, `App.Rendering.Wpf/Avalonia`, `App.Presentation`) |
| `Free.Shared.Ribbon` | Neutral declarative ribbon model | FreeX, FreeW, FreeP (`Ribbon.Definitions`, `App.Host`/`App.Avalonia` on every app) |
| `Free.Shared.Ribbon.Wpf` | WPF ribbon renderer/chrome (`SharedChromeResources.xaml`) over `Free.Shared.Ribbon` | FreeX.App.Host, FreeW.App.Host |
| `Free.Shared.Ribbon.Avalonia` | Avalonia ribbon renderer over `Free.Shared.Ribbon` | FreeX.App.Avalonia, FreeW.App.Avalonia, FreeP.App.Avalonia |
| `Free.Shared.Shell` | Portable shell helpers (e.g. `ExportAtomicWriter`) over `Free.Shared.AppServices` | FreeX.App.Host/App.Services/App.Avalonia, FreeW.App.Host/App.Avalonia/App.Presentation, FreeP.App.Host/App.Presentation |
| `Free.Shared.Shell.Wpf` | WPF-coupled shell helpers extracted from `FreeX.App.Host` | FreeX.App.Host, FreeW.App.Host |
| `Free.Shared.Shell.Avalonia` | Avalonia-coupled shell helpers | FreeX.App.Avalonia, FreeW.App.Avalonia, FreeP.App.Avalonia |
| `Free.Shared.Commands` | Document-agnostic undo/redo stack engine (`UndoRedoStack<TCommand, TPayload>`) extracted from FreeX's command bus | FreeX.Core.Commands, FreeW.Core.Model/App.Host, FreeP.Core.Model/App.Host |
| `Free.Shared.AppServices` | Domain-neutral app-service helpers (e.g. Velopack self-update orchestration) extracted from `FreeX.App.Services` | FreeX.App.Services/App.Presentation, FreeW.App.Avalonia/App.Presentation, FreeP.App.Host/App.Avalonia/App.Rendering.Avalonia/App.Presentation |
| `Free.Shared.AppServices.Windows` | Windows file-association registrar extracted from `FreeX.App.Host` | FreeX.App.Host |
| `Free.Shared.Theme` | Neutral theme/color model (`BrandThemes`, `RibbonVisualPalette`) | FreeX.App.Avalonia, FreeW.App.Host/App.Avalonia, FreeP.App.Host/App.Avalonia |
| `Free.Shared.Theme.Wpf` | WPF theme applier (`WpfThemeApplier`) over `Free.Shared.Theme` | FreeX.App.Host, FreeW.App.Host |
| `Free.Shared.Theme.Avalonia` | Avalonia theme applier over `Free.Shared.Theme` | FreeX.App.Avalonia, FreeW.App.Avalonia, FreeP.App.Avalonia |
| `Free.Shared.IO` | Portable file-IO helpers | FreeX.Core.IO, FreeW.Core.IO/App.Presentation, FreeP.App.Avalonia/App.Presentation |
| `Free.Shared.Opc` | Generic OPC (Open Packaging Conventions) helpers extracted from `FreeX.Core.IO` | FreeX.Core.IO, FreeW.Core.Model/Core.IO, FreeP.Core.Model/Core.IO |
| `Free.Shared.Pdf` | App-agnostic PDF page/document model + dependency-free WinAnsi emitter | FreeX.App.Services/App.Avalonia/App.Host, FreeW.App.Host/App.Avalonia, FreeP.Core.IO/App.Presentation |
| `Free.Shared.Pdf.Wpf` | WPF/PDFsharp rasterized-page + overlay companion to `Free.Shared.Pdf` | FreeX.App.Host, FreeW.App.Host |
| `Free.Shared.Pdf.Skia` | SkiaSharp emitter companion to `Free.Shared.Pdf` | FreeX.App.Avalonia, FreeW.App.Avalonia, FreeP.App.Host/App.Avalonia |
| `Free.Shared.Localization` | Portable localization/resource helpers | FreeX.App.Host/App.Localization/App.Presentation, FreeW.App.Localization, FreeP.App.Localization |

`shared/` projects generally use `InternalsVisibleTo` to keep extracted members visible only to their original
FreeX-side consumers until a member is intentionally promoted to the public shared API for FreeW/FreeP, so a
project appearing in this table is not proof every member is cross-app-usable yet.

## Key Principles

1. **UI depends on Core; Core never depends on UI.** The formula engine and workbook model run from unit tests with no UI.
2. **One source of truth: the engine.** UI sends commands; the engine mutates state; UI re-renders from `IViewportService`.
3. **Every mutation is a command.** No direct setters on the workbook from outside the engine. This gives undo/redo for free.
4. **The engine owns the dependency graph.** The `calc-chain` in `.xlsx` files is ignored — we build our own.
5. **File adapters are translation layers only.** No business logic in `Core.IO`.

## Current Implemented Baseline

- **Core.Model**: `Workbook`, `Sheet`, `Cell`, `ScalarValue` hierarchy (`BlankValue`, `NumberValue`, `BoolValue`, `TextValue`, `DateTimeValue`, `ErrorValue`), `CellAddress` (A1 notation), `GridRange`, `CellStyle` with `StyleId` registry (structural equality includes `NativeDifferential*` fields; `GetStyle` returns the registered instance directly without cloning), `NativeXmlPreserveBag` (keyed string bag replacing 12 `WorksheetXxxMetadataModel` classes)
- **Core.Formula**: Lexer → Parser → AST → Evaluator; 488 in-scope Excel built-in functions; dynamic arrays; LET/LAMBDA higher-order functions; cross-sheet reference support (`Sheet1!A1`)
- **Core.Calc**: `DependencyGraph` (topological sort, Kahn's algorithm, cycle detection), `RecalcEngine` (volatile-cell support), `ViewportService`
- **Core.Commands**: `ICommandBus` with undo/redo stack (count-bounded + 50 MB byte-budget via `IEstimatesMemory`), `EditCellsCommand`, `AddSheetCommand`, `RenameSheetCommand`, `FindReplaceService`
- **Core.IO**: `NativeJsonAdapter` (.fxl — compact JSON, SHA-256 password hashing via `NativePasswordHelper`), `XlsxFileAdapter` (ClosedXML 0.105.0 — stream-load, structured load warnings via `XlsxLoadResult`), `CsvFileAdapter`, delimited-text adapters, `SpreadsheetXmlFileAdapter` for Excel XML Spreadsheet 2003 `.xml`, `XsltWorkbookTransform` for safe XSLT-to-SpreadsheetML imports, `XmlNativeBagSerializer` for `NativeXmlPreserveBag` round-trip serialisation, and `OdsFileAdapter` (`.ods` — read/write, registered in `WorkbookFileAdapterCatalog`); the parked research in `docs/formats/ods-open-support-research.md` is superseded by this in-house adapter.
- **App.Services**: `WorkbookSession` — the shared session layer used by both `App.Host` (WPF) and `App.Avalonia`. Owns dirty state (`IsDirty`, `DirtyGeneration`, `TryMarkSavedIfNoEditsArrived`), viewport, undo/redo, selection, clipboard, sheet management, and file-context tracking. `WorkbookSessionFactory` constructs sessions from startup results or open results. `WorkbookDocumentState` (WPF-side dirty tracking with `SuppressClosePrompt` and generation counter — used by `App.Host`). `WorkbookFileAccessService` (macOS security-scoped bookmark support). `WorkbookStartupService`, `WorkbookSaveService`, and planner types (`ReviewWorkflowPlanner`, `ShareWorkbookPlanner`, `ExportReadinessPlanner`).
- **App.UI**: `GridView` — virtualized DrawingContext rendering (per-frame brush/pen/typeface caches reused via class-level fields), selection, row/column headers; `IUserMessageService` interface for injectable message dialogs
- **App.Host**: WPF composition root (Windows-only, `net10.0-windows10.0.19041.0`, `UseWPF=true`). `MainWindow` — formula bar, scrollbars, open/save dialogs, keyboard navigation, Find & Replace; `WpfUserMessageService` (MessageBox-backed `IUserMessageService`); localization foundation (`UiText`, `LocExtension`, neutral `Strings.resx`, 43 satellite resource cultures, `AppLanguageCatalog`, and `AppLocalization`); `HyperlinkNavigationPlanner` with URI scheme whitelist (`http`, `https`, `mailto`, `ftp`); `SaveCompletionPlanner` and `WindowCloseDecisionPlanner` (pure close/save decision planners with unit tests in `FreeX.App.Host.Logic.Tests`)
- **App.Avalonia**: Cross-platform Avalonia shell (`net10.0`; `net10.0-macos` when `EnableMacOsTargetFramework=true`). `MainWindow` — full spreadsheet host with open/save/export dialogs, keyboard navigation, and formula bar. Uses `WorkbookSession` from `App.Services` for all workbook state. `AvaloniaSaveCompletionPlanner` and `AvaloniaCloseDecisionPlanner` (extracted pure planners mirroring the WPF host pattern — tested in `FreeX.App.Avalonia.Tests`). Re-entrancy guards (`_isOpening`, `_isSaving`) are set before awaits to prevent overlapping operations. Save path captures `DirtyGeneration` before the first await and uses `TryMarkSavedIfNoEditsArrived` to detect mid-save edits without data loss.

New workbook creation is centralized in `NewWorkbookFactory`. Startup and File > New pass the full `FreeXOptions` object
so normalized default sheet count, font name, font size, and user name metadata seed the initial `Sheet1..N` workbook,
`StyleId.Default` style, and export document-property identity for newly created workbooks. Existing loaded workbooks keep their imported style registry; Options changes apply to
subsequent new workbooks.

Native `.fxl` files are versioned JSON documents. Current files declare `FileFormat = FreeX.NativeJsonWorkbook`,
`SchemaVersion = 1`, and `MinimumReaderVersion = 1`; unversioned legacy files remain readable and are migrated to the
current header on save, while future schema versions are rejected until an explicit migration is implemented.

## Key Architectural Decisions

See `docs/architecture/decisions/` for the full ADRs. Summary:

| ADR | Decision |
|-----|----------|
| [001](decisions/001-csharp-dotnet10-wpf.md) | C# 12 / .NET 10 / WPF for v1 |
| [002](decisions/002-style-registry.md) | Style registry: deduplicate by structural equality, `StyleId 0` = Default |
| [003](decisions/003-xlsx-fidelity.md) | XLSX fidelity contract: preserve modeled features, warn on unsupported package parts, and keep chart/shape theme-color fidelity partial until those adapters consume the workbook theme model |
| [004](decisions/004-volatile-functions.md) | Volatile functions: dirty-first evaluation order |
| [005](decisions/005-cross-sheet-references.md) | Cross-sheet refs: `Workbook?` threaded through evaluator chain |
| [006](decisions/006-find-replace.md) | Find & Replace: service in `Core.Commands`, `Func<Workbook>` in dialog |
| [007](decisions/007-commands-parity-closeout.md) | Commands parity closeout: model-backed gaps can go green; renderer/package/locale gaps stay explicit |
| [008](decisions/008-code-review-hardening-2026-05-28.md) | Code-review hardening (2026-05-28): 12 PRs covering correctness, security, performance, and architecture |

## Commands Parity Architecture

The May 2026 commands parity closeout keeps command mutation in `Core.Commands` and UI orchestration in `App.Host`.
Clipboard, paste, Format Painter, AutoFit, Format Cells, and Flash Fill are command-first features with undoable
model changes and focused planner/service tests. Rendering-only concerns, such as clipboard marquee, shrink-to-fit
text bounds, and deferred chart display, stay in `App.UI` or `App.Host`.
Border gallery presets are modeled as reusable `StyleDiff` planners in `Core.Commands`; `App.Host` only maps menu
choices to those planners and batches perimeter presets into one undoable command. Draw Border Grid and Erase Border
use the same remembered line color/style and grouped-sheet command path for clicked or dragged grid ranges. Draw Border
uses the outline/perimeter planner for clicked or dragged rectangular ranges, including all four edges for a single
cell; exact Excel freehand stroke fidelity remains a partial UI-fidelity gap.
Cell Style gallery commands use the shared `App.Services` preset planner to return deterministic `StyleDiff` values
for supported font, fill, border, number-format, alignment, and protection fields. The WPF host and Avalonia preview
shell both route selected-range preset application through the command bus, including workbook-theme-resolved accent
depth presets. The planner intentionally does not create workbook named styles, so full theme-bound named-style
semantics remain a parity gap.

Custom number formatting remains centralized in `Core.Calc.NumberFormatter`. It treats the `General` format token
case-insensitively and parses semicolon-delimited sections
into color, optional invariant numeric condition with signed/scientific thresholds and optional whitespace around
operators/thresholds, optional whitespace between leading color/condition directives, and cleaned format text before delegating to the existing numeric,
date/time, fraction, scientific, and text renderers. This keeps display behavior deterministic across machines while
supporting common Excel custom-format constructs such as conditional sections, named colors, default indexed `ColorN`
color prefixes with optional whitespace inside the bracket token, escaped literals including escaped layout directive characters, escaped section delimiters, and escaped
numeric-placeholder characters inside quoted-affix formats, explicit empty negative/zero positional sections and selected empty conditional date/time sections that suppress display, comma scaling, fixed and variable-denominator fractions, date/time, elapsed-time,
active `?` placeholder alignment spaces for ordinary integer/decimal numeric formats and numerator/denominator fraction fields, active percent scaling that preserves token placement and ignores quoted and escaped percent literals, text placeholders in either the fourth section or a single `@` section, explicit empty fourth text sections that suppress text display, text-section spacing/fill directives, visible currency symbols carried by LCID tokens including multi-character symbols in accounting fill-space patterns, raw and common-culture-labeled multi-character symbols generated by the Format Cells Accounting category, width-aware fill expansion for active `*` directives when the viewport supplies a column character width, and a bounded set of workbook-theme color directives: `ThemeDark1`, `ThemeLight1`, `ThemeDark2`, `ThemeLight2`, `ThemeAccent1` through `ThemeAccent6`, `ThemeHyperlink`, and `ThemeFollowedHyperlink`, including optional `TintNN` and `TintNN%` suffixes. Trailing active `_` skip directives keep modeled trailing spacing under width-aware formatting instead of falling back to generic left padding. Escaped `*` and `_` characters remain literals and do not trigger target-width expansion. Existing formatter calls without a target width continue to return compact deterministic text for clipboard, formulas, charts, and tests that do not need layout spacing. Full Excel localized currency-name catalogs and richer theme-token grammar beyond the modeled tint suffixes remain explicit parity gaps. Indexed color prefixes accept both compact `ColorN` and Excel-style spaced `Color N` tokens. Color prefixes and invariant numeric conditions are parsed at the section boundary and can
color numeric, date/time, and text-section display results. Color-token extraction only consumes recognized custom-format
colors, so elapsed-time bracket tokens such as `[h]`, `[m]`, and `[s]` remain available to the time formatter.
Date/time format conversion supports long and compact
AM/PM markers, disambiguates Excel `m`/`mm` tokens as minutes when adjacent to hour or second tokens across quoted
literals and bracket metadata, maps five-`m` month tokens to month initials, and rounds `.0`/`.00`/`.000`
fractional-second display to the requested precision for both clock time and elapsed-time formats. Elapsed-time
formats are shared by numeric serials and `DateTimeValue` serials so grid display is independent of which scalar type
holds the workbook value. Excel's special `[$-F800]` and `[$-F400]` tokens map to the current OS/.NET culture long-date and
long-time patterns for both date values and numeric date serials, including when Excel stores a trailing cached pattern
after the token, matching their system-format role in Excel while leaving explicit LCID separator mappings deterministic. The formatter also maps modeled LCIDs `401`, `402`, `404`, `405`, `406`,
`407`, `408`, `409`, `40A`, `40B`, `40C`, `40D`, `40E`, `410`, `411`, `412`, `413`, `414`, `415`, `416`, `418`, `419`, `41A`, `41B`, `41D`, `41E`, `41F`, `420`, `421`, `422`, `424`, `425`, `426`, `427`, `429`, `42A`, `42B`, `42C`, `434`, `435`, `436`, `437`, `439`, `43F`, `440`, `441`, `443`, `43E`, `450`, `453`, `454`, `455`, `45B`, `45E`, `461`, `463`, `468`, `46A`, `470`, `492`, `804`, `807`, `809`, `80A`, `813`, `816`, `100A`, `C01`, `C04`, `C09`, `C0C`, `C0A`, `1009`, `100C`, `1409`, `140A`, `1801`, `1809`, `180A`, `1C09`, `1C0A`, `200A`, `241A`, `240A`, `280A`, `280C`, `2C0A`, `300A`, `340A`, `3801`, `380A`, `380C`, `3C0A`, `400A`, `4009`, `445`, `447`, `449`, `44A`, `44E`, `440A`, and `500A` to deterministic decimal/group/date separators. The catalog can also carry non-Western group-size patterns, currently used for Indian grouping under `4009` (`en-IN`) plus native Indian LCIDs such as `439`, `445`, `449`, `44A`, and `44E`. For LCIDs that .NET can resolve, date/time format info starts from the platform culture so day and month names localize correctly, then FreeX reapplies the curated separator overrides. `WorkbookIndexedColorPalette` stores workbook-level overrides for Excel number-format `Color1` through `Color56`, seeded from XLSX `styles.xml` `indexedColors` when present and written back for authored overrides. `NumberFormatter` keeps the built-in palette as the fallback, accepts optional palette and workbook-theme contexts, and the viewport passes both so grid display applies loaded or authored indexed-color overrides plus the supported `Theme*` directives, including optional tint suffixes, to the cloned display style without mutating the style registry. Theme directives are ignored when theme context is not supplied. If an LCID token is not in the curated catalog,
`NumberFormatter` falls back fully to .NET `CultureInfo` number/date formats for that LCID or culture-name tokens such as `[$-fr-FR]`. Curated entries stay
authoritative for separators and grouping because they model Excel-specific or tested FreeX decisions; platform
globalization data broadens display for otherwise-unknown locale tokens and localized date names. Date serial rendering
keeps Gregorian calendar semantics when the resolved culture permits it, since FreeX's date serials follow Excel's
Gregorian serial-date model; output may still differ from Excel in edge locales or accounting-specific conventions.
The Format Cells Number tab uses the same formatter for its sample preview instead of a separate hardcoded preview
table when category controls synthesize a number format. Numeric preview formats with active layout directives use
the width-aware formatter path so accounting samples show the same modeled fill spacing as grid display, while
text-only custom formats with layout directives still preview text values. Its Accounting preset resolves to the modeled built-in
accounting code rather than the visually similar Currency code, so command selection, preview, and grid rendering share
the same accounting layout path. The live Accounting category controls also use the shared accounting builder, including
decimal-count-aware `?` placeholders in the zero section, so one-decimal and three-decimal accounting formats do not
fall back to the two-placeholder preset shape. The symbol picker keeps raw legacy symbol/code choices and adds
common `.NET` `RegionInfo` labels such as symbol plus native currency name and symbol plus English culture name;
selecting those labels still writes only the resolved symbol into the generated Currency or Accounting format code. Its Date and Time type lists expose the Excel `[$-F800]`
long-date and `[$-F400]` long-time special codes, but still delegate actual OS-localized rendering to
`NumberFormatter`. The Special category uses Excel-like labels such as Zip Code and Social Security Number as UI
aliases only; the dialog resolves them back to ordinary custom number-format codes before commands mutate cell styles.
Representative number, date/time, and text values keep the dialog preview aligned with the grid rendering path while
avoiding any new UI-specific formatter behavior.

Conditional Formatting authoring is split between lightweight WPF dialogs in `App.Host` and the `Core.Model`
`ConditionalFormat` model consumed by commands and XLSX IO. The rule manager clones the full modeled rule state
when editing or reordering so advanced rules such as color scales, data bars, icon sets, Top/Bottom, text, and date
rules do not lose fields. Cell-value thresholds may be constants or formulas, with relative references shifted from the
conditional-format range anchor, even though full Excel manager UI and icon rendering taxonomy remain partial.

Advanced chart families are modeled through `ChartType` and split between renderable/writable families and explicit
deferred families in `ChartTypeSupport`. The current renderable parity matrix covers 28 chart types, including classic
column/bar/line/pie/area/scatter/bubble/radar/stock/surface/3-D variants plus chartEx histogram, Pareto, waterfall,
treemap, sunburst, box-and-whisker, and funnel. `Core.IO` writes Excel-openable classic and chartEx packages, including
chartEx color/style sidecars (`id="10"` color style and `id="201"` chart style), relationships, content types, and the
AlternateContent drawing wrapper required by desktop Excel. `tools/FreeX.ChartInteropCompare` verifies FreeX render PNGs,
FreeX-authored XLSX opened/exported by Excel, Excel-authored XLSX opened/exported by Excel, and Excel-authored XLSX
loaded/saved by FreeX then reopened/exported by Excel. `ChartType.Map` remains recognized but deferred; unparseable or
unsupported chart package parts stay in the retained-opaque warning path.

PDF and XPS export share the WPF `PrintRenderer` so exported files match print preview layout. PDF export is implemented
through `PDFsharp-WPF` by rasterizing each `FixedDocument` page into a same-sized PDF page, then layering a simple vector
text overlay so exported worksheet text can be selected or searched while the raster page remains the visual source of
truth. Printed worksheet pages are `DrawingVisual` content, which cannot be introspected after drawing, so
`PrintRenderer` records the displayed cell strings, expanded header/footer text, comments-as-displayed note/comment text,
and page coordinates as `VisualHost` overlay metadata while it draws the raster page; worksheet-cell and header/footer
overlay strings are bounded to the same single-line ellipsis width used by their printed text so selectable/searchable
PDF text does not expose hidden clipped suffixes; draft-quality output skips displayed comment graphics and
their matching overlays, and workbook-scope bitmap page clones carry that metadata forward on an invisible host. The overlay
extractor also walks panel, decorator, and content-control wrappers so text nested
inside common WPF containers participates, and it flattens simple `TextBlock` `Run` and `LineBreak` inlines into the
same overlay stream, including `Run`/`LineBreak` content nested inside common `Span` derivatives such as bold and
italic inline containers. `InlineUIContainer` extraction recurses through simple visible wrappers such as decorators,
panels, content/header controls, and direct items controls without expanding arbitrary templates. WPF `AccessText` labels are also extracted with access-key underscores normalized out so searchable
PDF text matches the rendered label, and simple `TextBox` content is extracted with padding-aware positioning for
form-like fixed-document content. `RichTextBox` and `FlowDocumentScrollViewer` content is flattened through `TextRange`
so simple flow-document text also participates in search. Simple non-UIElement content on WPF `ContentControl` elements
such as labels is extracted through the same string value WPF renders, while UIElement content continues through the
traversal path. Simple non-UIElement headers and UIElement headers on `HeaderedContentControl` elements such as group
boxes are also extracted, and headered controls with both header and body text emit both strings. Simple non-UIElement
items and simple UIElement item content on
`ItemsControl` derivatives are emitted as overlay text through the same string value WPF renders for search and
selection while the raster page remains authoritative for item layout. Closed selector controls such as `ComboBox`
emit only the visible selected text instead of all drop-down items, so selectable/searchable PDF text mirrors the
collapsed raster state. Hidden and collapsed WPF elements are skipped so
the searchable overlay does not expose text absent from the raster page. Simple WPF `Glyphs.UnicodeString` runs are extracted as well,
using the glyph font URI name when present and an Arial overlay fallback otherwise. These text overlays improve select/search behavior without
promoting the whole PDF renderer to vector graphics. The Excel-like bitmap-text publish option is modeled on
`ExportOptions`; when selected it
keeps the raster page and suppresses the selectable text overlay for PDF output, matching the user's preference for
bitmap-only text when embedded-font fidelity is more important than search/select behavior. Printed worksheet hyperlinks are carried as separate `PdfLinkOverlay`
metadata on the same `VisualHost` boundary and are emitted as PDF `/Link` annotations after the raster page is drawn.
External web/file/email hyperlink targets are exported for included printed cells in active-sheet, selected-range, and
entire-workbook PDF exports, and bitmap-text mode does not suppress those link annotations because it only controls
selectable text overlays. Internal worksheet links (`PlaceInThisDocument`) are intentionally skipped until FreeX has
a PDF destination model that can map workbook locations to exported page coordinates. Embedded worksheet charts on
printed pages are rendered through the existing chart bitmap renderer and clipped to the printed page body so PDF and
XPS exports include the same raster chart content as print. The printed chart layer also records bounded selectable
PDF text overlays for fully visible embedded chart titles, X/Y axis titles including rotated Y-axis title metadata,
legend entries, category and value-axis tick labels, and data labels for classic embedded category charts, plus
slice legend entries and value/percentage data labels for embedded pie-family charts (pie, 3-D pie, and doughnut), while the
chart bitmap remains the visual source of truth. Vector chart graphics, chart-sheet pagination, full chart text
coverage, and full drawing-object z-order fidelity remain separate deferred scope. XPS export remains a separate ReachFramework-backed
path for Windows print-pipeline workflows. `ExportOptions` models active-sheet, selected-range, entire-workbook, and
one-based page-range scopes; selected-range export is implemented by passing a `GridRange` override into `PrintRenderer`,
workbook export combines visible worksheet documents rendered through the same sheet-level path, and active-sheet export resolves Excel-style grouped visible worksheets in workbook order rather than only the current sheet, PDF page ranges subset
the fixed-document pages directly, XPS page ranges wrap the renderer's `DocumentPaginator`, and the Excel-style
standard/minimum-size quality option is modeled explicitly. The Excel-style "Ignore print areas" option is modeled on
`ExportOptions` and flows into `PrintRenderer`; selected-range export still wins by passing an explicit range override,
while active-sheet and workbook export can bypass each sheet's stored `PrintArea` and render the used range. PDF export
honors the quality choice by changing raster page DPI while preserving the physical page size; XPS keeps the
print-pipeline paginator path. `ExportPlanner`
validates requested page-range starts and ends against the rendered page count before file creation, so out-of-range requests surface
as export-option errors instead of half-written files. `ExportReadinessPlanner` supplies the local Backstage status text
for PDF/XPS export readiness, selected-range availability, supported local options, and the no-Microsoft-account/cloud
boundary without invoking file dialogs or renderers. Extensionless export paths are normalized to `.pdf` when PDF is
inferred and to `.xps` when the save dialog explicitly selects XPS; explicit PDF/XPS save-dialog choices also replace
mismatched extensions so the written bytes and visible filename agree. PDF sheet-name bookmarks are modeled on `ExportOptions` and written through
`PdfDocument.Outlines`; bookmark targets are filtered and re-indexed after page-range selection so exported outlines
only point at pages that exist in the final PDF. Bookmark modes now distinguish sheet-name bookmarks, print-title
bookmarks derived from modeled repeated rows/columns with sheet-name fallback, and per-page number bookmarks. Bookmark-bearing PDFs request outline navigation through
`/PageMode /UseOutlines` and `/NonFullScreenPageMode /UseOutlines`. Bookmarks are intentionally PDF-only: the export options dialog labels
them as PDF bookmarks, and the dialog result factory only preserves a bookmark mode when the bookmark checkbox is
selected. XPS request summaries report selected bookmarks as PDF-only instead of silently treating XPS as
bookmark-capable. Likewise, XPS request summaries report the minimum-size quality choice as PDF-only because XPS uses
the fixed-document print pipeline instead of the PDF raster-DPI path, and report bitmap-text requests as PDF-only because
XPS is already written through the fixed-document package path. Full Excel document-property fidelity,
full Excel PDF publish options,
and full vectorization beyond simple text/link overlays remain parity gaps. PDF/A conformance and tagged PDF structure are
modeled as explicit unsupported publish choices: option summaries call them out, disabled dialog entries document the
boundary, and the export planner rejects requested PDF output that would otherwise silently produce a normal untagged
PDF.
When `IncludeDocumentProperties` is selected for PDF output, `App.Host` maps the current `Workbook` into
`PdfDocumentProperties` and writes the supported PDF Info dictionary fields. The current modeled subset is intentionally
small: workbook name becomes the PDF title and deterministic FreeX values fill author, subject, keywords, and creator.
PDF creator metadata still identifies FreeX on all generated PDFs; the exporter trims explicit PDF Info field values
and skips blank values before writing, so workbook-derived and future explicit metadata paths share one normalization
boundary. Generated PDFs default `/Lang` to deterministic `en-US` catalog metadata. The export options dialog exposes
that language tag as a normalized PDF-only option; known .NET culture tags are canonicalized from user input, including
underscore-to-hyphen cleanup and casing, invalid or blank tags fall back to `en-US`, the last accepted normalized user choice seeds the next export dialog, and the normalized value flows
through `ExportOptions.PdfLanguage` into the PDF catalog `/Lang` entry without affecting XPS package metadata. When a nonblank title is written, the exporter
also sets PDF viewer preferences to display the document title instead of the file name. Generated PDFs also set
`/PrintScaling /None` in viewer preferences so print dialogs that honor
the flag default to actual-size output instead of silently scaling exported worksheets, and set `/PageLayout /SinglePage`
by default so readers open exports in a predictable page-at-a-time view. Export options can override the initial PDF
layout to one-column or two-column variants and can request normal, bookmark-pane, or full-screen opening mode. They also set `/FitWindow` and `/CenterWindow` viewer
preferences as best-effort hints for PDF readers that honor window framing metadata, and `/PickTrayByPDFSize` so
print workflows can choose paper trays from exported worksheet page sizes when the reader/printer honors the hint. The option controls the additional
workbook-derived fields. XPS export writes the same modeled
title/creator/subject/keywords subset into the package core
properties when the option is selected and applies the same trim-and-skip normalization policy at the final
package-property boundary. This keeps document-property export useful without introducing a full Office
document-property subsystem.

PivotTable authoring remains model-first and worksheet-range only. `Core.Commands` owns undoable creation and refresh:
current-sheet insertion uses `AddPivotTableCommand`, while new-worksheet insertion uses `AddPivotTableToNewWorksheetCommand`
to create a unique PivotTable sheet, anchor the report at `A3`, and delegate cache/table materialization to the same
refresh path. `PivotTableRefreshService` also owns materialized value-cell formatting: supported built-in value-field
`numFmtId` values are resolved through `Core.Model.BuiltInNumberFormatCatalog` to `CellStyle.NumberFormat` codes before
PivotStyle visual styling is merged in, so number formats survive body, subtotal, grand-total, and stripe styling. Custom
PivotTable value-field number formats use
`Workbook.NumberFormatCatalog` for XLSX `numFmtId >= 164` entries; loaded data fields keep both the ID and resolved
format code, and authored catalogs are written back to `styles.xml`. When a generated stylesheet already uses a requested
custom ID for another format, the PivotTable catalog entry is remapped to the next free custom ID and authored or
source-preserved PivotTable XML is rewritten to match. The Value Field Settings dialog exposes a broad set of common
Excel-style built-in format presets covering integer/decimal number formats, comma and red-negative variants,
currency and accounting variants, short and long dates, time and elapsed-time formats, percentage, fraction, scientific, and text
formats while keeping the raw `numFmtId` override for loaded or advanced cases and editing custom format codes,
assigning authored custom codes to the workbook catalog path. Each preset gets its concrete format code from
`BuiltInNumberFormatCatalog`, so selecting a label such as Currency opens the nested Format Cells editor on the same
`$#,##0.00` code that refresh uses for `numFmtId=7`. Choosing a built-in preset clears any hidden custom format code left by the nested editor, preventing
stale custom codes from overriding the visible preset. When the nested editor returns a code that exactly matches a
known built-in preset, the dialog stores the built-in `numFmtId` instead of promoting that code to a custom catalog ID.
Duplicate preset aliases keep loaded or typed labels compatible, but the first preset for a built-in ID is the canonical
display label used when reopening the dialog.
`PivotTableModel.EmptyValueText` models Excel's "For empty cells show" option for generated matrix reports:
`PivotTableRefreshService` writes the configured text only for row/column intersections with no source rows, while
real zero aggregates, row totals, column totals, and grand totals remain numeric so formatting and calculations stay
predictable. Sheet cloning carries the option with the rest of the PivotTable model state. `PivotTableOptionsDialog`
and `ConfigurePivotTableOptionsCommand` are the command surface for editing this value; both normalize whitespace-only
input back to `null`, and the command snapshots the option with the rest of the PivotTable settings so undo restores
the previous rendered matrix.
`PivotTableModel.ErrorCaption` models the OOXML `errorCaption` option behind Excel's "For error values show" setting.
The PivotTable Options dialog and `ConfigurePivotTableOptionsCommand` edit and persist that caption with the same
whitespace-to-`null` behavior and undo snapshotting as the empty-cell caption. FreeX does not currently evaluate
PivotTable aggregate errors through a separate display-semantic path; the option is preserved for authored/read XLSX
metadata and future rendering support.
`PivotTableModel.GrandTotalCaption` models imported/authored custom grand-total caption text. The refresh service
materializes that caption for row-only, column-only, and matrix grand-total labels, and the same caption is used when
detecting grand-total cells for PivotStyle formatting, merged-label exclusion, and Show Details extraction.
Pivot cache data options remain owned by `PivotCacheModel`, not duplicated onto `PivotTableModel`. `PivotTableOptionsDialog`
reads the cache connected by `PivotTableModel.CacheId`, and `ConfigurePivotTableOptionsCommand` updates the cache's
`RefreshOnLoad`, `SaveData`, `EnableRefresh`, and `MissingItemsLimit` settings with undoable snapshots. The deleted-item
retention option follows OOXML's `missingItemsLimit`: `null` omits the attribute for Automatic, `0` means None, and the
dialog/command path normalizes positive selections to Excel's Maximum sentinel (`1,048,576`). This keeps XLSX cache
metadata, dialog state, and command mutation aligned while leaving external/OLAP cache execution out of scope.
`PivotStyleCatalog` owns the built-in `PivotStyleLight1..28`, `PivotStyleMedium1..28`, and `PivotStyleDark1..28`
name ranges shared by the full PivotTable Options dialog and the contextual PivotTable Design Styles gallery. Both
surfaces append the workbook's current authored style name when it is outside that built-in list. The Design gallery is
a focused style-only command surface instead of a shortcut to the full PivotTable Options dialog; OK applies just
`StyleName` through `ConfigurePivotTableOptionsCommand`, while Cancel/close performs no command. Other PivotTable
layout, display, cache, and print options remain unchanged and undo stays on the command path. This avoids
destructive style-name fallback when a loaded workbook uses a custom style while keeping the
visual renderer intentionally lightweight: `PivotStylePaletteResolver` maps selected built-in names to modeled header,
subtotal, grand-total, stripe, and border colors. When a workbook uses a custom theme, the supported Light/Medium/Dark
family subset, including `PivotStyleLight16` through `PivotStyleLight21`, resolves its base color from workbook theme accent slots and derives
subtotal, grand-total, stripe, and border colors through the same tint helper used by other theme-color references. The Office default keeps the existing fixed
palette snapshots for compatibility with current tests and loaded workbooks. Matrix row-grand-total columns are detected
from the header band and receive the same grand-total body styling as grand-total rows, while header cells keep
header-style precedence. Exact Excel table-style XML semantics and every built-in style's precise theme slot/tint recipe
remain partial.
`PivotTableModel.CompactRowLabelIndent` models Excel's compact-layout row-label indentation as style state instead of
embedding padding spaces into cell text. `PivotTableRefreshService` applies the configured indent to materialized compact
row-label cells after PivotTable visual styles, so the option composes with built-in style palettes and number-format
preservation. The PivotTable Options dialog clamps user-entered indentation to Excel's supported 0-15 style range, the
options command snapshots it for undo, sheet cloning carries it with the rest of the PivotTable model, and XLSX load/save
maps it through the pivot table definition `indent` attribute.
Nested PivotTable subtotal captions use the item from the field being subtotaled rather than always using the first row
field. This matters for compact reports with three or more row fields, where grouped `Region / Quarter / Channel`
outputs subtotal `Quarter` groups as `Q1 Total` or `Q2 Total` instead of repeating the outer `Region` caption for every
nested subtotal. Compact matrix reports materialize top or bottom subtotal rows for outer row groups, with each visible
column field intersection and the row grand-total column aggregated independently. `PivotTableModel.ShowFieldHeaders` models Excel's "Display field captions and filter drop-downs" option and maps to the
native `showHeaders` attribute. `PivotTableModel.ShowContextualTooltips` and
`PivotTableModel.ShowPropertiesInTooltips` model the PivotTable display tooltip options and map to native
`showDataTips` and `showMemberPropertyTips`. `PivotTableModel.ShowClassicLayout` models Excel's classic drag-in-grid
layout option and maps to native `showDropZones`. `PivotTableModel.MergeAndCenterLabels` models Excel's merge-label
layout option and maps to native `mergeItem`; refresh materializes it for non-compact row-label output by merging
contiguous repeated outer labels inside the PivotTable target range, including hidden-repeat continuation rows when
`RepeatItemLabels` is disabled, merging subtotal caption rows horizontally across the row-label field columns when no more-specific row label exists to the right, centering the retained top-left label cell in both directions while preserving any
PivotStyle-applied visual formatting, and merging compact matrix `Row Labels` headers vertically across bounded multi-row
column-header gaps. Stale PivotTable-owned merges are cleared before each refresh. `RepeatItemLabels`
and `BlankLineAfterItems` are honored by both row-only and row-plus-column matrix PivotTable materialization so outer
row labels and spacer rows behave consistently across report shapes. Exact Excel
merged-label behavior for compact layout remains separate visual fidelity work.
`PivotTableModel.ShowItemsWithNoDataOnRows` and `ShowItemsWithNoDataOnColumns` materialize row-field and column-field
items from PivotCache shared items even when the current source rows have no matching records. Refresh uses the same
cache-backed item-combination expansion for row-only, column-only, and matrix reports, and writes the configured
empty-cell text for generated no-data rows, columns, intersections, and subtotal value cells whose entire row group has
no source records.
`PivotTableModel.ShowExpandCollapseButtons` models Excel's on-screen PivotTable
expand/collapse button visibility separately from `PrintExpandCollapseButtons`. This follows OOXML's split between
`showDrill` for display state and `printDrill` for print output. `ConfigurePivotTableOptionsCommand` snapshots these
display/print flags independently, the Options dialog places display flags on the Display tab and the print flag on the
Printing tab, sheet cloning carries them, and XLSX load/save round-trips the attributes without deriving values from one
another.
`PivotTableModel.EnableDrill` models Excel's "Enable Show Details" PivotTable data option and maps to OOXML
`enableDrill`. The Options dialog exposes the setting on the Data tab, `ConfigurePivotTableOptionsCommand` snapshots it
for undo, and `DrillDownPivotTableCommand` refuses to create a detail sheet when the option is disabled. This keeps the
command behavior aligned with the persisted workbook option instead of treating `enableDrill` as passive metadata.
`PivotTableModel.PageOverThenDown` and `PivotTableModel.PageWrap` model Excel's report-filter field layout controls and
map to native `pageOverThenDown` and `pageWrap` attributes. They are surfaced through the PivotTable Options layout tab,
snapshotted by `ConfigurePivotTableOptionsCommand`, cloned with the sheet, and persisted through XLSX. The current grid
materialization writes page-field captions and selected-item text above the pivot body, using the modeled over-then-down
or down-then-over wrap order and leaving a blank row before the row/column/data-field body begins. PivotStyle rendering
uses that shifted body start for header, stripe, subtotal, grand-total, and compact-indent calculations, while applying
the selected PivotStyle header visual treatment to materialized report-filter caption/value cells above the separator
row. Exact Excel report-filter dropdown widgets and native visual details remain partial.
`PivotTableModel.AutofitColumnsOnUpdate` and `PivotTableModel.PreserveFormattingOnUpdate` model the two Excel
PivotTable Options format checkboxes that control update-time width and formatting behavior. They are stored as
PivotTable state, surfaced through `PivotTableOptionsDialog`, preserved by quick option commands when omitted,
snapshotted for undo, cloned with the sheet, and round-tripped through OOXML `applyWidthHeightFormats` and
`preserveFormatting` attributes. The current implementation records and preserves the user intent; full Excel
refresh-time layout heuristics remain separate from the option-state fidelity.
External/OLAP/data-model caches stay excluded from
execution; their package metadata is retained where covered by XLSX fidelity paths.
PivotCharts remain normal `ChartModel` instances bound back to `PivotTableModel` by name/cache metadata. The chart model
keeps a master `ShowPivotChartFieldButtons` switch plus per-button report-filter, axis-field, and value-field visibility
flags. `ChartRenderer` and `GridView` both honor the same flags, so rendered annotations and click targets stay aligned
when a user hides only one class of PivotChart field button. The PivotChart Options command is the owning mutation path
for these flags: `ConfigurePivotChartOptionsCommand` snapshots the master and per-button visibility booleans with the
chart style ID so undo restores the complete field-button state, while the host dialog exposes the same booleans rather
than keeping hidden UI-only state. Native JSON persists the PivotChart binding fields, chart style ID, field-button
visibility flags, and modeled chart design metadata such as pivot format XML, date-system/language, manual layouts,
external-data pointers, protection, print settings, rounded corners, blank display, and hidden-row display flags so
FreeX-authored workbooks do not lose chart option state outside XLSX.
Slicer and timeline metadata stays model-first for filters/cache linkage, with native floating drawing parts preserved
best-effort by package merge. For native drawing fidelity, `Core.IO` reads `twoCellAnchor` coordinates and nonvisual
shape names from related worksheet drawing parts into nullable `DrawingAnchor` and `DrawingShapeName` metadata on
`SlicerModel` and `TimelineModel`. Newly authored slicers and timelines receive a deterministic lightweight two-cell
anchor immediately to the right of the connected PivotTable target range so the existing drawing path can show them
without requiring a save/load round trip. Timeline authoring requires at least one real `DateTimeValue` in the selected
source field, matching the timeline filtering command's date-row semantics instead of treating ordinary numbers as date serials.
`MainWindow` passes anchored slicers/timelines connected to PivotTables on the active
sheet into `GridView`, which maps the two-cell anchors to viewport pixels and redraws lightweight native-control visuals
or object placeholders. Exact Excel styling and placement on sheets that differ from the connected PivotTable sheet remain
partial because the model does not yet persist the owning worksheet for native control drawing parts; unsupported drawing
XML remains package-preserved.

Structured table authoring stays command-owned. `CreateStructuredTableCommand` creates the model metadata and
`CreateStyledStructuredTableCommand` layers visible banding as one undoable operation. Loaded table totals metadata is
materialized by `RefreshStructuredTableTotalsCommand`, which writes totals-row labels, explicit totals formulas as text,
and common Excel totals functions (`sum`, `average`, `count`, `countNums`, `min`, and `max`) from the table data rows.
The command snapshots affected totals-row cells for undo. Basic structured-reference formulas are resolved from
`StructuredTableModel` metadata at formula evaluation and dependency-registration time through
`StructuredReferenceResolver`; formulas keep their `TableName[ColumnName]` shape instead of being rewritten to A1 ranges.
The evaluator carries the formula cell address in its context so current-row references can resolve relative to the
hosting table data row. The supported slice covers same-workbook data-body column references such as `Sales[Amount]`,
whole-table section selectors `#Headers`, `#Data`, `#All`, and `#Totals`, common section-column intersections such as
`Sales[[#Totals],[Amount]]`, and scalar current-row references such as `[@Amount]` or `Sales[@Amount]` when the formula
cell is inside the table data body. Data-body and section-scoped multi-column ranges such as `Sales[[Amount]:[Tax]]`
and `Sales[[#Data],[Amount]:[Tax]]` resolve to rectangular table ranges. Excel's `#This Row` selector resolves through
the same current-cell context as `[@Column]`, including row-scoped column ranges such as
`Sales[[#This Row],[Amount]:[Tax]]`. Unqualified `#This Row` selectors bind to the containing table for calculated
column-style formulas, for example `[[#This Row],[Amount]:[Tax]]`. Current-row references outside a table data row and
external workbook structured references remain outside this slice. Custom authored table-only style XML is retained on
load and fresh save, and supported custom table style DXFs materialize `wholeTable`, `headerRow`, `totalRow`,
row/column stripe, first/last column, and first/last header/total cell semantics. Unsupported/full Excel-only table
style elements remain raw XML-preserved and otherwise uninterpreted. Built-in table styles `TableStyleMedium2`-
`TableStyleMedium7` and `TableStyleLight16`-`TableStyleLight21` resolve Accent 1-6 banding from the active workbook
theme for gallery swatches and Format as Table materialization.

Flash Fill remains a deterministic pattern service, not an Excel-like ML inference engine. It supports conservative
single-column transforms including dotted-token extraction with variable dot counts for final-token patterns, exact three-token first or middle dotted-token extraction, middle-token removal across exact three-token dotted or delimiter-separated values, leading dotted-token removal, leading delimiter-token removal, local file final path stem and parent directory extraction,
dotted/underscored/hyphenated email display-name cleanup, plus-address email local-part tag removal,
spaced or compact colon/equal, slash, pipe, ASCII/Unicode arrow, and hyphen/en dash/em dash label-value splitting, first or last paired-delimiter qualifier extraction, semicolon-separated URL query-parameter first-name, last-name, titleized first/last-name, first-value, last-value, titleized value, same-name first-value, and same-name last repeated-value extraction, URL fragment extraction/titleization, decoded first, second, and parent URL path segment extraction and titleization, digit-mask formatting
such as phone-number punctuation copied from examples, calendar-valid embedded-date extraction/normalization and simple calendar-quarter extraction such as `2023-02-09` to `Q1` from
labeled text or direct dates with ambiguous multi-date sources rejected, weekday-prefixed and embedded numeric and English month-name date
component extraction that preserves raw month tokens while normalizing ordinal day tokens, pure and embedded time component extraction for hour, minute, second, and
meridiem from time-like values or labeled text with ambiguous component examples rejected, embedded time extraction/normalization plus supported two-time range endpoint extraction, including same-label ranges when endpoint examples are unambiguous, with
ambiguous multi-time sources rejected, US address component extraction including street unit suffixes/identifiers with spaced hash unit forms, known title/honorific, credential, and organization legal-suffix cleanup including international forms such as `Sdn Bhd`, `E.U.R.L.`, and `Zrt`,
and two-part full-name reordering such as `Ada Lovelace` to `Lovelace, Ada`, plus a small multi-column pattern set for adjacent first/last and first/middle/last name columns including initialed variants such as `Lovelace, A. B.`, `Ada Byron L.`, and `A. B. L.`. First/last-name,
first-initial/last-name, last-name/first-initial, and single-source plus adjacent-column first/middle/last middle-initial email generation, including reversed last/first/middle-initial aliases such as `lovelace.ada.b@contoso.com`, learn constant
domains and modeled `.`, `_`, or `-` separators from examples, as do first-name/last-initial aliases and first/middle/last column email aliases that use only the first and last name. Domain/public-suffix extraction
recognizes bounded common multi-label suffixes such as `co.uk`, `com.au`, and `co.nz`, including root-domain stem and suffix outputs.
It returns no result when the examples are ambiguous.

Spell Check remains a deterministic known-corrections service in `Core.Commands`, not dictionary-backed proofing. It
scans literal text cells, notes, threaded comment roots, and threaded comment replies in deterministic sheet/address order
and plans undoable replacement edits while leaving formula cells alone. The known-corrections catalog covers bounded
office, spreadsheet, data/analytics, sales/marketing/customer, customer-service/helpdesk/SLA, subscription/licensing/renewal, media/creative/design, IT/cloud/system, telecom/networking, formula/function/reporting, product/engineering/planning, quality/testing, documentation/support, reliability/maintenance, operations/planning, budget/stakeholder/project-control, procurement/inventory/supplier, finance/accounting/ledger, tax/audit/billing, banking/treasury, insurance/actuarial, healthcare/clinical, education/academic, facilities/real-estate, manufacturing/production, retail/e-commerce, energy/utilities, environment/sustainability, construction/field-service, transport/logistics, hospitality/food-service, travel/events, sports/fitness/wellness, public-safety/weather/emergency, government/public-sector, nonprofit/fundraising, research/lab/science, agriculture/field-operations, risk/action, invoice/supply-chain, meeting/communication, people/HR, UI/accessibility/ribbon, release/packaging/installer, localization/globalization/resource, legal/compliance, and security/access vocabulary while preserving ignored URL, email,
file path, identifier, and prefixed-word spans.
The host workflow keeps Ignore All case-insensitive for the current pass and persists Add to Dictionary custom words
through `FreeXOptions` so matching scanner results stay suppressed across sessions/workbooks without introducing a full
proofing dictionary engine.
The Options dialog's Proofing page edits that same custom dictionary through a local list model so OK persists
normalized add/remove/clear changes and Cancel leaves the original options object untouched.

Accessibility Checker remains a deterministic model-backed audit in `Core.Commands`, not a full WCAG or screen-reader
engine. It reports issues supported by current workbook state, including merged cells, blank structured-table headers,
low-contrast cell text against base, workbook theme/tint, patterned fills, and the modeled conditional-format formula
subset including scalar comparisons, simple arithmetic operands including unary negate/percent, finite `^`, and `+`/`-`/`*`/`/`, common scalar numeric functions (`ABS`, `INT`, `EVEN`, `ODD`, `ROUND`, `ROUNDUP`, `ROUNDDOWN`, `TRUNC`, `FACT`, `FACTDOUBLE`, `MROUND`, `CEILING`, `CEILING.MATH`, `CEILING.PRECISE`, `ISO.CEILING`, `FLOOR`, `FLOOR.MATH`, `FLOOR.PRECISE`, `MOD`, `QUOTIENT`, `COMBIN`, `COMBINA`, `PERMUT`, `PERMUTATIONA`, `MULTINOMIAL`, `GCD`, `LCM`, `SQRT`, `SQRTPI`, `SIGN`, `POWER`, `EXP`, `LN`, `LOG`, `LOG10`, `PI`, `DEGREES`, `RADIANS`, `SIN`, `CSC`, `CSCH`, `SINH`, `ASINH`, `ACOSH`, `COSH`, `SECH`, `TANH`, `ATANH`, `ACOTH`, `COTH`, `ASIN`, `ACOS`, `ACOT`, `COS`, `SEC`, `COT`, `TAN`, `ATAN`, `ATAN2`, `SERIESSUM`, `DELTA`, `ERF`, `ERF.PRECISE`, `ERFC`, `ERFC.PRECISE`, `GESTEP`, `BITAND`, `BITOR`, `BITXOR`, `BITLSHIFT`, `BITRSHIFT`, `BIN2DEC`, `HEX2DEC`, `OCT2DEC`, `DEC2BIN`, `DEC2HEX`, `DEC2OCT`, `BIN2HEX`, `BIN2OCT`, `HEX2BIN`, `HEX2OCT`, `OCT2BIN`, `OCT2HEX`, `BASE`, `DECIMAL`, `CONVERT`, `COMPLEX`, `IMREAL`, `IMAGINARY`, `IMABS`, `IMARGUMENT`, `IMCONJUGATE`, `IMCOS`, `IMCOSH`, `IMCOT`, `IMCSC`, `IMCSCH`, `IMDIV`, `IMEXP`, `IMLN`, `IMLOG10`, `IMLOG2`, `IMPOWER`, `IMPRODUCT`, `IMSIN`, `IMSINH`, `IMSEC`, `IMSECH`, `IMSQRT`, `IMSUB`, `IMSUM`, and `IMTAN`), text functions (`LEN`, `LENB`, `UPPER`, `LOWER`, `TRIM`, `LEFT`, `LEFTB`, `RIGHT`, `RIGHTB`, `MID`, `MIDB`, `FIND`, `FINDB`, `SEARCH`, `SEARCHB`, `EXACT`, `CONCAT`, `CONCATENATE`, `TEXTJOIN`, `SUBSTITUTE`, `REPLACE`, `REPLACEB`, `ARABIC`, `ROMAN`, `UNICHAR`, `UNICODE`, `CHAR`, `CODE`, `PROPER`, `REPT`, `CLEAN`, `T`, `VALUE`, `NUMBERVALUE`, `TEXT`, `FIXED`, `DOLLAR`, `ASC`, `DBCS`, `JIS`, `BAHTTEXT`, `TEXTBEFORE`, `TEXTAFTER`, `TEXTSPLIT`, `VALUETOTEXT`, `ARRAYTOTEXT`, `REGEXEXTRACT`, `REGEXREPLACE`, `REGEXTEST`, `ENCODEURL`, `FILTERXML`, and `PHONETIC`), information/reference functions (`N`, `TYPE`, `ERROR.TYPE`, `ADDRESS`, `CELL`, `INFO`, `FORMULATEXT`, `HYPERLINK`, `SHEET`, `SHEETS`, and `GETPIVOTDATA`), statistical selection/distribution functions (`LARGE`, `SMALL`, `RANK`, `RANK.EQ`, `RANK.AVG`, `PERCENTILE`, `PERCENTILE.INC`, `PERCENTILE.EXC`, `QUARTILE`, `QUARTILE.INC`, `QUARTILE.EXC`, `PERCENTRANK`, `PERCENTRANK.INC`, `PERCENTRANK.EXC`, `MODE`, `MODE.SNGL`, `PROB`, `PERCENTOF`, `NORMDIST`, `NORM.DIST`, `NORMINV`, `NORM.INV`, `NORMSDIST`, `NORM.S.DIST`, `NORMSINV`, `NORM.S.INV`, `PHI`, `GAUSS`, `STANDARDIZE`, `TDIST`, `T.DIST`, `T.DIST.RT`, `T.DIST.2T`, `TINV`, `T.INV`, `T.INV.2T`, `FDIST`, `F.DIST`, `F.DIST.RT`, `FINV`, `F.INV`, `F.INV.RT`, `CHIDIST`, `CHISQ.DIST`, `CHISQ.DIST.RT`, `CHIINV`, `CHISQ.INV`, `CHISQ.INV.RT`, `BETA.DIST`, `BETA.INV`, `BETADIST`, `BETAINV`, `GAMMA`, `GAMMA.DIST`, `GAMMA.INV`, `GAMMADIST`, `GAMMAINV`, `GAMMALN`, `GAMMALN.PRECISE`, `LOGNORM.DIST`, `LOGNORM.INV`, `LOGNORMDIST`, `LOGINV`, `EXPON.DIST`, `EXPONDIST`, `WEIBULL`, `WEIBULL.DIST`, `CORREL`, `PEARSON`, `COVARIANCE.P`, `COVARIANCE.S`, `RSQ`, `SLOPE`, `INTERCEPT`, `FORECAST`, `FORECAST.LINEAR`, `STEYX`, `KURT`, `SKEW`, `SKEW.P`, `TRIMMEAN`, `FISHER`, `FISHERINV`, `BINOM.DIST`, `BINOMDIST`, `BINOM.DIST.RANGE`, `BINOM.INV`, `CRITBINOM`, `HYPERGEOM.DIST`, `HYPGEOMDIST`, `NEGBINOM.DIST`, `NEGBINOMDIST`, `POISSON`, `POISSON.DIST`, `CONFIDENCE`, `CONFIDENCE.NORM`, `CONFIDENCE.T`, `Z.TEST`, `ZTEST`, `T.TEST`, `TTEST`, `F.TEST`, `FTEST`, `CHISQ.TEST`, and `CHITEST`), financial functions (`PMT`, `PV`, `FV`, `NPER`, `RATE`, `IPMT`, `PPMT`, `ISPMT`, `NPV`, `IRR`, `MIRR`, `XNPV`, `XIRR`, `SLN`, `SYD`, `DB`, `DDB`, `VDB`, `AMORDEGRC`, `AMORLINC`, `EFFECT`, `NOMINAL`, `RRI`, `PDURATION`, `FVSCHEDULE`, `CUMIPMT`, `CUMPRINC`, `DOLLARDE`, `DOLLARFR`, `COUPDAYBS`, `COUPDAYS`, `COUPDAYSNC`, `COUPNCD`, `COUPNUM`, `COUPPCD`, `ACCRINT`, `ACCRINTM`, `DISC`, `INTRATE`, `RECEIVED`, `PRICEDISC`, `PRICEMAT`, `TBILLEQ`, `TBILLPRICE`, `TBILLYIELD`, `DURATION`, `MDURATION`, `PRICE`, `YIELD`, `YIELDDISC`, `YIELDMAT`, `ODDFPRICE`, `ODDFYIELD`, `ODDLPRICE`, and `ODDLYIELD`), date/time functions (`DATE`, `TIME`, `DATEVALUE`, `TIMEVALUE`, `YEAR`, `MONTH`, `DAY`, `HOUR`, `MINUTE`, `SECOND`, `WEEKDAY`, `WEEKNUM`, `ISOWEEKNUM`, `EDATE`, `EOMONTH`, `DAYS`, `DATEDIF`, `DAYS360`, `YEARFRAC`, `WORKDAY`, `WORKDAY.INTL`, `NETWORKDAYS`, `NETWORKDAYS.INTL`, `TODAY`, and `NOW`), lookup/reference functions (`CHOOSE`, `MATCH`, `XMATCH`, `INDEX`, `VLOOKUP`, `HLOOKUP`, `XLOOKUP`, `LOOKUP`, `OFFSET`, and `INDIRECT`), row/column/reference-dimension functions (`ROW`, `COLUMN`, `ROWS`, `COLUMNS`, and `AREAS`), matrix/array functions (`MMULT`, `MDETERM`, `MINVERSE`, `TRANSPOSE`, and `MUNIT`), and bounded dynamic-array/reference shaper functions (`SEQUENCE`, `TAKE`, `DROP`, `EXPAND`, `CHOOSECOLS`, `CHOOSEROWS`, `TOCOL`, `TOROW`, `WRAPROWS`, `WRAPCOLS`, `HSTACK`, `VSTACK`, `FILTER`, `SORT`, `SORTBY`, `UNIQUE`, and `TRIMRANGE`), bounded `SUM`/`SUBTOTAL`/`AGGREGATE`/`SUMSQ`/`SUMXMY2`/`SUMX2MY2`/`SUMX2PY2`/`SUMPRODUCT`/`AVEDEV`/`DEVSQ`/`PRODUCT`/`AVERAGE`/`AVERAGEA`/`MEDIAN`/`MIN`/`MINIFS`/`MINA`/`MAX`/`MAXIFS`/`MAXA`/`COUNT`/`COUNTA`/`COUNTBLANK`/`STDEV`/`STDEV.S`/`STDEVP`/`STDEV.P`/`STDEVA`/`STDEVPA`/`VAR`/`VAR.S`/`VARP`/`VAR.P`/`VARA`/`VARPA`/`GEOMEAN`/`HARMEAN`/`SUMIF`/`COUNTIF`/`AVERAGEIF`/`SUMIFS`/`COUNTIFS`/`AVERAGEIFS` aggregate operands over literals, shifted references, scalar direct operands, nested bounded aggregate arguments, and guarded finite ranges, database aggregate functions (`DSUM`, `DAVERAGE`, `DCOUNT`, `DCOUNTA`, `DGET`, `DMAX`, `DMIN`, `DPRODUCT`, `DSTDEV`, `DSTDEVP`, `DVAR`, and `DVARP`) over shifted database and criteria ranges, truthiness predicates, zero-argument `TRUE()`/`FALSE()`/`NA()` functions, simple `IS*` predicates including `ISREF`/`ISFORMULA`, simple `IF`, `IFERROR`, `IFNA`, `IFS`, and `SWITCH` selector wrappers, and simple `XOR` logical wrappers, low-contrast text boxes against
explicit, theme, and workbook object-default fills, missing or generic PivotTable alternate title/description text, missing or generic object alternate/title/name text including common default object labels, compact/separator-numbered defaults, dated screenshot/photo variants including date/time screen-capture filename forms, camera/phone default filenames, and copied image filename defaults, hidden sheets/rows/columns with
modeled content including hyperlinks, structured tables, PivotTables, visible embedded charts, sparklines, pictures, shapes, and text boxes, hyperlink display text that is blank, an expanded generic action phrase such as `click for more details`, `click this link`, `this link`, `learn more here`, `more information`, `additional details`, `view link`, or `visit link`, a commerce CTA, a signup/support CTA, URL-like, or unclear, and charts whose title or axis title is missing or generic as the current accessible label.

Native JSON persists the local threaded-comment model, including author, replies, created/modified UTC activity
metadata, and resolved state, so FreeX's in-app comment threads survive native save/load. Comment navigation and
printable comment summaries surface authors, replies, and resolved state from that model. Reply edit/delete actions
can update resolved state atomically, keep disabled states aligned with selected/blank replies, support Ctrl+Enter for
selected reply edits, and undo restores the prior thread. XLSX threaded-comment roots and replies round-trip through
the modeled writer; cloud identity and coauthoring semantics remain outside the local model.

Selection Pane object editing uses lightweight `Name` fields on charts, pictures, text boxes, and drawing shapes.
Generated names remain the fallback when no explicit name is modeled. Visibility, z-order, and rename edits stay in
`Core.Commands`; `RenameSelectionPaneObjectCommand` snapshots the previous name for undo, while the host dialog only
plans rename/visibility/move changes from buttons, list keyboard shortcuts, or mixed drawing-object drag reorder and applies them through the command bus as
one `CompositeWorkbookCommand`, so a single dialog acceptance is one undo step. Native JSON persists modeled object names. XLSX drawing object name
load/save maps the drawing non-visual `cNvPr/@name` value for charts, pictures, text boxes, and drawing shapes to
the modeled object name, while deeper Office drawing IDs and other non-visual metadata remain best-effort package
details rather than first-class model state. `GridView` exposes interactive non-chart object handles for selected
shapes, text boxes, and pictures through the object-selection adornment layer; hidden/display-none objects are skipped,
and selected image crop handles remain supported on the same rendering path.

The Backstage File > Info panel is a host-only summary surface over existing model services. It reads
`WorkbookStatisticsService` and `AccessibilityCheckerService`, then formats protection/status copy through
`InfoPanelSummaryPlanner` when the Info view opens. It also reuses `ShareWorkbookPlanner` to show whether the
currently saved local file is ready for Windows Share or must go through Save As first, and `ExportReadinessPlanner`
to show local PDF/XPS readiness without requiring the workbook to be saved first. It does not introduce cloud account, version-history,
template, Document Inspector, or extended document-metadata subsystems.

The Backstage Account action is also local-only. `LocalAccountPlanner` reports the FreeX user name, Windows account,
device, app version, options file path, current workbook save/path status, Windows Share readiness, and PDF/XPS export readiness while explicitly
stating that Microsoft 365 sign-in, cloud links, and coauthoring are not implemented. Share readiness is planned through
`ShareWorkbookPlanner`, which trims and normalizes absolute local file paths, routes missing/invalid/unsaved paths
through Save As, and hands Windows Share the normalized local path.

Error Checking remains a deterministic model-backed audit in `Core.Commands`, not a full Excel heuristic inference
engine. It reports cached formula error values (`#DIV/0!`, `#VALUE!`, `#REF!`, `#NAME?`, `#N/A`, `#NUM!`, `#NULL!`, `#SPILL!`, `#CALC!`, and `#CIRCULAR!`), text cells that parse as finite invariant-culture numbers including
fullwidth digit/comma/decimal/scientific-notation forms with normalized exponent signs including Unicode minus plus small comma/decimal/sign, ordinary-space and no-break/thin-space group separators, trailing-sign number text, and Arabic-Indic/extended Arabic-Indic digit/decimal/thousands/percent variants, supported currency including fullwidth dollar/pound/yen/won and small dollar symbols, ASCII/fullwidth/small/Arabic percent, accounting-parentheses, and Unicode/fullwidth leading-sign forms, formulas
stored as text including apostrophe-prefixed and fullwidth-equals imports, two-digit-year text dates including fullwidth digit/Latin-letter/separator/comma variants, formulas whose direct parser-extracted precedents include missing or blank cells, table calculated
column formulas that differ from the column formula, and common aggregate formulas (`SUM`, `SUMSQ`, `AVEDEV`, `AVERAGE`,
`AVERAGEA`, `COUNT`, `COUNTA`, `DEVSQ`, `GEOMEAN`, `HARMEAN`, `MEDIAN`, `MIN`, `MINA`, `MAX`, `MAXA`, `PRODUCT`, `STDEV`,
`STDEVP`, `STDEV.S`, `STDEV.P`, `VAR`, `VARP`, `VAR.S`, `VAR.P`, `SUBTOTAL`, `AGGREGATE`) that omit valued adjacent cells, including through same-sheet named-range
arguments or valued gaps between separate arguments. It also reports literal cells whose values fail applied
data-validation rules. Rule toggles use
`Workbook.DisabledFormulaErrorCodes`, and per-cell ignore state reuses `Cell.IgnoreFormulaError` for both formula-error
and non-error issue kinds.

XLSX worksheet `ignoredErrors` fidelity uses that same `Cell.IgnoreFormulaError` bit as the modeled state. `Core.IO`
loads supported active `ignoredError` `sqref` cells/ranges into the bit and authors a conservative modeled
`ignoredErrors` block on save; detailed native ignored-error flags and unsupported reference forms are retained or
merged best-effort from the source package rather than fully interpreted.

XLSX worksheet `cellWatches` fidelity uses `Workbook.WatchedCells` as the durable modeled state shared with the
Watch Window services. `Core.IO` loads supported single-cell A1 `cellWatch/@r` refs with sheet IDs, skips malformed
refs without creating cells, and authors grouped worksheet `cellWatches` blocks on save. Native-only watch attributes
and unsupported entries are merged best-effort from the source package by matching `r` refs so modeled watches do not
duplicate retained source watches.

XLSX custom-view fidelity uses `Workbook.CustomViews` as the durable modeled state shared with Custom Views commands,
Native JSON, and the host dialog. `Core.IO` loads workbook `customWorkbookView` name/GUID entries only when matching
worksheet `customSheetView` entries provide view state that FreeX can represent: view mode, simple frozen/split
panes, gridline/headings/ruler/formula visibility, and zoom. The optional custom-view ID is persisted in the model for
stable XLSX GUID round-trip. Source-package merge treats modeled GUIDs as authoritative while preserving native-only
attributes and retaining unmatched native custom views best-effort; print settings, filter state, hidden row/column
snapshots, selections, personal-view metadata, and window geometry stay outside the modeled subset.

XLSX worksheet `scenarios` fidelity uses `Workbook.Scenarios` as the durable modeled state shared with the Scenario
Manager commands and UI. `Core.IO` loads supported worksheet `scenario` entries only when every `inputCells/@r` is a
same-sheet A1 cell reference and every changing value is a literal `@val`; load records definitions without applying
them. On save, workbook scenarios are grouped by sheet, so a cross-sheet model scenario becomes one worksheet scenario
entry per touched sheet with the shared scenario name. Source-package merge treats supported scenario names as
model-authoritative, preserving native attributes and safe children for still-modeled scenarios while avoiding
resurrection of removed supported entries; malformed or unsupported native-only scenario entries remain best-effort.

XLSX worksheet custom-property fidelity uses `Sheet.CustomProperties` as the durable modeled state. `Core.IO` loads
supported `customProperties/customPr` name/id pairs, writes them back on save, and persists them through Native JSON.
During source-package merge, supported modeled names are authoritative: matching native attributes and child elements
are copied onto still-modeled properties, removed supported entries are not resurrected, and malformed native-only
property entries remain best-effort.

XLSX worksheet calculation-property fidelity uses `Sheet.FullCalculationOnLoad` as the modeled subset of
`sheetCalcPr`. `Core.IO` loads and writes `sheetCalcPr/@fullCalcOnLoad`, persists it through Native JSON, and treats
the modeled flag as authoritative during source-package merge: native-only attributes and child elements are retained,
but a cleared modeled flag is not restored from the source worksheet.

XLSX worksheet phonetic-property fidelity uses `Sheet.PhoneticProperties` as raw worksheet-level metadata for
`phoneticPr` fontId/type/alignment attributes. FreeX does not render or edit phonetic text, but `Core.IO` loads,
writes, and persists those stable attributes through Native JSON. Source-package merge treats the modeled attributes as
authoritative while preserving native-only phonetic attributes and child elements best-effort.

XLSX workbook and worksheet view fidelity splits modeled view state from native supplemental views. The primary workbook
view continues to use workbook properties such as sheet-tab visibility, tab ratio, first visible sheet, and active tab;
additional native `workbookView` entries load into `Workbook.AdditionalViews`, persist through Native JSON, and save back
after ordinary model edits. The primary worksheet view continues to use `Sheet` view fields such as pane, zoom, gridline,
heading, ruler, formula, and active/top-left state; non-primary worksheet `sheetView` entries load into
`Sheet.AdditionalViews`, persist through Native JSON, and save back while native-only primary-view metadata remains
source-package retained.

XLSX worksheet sort-state and data-consolidation fidelity uses raw worksheet metadata models for load/save durability.
`Sheet.SortState` captures the worksheet `sortState` block, its stable attributes, and sort-condition metadata, and
`Sheet.DataConsolidation` captures the worksheet `dataConsolidate` block plus `dataRef` entries. Both persist through
Native JSON and round-trip back to XLSX; FreeX still defers full Excel UI/editing/execution semantics for those
surfaces.

XLSX worksheet allow-edit range fidelity uses `Sheet.AllowEditRanges` as the durable modeled state. `Core.IO` loads
supported single-area `protectedRange/@sqref` entries, skips malformed or multi-area entries as native-only metadata,
and writes modeled `protectedRanges` on save. During source-package merge, modeled supported `sqref`s are authoritative:
matching native attributes and child elements are copied onto still-modeled ranges, removed modeled ranges are not
resurrected, and unsupported native-only `protectedRange` entries are retained best-effort.

XLSX worksheet page-break fidelity uses `Sheet.RowPageBreaks` and `Sheet.ColumnPageBreaks` as the durable modeled
state. `Core.IO` lets ClosedXML load and author supported manual row/column break IDs, then merges native attributes
only onto still-modeled matching `<brk>` entries. Removed modeled breaks are not resurrected from the source package,
while malformed or native-only break entries are retained best-effort.

XLSX worksheet print/layout fidelity uses the `Sheet` print options and page setup fields as the durable modeled state.
During source-package metadata merge, `Core.IO` retains only native-only `printOptions` and `pageSetup` attributes such as
printer defaults or copy counts. Modeled attributes for gridlines, headings, centering, orientation, paper, scale,
first-page number, print quality, comment/error printing, black-and-white, and draft quality are not copied back from the
source worksheet when ClosedXML omits or rewrites them. Printer settings binary parts and `pageSetup` relationships stay
owned by the dedicated printer-settings retention path.

## Current Architectural Limitations

- Sheet rename rewrites existing sheet-qualified formula references through the formula AST/serializer path
- `Core.Model` has a workbook theme scaffold with native and XLSX theme-part persistence, loaded-cell-style theme-color resolution, drawing-object theme color references, chart theme-color references/rendering, loaded `fmtScheme` OOXML preservation on save and across modeled effect-name changes, first-supported-`effectStyle` group interpretation for bounded `outerShdw`/`prstShdw`/`innerShdw`/`glow`/`softEdge` non-chart drawing-object theme effects, and an undoable `SetWorkbookThemeCommand`; `Core.IO` has reusable DrawingML color parsing plus worksheet/drawing relationship-based load/save for embedded package parts for every current native chart type, including `twoCellAnchor` chart bounds/EMU offsets, `oneCellAnchor` bounds, `absoluteAnchor` bounds, no-header and no-category-column series range semantics, chart title/range with title text color/font size, axis titles with text color/font size, value-axis bounds/units/log-scale/number formats, axis gridline visibility/color/thickness, tick marks, axis label visibility, axis line color/thickness, legend visibility/position/text/fill/border/theme-text/font-size, global data-label visibility/position/content/number-format/fill/border/text/font/rotation/callout baseline, per-point data-label fill/border/text/font formatting, trendline type/equation/R-squared/line formatting, common column/area combo line-overlay and column/area/line/scatter secondary-value-axis package state, chart/plot area fill and plot border, bar direction/grouping, scatter/bubble X/Y ranges and value-axis pairs, bubble-size ranges, pie/doughnut first-slice angle and exploded-slice package state, doughnut hole size, line/scatter series color-width-dash-marker and marker-fill package formatting, filled-series fill/outline color-width-dash package formatting, chartEx style/color sidecars, chartEx axes/metadata for the supported modern chart families, and Excel-openability/visual-gate coverage for the 28-renderable-chart matrix; `App.Host` exposes initial Page Layout Themes, Colors, Fonts, and Effects preset dropdowns plus a custom theme dialog for name, heading/body fonts, effects, and core color slots, and `App.UI` renders Subtle/Refined plus imported `fmtScheme` outer/preset/inner-shadow/glow/soft-edge drawing-object effects while full OOXML effect semantics beyond that slice, richer chart formatting panes, and Map chart product scope remain future work
- CSV and delimited-text adapters support RFC 4180-style quoted fields, embedded line breaks, UTF BOM detection, Excel `sep=` directives, and literal-text preservation for formula/coercion-like text; multi-sheet workbook export remains intentionally limited to the first sheet for text formats
- Volatile function tracking is not thread-safe (single UI thread assumed)
- Style registry uses linear scan (acceptable for v1 style counts)
