using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for R69-services-file-open-save-6-3 (src/FreeX.App.Host/MainWindow.Backstage.cs,
/// SaveWorkbookToTargetAsync): Save As to a lossy plain/single-sheet format (CSV/TXT/PRN/SLK/DIF/DBF) used
/// to write silently with no feature-loss warning -- the ConfirmUnsupportedXlsxFeatureSave gate only ever
/// applied to ".xlsx". <see cref="LossyFormatFeatureLossPlanner"/> is the pure decision this gate now
/// consults before writing.
/// </summary>
public sealed class LossyFormatFeatureLossPlannerTests
{
    [Fact]
    public void RequiresFeatureLossConfirmation_MultiSheetWorkbookWithChart_SavedAsCsv_ReturnsTrue()
    {
        var workbook = new Workbook("Book1");
        var sheet1 = workbook.AddSheet("Sheet1");
        sheet1.Charts.Add(new ChartModel());
        workbook.AddSheet("Sheet2");

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".csv")
            .Should().BeTrue("a multi-sheet workbook with a chart loses both the extra sheet and the chart when saved as CSV");
    }

    [Fact]
    public void RequiresFeatureLossConfirmation_MultiSheetWorkbookNoChart_SavedAsCsv_ReturnsTrue()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".csv")
            .Should().BeTrue("CSV can only hold one worksheet, so a second sheet is always lost");
    }

    [Fact]
    public void RequiresFeatureLossConfirmation_SingleSheetWorkbookWithChart_SavedAsCsv_ReturnsTrue()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1").Charts.Add(new ChartModel());

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".csv")
            .Should().BeTrue("a chart has no CSV representation and would be silently dropped");
    }

    [Fact]
    public void RequiresFeatureLossConfirmation_SingleSheetPlainWorkbook_SavedAsCsv_ReturnsFalse()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".csv")
            .Should().BeFalse("a single-sheet workbook with no charts loses nothing by moving to CSV");
    }

    [Theory]
    [InlineData(".csv")]
    [InlineData(".txt")]
    [InlineData(".prn")]
    [InlineData(".slk")]
    [InlineData(".dif")]
    [InlineData(".dbf")]
    public void RequiresFeatureLossConfirmation_MultiSheetWorkbook_AppliesToEveryLossyPlainTextExtension(string extension)
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, extension)
            .Should().BeTrue($"{extension} is a single-sheet/plain-text format that cannot hold a second sheet");
    }

    [Fact]
    public void RequiresFeatureLossConfirmation_OdsWithVbaProject_ReturnsTrue()
    {
        // R83-services-doc-recovery-props-5-3: OdsFileAdapter has no VBA-project handling at all, and
        // neither the plain-text set here nor the .xlsx-only ConfirmUnsupportedXlsxFeatureSave gate in
        // MainWindow.Backstage.cs covered .ods, so a Save-As to .ods silently discarded the entire VBA
        // project with no confirmation whatsoever.
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        workbook.HasVbaProjectPackage = true;

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".ods")
            .Should().BeTrue("OdsFileAdapter has no VBA-project support, so saving as .ods would silently drop the macros");
    }

    [Fact]
    public void RequiresFeatureLossConfirmation_OdsWithoutVbaProject_ReturnsFalse()
    {
        // No-regression sibling: a workbook with no VBA project (even a multi-sheet one with charts,
        // which ODF can represent) must not be gated -- this planner's .ods rule is scoped to the
        // VBA-project loss, not to content ODF is capable of holding.
        var workbook = new Workbook("Book1");
        var sheet1 = workbook.AddSheet("Sheet1");
        sheet1.Charts.Add(new ChartModel());
        workbook.AddSheet("Sheet2");

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".ods")
            .Should().BeFalse("a workbook without a VBA project has nothing this planner's .ods rule needs to warn about");
    }

    [Fact]
    public void RequiresFeatureLossConfirmation_XlsxExtension_NeverAppliesHere_UsesItsOwnExistingGate()
    {
        var workbook = new Workbook("Book1");
        var sheet1 = workbook.AddSheet("Sheet1");
        sheet1.Charts.Add(new ChartModel());
        workbook.AddSheet("Sheet2");

        // .xlsx already has its own dedicated ConfirmUnsupportedXlsxFeatureSave gate in
        // MainWindow.Backstage.cs; this planner must not double up on it.
        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".xlsx")
            .Should().BeFalse("xlsx is not one of the plain/single-sheet lossy formats this planner covers");
    }
}
