# FreeX real-world fidelity corpus (on-demand)

This corpus backs the **on-demand** FreeX ↔ Microsoft Excel fidelity batch
(`tools/FreeX.FidelityCompare`). The manifest is also used by lightweight test guards, including the
EXINFM legacy `.xls` importer corpus test when those local files have been downloaded. The workbook
binaries are still optional and are not committed or required by CI. The corpus exists to open complex
real-world `.xlsx` and legacy `.xls` files in both FreeX and desktop Excel and compare visuals and
functionality.

## What is committed vs. downloaded

- **Committed:** `manifest.csv` (the catalogue) and the downloader (`tools/Fetch-FidelityCorpus.ps1`).
- **Not committed:** the workbook binaries. They live in `fidelity-corpus/files/`, which is git-ignored, so
  third-party files are never redistributed from this repo and the repo stays small.

## Getting the files

```powershell
pwsh tools/Fetch-FidelityCorpus.ps1                # download anything missing
pwsh tools/Fetch-FidelityCorpus.ps1 -Force         # re-download everything
pwsh tools/Fetch-FidelityCorpus.ps1 -Source exinfm # download only EXINFM legacy .xls files
```

## Manifest schema (`manifest.csv`)

`id,file,source,license,retrieved_on,url,feature_tags,notes`

- `license` is **required** for every downloaded row. Rows marked with a permissive or public-domain
  license can be treated as redistributable metadata-backed samples. Rows marked
  `free-download-redistribution-unconfirmed`, such as the EXINFM legacy `.xls` samples, are local-only
  validation inputs: the downloader can fetch them, but the workbook binaries remain ignored and must not
  be committed or redistributed from this repository. The current catalogue uses Apache POI test data
  (Apache-2.0), OfficeCLI chart/pivot workbooks (Apache-2.0), plus targeted MIT-licensed library fixtures
  for form controls, ActiveX controls, dropdown validation, Budget-vs-Actual chart data, chart overlays,
  PivotTables, chartsheets, and emoji/unicode strings.
  Together those rows cover charts, ChartEx/cx charts, pivots, conditional formatting, tables, data
  validation, formatting/themes, comments, merges, formulas, sparklines, drawings, images, text boxes,
  embedded objects, hyperlinks, protection, page setup, and sizeable worksheet data.
- `feature_tags` is space-separated (same convention as `test-corpus/manifest.csv`).

## Adding your own complex local workbooks

Public, freely-licensed files rarely reach real "dashboard" complexity. To exercise that, drop your own
workbooks into `fidelity-corpus/files/` (they stay git-ignored) and add a manifest row with
`source=local` and a `local://<file>` url — the downloader skips those but the fidelity batch still runs
them. Do **not** point committed rows at non-redistributable URLs.
