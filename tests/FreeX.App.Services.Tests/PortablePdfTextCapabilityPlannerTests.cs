using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class PortablePdfTextCapabilityPlannerTests
{
    [Fact]
    public void CreatePlan_ReturnsReadyForAsciiAndWinAnsiText()
    {
        var workbook = new Workbook("Budget Caf\u00e9");
        var sheet = workbook.AddSheet("R\u00e9sum\u00e9");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("S\u00e3o Paulo \u20ac \u2013"));
        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:A1", sheet.Id));

        var plan = PortablePdfTextCapabilityPlanner.CreatePlan(workbook, exportPlan);

        plan.IsReady.Should().BeTrue();
        plan.Status.Should().Be(PortablePdfTextCapabilityPlanStatus.Ready);
        plan.UnsupportedTextDiagnostics.Should().BeEmpty();
        plan.StatusText.Should().Be("Ready to render portable PDF text with ASCII/WinAnsi built-in Helvetica support: 4 text runs across 1 page.");
    }

    [Fact]
    public void CreatePlan_ReturnsUnsupportedUnicodeDiagnosticsForCyrillicAndEmojiText()
    {
        var workbook = new Workbook("Budget \u041a\u0438\u0457\u0432");
        var sheet = workbook.AddSheet("Summary");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region \U0001F4C8"));
        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:A1", sheet.Id));

        var plan = PortablePdfTextCapabilityPlanner.CreatePlan(workbook, exportPlan);

        plan.IsReady.Should().BeFalse();
        plan.Status.Should().Be(PortablePdfTextCapabilityPlanStatus.UnsupportedUnicodeText);
        plan.StatusText.Should().Contain("Portable PDF export currently supports ASCII and WinAnsi text only");
        plan.StatusText.Should().Contain("deferred embedded-font Unicode PDF path");
        plan.StatusText.Should().Contain("real licensed TrueType/OpenType font subset");
        plan.StatusText.Should().Contain("Type0/Identity-H text");
        plan.StatusText.Should().Contain("ToUnicode mappings");
        plan.StatusText.Should().Contain("parser, render, and text extraction validation");
        plan.StatusText.Should().Contain("workbook name on export page 1 contains U+041A");
        plan.StatusText.Should().Contain("cell A1 on export page 1 contains U+1F4C8");

        plan.UnsupportedTextDiagnostics.Should().HaveCount(2);
        var workbookDiagnostic = plan.UnsupportedTextDiagnostics
            .Should()
            .ContainSingle(diagnostic => diagnostic.Source == PortablePdfTextRunSource.WorkbookName)
            .Which;
        workbookDiagnostic.UnsupportedScalars.Select(scalar => scalar.CodePoint)
            .Should()
            .ContainInOrder("U+041A", "U+0438", "U+0457", "U+0432");

        var cellDiagnostic = plan.UnsupportedTextDiagnostics
            .Should()
            .ContainSingle(diagnostic => diagnostic.Source == PortablePdfTextRunSource.Cell)
            .Which;
        cellDiagnostic.Row.Should().Be(1);
        cellDiagnostic.Column.Should().Be(1);
        cellDiagnostic.UnsupportedScalars.Should().ContainSingle()
            .Which.CodePoint.Should().Be("U+1F4C8");
    }

    private static PortablePdfExportPlan CreateExportPlan(
        Workbook workbook,
        Sheet sheet,
        GridRange range)
    {
        var printPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: ResolveSheetIndex(workbook, sheet)),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 5),
            WorkbookExportPrintSurface.MacOs);

        printPlan.IsReady.Should().BeTrue();
        return PortablePdfExportPlanner.CreatePlan(printPlan);
    }

    private static int ResolveSheetIndex(Workbook workbook, Sheet sheet)
    {
        for (var index = 0; index < workbook.Sheets.Count; index++)
        {
            if (workbook.Sheets[index].Id == sheet.Id)
                return index;
        }

        throw new InvalidOperationException("Test workbook does not contain the requested sheet.");
    }
}
