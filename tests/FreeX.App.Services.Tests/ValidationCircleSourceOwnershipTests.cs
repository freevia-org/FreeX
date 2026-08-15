using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class ValidationCircleSourceOwnershipTests
{
    [Fact]
    public void BothHostsProjectSharedPerSheetWorkflow()
    {
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.DataTools.cs");
        var wpf = Read("src", "FreeX.App.Host", "MainWindow.DataCommands.cs");
        var wpfViewport = Read("src", "FreeX.App.Host", "MainWindow.Viewport.cs");

        avalonia.Should().Contain("WorkbookValidationCircleWorkflow.CircleInvalidData(");
        avalonia.Should().Contain("WorkbookValidationCircleWorkflow.Clear(_session.ActiveSheet)");
        avalonia.Should().Contain("WorkbookValidationCircleWorkflow.Prune(_session.Workbook, _session.ActiveSheet)");
        avalonia.Should().Contain("_session.ActiveSheet.ValidationCircleCells");
        avalonia.Should().NotContain("_validationCircleCells");
        avalonia.Should().NotContain("DataValidationCirclePlanner.FindInvalidDataCells");

        wpf.Should().Contain("WorkbookValidationCircleWorkflow.CircleInvalidData(_workbook, sheet)");
        wpf.Should().Contain("WorkbookValidationCircleWorkflow.Clear(sheet)");
        wpf.Should().Contain("WorkbookValidationCircleWorkflow.Prune(_workbook, sheet)");
        wpf.Should().NotContain("DataValidationCirclePlanner.FindInvalidDataCells");
        wpfViewport.Should().Contain("SheetGrid.ValidationCircleCells = sheet?.ValidationCircleCells;");
    }

    [Fact]
    public void InteractivePrintPreviewAndPdfRenderersShareCircleGeometry()
    {
        Read("src", "FreeX.App.UI", "GridView.Overlays.cs")
            .Should().Contain("ValidationCircleLayoutPlanner.CalculateEllipseBounds(");
        Read("src", "FreeX.App.Host", "PrintRenderer.GridCells.cs")
            .Should().Contain("ValidationCircleLayoutPlanner.CalculateEllipseBounds(");
        Read("src", "FreeX.App.Avalonia", "MainWindow.DataTools.cs")
            .Should().Contain("ValidationCircleLayoutPlanner.CalculateEllipseBounds(");
        Read("src", "FreeX.App.Presentation", "PageLayout", "PrintPreviewInstructionBuilder.cs")
            .Should().Contain("ValidationCircleLayoutPlanner.CalculateEllipseBounds(cell.Bounds)");
        Read("src", "FreeX.App.Services", "WorkbookPdfContentBuilder.cs")
            .Should().Contain("ValidationCircleLayoutPlanner.CalculateEllipseBounds(cellBounds)");
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(RepositoryFileLocator.Find(path));
}
