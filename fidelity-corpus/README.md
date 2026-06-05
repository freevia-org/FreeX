# FreeX real-world fidelity corpus (on-demand)

This corpus backs the **on-demand** FreeX ↔ Microsoft Excel fidelity batch
(`tools/FreeX.FidelityCompare`). It is **not** part of the normal build, test, or release flow — nothing
here is referenced by the test projects or CI. It exists to open complex real-world `.xlsx` files in both
FreeX and desktop Excel and compare visuals and functionality.

## What is committed vs. downloaded

- **Committed:** `manifest.csv` (the catalogue) and the downloader (`tools/Fetch-FidelityCorpus.ps1`).
- **Not committed:** the workbook binaries. They live in `fidelity-corpus/files/`, which is git-ignored, so
  third-party files are never redistributed from this repo and the repo stays small.

## Getting the files

```powershell
pwsh tools/Fetch-FidelityCorpus.ps1        # download anything missing
pwsh tools/Fetch-FidelityCorpus.ps1 -Force # re-download everything
```

## Manifest schema (`manifest.csv`)

`id,file,source,license,retrieved_on,url,feature_tags,notes`

- `license` is **required** for every downloaded row and must be permissive or public-domain. The current
  catalogue is Apache POI test data (Apache-2.0), chosen for broad feature coverage (charts, pivots,
  conditional formatting, tables, data validation, formatting/themes, comments, merges, formulas).
- `feature_tags` is space-separated (same convention as `test-corpus/manifest.csv`).

## Adding your own complex local workbooks

Public, freely-licensed files rarely reach real "dashboard" complexity. To exercise that, drop your own
workbooks into `fidelity-corpus/files/` (they stay git-ignored) and add a manifest row with
`source=local` and a `local://<file>` url — the downloader skips those but the fidelity batch still runs
them. Do **not** point committed rows at non-redistributable URLs.
