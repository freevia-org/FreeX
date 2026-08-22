# FreeX User Guide

**Version:** v1.0  
**Updated:** 2026-06-01

FreeX is a free, native Windows desktop spreadsheet application for `.xlsx` files. It reads and writes standard XLSX workbooks and supports formulas, charts, PivotTables, conditional formatting, data tools, and page layout.

FreeX is an independent project and is not affiliated with, authorized, sponsored, endorsed, or approved by Microsoft Corporation. Microsoft and Excel are trademarks of the Microsoft group of companies. All other trademarks are the property of their respective owners.

---

## Table of Contents

1. [Getting Started](#getting-started)
2. [Working with Cells](#working-with-cells)
3. [Formulas and Functions](#formulas-and-functions)
4. [Formatting Cells](#formatting-cells)
5. [Charts](#charts)
6. [PivotTables](#pivottables)
7. [Conditional Formatting](#conditional-formatting)
8. [Data Tools](#data-tools)
9. [Printing and Exporting](#printing-and-exporting)
10. [File Formats](#file-formats)
11. [Keyboard Shortcuts](#keyboard-shortcuts)

---

## Getting Started

### Opening a Workbook

- **New workbook:** Ctrl+N, or File -> New.
- **Open existing file:** Ctrl+O, or File -> Open. FreeX opens `.xlsx`, `.xlsm`, `.xltx`, `.xltm`, legacy `.xls`, `.xlsb`, `.xlt`, `.csv`, `.tsv`, `.tab`, `.txt`, `.ods`, `.html`/`.htm`, `.mht`/`.mhtml`, `.xml` (Spreadsheet 2003), `.slk`, `.dif`, `.dbf`, `.pdf`, and its own `.fxl` native format. See [File Formats](#file-formats) for which of these can also be saved.
- **Recent files:** File -> Recent Files shows the last-used workbooks.

### The Window Layout

| Area | Purpose |
|---|---|
| **Ribbon** | Tabbed toolbar: Home, Insert, Draw, Page Layout, Formulas, Data, Review, View, Chart (context), and PivotTable (context). |
| **Formula Bar** | Displays and edits the active cell's content or formula. Expand it with Ctrl+Shift+U. |
| **Grid** | The cell grid. Click to navigate; drag to select. |
| **Sheet Tabs** | Add, rename, reorder, hide, or delete sheets by right-clicking a tab. |
| **Status Bar** | Sum, count, average, min, and max for the current selection. |

### Saving

- **Save (Ctrl+S):** Overwrites the current file. New workbooks prompt for Save As.
- **Save As (F12):** Choose a name, folder, and format (`.xlsx`, `.csv`, `.fxl`).
- **Auto-save is not enabled by default.** Save frequently.

---

## Working with Cells

### Entering Data

Click a cell and type. Press:
- **Enter** - confirm and move down.
- **Tab** - confirm and move right.
- **Escape** - cancel the edit.
- **F2** - enter edit mode for an existing cell.
- **Alt+Enter** - insert a line break within a cell.

### Navigating

| Key | Action |
|---|---|
| Arrow keys | Move one cell |
| Ctrl+Arrow | Jump to the last non-empty cell in a direction |
| Home / Ctrl+Home | Start of row / go to A1 |
| Ctrl+End | Go to last used cell |
| Ctrl+G / F5 | Go To dialog - jump to any cell address |
| Page Up / Down | Move by viewport page |
| Alt+Page Up / Down | Move one page left/right |

### Selecting

| Key | Action |
|---|---|
| Shift+Arrow | Extend selection |
| Ctrl+Shift+Arrow | Extend to data boundary |
| Ctrl+A | Select current data region; press again for whole sheet |
| Ctrl+Space / Shift+Space | Select entire column(s) / row(s) |
| F8 | Toggle Extend Selection mode |
| Shift+F8 | Toggle Add to Selection mode |
| Alt+; | Select visible cells only |

### Cut, Copy, and Paste

- **Ctrl+C / Ctrl+X / Ctrl+V** - standard copy, cut, paste.
- **Ctrl+Alt+V** - Paste Special: choose values, formats, formulas, transpose, arithmetic operations, paste link, paste picture, and more.
- **Paste Values (Ctrl+Shift+V)** - pastes results only, no formulas.

### Insert and Delete Rows/Columns

- **Ctrl++ / Ctrl+Shift+=** - Insert rows or columns (dialog selects shift direction).
- **Ctrl+-** - Delete rows or columns (dialog selects shift direction).
- Right-click a row or column header for Insert/Delete/Hide/Unhide options.

### Merge Cells

Home tab -> Merge & Center drop-down: Merge and Center, Merge Across, Merge Cells, Unmerge Cells.

### Freeze Panes

View tab -> Freeze Panes: freeze top row, first column, or a custom split at the active cell.

---

## Formulas and Functions

### Entering a Formula

Type `=` to start a formula. FreeX supports a broad spreadsheet-formula syntax, including:
- Arithmetic: `+`, `-`, `*`, `/`, `^`
- Comparison: `=`, `<>`, `<`, `>`, `<=`, `>=`
- Text concatenation: `&`
- Array literals: `{1,2,3}` or `{1;2;3}`

While you are typing a new formula, the status bar shows **Enter** mode. Selecting a cell or range with the mouse or arrow keys inserts that reference into the formula and switches the status bar to **Point** mode. Press **F2** while editing a formula to toggle between **Edit** mode, where arrow keys move the text caret, and **Point** mode, where arrow keys select worksheet references. If a reference is selected in the formula text, selecting another cell or range replaces that reference; to add another argument, type the separator or operator first, such as `,` or `+`, and then select the next range.

### Cross-Sheet References

Use `SheetName!A1` or `'Sheet Name'!A1` to reference another worksheet.

### Dynamic Arrays

Functions that return multiple values (FILTER, SORT, UNIQUE, SEQUENCE, RANDARRAY, XLOOKUP, XMATCH, and others) spill results into adjacent cells automatically. The spill range is bordered with a blue outline.

### Supported Functions

FreeX implements **488 in-scope Excel functions**, including:

| Category | Examples |
|---|---|
| Math & Trig | SUM, SUBTOTAL, AGGREGATE, SUMIF, SUMIFS, SUMPRODUCT, SERIESSUM, MMULT, MUNIT, MINVERSE |
| Statistical | AVERAGE, COUNT, CORREL, STDEVA, VARPA, BINOM.DIST, POISSON.DIST, CONFIDENCE.NORM, Z.TEST, TRIMMEAN |
| Lookup & Reference | VLOOKUP, HLOOKUP, LOOKUP, XLOOKUP, INDEX, MATCH, XMATCH, OFFSET, INDIRECT, CHOOSE |
| Dynamic Arrays | FILTER, SORT, SORTBY, UNIQUE, SEQUENCE, TAKE, DROP, EXPAND, CHOOSECOLS, TOCOL, WRAPROWS |
| Text | LEFT, MID, TEXTBEFORE, TEXTAFTER, TEXTSPLIT, VALUETOTEXT, REGEXREPLACE, REGEXTEST, BAHTTEXT, ENCODEURL |
| Date & Time | TODAY, NOW, DATE, YEAR, MONTH, DAY, WORKDAY, NETWORKDAYS, EOMONTH, DATEDIF |
| Logical | IF, IFS, AND, OR, NOT, IFERROR, IFNA, SWITCH, CHOOSE |
| Higher-Order | LET, LAMBDA, MAP, REDUCE, SCAN, BYROW, BYCOL, MAKEARRAY |
| Financial | NPV, IRR, PMT, CUMIPMT, CUMPRINC, AMORLINC, XNPV, XIRR, PRICE, YIELD, ACCRINT |
| Information | ISNUMBER, ISTEXT, ISBLANK, ISERROR, ADDRESS, CELL, FORMULATEXT, SHEET, TYPE, NA, ISREF |
| Database | DSUM, DAVERAGE, DCOUNT, DCOUNTA, DGET, DMAX, DMIN, DPRODUCT, DSTDEV, DSTDEVP, DVAR, DVARP |
| Engineering | CONVERT, BIN2DEC, DEC2HEX, BITAND, BITOR, BITXOR, GESTEP |

Press **Shift+F3** to open the Insert Function dialog with category search.

### Name Manager

- **Ctrl+F3** - Open Name Manager to create, edit, and delete named ranges.
- **Ctrl+Shift+F3** - Create names from selected row/column labels.
- Type a name directly in the Name Box (left of the formula bar) and press Enter to define a name.

### Formula Auditing

Formulas tab -> Trace Precedents / Trace Dependents draws arrows showing cell relationships. Evaluate Formula steps through a formula's calculation order.

Error Checking lists deterministic workbook issues such as cached formula errors including `#DIV/0!`, `#VALUE!`, `#REF!`, `#NAME?`, `#N/A`, `#NUM!`, `#NULL!`, `#SPILL!`, `#CALC!`, and `#CIRCULAR!`, inconsistent formulas, formulas stored as text including apostrophe-prefixed and fullwidth-equals formulas, text-number/date warnings including apostrophe-prefixed, fullwidth digit/comma/decimal/scientific-notation forms with normalized exponent signs including Unicode minus, small comma/decimal/sign, ordinary-space and no-break/thin-space group separators, trailing-sign number text, and Arabic-Indic/extended Arabic-Indic digit/decimal/thousands/percent variants, currency-symbol including yen/fullwidth yen, fullwidth dollar/pound/won, small dollar, Indian rupee, won, shekel, baht, ruble, lira, Philippine peso, Vietnamese dong, naira, hryvnia, tenge, and Costa Rican colon symbols with ASCII, Unicode minus, fullwidth plus/minus, and small hyphen-minus signs, ASCII/fullwidth/small/Arabic percent signs, and accounting-parentheses number text plus apostrophe-prefixed, separator, month-name, weekday-prefixed, and fullwidth digit/Latin-letter/separator/comma numeric or month-name text dates with two-digit years, blank references, omitted aggregate cells across aggregate-family formulas including `AVERAGEA`, `MINA`, and `MAXA`, unlocked formula cells, and invalid data-validation entries. Use Ignore Error for a selected issue or File -> Options -> Formulas to change the supported rule set.

### Calculation

- **F9** - Recalculate the workbook.
- **Shift+F9** - Recalculate the active sheet only.
- **Ctrl+Alt+F9** - Force full workbook recalculation.
- File -> Options -> Formulas to switch between Automatic, Automatic Except Data Tables, and Manual.

---

## Formatting Cells

### Quick Formatting (Home Tab)

| Action | Shortcut |
|---|---|
| Bold | Ctrl+B |
| Italic | Ctrl+I |
| Underline | Ctrl+U |
| Strikethrough | - (Home tab) |
| Font color / Fill color | Home tab dropdowns |
| Borders | Home tab Borders gallery |
| Number format: General | Ctrl+Shift+~ |
| Number format: Number | Ctrl+Shift+! |
| Number format: Time | Ctrl+Shift+@ |
| Number format: Date | Ctrl+Shift+# |
| Number format: Currency | Ctrl+Shift+$ |
| Number format: Percentage | Ctrl+Shift+% |
| Number format: Scientific | Ctrl+Shift+^ |

### Format Cells Dialog (Ctrl+1)

Six tabs:
- **Number** - Choose category (General, Number, Currency, Accounting, Date, Time, Percentage, Fraction, Scientific, Text, Special, Custom) and set decimal places, symbols, and negative-number display.
- **Alignment** - Horizontal/vertical alignment, text wrap, shrink-to-fit, indent, text rotation, and merge.
- **Font** - Font family, size, style, underline type, color, strikethrough, superscript, subscript.
- **Fill** - Background color, gradient, and pattern.
- **Border** - Line style, color, and preset/custom border edges.
- **Protection** - Locked and Hidden flags (take effect only when sheet protection is on).

### Column Width and Row Height

- Drag the column/row header border to resize.
- Double-click the header border for AutoFit.
- Home -> Format -> AutoFit Column Width / AutoFit Row Height.

### Cell Styles

Home -> Cell Styles gallery applies preset font, fill, border, and number-format combinations.

### Format Painter

Home -> Format Painter (paint-brush icon) copies the format of the selected cell. Click once to paint once; double-click to paint repeatedly. Press Escape to stop.

---

## Charts

### Creating a Chart

1. Select the data range including headers.
2. Insert tab -> choose a chart type.
3. The chart is inserted as a floating object on the active sheet.

### Supported Chart Types

| Family | Types |
|---|---|
| Column | Clustered, Stacked, 100% Stacked, 3-D Clustered |
| Bar | Clustered, Stacked, 100% Stacked, 3-D Clustered |
| Line | Line, Stacked Line, 100% Stacked Line, Line with Markers |
| Pie / Doughnut | Pie, Exploded Pie, Doughnut |
| Scatter (XY) | Scatter, Scatter with Lines, Scatter with Smooth Lines |
| Bubble | Bubble |
| Radar | Radar, Radar with Markers, Filled Radar |
| Stock | High-Low-Close, Open-High-Low-Close, Volume Stock |
| Surface | 2-D Surface, 3-D Surface (matrix rendering) |
| Statistical / chartEx | Histogram, Pareto, Box-and-Whisker |
| Hierarchy / chartEx | Treemap, Sunburst |
| Waterfall / Funnel | Waterfall, Funnel |
| Combo | Mixed series using secondary axis |
| Area | Area, Stacked Area |

Charts not yet supported for authoring/rendering: Filled Map. True 3-D surface mesh rendering remains partial; FreeX renders 3-D surface charts as a value-colored matrix.

### Chart Tab (Context)

When a chart is selected, a Chart tab appears in the ribbon with commands for:
- **Chart Type** - Change the chart family.
- **Switch Row/Column** - Swap the data orientation.
- **Chart Title / Axis Titles / Legend / Data Labels** - Toggle and configure labels.
- **Chart Area Fill / Plot Area Fill** - Set background colors.
- **Axis Scale** - Configure primary and secondary axis bounds, units, and log scale.
- **Gridlines** - Toggle major/minor horizontal and vertical gridlines.
- **Format Bar/Column** - Set gap width (0-500%) and overlap (-100-100%) for bar and column charts.
- **Format Bubble Chart** - Set bubble scale (1-300%), show/hide negative bubbles, and bubble size representation (Area or Width).

### Editing Chart Data

Double-click the chart to enter chart-edit mode. The selection handles show the data source range. Use the ribbon Chart tab to change series or chart options.

### Moving and Resizing Charts

Click the chart border to select it. Drag to move, drag corner handles to resize.

---

## PivotTables

### Creating a PivotTable

1. Select any cell in your data range.
2. Insert tab -> PivotTable.
3. Choose the source range and destination.
4. The PivotTable Field List opens on the right.

### Building the Layout

Drag fields from the field list into the four areas:
- **Filters** - Top-level report filters.
- **Columns** - Column groupings.
- **Rows** - Row groupings.
- **Values** - Summarized metrics (Sum, Count, Average, Min, Max, etc.).

### PivotTable Options

Right-click the PivotTable or use the PivotTable tab:
- **Refresh** - Reload data from the source range.
- **Change Data Source** - Update the source range.
- **Report Layout** - Compact, Outline, or Tabular form.
- **Field Settings** - Change aggregation function, number format, and field options.
- **Show Values As** - Percent of total, running total, difference from, rank, index, and other display modes.

### Grouping

Right-click a date or number field in the PivotTable to group by days, months, quarters, years, or custom ranges.

### Slicers and Timelines

Insert tab -> Insert Slicer / Insert Timeline (for date fields) to add visual filter controls. Multiple PivotTables can share the same slicer.

### PivotCharts

Insert tab -> PivotChart from a PivotTable to create a chart bound to the pivot data. Chart type changes and field layout updates synchronize with the PivotTable.

---

## Conditional Formatting

### Quick Rules (Home -> Conditional Formatting)

- **Highlight Cell Rules** - Greater than, Less than, Between, Equal to, text containing, dates.
- **Top/Bottom Rules** - Top 10 items, bottom 10%, above/below average.
- **Data Bars** - Proportional fill bars inside cells.
- **Color Scales** - Two- or three-color gradient across the range.
- **Icon Sets** - Traffic lights, arrows, flags, ratings, and more.
- **New Rule** - Full rule builder for cell value, formula, or any condition.

### Managing Rules

Home -> Conditional Formatting -> Manage Rules opens the rule manager for the selection or entire sheet. Rules apply in listed priority order; check "Stop If True" to prevent lower-priority rules from running.

### Formula-Based Rules

Use a formula that returns TRUE/FALSE to apply to any range. The formula references the top-left cell of the applied range. For example, `=$B2>100` highlights rows where column B exceeds 100.

---

## Data Tools

### Sort

Data tab -> Sort (A-Z, Z-A, or custom multi-level sort dialog). Sort by cell value, cell color, font color, or icon. Hold Shift while clicking the header-sort buttons to add secondary sort keys.

### AutoFilter

Data tab -> AutoFilter (or Home -> Sort & Filter -> Filter). Column header dropdowns let you filter by value, color, text/number/date conditions, and search. The Data tab also has Clear Filter.

### Advanced Filter

Data tab -> Advanced Filter copies filtered rows to another location or filters in place, using a criteria range you define on the sheet.

### Text to Columns

Data tab -> Text to Columns splits cell content on a delimiter or fixed-width positions.

### Remove Duplicates

Data tab -> Remove Duplicates with column selection for matching.

### Data Validation

Data tab -> Data Validation. Set allowable values (whole number, decimal, list, date, time, text length, custom formula). Add an input message and error alert. Paste Validation transfers rules.

### Consolidate

Data tab -> Consolidate aggregates values from multiple ranges, including across sheets.

### What-If Analysis

Data tab -> What-If Analysis:
- **Goal Seek** - Find the input value that produces a target result.
- **Scenario Manager** - Name and switch between sets of input cell values. Summary reports can include optional result cells so each scenario column shows the resulting output values.
- **Data Table** - One- or two-variable sensitivity tables.

### Forecast Sheet

Data tab -> Forecast Sheet generates a forecast using exponential smoothing, adding a new sheet with projections and a chart.

### Subtotals

Data tab -> Subtotal inserts automatic subtotals at group changes. Group and Outline controls collapse/expand groups.

### Flash Fill

Data tab -> Flash Fill (or Ctrl+E) infers a pattern from your manual examples and fills the rest of the column.

FreeX supports deterministic Flash Fill patterns rather than Excel's full ML-like inference. Examples include:
- Extracting or removing delimiter-based tokens, including final dotted tokens such as file extensions, exact three-token first or middle dotted-token extraction, middle-token removal across exact three-token dotted or delimiter-separated values, leading dotted-token removal, leading delimiter-token removal, local file final path stems and parent folder names, variable-depth final and penultimate hyphen/slash/backslash/underscore segments, label-value splits/removals around punctuation, pipe, and ASCII/Unicode arrows or dashes, and first or last bracketed qualifiers.
- Extracting web and email domain pieces, including hosts, domain/public suffixes such as `northwind.co.uk` to `co.uk`, root-domain stems from variable-depth subdomains and curated multi-label public suffixes, ampersand- or semicolon-separated first/last query names, title-cased first/last query names such as `promoCode=spring` to `Promo Code`, first/last values, title-cased query values such as `category=powder-skis` to `Powder Skis`, same-name first values, and same-name last repeated values, decoded URL fragment identifiers and title-cased fragments such as `#powder-skis` to `Powder Skis`, final URL path segments such as `road-bike?ref=nav` to `road-bike`, decoded parent URL path segments such as `/catalog/bikes/road-bike.html` to `bikes`, first and second URL path segments such as `/regions/north/bikes/road-bike.html` to `regions` or `north`, and title-cased first, second, or parent URL path labels such as `/shop/powder-skis/powder-ski.html` to `Powder Skis`.
- Reformatting two- and three-part names, including last-name-first forms, middle-token drops, adjacent first/middle/last column combinations, and initial abbreviations such as `A. Lovelace`, `A. L.`, `Lovelace, Ada`, `Lovelace, A. B.`, `Ada B. Lovelace`, `Ada Byron L.`, `A. B. Lovelace`, and `A. B. L.`.
- Cleaning known titles, honorifics, credentials, and organization suffixes such as `Dr. Ada Lovelace Jr.`, `Reverend Grace Hopper R.N.`, comma-attached `Dr. Ada Lovelace,Jr.,Ph.D.`, `Northwind Traders,LLC`, or international legal suffixes such as `Contoso Sdn Bhd`, `Fabrikam Research,E.U.R.L.`, and `Tailspin Zrt Nyrt`.
- Building first/last-name email aliases with learned constant domains and `.`, `_`, or `-` separators, including first-initial/last-name, last-name/first-initial, first/last-initial forms, three-part full-name middle-initial aliases such as `ada.b.lovelace@contoso.com`, `ablovelace@contoso.com`, `lovelace.ada.b@contoso.com`, `lovelaceab@contoso.com`, or `lovelaceba@contoso.com`, and adjacent first/middle/last column sources that either use only the first/last names or include the middle initial.
- Applying digit-mask punctuation copied from examples, such as phone-number formatting, extracting US phone area codes/local numbers, and extracting phone extensions from `x`, `ext`, `ext.`, or `extension` markers.
- Normalizing calendar-valid numeric or English month-name dates, including ordinal day suffixes, calendar-quarter outputs such as `2023-02-09` to `Q1`, weekday-prefixed and embedded numeric or month-name date component extraction, and month-name dates embedded in labels.
- Extracting or normalizing time-like values, including hour, minute, second, meridiem, embedded time components from labels, embedded 12-hour or 24-hour times, and supported endpoints from two-time ranges including same-label ranges when examples are unambiguous.
- Extracting US address components such as street, street number, street name, street without trailing unit/suite, unit/suite suffix including spaced hash forms such as `Unit # 5`, unit/suite identifier, city, state, ZIP, ZIP+4 base ZIP, and the ZIP+4 extension.

If the examples are ambiguous, inconsistent, or outside the supported pattern set, Flash Fill leaves the remaining cells unchanged.

---

## Printing and Exporting

### Print Preview (Ctrl+P)

The print preview shows how the active sheet will print. Controls in the preview:
- **Printer** - Choose the output device.
- **Copies** - How many copies.
- **Print Range** - All pages, current page, or a custom page range.
- **Orientation, Paper Size, Margins, Scale** - Adjust without leaving the preview.
- **Gridlines / Headings** - Toggle printed gridlines and row/column headers.
- **Ignore Print Area** - Preview/print the full sheet, ignoring any set print area.

### Page Setup

Page Layout tab -> Page Setup:
- **Page** - Orientation, scaling (% or fit to NxM pages), paper size.
- **Margins** - Top, bottom, left, right, header, footer.
- **Sheet** - Print area, print titles (rows/columns to repeat), gridlines, headings, row/column order.

### Setting a Print Area

Select the range to print, then Page Layout -> Print Area -> Set Print Area. Page Layout -> Print Area -> Clear Print Area removes it.

### Export to PDF

File -> Export to PDF/XPS. Options:
- **Active sheet, selected range, or entire workbook.**
- **Page range and quality** (standard/minimum size).
- **Bookmarks** - Sheet names, print titles, or page numbers.
- **PDF options** - Open-after-publish, initial view, bitmap text mode.

### Headers and Footers

Insert tab -> Header & Footer (or Page Layout -> Page Setup -> Header/Footer tab) to add page numbers, file name, date, and custom text to printed pages.

---

## File Formats

### XLSX (`.xlsx`)

The primary format. FreeX reads and writes standard OOXML `.xlsx` files. When opening an Excel-authored file:
- All supported features are loaded into the workbook model.
- Unsupported features (VBA macros, Power Query, ActiveX controls, etc.) are detected and reported as warnings. The package parts for those features are preserved and written back unchanged so you do not lose them when saving.

### Other Excel formats (open-only)

FreeX opens but cannot save back to these Excel formats; use Save As to write an editable `.xlsx` or `.fxl` copy:
- `.xlsm` (macro-enabled workbook), `.xltx`/`.xltm` (templates, open as a new untitled workbook).
- Legacy `.xls`, `.xlsb` (binary), and `.xlt` (legacy template).

### CSV, TSV, and delimited text (`.csv`, `.tsv`, `.tab`, `.txt`)

FreeX opens and saves these as single-sheet workbooks. Delimiter detection is automatic on open for `.csv`/`.txt`; `.tsv`/`.tab` are tab-delimited.

### Other spreadsheet interchange formats

FreeX also opens and saves `.ods` (OpenDocument Spreadsheet), `.html`/`.htm` and `.mht`/`.mhtml` (web page), `.xml` (XML Spreadsheet 2003), `.slk` (SYLK), and `.dif` (Data Interchange Format). `.dbf` (dBASE) and `.pdf` are open-only, matching Excel's own read-only handling of those formats.

### Native Format (`.fxl`)

FreeX's own JSON-based format. Smaller than XLSX for workbooks without complex Excel-only metadata. Use `.fxl` when working primarily in FreeX; use `.xlsx` for compatibility with Excel and other applications.

### Opening Files with Warnings

If a workbook contains features FreeX cannot fully model (VBA, Power Query, embedded objects, etc.), an info bar shows on open. The file opens with all supported content visible; unsupported package parts are retained invisibly and will be written back on save. **No data is silently discarded.**

---

## Keyboard Shortcuts

### File and Application

| Shortcut | Action |
|---|---|
| Ctrl+N | New workbook |
| Ctrl+O | Open file |
| Ctrl+S | Save |
| F12 | Save As |
| F1 | Help online |
| Ctrl+W / Ctrl+F4 | Close workbook |
| Ctrl+P | Print preview |
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |

### Navigation

| Shortcut | Action |
|---|---|
| Ctrl+Arrow | Jump to data boundary |
| Ctrl+Home | Go to A1 |
| Ctrl+End | Go to last used cell |
| Ctrl+G / F5 | Go To |
| Ctrl+Backspace | Scroll active cell into view |

### Selection

| Shortcut | Action |
|---|---|
| Ctrl+A | Select data region / all |
| Ctrl+Space | Select column |
| Shift+Space | Select row |
| Ctrl+Shift+* | Select current region |
| Alt+; | Select visible cells only |

### Editing

| Shortcut | Action |
|---|---|
| F2 | Edit active cell |
| Ctrl+F2 | Focus formula bar |
| Delete | Clear cell contents |
| Alt+Enter | Insert line break in cell |
| Ctrl+Enter | Fill selection with entry |
| Ctrl+' | Copy formula from cell above |
| Ctrl+Shift+" | Copy value from cell above |
| Ctrl+D / Ctrl+R | Fill down / fill right |

### Formatting

| Shortcut | Action |
|---|---|
| Ctrl+1 | Format Cells dialog |
| Ctrl+B | Bold |
| Ctrl+I | Italic |
| Ctrl+U | Underline |
| Ctrl+Shift+~ | General number format |
| Ctrl+Shift+! | Number format |
| Ctrl+Shift+# | Date format |
| Ctrl+Shift+$ | Currency format |
| Ctrl+Shift+% | Percentage format |
| Ctrl+Shift+& | Outline border |
| Ctrl+Shift+_ | Remove borders |

### Formulas

| Shortcut | Action |
|---|---|
| Shift+F3 | Insert Function |
| Ctrl+F3 | Name Manager |
| Ctrl+Shift+F3 | Create Names from Selection |
| Ctrl+\` | Show Formulas |
| F9 | Recalculate |
| Ctrl+Shift+U | Expand/collapse formula bar |

### Find and Replace

| Shortcut | Action |
|---|---|
| Ctrl+F | Find |
| Ctrl+H | Replace |

### Data

| Shortcut | Action |
|---|---|
| Ctrl+E | Flash Fill |
| Ctrl+Shift+L | Toggle AutoFilter |

---

## Tips and Tricks

- **Named Ranges in Formulas:** Type a range name instead of a cell address. Name Manager (Ctrl+F3) lists all defined names.
- **Absolute vs. Relative References:** Use `$A$1` for absolute, `A1` for relative, `$A1` or `A$1` for mixed. Press **F4** while editing a cell reference to cycle through the options.
- **Array Formulas:** Most functions handle arrays natively. For legacy array behavior, Ctrl+Shift+Enter enters a curly-brace array formula.
- **Custom Number Formats:** In Format Cells -> Number -> Custom, enter XLSX number-format codes (e.g., `#,##0.00` for two-decimal thousands, `dd/mm/yyyy` for dates).
- **Freeze Headers:** View -> Freeze Top Row keeps row 1 visible while scrolling.
- **Multiple Sheets:** Right-click a sheet tab for color, rename, move, copy, hide/unhide, and insert options. Hold Ctrl (or Command on macOS) while clicking tabs to select multiple sheets; use Shift-click to select a range of visible sheets.
- **Spell Check (F7):** Checks the active sheet's text content, including common office, spreadsheet/business-report, data/analytics, sales/marketing/customer, customer-service/helpdesk/SLA, subscription/licensing/renewal, media/creative/design, IT/cloud/system, telecom/networking, formula/function/reporting, product/engineering/planning, quality/testing, documentation/support, reliability/maintenance, operations/planning, budget/stakeholder/project-control, procurement/inventory/supplier, finance/accounting/ledger, tax/audit/billing, banking/treasury, insurance/actuarial, healthcare/clinical, education/academic, facilities/real-estate, manufacturing/production, retail/e-commerce, energy/utilities, environment/sustainability, construction/field-service, transport/logistics, hospitality/food-service, government/public-sector, nonprofit/fundraising, research/lab/science, agriculture/field-operations, travel/events, sports/fitness/wellness, public-safety/weather/emergency, calendar/status, report-typo, risk/action tracking, invoice/supply-chain, meeting/communication, people/HR, UI/accessibility/ribbon, release/packaging/installer, localization/globalization/resource, legal/compliance, and security/access misspellings such as `availible`, `statment`, `dashbord`, `metrc`, `analytcs`, `campain`, `marketting`, `pipline`, `opportunty`, `helpdeskk`, `escalaton`, `incidnt`, `workarond`, `prioroty`, `severty`, `outagee`, `ticketng`, `queu`, `breachd`, `servcelevel`, `supportdesk`, `triagee`, `callbackk`, `chatbottt`, `subscrption`, `subscribtion`, `licnse`, `licensng`, `renewl`, `renewel`, `expiraton`, `expirng`, `cancelation`, `cancellaton`, `entitlment`, `overagee`, `prorateed`, `seatss`, `triall`, `billngcycle`, `mockp`, `desgin`, `brandng`, `pallete`, `wirefram`, `prototyp`, `renderng`, `typograpy`, `storybord`, `copywritng`, `illustation`, `deploymnt`, `databse`, `configration`, `syncronize`, `fiberr`, `opticl`, `modemm`, `latancy`, `bandwith`, `gatewayy`, `cellularr`, `subscrber`, `activaton`, `provisoning`, `vlookp`, `xlookp`, `pivottabel`, `functon`, `requirment`, `roadmp`, `backlogg`, `featre`, `testng`, `qaulity`, `validaton`, `coverge`, `manul`, `documnt`, `suport`, `troubleshot`, `relability`, `incidentt`, `outtage`, `maintnance`, `milstone`, `dependancy`, `budjet`, `stakehlder`, `estimte`, `changereq`, `inventry`, `suppler`, `warehous`, `procuremnt`, `acount`, `payble`, `ledgr`, `reconcilation`, `witholding`, `taxble`, `deducton`, `billng`, `auditt`, `reimbursment`, `treasry`, `cashflw`, `liqudity`, `collaterl`, `princpal`, `intrest`, `maturty`, `escroww`, `disbursemnt`, `settlemnt`, `transacton`, `bankng`, `premum`, `deductble`, `cliam`, `cliams`, `acturial`, `annuitiy`, `underwritng`, `benificiary`, `reinsurence`, `endorsemnt`, `patint`, `patints`, `symptms`, `clinicly`, `treatmnt`, `diagnosys`, `medicaton`, `prescriptn`, `vaccinaton`, `laborotory`, `studnt`, `studnts`, `clasroom`, `curriculm`, `assignmnt`, `syllbus`, `registrr`, `enrollmnt`, `attendence`, `gradution`, `facilty`, `facilties`, `tenent`, `occupncy`, `leasng`, `maintenence`, `renovatn`, `utilties`, `janitoral`, `inspeciton`, `manufactruing`, `prodction`, `assembely`, `machinary`, `shiftt`, `yieldd`, `scrapp`, `throughputt`, `downtimee`, `linebalnce`, `merchandisng`, `checkuot`, `catlog`, `catlogue`, `shpping`, `refundd`, `promotn`, `couponn`, `fulfilmnt`, `curbsidee`, `wishlistt`, `electricty`, `generaton`, `transmision`, `voltagee`, `sustainablity`, `emisions`, `decarbonizaton`, `renewble`, `biodiveristy`, `conservaton`, `recyling`, `compostng`, `greenhose`, `disclousre`, `efficency`, `climte`, `stewardhsip`, `constrction`, `contracor`, `subcontracor`, `bluepritn`, `permitt`, `insulatoin`, `excavatoin`, `scafolding`, `safetey`, `punchlistt`, `walkthru`, `workordr`, `transporation`, `logstics`, `routng`, `dispatchh`, `schedulng`, `resturant`, `caterng`, `reservaton`, `hospitallity`, `roomservce`, `municipl`, `constituant`, `appropreation`, `donaton`, `fundraisin`, `volunter`, `reserch`, `experment`, `sampel`, `samplng`, `labratory`, `protocolll`, `hypothsis`, `analysisis`, `reagentt`, `calibraton`, `microscop`, `sequencng`, `genotypingg`, `chromatograpy`, `spectromtry`, `centrifugee`, `incubaton`, `replicatee`, `specimennt`, `harvst`, `irrigaton`, `fertilzer`, `pesticde`, `croping`, `seedlng`, `greenhous`, `livestok`, `pasturee`, `orchrd`, `vinyard`, `grazng`, `fencng`, `manuree`, `sprayng`, `nurseryy`, `pollinaton`, `ripenes`, `grainn`, `travell`, `itinery`, `bookng`, `fligth`, `airlnie`, `departue`, `arival`, `passprt`, `bagage`, `lugage`, `boardng`, `airfaire`, `shuttel`, `veneu`, `confernce`, `registraton`, `sessoin`, `speker`, `exhbit`, `bootth`, `athleet`, `competion`, `tournment`, `pracitce`, `equpment`, `fitnes`, `wellnes`, `exercize`, `workot`, `leaguee`, `seasn`, `scorebord`, `scorng`, `officiatng`, `conditoning`, `hydraton`, `membrship`, `schedual`, `regimn`, `rehabilitaton`, `emergncy`, `evacuaton`, `sheltr`, `respnse`, `hazrd`, `wildifre`, `floodng`, `stormm`, `smokee`, `alertt`, `drilll`, `sirenn`, `rescuee`, `outbrek`, `quarantne`, `sanitaton`, `dispatchr`, `weathr`, `advisry`, `warningg`, `calandar`, `dedline`, `stauts`, `feild`, `flitered`, `timline`, `risck`, `actoin`, `mitgation`, `escallate`, `custmer`, `invoce`, `paymnt`, `shippment`, `meating`, `agnda`, `communcation`, `notfication`, `employe`, `maneger`, `departmant`, `perfomance`, `accesibility`, `acessibility`, `keybord`, `shortct`, `shortcutt`, `ribbn`, `toolbaar`, `dialogg`, `buton`, `checkbx`, `comboboxx`, `tooltp`, `navigaton`, `focuss`, `screenreder`, `alttextt`, `keytipp`, `instalation`, `instaler`, `packge`, `packging`, `publsh`, `publishng`, `artifactt`, `verison`, `manifestt`, `manfiest`, `certificat`, `signng`, `previeww`, `distributon`, `releasecandidate`, `localizaton`, `globalizaton`, `internatonalization`, `translaton`, `langauge`, `cultre`, `resorce`, `resxfile`, `fallbackk`, `localee`, `regionalseting`, `pseudolocalizaton`, `pluralizaton`, `timezonee`, `righttoleft`, `complaince`, `polcy`, `contrct`, `privicy`, `confidental`, `securty`, `permisson`, `passwrod`, and `firewal`.
