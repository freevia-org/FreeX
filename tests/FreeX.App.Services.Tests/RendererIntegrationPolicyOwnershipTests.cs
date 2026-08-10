using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class RendererIntegrationPolicyOwnershipTests
{
    [Fact]
    public void WpfAndAvaloniaRenderersDelegateResidualIntegrationPolicies()
    {
        var wpfWindow = Read("src", "FreeX.App.Host", "MainWindow.xaml.cs");
        var wpfBackstage = Read("src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        var wpfLifecycle = Read("src", "FreeX.App.Host", "MainWindow.WorkbookLifecycle.cs");
        var wpfViewport = Read("src", "FreeX.App.Host", "MainWindow.Viewport.cs");
        var wpfCells = Read("src", "FreeX.App.Host", "MainWindow.CellsCommands.cs");
        var wpfGrid = Read("src", "FreeX.App.UI", "GridView.cs");
        var wpfPrint = Read("src", "FreeX.App.Host", "MainWindow.PrintExport.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var avaloniaViewport = Read("src", "FreeX.App.Avalonia", "MainWindow.RibbonMenuWires.cs");
        var avaloniaPrint = Read("src", "FreeX.App.Avalonia", "MainWindow.Print.cs");

        wpfWindow.Should().Contain("private readonly WorkbookReadOnlySession _workbookReadOnlySession = new();");
        avalonia.Should().Contain("private readonly WorkbookReadOnlySession _workbookReadOnlySession = new();");
        wpfBackstage.Should().Contain("_workbookReadOnlySession.PlanOpen(workbook)");
        avalonia.Should().Contain("_workbookReadOnlySession.PlanOpen(workbook)");
        wpfLifecycle.Should().Contain("_workbookReadOnlySession.ResolveExistingSaveTarget(");
        avalonia.Should().Contain("_workbookReadOnlySession.ResolveExistingSaveTarget(");
        (wpfWindow + wpfBackstage + wpfLifecycle).Should().NotContain("_isWorkbookReadOnly");
        avalonia.Should().NotContain("_isWorkbookReadOnly");

        wpfViewport.Should().Contain("WorkbookViewportScrollPlanner.PlanStructuralEditOriginShift(");
        avaloniaViewport.Should().Contain("WorkbookViewportScrollPlanner.PlanStructuralEditOriginShift(");
        wpfCells.Should().Contain("ShiftScrollOriginForRowEdit(result.TargetRange.Start.Row, result.ViewportRowDelta)");
        wpfCells.Should().Contain("ShiftScrollOriginForColEdit(result.TargetRange.Start.Col, result.ViewportColumnDelta)");
        wpfViewport.Should().NotContain("Math.Clamp((long)topRow + rowDelta");
        avaloniaViewport.Should().NotContain("Math.Clamp((long)currentTopRow + rowDelta");

        wpfGrid.Should().Contain("CellTextShrinkPlanner.ResolveFontSize(");
        avalonia.Should().Contain("CellTextShrinkPlanner.ResolveFontSize(");
        wpfGrid.Should().NotContain("while (fontSize > minimumFontSize");
        avalonia.Should().NotContain("while (fontSize > ShrinkToFitMinimumFontSize");

        avalonia.Should().Contain("SpreadsheetDisplayFormatter.FormatCellReference(address, useR1C1ReferenceStyle: false)");
        avalonia.Should().Contain("SpreadsheetDisplayFormatter.FormatRangeReference(");
        avalonia.Should().NotContain("CellAddress.NumberToColumnName(address.Col) + address.Row");

        wpfPrint.Should().Contain("PagePrintTextPlanner.ResolveWorkbookDirectoryTokenValue(_currentFilePath)");
        avaloniaPrint.Should().Contain("PagePrintTextPlanner.ResolveWorkbookDirectoryTokenValue(_session.CurrentFilePath)");
        wpfPrint.Should().NotContain("Path.GetDirectoryName(");
        avaloniaPrint.Should().NotContain("Path.GetDirectoryName(");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(RepositoryFileLocator.Find(parts));
}
