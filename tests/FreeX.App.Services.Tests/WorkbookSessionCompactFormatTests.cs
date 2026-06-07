using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionCompactFormatTests
{
    [Fact]
    public void ApplySelectedRangeCompactFormat_AppliesStyleBorderAndFontSizeAsSingleUndoableEdit()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var fill = new CellColor(252, 228, 214);
        var expectedBorder = new CellBorder(BorderStyle.Thin, CellColor.Black);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(a1, b2));

        var result = session.ApplySelectedRangeCompactFormat(
            new StyleDiff(Bold: true, FillColor: fill, FontSize: 24),
            CellBorderPreset.Outside);

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b2));
        var a1Style = GetStyle(workbook, sheet, a1);
        a1Style.Bold.Should().BeTrue();
        a1Style.FillColor.Should().Be(fill);
        a1Style.FontSize.Should().Be(24);
        a1Style.BorderTop.Should().Be(expectedBorder);
        a1Style.BorderLeft.Should().Be(expectedBorder);
        var b2Style = GetStyle(workbook, sheet, b2);
        b2Style.Bold.Should().BeTrue();
        b2Style.FillColor.Should().Be(fill);
        b2Style.FontSize.Should().Be(24);
        b2Style.BorderRight.Should().Be(expectedBorder);
        b2Style.BorderBottom.Should().Be(expectedBorder);
        sheet.RowHeights[1].Should().Be(37);
        sheet.RowHeights[2].Should().Be(37);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.CanUndo.Should().BeFalse();
        session.CanRedo.Should().BeTrue();
        GetStyle(workbook, sheet, a1).Should().Be(CellStyle.Default);
        sheet.GetStyleOnly(b2.Row, b2.Col).Should().BeNull();
        sheet.RowHeights.Should().BeEmpty();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.CanRedo.Should().BeFalse();
        GetStyle(workbook, sheet, a1).FontSize.Should().Be(24);
        GetStyle(workbook, sheet, b2).BorderBottom.Should().Be(expectedBorder);
        sheet.RowHeights[1].Should().Be(37);
        sheet.RowHeights[2].Should().Be(37);
    }

    [Fact]
    public void ApplySelectedRangeCompactFormat_PropagatesAcrossGroupedSheets()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB2 = new CellAddress(summary.Id, 2, 2);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var detailsB2 = new CellAddress(details.Id, 2, 2);
        SeedCells(summary, new GridRange(summaryA1, summaryB2));
        SeedCells(details, new GridRange(detailsA1, detailsB2));
        var expectedBorder = new CellBorder(BorderStyle.Thin, CellColor.Black);
        var expectedRowHeight = Math.Min(409.5, FontSizePlanner.EstimateFittingRowHeight(24));
        var session = CreateSession(workbook);
        session.SelectAllVisibleSheets();
        session.SelectRange(new GridRange(summaryA1, summaryB2));

        var result = session.ApplySelectedRangeCompactFormat(
            new StyleDiff(Italic: true, FontSize: 24),
            CellBorderPreset.Outside);

        result.Success.Should().BeTrue();
        GetStyle(workbook, summary, summaryA1).Italic.Should().BeTrue();
        GetStyle(workbook, summary, summaryA1).FontSize.Should().Be(24);
        GetStyle(workbook, summary, summaryA1).BorderTop.Should().Be(expectedBorder);
        GetStyle(workbook, details, detailsB2).Italic.Should().BeTrue();
        GetStyle(workbook, details, detailsB2).FontSize.Should().Be(24);
        GetStyle(workbook, details, detailsB2).BorderBottom.Should().Be(expectedBorder);
        summary.RowHeights[1].Should().Be(expectedRowHeight);
        details.RowHeights[2].Should().Be(expectedRowHeight);
        session.IsWorkbookGrouped.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        GetStyle(workbook, summary, summaryA1).Should().Be(CellStyle.Default);
        GetStyle(workbook, details, detailsB2).Should().Be(CellStyle.Default);
        summary.RowHeights.Should().BeEmpty();
        details.RowHeights.Should().BeEmpty();
    }

    [Fact]
    public void ApplySelectedRangeCompactFormat_UsesRequestedBorderStyleAndColorForRangeRelativePreset()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var borderColor = new CellColor(33, 115, 70);
        var expectedBorder = new CellBorder(BorderStyle.Double, borderColor);
        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(a1, b2));

        var result = session.ApplySelectedRangeCompactFormat(
            new StyleDiff(),
            CellBorderPreset.Outside,
            BorderStyle.Double,
            borderColor);

        result.Success.Should().BeTrue();
        GetStyle(workbook, sheet, a1).BorderTop.Should().Be(expectedBorder);
        GetStyle(workbook, sheet, a1).BorderLeft.Should().Be(expectedBorder);
        GetStyle(workbook, sheet, b2).BorderRight.Should().Be(expectedBorder);
        GetStyle(workbook, sheet, b2).BorderBottom.Should().Be(expectedBorder);
    }

    [Fact]
    public void ApplySelectedRangeCompactFormat_UsesRequestedBorderStyleAndColorForInsidePreset()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var borderColor = new CellColor(112, 48, 160);
        var expectedBorder = new CellBorder(BorderStyle.Dashed, borderColor);
        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(a1, b2));

        var result = session.ApplySelectedRangeCompactFormat(
            new StyleDiff(),
            CellBorderPreset.Inside,
            BorderStyle.Dashed,
            borderColor);

        result.Success.Should().BeTrue();
        var a1Style = GetStyle(workbook, sheet, a1);
        a1Style.BorderTop.Should().Be(new CellBorder(BorderStyle.None));
        a1Style.BorderLeft.Should().Be(new CellBorder(BorderStyle.None));
        a1Style.BorderRight.Should().Be(expectedBorder);
        a1Style.BorderBottom.Should().Be(expectedBorder);
        var b2Style = GetStyle(workbook, sheet, b2);
        b2Style.BorderTop.Should().Be(expectedBorder);
        b2Style.BorderLeft.Should().Be(expectedBorder);
        b2Style.BorderRight.Should().Be(new CellBorder(BorderStyle.None));
        b2Style.BorderBottom.Should().Be(new CellBorder(BorderStyle.None));
    }

    [Fact]
    public void ApplySelectedRangeCompactFormat_EmptyRequestSucceedsWithoutDirtyingWorkbook()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(workbook);
        session.SelectCell(a1);

        var result = session.ApplySelectedRangeCompactFormat(new StyleDiff(), borderPreset: null);

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        sheet.GetCell(a1).Should().BeNull();
        sheet.GetStyleOnly(a1.Row, a1.Col).Should().BeNull();
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static CellStyle GetStyle(Workbook workbook, Sheet sheet, CellAddress address)
    {
        var styleId = sheet.GetCell(address)?.StyleId ??
            sheet.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        return workbook.GetStyle(styleId);
    }

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }

    private static void SeedCells(Sheet sheet, GridRange range)
    {
        foreach (var address in range.AllCells())
            sheet.SetCell(address, new TextValue($"{address.Row},{address.Col}"));
    }
}
