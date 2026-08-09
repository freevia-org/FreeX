# FreeX Troubleshooting Guide

**Version:** v1.0  
**Updated:** 2026-05-25

This guide covers common issues, error messages, and their resolutions.

---

## Table of Contents

1. [Opening Files](#opening-files)
2. [Unsupported Feature Warnings](#unsupported-feature-warnings)
3. [Formula and Calculation Issues](#formula-and-calculation-issues)
4. [Display and Rendering Issues](#display-and-rendering-issues)
5. [Charts](#charts)
6. [PivotTables](#pivottables)
7. [Printing and Exporting](#printing-and-exporting)
8. [Performance with Large Workbooks](#performance-with-large-workbooks)
9. [Saving and File Issues](#saving-and-file-issues)
10. [Crash Recovery and Reporting](#crash-recovery-and-reporting)
11. [Known Limitations](#known-limitations)

---

## Opening Files

### The file opens but shows an info/warning bar

FreeX detected features it cannot fully model (such as VBA macros, Power Query, or embedded objects). The warning bar lists what was detected. All supported content is loaded correctly; unsupported package parts are preserved and will be written back on save unchanged. **No data is silently lost.**

Common warning messages and what they mean:

| Warning | Meaning |
|---|---|
| **VBA macros detected** | The file contains VBA code. FreeX cannot run macros. The code is preserved and will be saved back, but buttons and macro-assigned shortcuts will not work. |
| **Power Query / Power Pivot** | Data model or query connections are present. FreeX cannot refresh them, but the cached data and metadata are retained. |
| **Unsupported chart type** | The file contains a chart family or package shape outside the current FreeX model, such as Filled Map or an unparseable future chart part. The chart package part is retained and disclosed rather than silently dropped. |
| **ActiveX / Form controls** | Form controls or ActiveX objects are present. They are preserved but not interactive. |
| **Threaded comments** | FreeX loads and saves local threaded comment roots, replies, authors, timestamps, and resolved state, but Microsoft 365 cloud sync, co-authoring presence, and service-side conversation state are outside local workbook fidelity. |
| **Track changes / revision history** | Revision history is present and preserved but not shown in the editing UI. |
| **Digital signature** | The file carries a digital signature that will be broken if you save with changes. |
| **Custom Ribbon UI** | A custom ribbon XML definition is present. FreeX shows its own ribbon; the customization is preserved. |

### "This file format is not supported"

FreeX opens `.xlsx`, `.xlsm`, `.xltx`, `.xltm`, legacy `.xls`, `.xlsb`, `.xlt`, delimited text (`.csv`, `.tsv`, `.tab`, `.txt`), XML Spreadsheet 2003 (`.xml`), OpenDocument Spreadsheet (`.ods`), web page (`.html`/`.htm`, `.mht`/`.mhtml`), SYLK (`.slk`), DIF (`.dif`), dBASE (`.dbf`), PDF (`.pdf`), and FreeX workbook (`.fxl`) files. Save support is narrower: legacy binary workbooks and templates (`.xls`, `.xlsb`, `.xlt`), dBASE (`.dbf`), and PDF (`.pdf`) are open-only imports, so use Save As to write an editable `.xlsx` or `.fxl` copy after opening them. Macro-enabled workbooks and templates (`.xlsm`, `.xltm`) and `.xltx` templates open **and** save.

### The file opens but data looks wrong or is missing

1. Check whether the file has hidden rows, columns, or sheets: Home -> Format -> Hide & Unhide, or right-click a sheet tab.
2. If formulas show as text instead of results, the cells may be formatted as Text. Select them, Home -> Number format -> General, then re-enter the formulas or press Ctrl+` twice to refresh.
3. If the workbook uses a feature FreeX does not model (external data connections, Power Query output ranges), those cells may be blank until the data is manually entered.

### "Minimum reader version not supported" (`.fxl` files)

The `.fxl` file was saved by a newer version of FreeX. Update to the latest FreeX release to open it.

---

## Unsupported Feature Warnings

### What gets preserved when I save?

When you open an `.xlsx` file with unsupported features and save it:
- All FreeX-modeled content (cells, formulas, styles, charts you can edit, PivotTables, etc.) is written from the in-memory model.
- Unsupported package parts (VBA project, Power Query definitions, embedded objects, custom Ribbon XML, etc.) are merged back from the original file's package.
- The result is your edits plus the preserved unsupported content - not a clean file, but not a broken one either.

### I saved and my macros stopped working

FreeX preserves the VBA project bytes, but does not re-sign the project or update any security metadata. If Excel's macro-security settings require signed macros, it may refuse to run them after FreeX saves the file. Open the file in Excel and re-sign the project if needed.

### The file shows a "repair" dialog in Excel after I save from FreeX

This can happen if:
- A formula uses a syntax or function FreeX writes differently than Excel expects. Report the specific formula.
- A chart or object was modified in a way Excel's repair heuristic flags. Try saving from Excel first to normalise the package before further edits.

Report repair dialogs with the workbook name and the repair log text so the issue can be investigated.

---

## Formula and Calculation Issues

### Cell shows `#NAME?`

The formula contains a name or function FreeX does not recognise.
- Check the function name for spelling.
- Named ranges are case-insensitive but must be defined. Open Name Manager (Ctrl+F3) to verify the name exists.
- If the formula uses a custom name from a VBA module, FreeX cannot evaluate it.

### Cell shows `#REF!`

A cell reference points to a cell that no longer exists, usually because rows or columns were deleted. Re-enter the formula with a valid reference.

### Cell shows `#VALUE!`

The formula is trying to use the wrong type of argument (for example, adding text to a number). Check argument types for the function in question.

### Cell shows `#DIV/0!`

The formula is dividing by zero or a blank cell. Wrap with `=IFERROR(formula, 0)` or check that the divisor cell contains a value.

### Cell shows `#SPILL!`

A dynamic-array formula cannot spill because the destination range is blocked by non-empty cells. Clear the cells in the spill area.

### Formulas are not recalculating

Calculation may be set to Manual. Press **F9** to recalculate the workbook, or set File -> Options -> Formulas -> Calculation to Automatic.

### A formula that works in Excel gives a different result in FreeX

1. Check whether the formula depends on a regional locale date or number format. FreeX uses invariant number parsing; locale-specific formats may differ.
2. Check for volatile functions (NOW, TODAY, RAND, RANDBETWEEN, OFFSET, INDIRECT) - these recalculate on every change, and the result may differ from a cached Excel value.
3. Copy-paste the formula into a new workbook to isolate whether workbook context (named ranges, protected sheets) is a factor.
4. If you can reproduce the discrepancy with a minimal example, please report it as a bug with both the expected and actual results.

### `XLOOKUP` / `XMATCH` / `FILTER` / other modern functions not found

These functions are fully supported. If the cell shows `#NAME?`, the formula text may have been corrupted (e.g., a language pack substituted a localized name). Re-enter the function name in English.

### The formula bar shows the formula but the cell shows the formula text

The cell is formatted as Text. Change the format to General (Ctrl+Shift+~) and re-enter the formula by pressing F2 then Enter.

---

## Display and Rendering Issues

### Text is cut off in a cell

- Enable **Wrap Text** (Home -> Wrap Text or Format Cells -> Alignment).
- Or widen the column by dragging the column header border or using Home -> Format -> AutoFit Column Width.

### Numbers display as `########`

The column is too narrow to show the value. Widen the column or double-click the column header border to AutoFit.

### A cell shows a number but I expect a date

The cell is formatted as a number. Select it and apply a date format: Format Cells (Ctrl+1) -> Date, or press Ctrl+Shift+#.

### Colors look different from Excel

FreeX renders indexed colors from the workbook's color palette. If no palette override is present, the default Excel indexed palette is used. Theme colors (ThemeColor 1-12) render using the workbook theme's color map when a theme is loaded. Theme-color rendering fidelity is partial for complex theme-effect chains.

### High-DPI display: text or icons appear blurry

FreeX uses WPF's built-in DPI scaling. If text still appears blurry, try adjusting Windows display scaling (Settings -> Display -> Scale) and restart the application.

---

## Charts

### My chart is blank or empty

Check that the source data range is correct. Click the chart to select it, then use the Chart tab -> Edit Data Source to verify the series ranges.

### The chart type I need is not in the Insert menu

Current chartEx families (Treemap, Sunburst, Histogram, Pareto, Box-and-Whisker, Waterfall, and Funnel) are supported in the chart interop matrix. Filled Map remains deferred and unparseable future chart package parts are preserved with an unsupported-feature warning.

### A chart PNG export looks slightly different in automated comparison

The chart parity harness compares both packages and Excel-exported chart PNGs. When Excel-native and FreeX round-tripped XLSX packages are byte-identical, minor differences from repeated Excel PNG export are treated as export repeatability noise rather than a workbook openability issue.

### Chart colors are wrong

Chart series colors follow the workbook theme. If the theme was not fully loaded, series may use default OxyPlot colors instead of the Excel theme colors. This is a known partial gap in theme-color extraction.

### A chart disappeared after I edited cells

If you deleted the entire data range a chart was bound to, the chart may become empty. Use Ctrl+Z to undo, then adjust your edit to preserve the source range.

### Bar chart bars overlap unexpectedly

Select the chart, Chart tab -> Format Bar/Column, and set Gap Width and Overlap to the desired values. Gap Width 0% removes all space between bars; 100% (default) gives normal spacing. Overlap 0% (default) places bars side by side; positive values overlap them.

---

## PivotTables

### PivotTable shows no data after creation

Make sure the source range includes headers in the first row. Blank or duplicate header names can prevent fields from loading correctly.

### Refresh doesn't update the PivotTable

The PivotTable may be bound to an external data source (Power Query, OLAP) that FreeX cannot refresh. For source ranges on the same sheet, ensure the range includes all data rows and click PivotTable tab -> Refresh.

### Numbers in the PivotTable don't match my source data

Check the field's aggregation function (right-click a value cell -> Field Settings). The default is Sum; if your source data has text or errors in numeric columns, the count may be used instead.

### Grouping a date field fails

The date column must contain Excel date serial values (not text dates). If the column was imported as text, convert it with `=DATEVALUE(A1)` or use Text to Columns -> Date column format.

---

## Printing and Exporting

### Print preview looks different from the grid

- If the grid shows more data than the print preview, a Print Area may be set. Page Layout -> Print Area -> Clear Print Area to remove it, or add the missing range.
- If rows/columns are printed that shouldn't be, check for print titles: Page Layout -> Page Setup -> Sheet -> Print Titles.
- Freeze Panes affects the view but not print output.

### PDF export produces too many or too few pages

Adjust Page Layout -> Scale to Fit - set Width/Height to the number of pages you want, or set a Scale % explicitly.

### PDF text is not searchable

By default, FreeX overlays searchable text on the PDF raster. If you exported with the "Bitmap Text" option enabled, the PDF will be raster-only. Re-export without that option.

### Headers/footers appear wrong or missing

Verify the header/footer text in Insert -> Header & Footer. `&P` inserts the page number, `&N` inserts the total page count, `&D` inserts the date, `&F` inserts the file name.

---

## Performance with Large Workbooks

### Opening a large file is slow

Large workbooks (many formulas, complex PivotTables, many worksheets) take time to load because FreeX builds the full dependency graph on open. Opening time is roughly proportional to the number of formula cells.

### Scrolling feels slow with many formulas

FreeX recalculates volatile formulas (RAND, NOW, OFFSET, INDIRECT) on every grid repaint. Minimize the use of volatile functions in large ranges.

### The application stops responding during recalculation

Recalculation is single-threaded. For workbooks with hundreds of thousands of formula cells and complex cross-sheet dependencies, recalculation may take several seconds. The UI remains unavailable during this time. Press **Escape** to try to cancel a running calculation (works for most compute-intensive paths).

### Memory usage is high

FreeX loads the entire workbook into memory. Very large workbooks with many dense data ranges and multiple sheets will require more RAM. If memory is a concern, split large workbooks into smaller files.

---

## Saving and File Issues

### "Unable to save - file is in use by another process"

The file is locked by another application (Excel, antivirus scan, cloud-sync client). Close the other application or wait for the sync to finish, then save again. Alternatively, use File -> Save As to save under a new name.

### Saving as CSV loses formatting, formulas, and multiple sheets

CSV files can only store cell values from a single sheet. All formatting and formula structure is lost. Use XLSX or FXL if you need to preserve those.

### The file size is much larger after saving

FreeX preserves the original XLSX package parts that it does not model, which can inflate file size when the source file had large embedded objects or media. It does not compact or remove those parts. To reduce size, open in Excel and save a clean copy, removing unused content.

---

## Crash Recovery and Reporting

### FreeX crashed - how do I recover my work?

FreeX does not currently have auto-save or crash recovery. Save frequently (Ctrl+S) to protect your work.

If the crash left a temporary file open, check for `.tmp` files in the same folder as your workbook.

### Reporting a crash or bug

When reporting an issue, please include:
- The FreeX version (Help -> About).
- What you were doing when the crash or unexpected behavior occurred.
- A minimal repro file if possible (share via GitHub issues).
- Any error dialog text or Windows Event Viewer entry.

File issues at the project's GitHub repository.

### Error dialog: "An unexpected error occurred"

An unhandled exception was caught. The error message and stack trace in the dialog can help diagnose the cause. Copy the full text before dismissing the dialog and include it in your bug report.

---

## Known Limitations

These are documented product decisions, not bugs. They will not be fixed unless the product scope changes.

| Limitation | Reason |
|---|---|
| **VBA macros do not run** | VBA is a Microsoft proprietary runtime. Macro project bytes are preserved on save. |
| **Power Query / Power Pivot cannot refresh** | Requires the Microsoft data-model engine (M/DAX runtimes). |
| **No Microsoft 365 cloud sync or co-authoring** | Requires Microsoft identity, OneDrive/SharePoint infrastructure, and real-time conflict resolution. |
| **No external linked-data types** (Stocks, Geography) | Requires live Microsoft data service connectivity. |
| **Filled Map charts are not authorable** | Map charts are recognized as deferred scope and preserved when possible; they are not yet modeled for FreeX authoring/rendering. |
| **True 3D mesh surface rendering is partial** | 3D surface charts render as a value-colored matrix; full 3D mesh graphics remain a deferred renderer enhancement. |
| **Theme-color effects are partial** | Full OOXML theme-effect chains (reflection, bevel, 3D, etc.) are not interpreted; base theme colors plus bounded non-chart theme outer-shadow, inner-shadow, glow, and soft-edge defaults are applied. |
| **Advanced multi-window workbook view** | New Window, Switch Windows, Hide/Unhide, View Side by Side, Synchronous Scrolling, and Reset Window Position are registry-backed. Arrange All stores the workbook arrangement choice; exact Excel-style automatic tiling for every Arrange All layout remains partial. |
| **OLE embedded objects are not interactive** | Embedded Word documents, images, or other OLE objects are preserved as package bytes but not rendered or editable. |
