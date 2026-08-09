# FreeW

FreeW is a Word-class word processor for Windows. It opens and saves WordprocessingML documents
and uses a catalog-backed adapter layer for additional document formats, with rich editing,
a Word-style ribbon, file lifecycle, and print - keeping the project, branding, and release
artifacts independent from Microsoft.

- **Platform:** native Windows desktop app, WPF, `net10.0-windows`, with an Avalonia shell for Linux/macOS-oriented work.
- **Document formats:** DOCX/DOCM/DOTX/DOTM through Office Open XML, plus catalog-backed adapters for Word XML, RTF, HTML/HTM, MHTML/MHT, PDF import, legacy DOC/DOT import, and plain text variants.
- **Foundation:** built on the shared `Free.Shared.*` tier (Ribbon, Opc, AppServices, Commands, Shell)
  with **zero coupling to FreeX**, the sibling spreadsheet app in the same monorepo. FreeW and FreeX
  share only the `Free.Shared.*` libraries; neither references the other.

> Status: feature-complete through roadmap Milestones **A-U** plus follow-up file-format, corpus,
> icon-audit, and platform slices on current mainline. See
> [`../docs/planning/freew-roadmap.md`](../docs/planning/freew-roadmap.md) for the historical
> per-feature implementation log and [`../docs/planning/freew-file-formats.md`](../docs/planning/freew-file-formats.md)
> for the current format-adapter matrix.

---

## Architecture

FreeW is split into a pure model, a pure-ish IO layer, and the WPF app host. The model and IO target
`net10.0` (no WPF/Windows dependency) so they are unit-testable on any runner; only the app host pulls
in WPF.

| Project | TFM | Responsibility |
|---|---|---|
| `FreeW.Core.Model` | `net10.0` | Pure document model (`TextDocument`, `Paragraph`, `Run`, `Table`, `InlineImage`), run/paragraph formatting records, the named-style catalog, page settings, side stores (footnotes, endnotes, comments, sources, index entries…), editing **commands**, and a large family of **pure helpers** (outline, TOC, citations, mail merge, compare, autocorrect, change case, sort/convert, statistics, …). |
| `FreeW.Core.IO` | `net10.0` | Document reader/writer layer: WordprocessingML `.docx` over `System.IO.Compression.ZipArchive`, `IDocumentFileAdapter`, `DocumentFileAdapterCatalog`, catalog-derived format resolution, and adapters for DOCX family, Word XML, RTF, HTML/MHTML, PDF import, legacy DOC/DOT import, and plain text. |
| `FreeW.App.Host` | `net10.0-windows` (WPF) | The application: a `RichTextBox`/`FlowDocument` editing surface (`DocumentView`), the ribbon, catalog-backed file commands, dialogs, autosave, print/preview. `WinExe`. |
| `FreeW.Core.Model.Tests` | `net10.0` | xUnit tests for the model + commands + helpers. |
| `FreeW.Core.IO.Tests` | `net10.0` | xUnit `.docx` round-trip tests. |

### Shared tier reused

| Shared library | Used for |
|---|---|
| `Free.Shared.Ribbon` | Declarative ribbon model/builder used to compose FreeW's tabs. |
| `Free.Shared.Commands` | `UndoRedoStack` — FreeW layers a document command bus on top (see below). |
| `Free.Shared.Opc` | OPC/OOXML packaging helpers + `SecureXmlReaderSettings` (hardened XML) for the docx reader. |
| `Free.Shared.AppServices` | Recent files, autosave snapshots, per-product storage/settings, under a FreeW identity. |
| `Free.Shared.Shell` | Window/dialog chrome shared with the rest of the suite. |

### Undo/redo command bus

Editing goes through `IDocumentCommand` + a `DocumentCommandBus` built over the shared
`UndoRedoStack`. Commands capture a snapshot and revert on undo (insert/delete text, set run/paragraph
formatting, set style, insert/replace blocks, table edits, etc.). The `DocumentView` redraws on bus
change, so every ribbon action that mutates the document is undoable/redoable.

### Editing surface

`DocumentView : RichTextBox` renders the model into a WPF `FlowDocument` (resolving each run/paragraph
through the style catalog + document defaults) and maps edits back to the model on commit. This is the
right tool for a text surface (built-in caret, selection, spell-check, IME) — FreeW does **not** reuse
FreeX's cell-grid surface.

### CI

