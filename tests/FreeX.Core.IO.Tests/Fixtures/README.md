`Simple.xls` is copied from the ExcelDataReader test data set for legacy BIFF reader coverage.

Source: https://github.com/ExcelDataReader/ExcelDataReader/blob/develop/src/TestData/xls/SIMPLE.XLS
License: MIT, matching the upstream ExcelDataReader repository.

`Simple.xlsb` is copied from the ExcelDataReader test data set (`NumDoubleDateBoolString.xlsb`)
for BIFF12 binary-workbook reader coverage. It has three sheets; the first carries mixed cell
types (number, double, date, bool, text).

Source: https://github.com/ExcelDataReader/ExcelDataReader/blob/develop/src/TestData/NumDoubleDateBoolString.xlsb
License: MIT, matching the upstream ExcelDataReader repository.

`CellGradientFillLinear.xlsx` is an Excel-authored cell gradient-fill parity fixture used to
verify the WPF gradient render against Excel ground truth. It carries three labeled gradient
blocks on sheet `Gradient`:

- `B2:E5`  — 2-stop linear, degree 0 (left->right): blue (0,70,200) -> orange (255,140,0)
- `B7:E10` — 2-stop linear, degree 90 (top->bottom): green (0,160,0) -> white (255,255,255)
- `B12:E15` — 3-stop linear, degree 0: red (220,30,30) -> yellow (255,230,0) -> blue (30,60,220)

It was produced by the `--generate-excel-cellstyle-corpus-fixtures` tool (which lays out the
labels and ranges) and then having the `<gradientFill>` elements injected into `xl/styles.xml`
deterministically (the Excel COM `Interior.Gradient` API returns null when driven headless, so
the gradient stops are written directly into the OOXML). Excel opens the file without recovery and
reads back the stop counts/degrees/colors exactly (verified via `Interior.Gradient.ColorStops`).
