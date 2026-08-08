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
    public void R128_RequiresFeatureLossConfirmation_SingleSheetWorkbookWithPicture_SavedAsCsv_ReturnsTrue()
    {
        // r128-services-lossy-format-drawing-1: a single-sheet workbook whose only "rich" content is one
        // inserted picture (no chart) used to return false here -- DelimitedTextWorkbookWriter only ever
        // enumerates cell values and never looks at Sheet.Pictures, so the picture was silently and
        // completely discarded with zero Save-As warning, the same class of loss the Charts.Count check
        // exists specifically to catch.
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1").Pictures.Add(new PictureModel());

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".csv")
            .Should().BeTrue("a picture has no CSV representation and would be silently dropped");
    }

    [Fact]
    public void R128_RequiresFeatureLossConfirmation_SingleSheetWorkbookWithDrawingShape_SavedAsCsv_ReturnsTrue()
    {
        // r128-services-lossy-format-drawing-1: same gap for autoshapes/textboxes -- Sheet.DrawingShapes
        // was never consulted either.
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1").DrawingShapes.Add(new DrawingShapeModel());

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".csv")
            .Should().BeTrue("a drawing shape has no CSV representation and would be silently dropped");
    }

    [Fact]
    public void R128_RequiresFeatureLossConfirmation_SingleSheetWorkbookWithTextBox_SavedAsCsv_ReturnsTrue()
    {
        // r128-services-lossy-format-drawing-1: identical bug pattern for text boxes, the third sibling
        // drawing-object collection alongside DrawingShapes/Pictures that DelimitedTextWorkbookWriter
        // (and the SLK/DIF/DBF/PRN writers) never look at.
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1").TextBoxes.Add(new TextBoxModel());

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".csv")
            .Should().BeTrue("a text box has no CSV representation and would be silently dropped");
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".prn")]
    [InlineData(".slk")]
    [InlineData(".dif")]
    [InlineData(".dbf")]
    public void R128_RequiresFeatureLossConfirmation_SingleSheetWorkbookWithPicture_AppliesToEveryLossyPlainTextExtension(string extension)
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1").Pictures.Add(new PictureModel());

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, extension)
            .Should().BeTrue($"{extension} cannot hold a picture any more than CSV can");
    }

    [Fact]
    public void R128_RequiresFeatureLossConfirmation_SingleSheetPlainWorkbook_SavedAsCsv_StillReturnsFalse()
    {
        // No-regression sibling: a genuinely plain single-sheet workbook (no chart, no picture, no shape,
        // no text box) must still return false -- this planner must not become over-eager and gate every
        // save. Distinct from RequiresFeatureLossConfirmation_SingleSheetPlainWorkbook_SavedAsCsv_ReturnsFalse
        // above in exercising the same collections the fix touches, each left empty.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Charts.Should().BeEmpty();
        sheet.DrawingShapes.Should().BeEmpty();
        sheet.Pictures.Should().BeEmpty();
        sheet.TextBoxes.Should().BeEmpty();

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".csv")
            .Should().BeFalse("a single-sheet workbook with no chart, shape, picture, or text box loses nothing by moving to CSV");
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
