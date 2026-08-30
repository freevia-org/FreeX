using System.Globalization;

using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// Round-174 regression: shared-localization-rtl F1 - PivotFieldModel.SelectedItems ("Select
// Items..."/slicer per-field filters) is matched against a freshly computed row key
// (PivotTableRefreshService.MatchesFieldSelections -> GroupKeyText -> KeyText) that formats an
// ungrouped Number as `value.ToString(CultureInfo.CurrentCulture)` and an ungrouped Date as
// `ToShortDateString()` - both locale-dependent. But the "Select Items..." dialog and
// slicer/timeline panes read their candidate captions through
// SpreadsheetDisplayFormatter.FormatCellValue's CellDisplay profile, which formats a Number as
// `value.ToString(CultureInfo.InvariantCulture)` and a Date as a fixed "yyyy-MM-dd" invariant
// string - independent of CurrentCulture. So under any CurrentCulture that isn't itself
// invariant (e.g. de-DE, comma decimal / dot-order date), an invariant-formatted caption never
// matched the CurrentCulture-formatted row key, and the field silently filtered out every row.
// These tests hold CurrentCulture fixed at de-DE for the whole Refresh call (both "capture" and
// "refresh" happen under the one culture) because the invariant-caption defect reproduces
// standalone, with no locale change between sessions required - the cross-machine reopen
// scenario in the finding is a strict superset of this.
public sealed partial class PivotTableRefreshServiceTests
{
    private static void SeedFractionalQuantitySalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Quantity"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(1.5));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new NumberValue(2.5));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new NumberValue(3.5));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
    }

    private static void SeedShipDateSalesData(Sheet sheet, DateTime shipDate)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("ShipDate"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new DateTimeValue(shipDate.AddDays(-1).ToOADate()));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new DateTimeValue(shipDate.ToOADate()));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new DateTimeValue(shipDate.AddDays(1).ToOADate()));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
    }

    private static void RunUnderCulture(string cultureName, Action action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Refresh_NumberFieldSelectedItems_MatchesInvariantFormattedCaption_UnderCommaDecimalCulture()
    {
        RunUnderCulture("de-DE", () =>
        {
            var workbook = new Workbook("PivotRefreshTest");
            var sheet = workbook.AddSheet("Data");
            SeedFractionalQuantitySalesData(sheet);
            var pivot = new PivotTableModel
            {
                Name = "PivotTable1",
                CacheId = 1,
                SourceRange = Range(sheet, "A1", "B4"),
                TargetRange = Range(sheet, "D2", "F8")
            };
            // "2.5" is exactly what the real "Select Items..." dialog would have persisted for
            // this value (SpreadsheetDisplayFormatter.FormatCellValue -> InvariantCulture), NOT
            // what de-DE's CurrentCulture would format it as ("2,5").
            pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["2.5"]));
            pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

            PivotTableRefreshService.Refresh(workbook, sheet, pivot);

            // The row for 2.5 must survive the filter - and, since GroupKeyText still renders
            // the surviving row's own label under the active CurrentCulture, that label is the
            // de-DE comma form, not the invariant caption that selected it.
            Text(sheet, "D3").Should().Be("2,5");
            Number(sheet, "E3").Should().Be(20);
            Text(sheet, "D4").Should().Be("Grand Total");
            Number(sheet, "E4").Should().Be(20);
            sheet.GetCell(Addr(sheet, "D5")).Should().BeNull();
        });
    }

    [Fact]
    public void Refresh_NumberFieldSelectedItems_StillMatchesCurrentCultureFormattedCaption_UnderCommaDecimalCulture()
    {
        RunUnderCulture("de-DE", () =>
        {
            var workbook = new Workbook("PivotRefreshTest");
            var sheet = workbook.AddSheet("Data");
            SeedFractionalQuantitySalesData(sheet);
            var pivot = new PivotTableModel
            {
                Name = "PivotTable1",
                CacheId = 1,
                SourceRange = Range(sheet, "A1", "B4"),
                TargetRange = Range(sheet, "D2", "F8")
            };
            // Sibling/no-regression case: a caption captured via a connected slicer
            // (PivotSharedItemCaptionResolver, which formats with CurrentCulture) under the
            // SAME de-DE culture the refresh runs under must keep matching exactly as before -
            // the invariant fallback added for the dialog-caption case must not replace this
            // existing CurrentCulture match, only supplement it.
            pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["2,5"]));
            pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

            PivotTableRefreshService.Refresh(workbook, sheet, pivot);

            Text(sheet, "D3").Should().Be("2,5");
            Number(sheet, "E3").Should().Be(20);
            Text(sheet, "D4").Should().Be("Grand Total");
            Number(sheet, "E4").Should().Be(20);
            sheet.GetCell(Addr(sheet, "D5")).Should().BeNull();
        });
    }

    [Fact]
    public void Refresh_DateFieldSelectedItems_MatchesInvariantFormattedCaption_UnderDayFirstDateCulture()
    {
        RunUnderCulture("de-DE", () =>
        {
            var workbook = new Workbook("PivotRefreshTest");
            var sheet = workbook.AddSheet("Data");
            var shipDate = new DateTime(2026, 8, 30);
            SeedShipDateSalesData(sheet, shipDate);
            var pivot = new PivotTableModel
            {
                Name = "PivotTable1",
                CacheId = 1,
                SourceRange = Range(sheet, "A1", "B4"),
                TargetRange = Range(sheet, "D2", "F8")
            };
            // "2026-08-30" is exactly what the real "Select Items..." dialog would have
            // persisted (SpreadsheetDisplayFormatter's CellDisplay profile formats a Date as a
            // fixed invariant "yyyy-MM-dd"), NOT de-DE's short-date form ("30.08.2026").
            pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["2026-08-30"]));
            pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

            PivotTableRefreshService.Refresh(workbook, sheet, pivot);

            Text(sheet, "D3").Should().Be(shipDate.ToShortDateString());
            Number(sheet, "E3").Should().Be(20);
            Text(sheet, "D4").Should().Be("Grand Total");
            Number(sheet, "E4").Should().Be(20);
            sheet.GetCell(Addr(sheet, "D5")).Should().BeNull();
        });
    }

    [Fact]
    public void Refresh_DateFieldSelectedItems_StillMatchesCurrentCultureFormattedCaption_UnderDayFirstDateCulture()
    {
        RunUnderCulture("de-DE", () =>
        {
            var workbook = new Workbook("PivotRefreshTest");
            var sheet = workbook.AddSheet("Data");
            var shipDate = new DateTime(2026, 8, 30);
            SeedShipDateSalesData(sheet, shipDate);
            var pivot = new PivotTableModel
            {
                Name = "PivotTable1",
                CacheId = 1,
                SourceRange = Range(sheet, "A1", "B4"),
                TargetRange = Range(sheet, "D2", "F8")
            };
            // Sibling/no-regression case: a slicer-captured caption (CurrentCulture short-date
            // text) under the SAME culture the refresh runs under must keep matching exactly as
            // before.
            pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: [shipDate.ToShortDateString()]));
            pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

            PivotTableRefreshService.Refresh(workbook, sheet, pivot);

            Text(sheet, "D3").Should().Be(shipDate.ToShortDateString());
            Number(sheet, "E3").Should().Be(20);
            Text(sheet, "D4").Should().Be("Grand Total");
            Number(sheet, "E4").Should().Be(20);
            sheet.GetCell(Addr(sheet, "D5")).Should().BeNull();
        });
    }
}
