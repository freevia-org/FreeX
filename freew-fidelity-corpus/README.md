# FreeW real-world DOCX fidelity corpus (on-demand)

This corpus backs the **on-demand** FreeW DOCX fidelity work. It is intentionally shaped like
FreeX's `fidelity-corpus`: the catalogue and downloader are committed, while third-party DOCX
binaries are downloaded into an ignored `files/` folder and are never redistributed from this repo.
Curated repo-generated fixtures may also live under `files/` when each tracked DOCX has a manifest
row with `source=local` and a `local://files/...` provenance URL.

Current status as of 2026-07-01: `manifest.csv` has 157 rows: 134 redistributable on-demand rows
plus 23 repo-generated local fixtures under `files/review/` and `files/tables/`. The manifest is
guarded by `freew/FreeW.Core.IO.Tests/FreeWFidelityCorpusManifestTests.cs`. The current expansion
summary is [../docs/fidelity/2026-06-19-freew-corpus-feature-growth.md](../docs/fidelity/2026-06-19-freew-corpus-feature-growth.md).

The seed corpus focuses on Microsoft Word / WordprocessingML features FreeW needs to learn to open,
preserve, render, and eventually edit:

- rich run and paragraph formatting
- styles and numbering
- tables, nested tables, and table-contained notes
- comments and tracked insert/delete revisions
- footnotes and endnotes
- multi-section headers and footers
- images, drawings, charts, embedded documents, and attachments
- text effects, WordArt, advanced typography, proofing anchors, bidi/RTL text, and page-layout variants
- sizeable stress documents with mixed package parts

The initial seed uses Apache POI's `test-data/document` and `test-data/integration` DOCX fixtures
under Apache-2.0. The corpus now also includes targeted Apache-2.0 fixtures from Apache Tika and
docx4j plus MIT-licensed fixtures from Open XML PowerTools and the Open XML SDK. Each file is
referenced by a direct raw GitHub URL in `manifest.csv`.

## What is committed vs. downloaded

- **Committed:** `manifest.csv`, `tools/Fetch-FreeWFidelityCorpus.ps1`, and visual-runner scripts.
- **Committed local fixtures:** repo-generated DOCX files under `freew-fidelity-corpus/files/review/`
  and `freew-fidelity-corpus/files/tables/`, each covered by a `source=local` manifest row.
- **Not committed:** downloaded third-party DOCX binaries in `freew-fidelity-corpus/files/` and future
  run output under `freew-fidelity-corpus/runs/`, including generated F2 DOCX fixtures, PNG/PDF
  captures, raw visual manifests with machine-local absolute paths, and comparison scratch files.

## Getting the files

```powershell
pwsh tools/Fetch-FreeWFidelityCorpus.ps1
pwsh tools/Fetch-FreeWFidelityCorpus.ps1 -Force
```

Run the commands from the repository root; `tools/Fetch-FreeWFidelityCorpus.ps1` writes files under
`freew-fidelity-corpus/files/`.

## Manifest schema

`id,file,source,license,retrieved_on,url,feature_tags,notes`

- `license` is required for every downloaded row and must be permissive or public-domain.
- `feature_tags` is space-separated. Prefer concrete WordprocessingML features such as
  `headers-footers`, `comments`, `tracked-changes`, `footnotes`, `endnotes`, `numbering`,
  `tables`, `images`, `drawings`, `charts`, `embedded-objects`, `attachments`, `styles`,
  `content-controls`, `smartart`, `custom-xml`, `vml`, `shapes`, `watermarks`, `altchunk`,
  `mail-merge`, `wordart`, `text-effects`, `advanced-typography`, `rtl`, `proofing`,
  `document-background`, `page-layout`, and `stress`.

## Fidelity runs

**Round-trip (no Word needed):** the corpus-gated test
`freew/FreeW.Core.IO.Tests/FreeWFidelityCorpusRoundTripTests.cs` opens + round-trips every `files/` doc and
asserts no modelled-content loss (it no-ops when `files/` is absent). Findings:
`docs/fidelity/2026-06-17-freew-corpus-roundtrip.md` is the historical 26-file baseline; the current
corpus growth note is `docs/fidelity/2026-06-19-freew-corpus-feature-growth.md`.

**FreeW visual evidence smoke (no Word needed):** use the repeatable WPF + Avalonia runner when you
need local evidence that the shared manifest contract is healthy:

```powershell
pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/visual-evidence-smoke
```

The runner generates F2 DOCX fixtures under `runs/<name>/fixtures/f2/`, renders WPF evidence through
`freew/tools/FreeW.FidelityRender` in composite mode, renders Avalonia page-layout evidence through
`freew/tools/FreeW.PageLayoutShot`, validates both `freew_visual_evidence_manifest.json` files through
the shared `FreeW.App.Presentation` normalizer, and writes retained summaries:
`freew_visual_evidence_summary.json` and `freew_visual_evidence_summary.md`. Those summaries contain
stable relative paths, scenario IDs, host IDs, dimensions, byte lengths, SHA-256 hashes, pixel stats,
and trust status. Keep the entire run folder local; do not commit generated images, DOCX files, PDFs,
or raw absolute-path manifests.

When MS Word is available on the machine, the same runner can generate PNG baselines from the
generated DOCX fixtures and then compare both WPF and Avalonia outputs against Word in one manifest:

```powershell
pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 `
  -OutDir freew-fidelity-corpus/runs/visual-evidence-word `
  -IncludeWordBaseline
```

When MS Word PNG baselines have already been captured, pass their PNG folder directly:

```powershell
pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 `
  -OutDir freew-fidelity-corpus/runs/visual-evidence-word `
  -WordBaselineDir freew-fidelity-corpus/runs/word-baseline `
  -BaselineTolerance word-png-default
```

The baseline folder may contain PNGs either under `<scenario>/<output>.png` or directly at the root
using the same output names emitted by the visual evidence runner. The shared comparison policy maps
comparable Avalonia page-composition rows to their F2 Word baselines, skips unmapped rows such as
draft/web layout truthfully, and fails the normalized summary for missing or out-of-tolerance Word
baselines.

**Visual vs MS Word / LibreOffice:** run on a machine that has MS Word (preferred) or LibreOffice installed:

```powershell
pwsh freew-fidelity-corpus/tools/Run-VisualFidelity.ps1
# options: -Baseline word|libreoffice|auto  -Docs bookmarks.docx,delins.docx  -FilesDir ...  -OutDir ...
```

It (1) renders FreeW's side with the `freew/tools/FreeW.FidelityRender` tool (docx → PNG via the real
`DocumentView`/`FlowDocument` path), (2) renders the ground truth via Word COM (`ExportAsFixedFormat`) or
`soffice --convert-to pdf` and rasterizes to PNG (needs `pdftoppm`, `magick`, or `soffice`), and (3) diffs
each page pair (mean abs pixel delta + % changed) into `runs/visual-<timestamp>/visual-fidelity.csv`, with
the per-page PNGs kept under `freew/` and `baseline/` for eyeballing. The render tool can also be run alone:
`dotnet run --project freew/tools/FreeW.FidelityRender -- <docx|dir> <outDir> [maxPages]`.

## Local/private additions

For truly messy real-world documents that cannot be redistributed, drop files into
`freew-fidelity-corpus/files/` and add manifest rows with `source=local` and
`url=local://files/<relative-path-under-files>`. The downloader skips local rows but future FreeW
fidelity tooling can still run them from the ignored folder. Do not commit private/local-only
documents unless they are repo-generated or otherwise redistribution-safe and manifest-described.
