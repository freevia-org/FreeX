using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowPivotHeaderDropdownSourceTests
{
    [Fact]
    public void MainWindow_WiresRenderedPivotHeaderDropdownsToPivotFieldMenu()
    {
        var constructorSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");
        var handlerSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotHeaderDropdowns.cs");
        var menuSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotChartCommands.cs");
        var pivotSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotCommands.cs");

        constructorSource.Should().Contain("SheetGrid.PivotHeaderDropdownRequested += OnPivotHeaderDropdownRequested;");
        viewportSource.Should().Contain("PivotHeaderDropdownPlanner.BuildTargets(_workbook, sheet)");
        viewportSource.Should().Contain("SheetGrid.PivotHeaderDropdowns = pivotHeaderDropdownTargets");
        handlerSource.Should().Contain("_pivotFieldMenuContextCaption = target.FieldCaption;");
        handlerSource.Should().Contain("SetActiveCell(headerCell);");
        handlerSource.Should().Contain("CreatePivotFieldContextMenu();");
        pivotSource.Should().Contain("return _pivotFieldMenuContextCaption;");
        menuSource.Should().Contain("More Sort Options...");
        menuSource.Should().Contain("Custom sort lists and manual PivotTable ordering are not yet supported.");
    }
}
