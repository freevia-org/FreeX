using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowSourceHygieneTests
{
    [Fact]
    public void MainWindow_MergesVisualRefreshResourceDictionaries()
    {
        var mainWindowPath = WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml");
        var appHostDirectory = Directory.GetParent(mainWindowPath)!.FullName;
        var xaml = File.ReadAllText(mainWindowPath);

        File.Exists(Path.Combine(appHostDirectory, "Resources", "ThemeResources.xaml")).Should().BeTrue();
        File.Exists(Path.Combine(appHostDirectory, "Resources", "IconResources.xaml")).Should().BeTrue();
        xaml.Should().Contain("Source=\"Resources/ThemeResources.xaml\"");
        xaml.Should().Contain("Source=\"Resources/IconResources.xaml\"");
    }

    [Fact]
    public void QuickAccessToolbar_UsesConsistentIconFontGlyphs()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        xaml.Should().Contain("x:Name=\"SaveQatBtn\"");
        xaml.Should().Contain("FreexcelQatOnAccentIcon");
        xaml.Should().NotContain("Content=\"💾\"");
        xaml.Should().NotContain("Content=\"↩\"");
        xaml.Should().NotContain("Content=\"↪\"");
    }

    [Fact]
    public void MainWindow_UsesVisibleFreexcelBrandingAndWindowIcon()
    {
        var mainWindowPath = WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml");
        var projectPath = WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "Freexcel.App.Host.csproj");
        var appHostDirectory = Directory.GetParent(mainWindowPath)!.FullName;
        var theme = File.ReadAllText(Path.Combine(appHostDirectory, "Resources", "ThemeResources.xaml"));
        var xaml = File.ReadAllText(mainWindowPath);
        var project = File.ReadAllText(projectPath);

        File.Exists(Path.Combine(appHostDirectory, "Resources", "Freexcel.ico")).Should().BeTrue();
        xaml.Should().Contain("Icon=\"Resources/Freexcel.ico\"");
        xaml.Should().Contain("x:Name=\"TitleBarAppIcon\"");
        xaml.Should().Contain("<TextBlock Text=\"FREE\"");
        xaml.Should().Contain("<TextBlock Text=\"X\"");
        xaml.Should().NotContain("<Image Source=\"Resources/Freexcel.ico\"");
        xaml.Should().NotContain("<TextBlock Text=\"F\" Foreground=\"{StaticResource FreexcelGreenBrush}\"");
        theme.Should().Contain("x:Key=\"FreexcelTitleBarBrush\"");
        xaml.Should().Contain("Background=\"{StaticResource FreexcelTitleBarBrush}\"");
        project.Should().Contain("<ApplicationIcon>Resources\\Freexcel.ico</ApplicationIcon>");
    }

    [Fact]
    public void MainWindow_DoesNotKeepLegacyZoomConversionHelpers()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().NotContain("SliderToZoomPct(");
        source.Should().NotContain("ZoomPctToSlider(");
    }

    [Fact]
    public void PersistentFormatPainter_UsesPreviewMouseDownSoButtonDoubleClickCannotBeOverwrittenByClick()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        source.Should().Contain("private bool _formatPainterPersistent;");
        source.Should().Contain("FormatPainterBtn_PreviewMouseLeftButtonDown");
        source.Should().Contain("if (e.ClickCount != 2) return;");
        source.Should().Contain("CaptureFormatPainterSource(persistent: true);");
        source.Should().Contain("e.Handled = true;");
        source.Should().Contain("CancelFormatPainter");
        xaml.Should().Contain("PreviewMouseLeftButtonDown=\"FormatPainterBtn_PreviewMouseLeftButtonDown\"");
        xaml.Should().NotContain("MouseDoubleClick=\"FormatPainterBtn_MouseDoubleClick\"");
    }

    [Fact]
    public void FormatPainterApplication_UsesTargetSelectionRangeWhenAvailable()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("private SheetId? _formatPainterSourceSheetId;");
        source.Should().Contain("private GridRange? _formatPainterSourceRange;");
        source.Should().Contain("private bool _formatPainterTargetSelectionActive;");
        source.Should().Contain("TryApplyFormatPainter(GridRange targetRange)");
        source.Should().Contain("_formatPainterSourceRange = range;");
        source.Should().Contain("var targetSheetIds = CurrentGroupedEditSheetIds();");
        source.Should().Contain("FormatPainterCommandFactory.Create(_workbook, sourceSheet, sourceRange, targetRange)");
        source.Should().Contain("new CompositeWorkbookCommand(\"Format Painter\", targetSheetIds.Select(CreateCommand).ToList())");
        source.Should().Contain("SheetGrid.SelectedRange is { } selectedRange");
        source.Should().Contain("selectedRange.Contains(newAddr)");
        source.Should().Contain("TryApplyFormatPainter(selectedRange)");
        source.Should().NotContain("var targetRange = new GridRange(addr, addr);");
    }

    [Fact]
    public void AutoFitMenuHandlers_UsePlannerAndPerTargetExplicitSizes()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
        var planner = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AutoFitPlanner.cs"));

        source.Should().Contain("AutoFitPlanner.PlanRowHeights");
        source.Should().Contain("AutoFitPlanner.PlanColumnWidths");
        source.Should().Contain("new SetRowHeightCommand(sheetId, plans[0].Index, plans[0].Index, plans[0].Size)");
        source.Should().Contain("new SetColumnWidthCommand(sheetId, plans[0].Index, plans[0].Index, plans[0].Size)");
        source.Should().Contain("new SetRowHeightCommand(sheetId, plan.Index, plan.Index, plan.Size)");
        source.Should().Contain("new SetColumnWidthCommand(sheetId, plan.Index, plan.Index, plan.Size)");
        source.Should().NotContain("return new SetRowHeightCommand(sheetId, range.Start.Row, range.End.Row, height)");
        source.Should().NotContain("return new SetColumnWidthCommand(sheetId, range.Start.Col, range.End.Col, width)");
        planner.Should().Contain("AutoFitSizingService.EstimateRowHeight");
        planner.Should().Contain("AutoFitSizingService.EstimateColumnWidth");
        source.Should().NotContain("new SetRowHeightCommand(sheetId, range.Start.Row, range.End.Row, height: null)");
        source.Should().NotContain("new SetColumnWidthCommand(sheetId, range.Start.Col, range.End.Col, width: null)");
    }

    [Fact]
    public void AdvancedChartFamilies_ArePresentedAsDeferredInsteadOfAuthored()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        source.Should().Contain("ShowDeferredChartFamilyMessage");
        source.Should().Contain("retained when opening XLSX files");
        source.Should().NotContain("InsertChartOfType(ChartType.Surface)");
        source.Should().NotContain("InsertChartOfType(ChartType.Treemap)");
        source.Should().NotContain("InsertChartOfType(ChartType.Sunburst)");
        source.Should().NotContain("InsertChartOfType(ChartType.Histogram)");
        source.Should().NotContain("InsertChartOfType(ChartType.Pareto)");
        source.Should().NotContain("InsertChartOfType(ChartType.BoxAndWhisker)");
        source.Should().NotContain("InsertChartOfType(ChartType.Waterfall)");
        source.Should().NotContain("InsertChartOfType(ChartType.Funnel)");
        source.Should().NotContain("InsertChartOfType(ChartType.Map)");
        source.Should().NotContain("InsertChartOfType(ChartType.ThreeDColumn)");
        xaml.Should().Contain("Click=\"DeferredChartFamilyMenuItem_Click\"");
        xaml.Should().Contain("Surface");
        xaml.Should().Contain("Treemap");
        xaml.Should().Contain("Sunburst");
        xaml.Should().Contain("Histogram");
        xaml.Should().Contain("Pareto");
        xaml.Should().Contain("Box Plot");
        xaml.Should().Contain("Waterfall");
        xaml.Should().Contain("Funnel");
        xaml.Should().Contain("Map");
        xaml.Should().Contain("3D Column");
    }
}
