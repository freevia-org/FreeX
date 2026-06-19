# FreeX.FormatCrossCheck

External cross-validation of FreeX's written files using **headless LibreOffice** as an independent
consumer. Closes the gap left by FreeX's other fidelity harnesses, which only read FreeX's output back
with FreeX itself.

For each FreeX-writable interchange format that LibreOffice understands (xlsx, ods, SpreadsheetML `.xml`,
html, csv):

```
FreeX writes the file -> soffice --headless --convert-to xlsx -> FreeX loads it -> compare values+formulas+structure
```

## Run
```powershell
dotnet run --project tools/FreeX.FormatCrossCheck -c Release                 # default source set
dotnet run --project tools/FreeX.FormatCrossCheck -c Release -- a.xlsx b.xlsx # specific sources
dotnet run --project tools/FreeX.FormatCrossCheck -c Release -- --format=ods  # one format only
```
- Exit `0` = no FreeX-output-defect, `1` = a defect, `2` = LibreOffice not found.
- Report: `%TEMP%\formatcrosscheck\REPORT.txt`.
- Override soffice path: `FREEX_SOFFICE=<path-to-soffice.com>`.

## LibreOffice install (one-time)
```
winget install --id TheDocumentFoundation.LibreOffice -e --accept-source-agreements --accept-package-agreements
```

## Scope
Values + formulas + sheet structure (the interop-critical core). **Styles are out of scope for v1** —
`tools/FreeX.FormatFidelity` already covers style ceilings.

## Classification
The tool separates **LibreOffice-coercion** (formula re-spelling into OpenFormula, pivot regeneration,
formula recalc, CSV/HTML flattening, multi-sheet collapse, the SpreadsheetML Boolean-import limitation)
from a real **FreeX-output-defect** (a literal value FreeX wrote that LibreOffice mis-read, or a formula
that vanished into a literal in a format that should keep it). Only the latter fails the run.

See `docs/fidelity/2026-06-19-libreoffice-crosscheck.md` for the full findings and the one
reverse-direction reader caveat (FreeX's ClosedXML xlsx reader on a LibreOffice-authored workbook).

**Not a CI/merge gate** — LibreOffice may not be present on CI. Run on demand.
