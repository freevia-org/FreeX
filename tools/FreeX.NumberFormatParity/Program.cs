using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FreeX.ToolsShared.Wpf;

/// <summary>
/// FreeX NumberFormat Parity Capture Tool
///
/// Opens Excel via COM (late-binding, no PIA), iterates a matrix of
/// (value, formatCode) pairs, reads range.Text as ground truth, and
/// writes a CSV:
///   value,valueKind,formatCode,excelText
///
/// Run on Windows with Excel installed.
/// Culture is pinned to en-US before any COM calls.
///
/// Usage:
///   dotnet run --project tools/FreeX.NumberFormatParity -- [outputPath]
///   Default output: tests/FreeX.Core.Calc.Tests/TestData/ExcelNumberFormatMatrix.csv
/// </summary>

var culture = CultureInfo.GetCultureInfo("en-US");
System.Threading.Thread.CurrentThread.CurrentCulture = culture;
System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// ── Output path ───────────────────────────────────────────────────────────────
var outputPath = args.Length > 0 ? args[0] :
    Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",  // up to repo root from bin/Release/net10.0-windows/
        "tests", "FreeX.Core.Calc.Tests", "TestData", "ExcelNumberFormatMatrix.csv");
outputPath = Path.GetFullPath(outputPath);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

// ── Value matrix ─────────────────────────────────────────────────────────────
// ValueKind: Number | DateSerial | Text | Bool
// display: the raw value as stored in the CSV "value" column
var values = new List<(object rawValue, string kind, string display)>
{
    // Plain numbers
    (0.0,           "Number",     "0"),
    (1.0,           "Number",     "1"),
    (-1.0,          "Number",     "-1"),
    (0.5,           "Number",     "0.5"),
    (-0.5,          "Number",     "-0.5"),
    (1234.567,      "Number",     "1234.567"),
    (1234567.89,    "Number",     "1234567.89"),
    (0.00123,       "Number",     "0.00123"),
    (-1234.567,     "Number",     "-1234.567"),
    (1e15,          "Number",     "1E+15"),
    (1e-4,          "Number",     "1E-04"),

    // Date serials (Excel OA date serial numbers)
    (1.0,           "DateSerial", "1"),
    (2.0,           "DateSerial", "2"),
    (59.0,          "DateSerial", "59"),
    (60.0,          "DateSerial", "60"),
    (61.0,          "DateSerial", "61"),
    (45292.0,       "DateSerial", "45292"),
    (45292.520833,  "DateSerial", "45292.520833"),
    (45658.0,       "DateSerial", "45658"),

    // Fractions / edge cases
    (0.125,         "Number",     "0.125"),
    (0.3333,        "Number",     "0.3333"),
    (2.75,          "Number",     "2.75"),
    (-1.5,          "Number",     "-1.5"),

    // Text
    ("hello",       "Text",       "hello"),
    ("123",         "Text",       "123"),
    ("",            "Text",       ""),

    // Booleans
    (true,          "Bool",       "TRUE"),
    (false,         "Bool",       "FALSE"),
};

// ── Format codes ─────────────────────────────────────────────────────────────
var formats = new List<string>
{
    "General",
    "0",
    "0.00",
    "#,##0",
    "#,##0.00",
    "0%",
    "0.00%",
    "0.00E+00",
    "##0.0E+0",
    "# ?/?",
    "# ??/??",
    "?/8",
    "m/d/yyyy",
    "d-mmm-yy",
    "h:mm AM/PM",
    "h:mm:ss",
    "[h]:mm:ss",
    "[m]:ss",
    "m/d/yyyy h:mm",
    "mmmmm d yyyy",
    "0;-0;0;\"text\"",
    "0;(0);\"-\";@",
    "[Red]0;[Blue]0",
    "[>=1000]#,##0,\"K\";0",
    "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)",
    "[$€-407]#,##0.00",
    "0,",
    "0,,",
    "@",
};

// ── Excel COM capture (late binding) ─────────────────────────────────────────
Console.WriteLine($"Output: {outputPath}");
Console.WriteLine($"Matrix: {values.Count} values x {formats.Count} formats = {values.Count * formats.Count} cells");

dynamic? excel = null;
dynamic? wb = null;
dynamic? ws = null;

var rows = new List<string[]>();
int processed = 0;
int skipped = 0;

try
{
    try
    {
        excel = ExcelComAutomation.CreateExcelApplication(
            "ERROR: Excel.Application COM class not found. Is Excel installed?",
            "ERROR: Excel.Application COM activation returned null.");
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    excel.Visible = false;
    excel.DisplayAlerts = false;

    wb = excel.Workbooks.Add();
    ws = wb.Worksheets[1];

    // Use a single cell for all operations
    dynamic cell = ws.Cells[1, 1];

    foreach (var (rawValue, kind, display) in values)
    {
        foreach (var fmt in formats)
        {
            try
            {
                // Clear and reset
                cell.ClearContents();
                cell.NumberFormat = "General";

                // Set value based on kind
                if (kind == "Text")
                {
                    // Force text by setting format to @ first
                    cell.NumberFormat = "@";
                    cell.Value2 = rawValue;
                }
                else if (kind == "Bool")
                {
                    // Excel booleans: store as 1/0 but with boolean cell type isn't straightforward
                    // via Value2. Use Formula to set TRUE/FALSE.
                    cell.Formula = (bool)rawValue ? "=TRUE()" : "=FALSE()";
                }
                else
                {
                    // Number or DateSerial — both stored as double
                    cell.Value2 = rawValue;
                }

                // Apply format (may throw for invalid combinations)
                try
                {
                    cell.NumberFormat = fmt;
                }
                catch
                {
                    rows.Add([display, kind, fmt, "N/A"]);
                    skipped++;
                    continue;
                }

                // Read displayed text
                string text;
                try
                {
                    text = (string)(cell.Text ?? "");
                }
                catch
                {
                    text = "";
                }

                rows.Add([display, kind, fmt, text]);
                processed++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ERROR ({display}, {fmt}): {ex.Message}");
                rows.Add([display, kind, fmt, "N/A"]);
                skipped++;
            }
        }
    }

    Console.WriteLine($"Captured: {processed} OK, {skipped} N/A");
}
finally
{
    ExcelComAutomation.TryCloseWorkbook(wb);
    try { excel?.Quit(); } catch { /* ignore */ }
    ExcelComAutomation.ReleaseComObject(ws);
    ExcelComAutomation.ReleaseComObject(wb);
    ExcelComAutomation.ReleaseComObject(excel);
}

// ── Write CSV ─────────────────────────────────────────────────────────────────
using var sw = new StreamWriter(outputPath, false, System.Text.Encoding.UTF8);
sw.WriteLine("value,valueKind,formatCode,excelText");
foreach (var row in rows)
{
    sw.WriteLine(string.Join(",", row.Select(CsvEscape)));
}

Console.WriteLine($"Written: {outputPath}");
Console.WriteLine($"Rows: {rows.Count + 1} (including header)");
return 0;

static string CsvEscape(string s)
{
    // Always quote fields that contain comma, double-quote, newline, or CR
    if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    return s;
}
