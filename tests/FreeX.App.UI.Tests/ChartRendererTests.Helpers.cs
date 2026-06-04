using System.Globalization;
using System.IO;
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
        var method = typeof(ChartRenderer).GetMethod(
            "BuildPlotModel",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(ChartModel), typeof(ViewportModel)]);
        method.Should().NotBeNull();
        return method!.Invoke(null, [chart, viewport]).Should().BeOfType<PlotModel>().Subject;
    }

    private static PlotModel? BuildNullablePlotModel(ChartModel chart, ViewportModel viewport)
    {
        var method = typeof(ChartRenderer).GetMethod(
            "BuildPlotModel",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(ChartModel), typeof(ViewportModel)]);
        method.Should().NotBeNull();
        return method!.Invoke(null, [chart, viewport]) as PlotModel;
    }

    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport, WorkbookTheme theme)
    {
        var method = typeof(ChartRenderer).GetMethod(
            "BuildPlotModel",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(ChartModel), typeof(ViewportModel), typeof(WorkbookTheme)]);
        method.Should().NotBeNull();
        return method!.Invoke(null, [chart, viewport, theme]).Should().BeOfType<PlotModel>().Subject;
    }

    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, null, text, null, StyleId.Default, null);

    private static ChartDataCell ChartCell(SheetId sheetId, uint row, uint col, string text) =>
        new(sheetId, row, col, text);

    private static ChartDataCell ChartCell(SheetId sheetId, uint row, uint col, string text, ScalarValue rawValue) =>
        new(sheetId, row, col, text, rawValue);

    private static string FindWorkspaceFile(params string[] relativeParts) =>
        WorkspaceFileLocator.Find(relativeParts);

    private static void RunWithCulture(string cultureName, Action action)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
