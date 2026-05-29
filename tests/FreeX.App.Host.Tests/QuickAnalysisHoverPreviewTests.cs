using FluentAssertions;
using FreeX.App.UI;
using System.IO;
using System.Reflection;

namespace FreeX.App.Host.Tests;

public sealed class QuickAnalysisHoverPreviewTests
{
    [Fact]
    public void QuickAnalysisHoverAndKeyboardFocus_SetAndClearGridPreviewRange()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("item.GotKeyboardFocus += QuickAnalysisMenuItem_GotKeyboardFocus;");
        source.Should().Contain("item.LostKeyboardFocus += QuickAnalysisMenuItem_LostKeyboardFocus;");
        source.Should().Contain("private void QuickAnalysisMenuItem_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)");
        source.Should().Contain("private void QuickAnalysisMenuItem_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)");
        source.Should().Contain("ShowQuickAnalysisPreview(sender);");
        source.Should().Contain("ClearQuickAnalysisPreview();");
        source.Should().Contain("SheetGrid.QuickAnalysisPreviewRange = preview.Range");
        source.Should().Contain("SheetGrid.QuickAnalysisPreviewRange = null");
    }

    [Fact]
    public void QuickAnalysisHoverAndKeyboardFocus_SetAndClearGridPreviewVisual()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("SheetGrid.QuickAnalysisPreviewVisual = MapQuickAnalysisPreviewVisual(preview.PreviewVisual.Kind)");
        source.Should().Contain("SheetGrid.QuickAnalysisPreviewVisual = GridQuickAnalysisPreviewVisualKind.None");
        source.Should().Contain("private static GridQuickAnalysisPreviewVisualKind MapQuickAnalysisPreviewVisual(");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.ColorScale => GridQuickAnalysisPreviewVisualKind.ColorScale");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.IconSet => GridQuickAnalysisPreviewVisualKind.IconSet");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.Highlight => GridQuickAnalysisPreviewVisualKind.Highlight");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.ClearFormat => GridQuickAnalysisPreviewVisualKind.ClearFormat");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.TotalFormula => GridQuickAnalysisPreviewVisualKind.TotalFormula");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.Table => GridQuickAnalysisPreviewVisualKind.Table");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.LineSparkline => GridQuickAnalysisPreviewVisualKind.LineSparkline");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.ColumnSparkline => GridQuickAnalysisPreviewVisualKind.ColumnSparkline");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.WinLossSparkline => GridQuickAnalysisPreviewVisualKind.WinLossSparkline");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.ColumnChart => GridQuickAnalysisPreviewVisualKind.ColumnChart");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.LineChart => GridQuickAnalysisPreviewVisualKind.LineChart");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.BarChart => GridQuickAnalysisPreviewVisualKind.BarChart");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.StackedColumnChart => GridQuickAnalysisPreviewVisualKind.StackedColumnChart");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.PieChart => GridQuickAnalysisPreviewVisualKind.PieChart");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.AreaChart => GridQuickAnalysisPreviewVisualKind.AreaChart");
        source.Should().Contain("QuickAnalysisPreviewVisualKind.ScatterChart => GridQuickAnalysisPreviewVisualKind.ScatterChart");
    }

    [Theory]
    [InlineData(QuickAnalysisPreviewVisualKind.Highlight, GridQuickAnalysisPreviewVisualKind.Highlight)]
    [InlineData(QuickAnalysisPreviewVisualKind.ClearFormat, GridQuickAnalysisPreviewVisualKind.ClearFormat)]
    [InlineData(QuickAnalysisPreviewVisualKind.TotalFormula, GridQuickAnalysisPreviewVisualKind.TotalFormula)]
    [InlineData(QuickAnalysisPreviewVisualKind.Table, GridQuickAnalysisPreviewVisualKind.Table)]
    [InlineData(QuickAnalysisPreviewVisualKind.LineSparkline, GridQuickAnalysisPreviewVisualKind.LineSparkline)]
    [InlineData(QuickAnalysisPreviewVisualKind.ColumnSparkline, GridQuickAnalysisPreviewVisualKind.ColumnSparkline)]
    [InlineData(QuickAnalysisPreviewVisualKind.WinLossSparkline, GridQuickAnalysisPreviewVisualKind.WinLossSparkline)]
    [InlineData(QuickAnalysisPreviewVisualKind.ColumnChart, GridQuickAnalysisPreviewVisualKind.ColumnChart)]
    [InlineData(QuickAnalysisPreviewVisualKind.LineChart, GridQuickAnalysisPreviewVisualKind.LineChart)]
    [InlineData(QuickAnalysisPreviewVisualKind.BarChart, GridQuickAnalysisPreviewVisualKind.BarChart)]
    [InlineData(QuickAnalysisPreviewVisualKind.StackedColumnChart, GridQuickAnalysisPreviewVisualKind.StackedColumnChart)]
    [InlineData(QuickAnalysisPreviewVisualKind.PieChart, GridQuickAnalysisPreviewVisualKind.PieChart)]
    [InlineData(QuickAnalysisPreviewVisualKind.AreaChart, GridQuickAnalysisPreviewVisualKind.AreaChart)]
    [InlineData(QuickAnalysisPreviewVisualKind.ScatterChart, GridQuickAnalysisPreviewVisualKind.ScatterChart)]
    public void MapQuickAnalysisPreviewVisual_MapsLightweightPreviewFamilies(
        QuickAnalysisPreviewVisualKind hostKind,
        GridQuickAnalysisPreviewVisualKind expectedGridKind)
    {
        MapQuickAnalysisPreviewVisual(hostKind).Should().Be(expectedGridKind);
    }

    [Fact]
    public void MapQuickAnalysisPreviewVisual_LeavesEmptyPreviewWithoutGridVisual()
    {
        MapQuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind.None).Should().Be(GridQuickAnalysisPreviewVisualKind.None);
    }

    private static GridQuickAnalysisPreviewVisualKind MapQuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind kind)
    {
        var method = typeof(MainWindow).GetMethod("MapQuickAnalysisPreviewVisual", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return ((GridQuickAnalysisPreviewVisualKind?)method!.Invoke(null, [kind])).Should().NotBeNull().And.Subject!.Value;
    }
}
