using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport)
    {
        return ChartRenderer.BuildPlotModel(chart, viewport).Should().BeOfType<PlotModel>().Subject;
    }

    private static PlotModel? BuildNullablePlotModel(ChartModel chart, ViewportModel viewport)
    {
        return ChartRenderer.BuildPlotModel(chart, viewport) as PlotModel;
    }

    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport, WorkbookTheme theme)
    {
        return ChartRenderer.BuildPlotModel(chart, viewport, theme).Should().BeOfType<PlotModel>().Subject;
    }

    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, null, text, null, StyleId.Default, null);

    private static ChartDataCell ChartCell(SheetId sheetId, uint row, uint col, string text) =>
        new(sheetId, row, col, text);

    private static ChartDataCell ChartCell(SheetId sheetId, uint row, uint col, string text, ScalarValue rawValue) =>
        new(sheetId, row, col, text, rawValue);

    private static void RunWithCulture(string cultureName, Action action)
    {
        using var cultureScope = TestCultureScope.CurrentCultureAndUICulture(cultureName);
        action();
    }
}