A dedicated workflow, `.github/workflows/freew-ci.yml` ("FreeW CI"), gates the FreeW lane on PRs/pushes
that touch `freew/**`, `FreeW.slnx`, or `shared/**`: it builds `FreeW.slnx` in Release (0 warnings
enforced) then runs `dotnet test FreeW.slnx --no-build` on `windows-latest` with .NET 10.

---

## Feature catalog

Grouped by area. Every item below is implemented and (where it touches IO) round-trips through the
docx reader/writer; unsupported renderings are noted under [Known limitations](#known-limitations).

### Editing & character formatting
- Typing/selection/caret with full undo/redo via the command bus.
- Font family, size, color, **bold** / *italic* / underline / strikethrough.
- Text **highlight** (encoded as `w:shd w:fill`, exact-hex round-trip).
- Character effects: superscript / subscript (`w:vertAlign`), small caps & all caps (`w:smallCaps` / `w:caps`).
- **Change Case** (UPPER / lower / Sentence / Capitalize Each Word / tOGGLE).
- Format Painter (capture formatting, apply to the next selection).
- Clear All Formatting (reset runs to default).
- Find / Replace (modeless) with match-case, **whole-word**, wrap, Replace All, replace-all-in-selection,
  and **Go To** (heading / bookmark / doc start–end).
- Spell-check (live red squiggles + suggestions) with a persistent **custom dictionary** (`.lex`) and a toggle.
- AutoCorrect / smart typing (smart quotes, `--`→en/em dash, `(c)`/`(r)`/`(tm)`, `...`→…, sentence caps) plus
  **AutoFormat As You Type** (automatic bulleted/numbered lists, ordinals→superscript, `1/2`→½, URLs/e-mail→hyperlinks);
  each rule individually toggled in the **AutoCorrect Options** dialog (Options → *AutoFormat As You Type* tab) and persisted.
- Paste Special: Paste Text Only / Merge Formatting (Ctrl+Shift+V).
- Insert **Symbol** (glyph picker) and **Date & Time**.

### Paragraph formatting
- Alignment (left/center/right/justify), line spacing, space before/after.
- Indentation: increase/decrease step + explicit left/right/first-line (hanging) indents.
- **Tab stops** (left/center/right/decimal) with **tab leaders** (dots/dashes/underline) — `w:tabs`/`w:tab`;
  set/clear via the **Tabs dialog** (Home > Paragraph > Tabs…).
- Paragraph **borders & shading** (`w:pBdr` + paragraph `w:shd`).
- Flow control: keep-with-next, keep-lines-together, widow control (`w:keepNext`/`w:keepLines`/`w:widowControl`).
- Bulleted / numbered / **multilevel** lists persisted to `word/numbering.xml` (`w:numPr`); multilevel
  lists show Word's accumulated outline markers (`1`, `1.1`, `1.1.1`) live in the editor.
- **Drop cap** (split first letter into an oversized run).

### Styles, themes & design
- Built-in style catalog: Normal, Title, Subtitle, Heading 1–3, Quote, Caption, TOC/Index styles, etc.
- Styles gallery / combo applies a paragraph `StyleId` (round-trips via `pStyle` + `styles.xml`).
- **Custom styles** (create/modify/delete with a built-in guard; safe unique-id generation) round-tripping run formatting.
- **Document themes** (Office / Slate / Berlin / Ion) with a live-preview gallery; persisted as a real
  `word/theme/theme1.xml` part (`a:clrScheme` + `a:fontScheme`) and recovered on open.

### Page & section layout
- Margins, orientation, page size; honoured by docx save and print.
- **Multi-column** layout via the **Columns** dialog (One/Two/Three/Left/Right presets, custom count, spacing, line-between; `sectPr/w:cols` with `w:num`/`w:space`/`w:sep` and explicit `w:col` widths for the unequal Left/Right presets).
- **Headers & footers** with a live **PAGE-number field** (`word/header1.xml`/`footer1.xml`, `w:fldSimple`).
- Page borders + **watermark** (page border `w:pgBorders`; watermark stored as a `docProps/custom.xml` custom property).
- **Line numbers** (continuous / restart-each-page, `sectPr/w:lnNumType`) drawn in the live editor margin and in print preview.
- **Hyphenation** (Layout > Hyphenation: None / Automatic / Manual + Options…): a pure English syllable hyphenator inserts soft hyphens into the live document; document-level `w:autoHyphenation` / `w:hyphenationZone` / `w:consecutiveHyphenLimit` / `w:doNotHyphenateCaps` and per-paragraph `w:suppressAutoHyphens` round-trip via `settings.xml` / `pPr`.
- Vertical page alignment (`sectPr/w:vAlign`), different-first-page (`w:titlePg`).
- **Different odd & even page** headers/footers (`w:evenAndOddHeaders` + `header2.xml`/`footer2.xml`, `w:type="even"`).
- **Page background colour** (`w:background` + `w:displayBackgroundShape`).
- **Multiple sections** with per-section page setup and break kinds (continuous / next / even / odd page).
- Cover page, horizontal rule, manual page break.

### Tables
- Insert table; insert/delete row & column at the caret (undoable).
- **Cell merge & split** (`w:gridSpan` + `w:vMerge` restart/continue).
- Per-cell shading & width, grid column widths (`w:tcPr/w:shd`, `w:tcW`, `w:tblGrid/w:gridCol`).
- **Table styles**: header row (bold + shaded), banded rows, repeat-header-row (`w:tblHeader` + `w:tblLook`).
- Convert text ↔ table, and stable sort of paragraphs / table rows (asc/desc, case-aware).

### Images / illustrations
- Insert inline picture (DrawingML `w:drawing`, PNG part under `word/media`, rels + content type).
- Resize the selected image (undoable); image alignment; **alt text** (`wp:docPr @descr`).
- **Floating images & text wrapping** — Inline / Square / Tight / Top-and-Bottom / Behind / In-Front
  (`wp:anchor` with `wp:positionH/V` + the matching wrap element; inline images stay `wp:inline`).
- **Shapes & text boxes** — Rectangle / Rounded Rectangle / Ellipse / Text Box (`wps:wsp`, preset geometry,
  optional fill, `w:txbxContent` body).
- **WordArt** — decorative text presets (fill / gradient / outline / shadow) via DrawingML text effects.
- **Equations** — inline OMML (`m:oMath`) with an Insert > Equation structure gallery: text, super/sub/sub-superscript
  (`m:sSup`/`m:sSub`/`m:sSubSup`), fractions (`m:f`), radicals (square & nth root, `m:rad`), n-ary operators
  (sum/integral/product with limits, `m:nary`), brackets/delimiters (`m:d`) and matrices (`m:m`) — each round-trips.
- **Charts** — column / bar / line / pie as a self-contained `word/charts/chartN.xml` part (data in literal caches).
- **SmartArt** — List / Process / Hierarchy diagrams (four `word/diagrams/*` parts; node text + structure).
- **OLE embedded objects** — embed a binary payload + ProgID with an icon (`w:object` / `o:OLEObject` +
  `word/embeddings/oleObjectN.bin`).

### Advanced typography
- Character spacing (expanded/condensed), kerning, raised/lowered position (`w:spacing`/`w:kern`/`w:position`).
- Ligatures, stylistic sets, number forms & number spacing (`w14:ligatures`/`w14:stylisticSets`/`w14:numForm`/`w14:numSpacing`).

### References
- **Footnotes** (`word/footnotes.xml`, `w:footnoteReference`) and **endnotes** (`word/endnotes.xml`) — they coexist.
- **Table of Contents** (built from the heading outline; Insert + Update).
- **Index** (mark entry → build sorted/deduped index, `IndexHeading`/`IndexEntry` styles).
- **Citations & bibliography** in **APA / MLA / Chicago / IEEE / Turabian / Harvard / Vancouver / GOST / ISO 690** (style- and source-type-aware: Book / Journal Article / Web Site each format per the chosen style, in-text + bibliography/reference-list output under the right heading: References / Works Cited / Bibliography). Numeric IEEE/Vancouver insertions use source-order visible numbers like `[1]`, `[2]`, and generated numeric reference lists keep source-order numbered entries. The selected style and the source data persist to `word/bibliography/sources.xml` and round-trip.
- **Captions** ("Figure/Table N: …") with automatic figure/table numbering, plus a **Table of Figures**.
- **Cross-references** to headings / bookmarks / captions / footnotes (clickable internal link when anchored).
- **Bookmarks** + internal hyperlinks (`w:bookmarkStart/End`, `w:hyperlink w:anchor`) with a Bookmark Manager + Go To.
- **Hyperlinks** (external `w:hyperlink` + `word/_rels`, deduped per URL) with edit/remove and ScreenTip (`w:tooltip`).

### Review & collaboration
- **Comments** (`word/comments.xml`, `w:commentRangeStart/End` + `w:commentReference`) with author/text tooltip.
- **Track changes** (`w:ins`/`w:del`, author/date) with Track-Changes toggle and Accept All / Reject All.
- **Compare documents** — two-level (paragraph-anchor + word-level) LCS diff producing tracked changes.
- **Restrict editing / protection** — Restrict Editing pane (No changes / Tracked changes / Comments / Filling in forms), enforced on the live editor (read-only, forced track-changes, comment-only) and persisted as `w:documentProtection` (`w:edit` + `w:enforcement`) in `settings.xml`; "Stop Protection" lifts it.
- **Mark as Final** — Word's advisory read-only flag (`_MarkAsFinal` boolean custom property in `docProps/custom.xml`): locks the editor, shows a "Marked as Final" banner, and "Edit Anyway" clears it.
- **Document Inspector** (count + selectively remove comments / revisions / properties / bookmarks).
- **Check Accessibility** — alt-text, link-text, heading-order, table-header, and WCAG contrast rules with a grouped report.
- Word count + live status bar; **Document Statistics** dialog (words/chars/sentences/syllables, reading time, Flesch reading ease).
- **Read Aloud** (Review > Speech) — local, in-box text-to-speech (`System.Speech`) that reads from the caret to the end of the document, paragraph by paragraph (table cells included); robust when no voice is installed.

### Mailings
- **Mail merge**: `«Field»` placeholders, CSV data source, insert field, preview record (next/prev), Finish & Merge to a new document, printer, or selected-record e-mail drafts, plus direct Send E-mail Messages draft handoff to the default mail client (FreeW never auto-sends).

### Content & navigation
- **Content controls** (`w:sdt`): plain-text, rich-text (`w:richText`), clickable checkbox (`w14:checkbox`),
  date picker (`w:date`), drop-down list (`w:dropDownList`) and combo box (`w:comboBox`) — the list controls
  offer their `w:listItem` choices on click.
- **Quick Parts / AutoText**: save selection + insert, persisted as `quickparts.json`.
- **Navigation pane** (heading outline; click to scroll) and **Outline tools** (promote/demote, collapse/expand).
- **Cross-document insert**: "Text from File" deep-clones another `.docx`'s blocks at the caret (bringing missing styles).
- Insert Field (DATE/TIME/FILENAME/AUTHOR/NUMPAGES as `w:fldSimple` with the right `w:instr`).

### View & reading
- **Real Word-style ribbon** rendered by the shared `Free.Shared.Ribbon.Wpf.RibbonWpfRenderer` (Large/Medium/Small
  controls, group dividers/labels, vector glyphs) with **live-preview Styles & Theme galleries** and an **Alt KeyTips** overlay.
- **Backstage / File menu** — full-window New/Open/Save/Save As/Print/Export/Info/Recent/Options.
- **Paginated Print-Layout view** (discrete pages, margins, page shadow) plus **horizontal/vertical rulers** and a
  Word-style status bar (Page X of Y, Section X of N, word count, zoom, view switches).
- **Zoom** 50–200% (slider / ± / Ctrl+wheel).
- **Read mode** (hides chrome, centered reading column) + live selection word/char count.
- **Outline view** (View &gt; Outline) — the document as an indented heading/body outline with an Outlining
  mini-toolbar (Show Level 1–9/All, Promote / Demote / Promote to Heading 1, Move Up/Down, Expand/Collapse,
  Show First Line Only). Reuses the existing reversible heading operations; switching views never mutates the model.
- **Show formatting marks** (¶ / · / →) drawn as a non-destructive adorner overlay.
- Document properties dialog (`docProps/core.xml`, Dublin Core).

### File lifecycle
- New / Open / Save / Save As over `DocxReader`/`DocxWriter` with file dialogs, dirty-state + title bar, Ctrl+N/O/S.
- **Recent files** (shared `RecentFilesStore`, persisted under FreeW's data folder).
- **Autosave & recovery** (shared `AutosaveSnapshotStore`): a `.docx` snapshot every 30 s while dirty, recovery offered on startup, cleaned up on clean exit.
- **Print + Export PDF**: Ctrl+P → WPF `PrintDialog` over the paginator; "Microsoft Print to PDF" for PDF. Paginated WYSIWYG **Print Preview** (`FlowDocumentPageViewer`).

### docx fidelity at a glance

OOXML parts FreeW reads and/or writes:

| Part | Notes |
|---|---|
| `[Content_Types].xml`, `_rels` | Full content-type + relationship graph. |
| `word/document.xml` | Paragraphs, runs, `rPr`/`pPr`, tables (`w:tbl`), drawings, fields, sections (`sectPr`). |
| `word/styles.xml` | Built-in + custom styles, document defaults. |
| `word/numbering.xml` | Bullet / decimal / multilevel abstract nums. |
| `word/settings.xml` | Document protection, auto-hyphenation, odd/even headers, display-background. |
| `word/theme/theme1.xml` | Document theme (`a:clrScheme` + `a:fontScheme`). |
| `word/header1.xml` / `footer1.xml` / `header2.xml` / `footer2.xml` | Default + even-page headers/footers + PAGE field, referenced from `sectPr`. |
| `word/footnotes.xml` / `endnotes.xml` | Footnotes and endnotes. |
| `word/comments.xml` | Review comments. |
| `word/charts/chartN.xml` | DrawingML charts (column/bar/line/pie, literal-cache data). |
| `word/diagrams/*` | SmartArt diagrams (data / layout / quickStyle / colors). |
| `word/embeddings/oleObjectN.bin` | OLE embedded-object payloads. |
| `word/media/*` | Inline PNG images + shape/WordArt/OLE-icon presentation. |
| `docProps/core.xml` | Dublin Core document properties. |
| `docProps/custom.xml` | Watermark (stored as a custom property). |

---

## Build & test

From the repo root:

```powershell
# Build (Release; 0 warnings enforced via TreatWarningsAsErrors)
dotnet build FreeW.slnx -c Release

# Run the FreeW test lane (~580+ tests across model + IO)
dotnet test FreeW.slnx

# Run the app
dotnet run --project freew/FreeW.App.Host
```

`FreeW.slnx` contains the five FreeW projects plus the `Free.Shared.*` libraries they depend on — it is
self-contained and does not pull in FreeX. The build treats warnings as errors, so a green build is a
0-warning build.

---

## Known limitations

FreeW renders into a WPF `FlowDocument`, which is excellent for live editing but cannot reproduce every
print-layout detail. The model/IO still round-trip these faithfully; the live view is the approximation.

- **Tab leaders** are carried verbatim through round-trip but are **not drawn** live (FlowDocument has
  no tab-leader API); FreeW preserves them via a paragraph `Tag`.
- **Tab stops** are editable via the Tabs dialog (set/clear/clear-all, position + alignment + leader) and
  preserved via the paragraph `Tag` (FlowDocument has no tab-stop API, so custom stops are not drawn live).
- **Vertical page alignment** (`w:vAlign`) is persisted but not reflowed live.
- **Widow/orphan control** is stored in the model/docx but is model-only on screen.
- **Watermark** is stored as a `docProps/custom.xml` custom property (FreeW's own convention), not as a
  header drawing; it renders as a faint rotated overlay in the editor/preview.
- **Outline collapse/expand** is view-only — hidden body blocks are re-spliced on commit so they can't
  be lost.

### Not implemented

FreeW targets the mainstream Word surface and deliberately excludes **cloud / Microsoft 365 integration**
(online video, translate/research/editor services — FreeW is local-file by default) and **proprietary
features** (macros/VBA, IRM, digital signatures, 3D models). Also out of scope by design: ink/handwriting,
linked (overflowing) text boxes, and master/subdocuments.

A few shipped features are **data-faithful but visually simplified**: charts embed their data as literal
caches rather than an editable companion xlsx (Word's "Edit Data" is unavailable); SmartArt round-trips its
node text/structure but no rendered `dsp` geometry (Word re-lays-out on open); `wp:wrapTight` carries no wrap
polygon; theme `a:fmtScheme` effect-set selection round-trips, while deeper DrawingML effect consumption by
every object type remains incremental. Citation inserts and generated bibliographies are ordinary visible
text/paragraphs rather than live Word `CITATION` / `BIBLIOGRAPHY` field runs. Real Word visual/reference
baselines still require Word COM on the validation machine. Each is noted in code at its site.

---

## Roadmap

The single source of truth for FreeW's feature set, per-feature implementation notes, and status log is
[`../docs/planning/freew-roadmap.md`](../docs/planning/freew-roadmap.md). Milestones A through U are
complete.
