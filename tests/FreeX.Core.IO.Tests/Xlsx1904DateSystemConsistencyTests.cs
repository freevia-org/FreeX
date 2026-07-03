using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the 1904-date-system storage/interpretation mismatch (review finding F4):
/// a date/time cell value loaded from a 1904-date-system xlsx was stored as a 1900-epoch OADate
/// serial by <see cref="XlsxClosedXmlCellMapper"/>, while the 1904-aware date functions
/// (YEAR/MONTH/DAY/...) reinterpret that same stored serial as a 1904-epoch-relative serial when
/// <see cref="Workbook.Uses1904DateSystem"/> is true — so every date formula referencing a loaded
/// 1904-workbook date cell was off by the 1462-day epoch difference (~4 years). The fix makes the
/// mapper produce a 1904-epoch-relative serial on load (matching the raw on-disk serial Excel itself
/// writes for a date1904 workbook) so storage and function interpretation agree, and performs the
/// mirror-image conversion on save.
/// </summary>
public sealed class Xlsx1904DateSystemConsistencyTests
{
    [Fact]
    public void DateCellFromXlsx1904Workbook_FeedsCorrectCalendarValuesToDateFunctions()
    {
        // Simulate an Excel-authored 1904-system workbook containing a known date, by round-tripping
        // through ClosedXML directly (bypassing our own cell mapper) so the xlsx package's raw <v>
        // serial is whatever ClosedXML itself writes for date1904="1" plus a real DateTime cell value —
        // i.e. exactly what a genuine external 1904 workbook would contain on disk.
        var knownDate = new DateTime(2024, 6, 15);
        using var package = BuildClosedXml1904PackageWithDateCell(knownDate);

        var workbook = new XlsxFileAdapter().Load(package);
        workbook.Uses1904DateSystem.Should().BeTrue();

        var sheet = workbook.GetSheetAt(0);

        sheet.GetValue(1, 1).Should().BeOfType<DateTimeValue>();

        // Drive YEAR/MONTH/DAY via real formula cells referencing the loaded date cell, then
        // recalculate exactly like the app would.
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 1), "YEAR(A1)");
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 1), "MONTH(A1)");
        sheet.SetFormula(new CellAddress(sheet.Id, 4, 1), "DAY(A1)");

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(2024), "YEAR() must not be off by the 1462-day/~4-year 1904 epoch shift");
        sheet.GetValue(3, 1).Should().Be(new NumberValue(6));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void DateCell_SavedFromA1904Workbook_RoundTripsToTheSameCalendarDate()
    {
        // Build entirely through the FreeX model/adapter (Save then Load) to verify the WRITE path
        // (XlsxClosedXmlCellMapper.MapValueInverse) is the consistent mirror image of the read path:
        // a DateTimeValue authored under the 1904 convention must reload to the same calendar date.
        var workbook = new Workbook("RoundTrip1904") { Uses1904DateSystem = true };
        var sheet = workbook.AddSheet("Data");
        var address = new CellAddress(sheet.Id, 1, 1);

        var knownDate = new DateTime(1950, 3, 20);
        double serial1904 = (knownDate - new DateTime(1904, 1, 1)).TotalDays;
        sheet.SetCell(address, new DateTimeValue(serial1904));

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Uses1904DateSystem.Should().BeTrue();

        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.SetFormula(new CellAddress(reloadedSheet.Id, 2, 1), "YEAR(A1)");
        reloadedSheet.SetFormula(new CellAddress(reloadedSheet.Id, 3, 1), "MONTH(A1)");
        reloadedSheet.SetFormula(new CellAddress(reloadedSheet.Id, 4, 1), "DAY(A1)");

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(reloaded);

        reloadedSheet.GetValue(2, 1).Should().Be(new NumberValue(1950));
        reloadedSheet.GetValue(3, 1).Should().Be(new NumberValue(3));
        reloadedSheet.GetValue(4, 1).Should().Be(new NumberValue(20));

        // The reloaded cell's own internal serial should also be YEAR/MONTH/DAY-consistent when
        // evaluated directly (storage and function interpretation must agree).
        var evaluator = new FormulaEvaluator();
        evaluator.Evaluate("=YEAR(A1)", reloadedSheet, reloaded).Should().Be(new NumberValue(1950));
    }

    /// <summary>
    /// Builds a minimal single-sheet xlsx package using ClosedXML directly (not FreeX's
    /// <see cref="XlsxFileAdapter"/> save path), with workbookPr date1904="1" and cell A1 holding the
    /// raw numeric serial a genuine Excel-authored 1904-system workbook would contain for
    /// <paramref name="date"/> (day count since 1904-01-01). ClosedXML itself always writes cell dates
    /// as 1900-epoch OADate serials regardless of the date1904 flag, so both the flag and the cell's
    /// &lt;v&gt; are patched by hand here to reproduce the real on-disk 1904 contract, independent of
    /// any assumption FreeX's own save path makes.
    /// </summary>
    private static MemoryStream BuildClosedXml1904PackageWithDateCell(DateTime date)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        sheet.Cell("A1").Value = date;

        var package = new MemoryStream();
        workbook.SaveAs(package);
        package.Position = 0;

        XlsxPackageTestHelper.PatchPackageXml(package, "xl/workbook.xml", document =>
        {
            // ClosedXML writes the package with a PREFIXED spreadsheetml namespace (<x:worksheet>,
            // <x:v>, …) and no default xmlns, so GetDefaultNamespace() would return the empty
            // namespace and match nothing. Use the spreadsheetml namespace explicitly.
            System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var root = document.Root!;
            var workbookPr = root.Element(ns + "workbookPr");
            if (workbookPr is null)
            {
                workbookPr = new System.Xml.Linq.XElement(ns + "workbookPr");
                root.AddFirst(workbookPr);
            }
            workbookPr.SetAttributeValue("date1904", "1");
        });

        double serial1904 = (date - new DateTime(1904, 1, 1)).TotalDays;
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/worksheets/sheet1.xml", document =>
        {
            // ClosedXML writes the package with a PREFIXED spreadsheetml namespace (<x:worksheet>,
            // <x:v>, …) and no default xmlns, so GetDefaultNamespace() would return the empty
            // namespace and match nothing. Use the spreadsheetml namespace explicitly.
            System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var valueElement = document.Descendants(ns + "v").First();
            valueElement.Value = serial1904.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        });

        package.Position = 0;
        return package;
    }
}
