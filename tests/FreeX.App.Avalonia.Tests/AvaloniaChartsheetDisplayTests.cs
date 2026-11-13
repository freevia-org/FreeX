using System.Reflection;
using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless proof that an Avalonia chartsheet shows its chart full-window instead of the empty cell
/// grid (WPF parity: <c>MainWindow.Chartsheet.cs</c>), keeps sheet tabs, selects the chart for the
/// Chart Design/Format contextual tabs, and switches back to a normal grid without crashing.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaChartsheetDisplayTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ActivatingChartsheet_ShowsChartInsteadOfGrid_AndSwitchingBackRestoresGrid()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var data = window.Session.Workbook.AddSheet("Data");
                window.Session.SelectSheet(data.Id);
                for (uint row = 1; row <= 4; row++)
                {
                    data.SetCell(new CellAddress(data.Id, row, 1), new TextValue("Item" + row));
                    data.SetCell(new CellAddress(data.Id, row, 2), new NumberValue(row * 10));
                }

                var embedded = ChartCommandWorkflowPlanner.BuildEmbeddedChartCommand(
                    data,
                    new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 4, 2)),
                    ChartType.Column);
                window.Session.ExecuteReviewCommand(embedded).Success.Should().BeTrue();
                var chartId = data.Charts.Should().ContainSingle().Subject.Id;

                // Move Chart > New sheet creates a real chartsheet (SheetKind.Chartsheet).
                var move = new MoveChartToNewSheetCommand(data.Id, chartId, "Sales Chart");
                window.Session.ExecuteReviewCommand(move).Success.Should().BeTrue();
                var chartsheetId = window.Session.Workbook.Sheets.Single(sheet => sheet.Name == "Sales Chart").Id;

                window.Session.SelectSheet(chartsheetId);
                Invoke(window, "RefreshShell", "test");

                window.Session.ActiveSheet.IsChartsheet.Should().BeTrue();
                window.IsChartsheetViewShowing.Should().BeTrue("a chartsheet replaces the cell grid with the chart view");

                var host = FindChartsheetHost(window);
                host.Should().NotBeNull();
                host!.Children.Should().NotBeEmpty("the existing chart renderer must paint the chart into the view");
                var chart = window.Session.ActiveSheet.ChartsheetChart!;
                window.Session.ActiveSheet.Charts.Should().ContainSingle();
                GetField<Guid?>(window, "_selectedDrawingObjectId").Should().Be(chart.Id,
                    "the chartsheet chart is implicitly selected so Chart Design/Format apply");
                GetField<SelectionPaneObjectKind?>(window, "_selectedDrawingObjectKind")
                    .Should().Be(SelectionPaneObjectKind.Chart);

                // Back to the data worksheet: the grid returns and the chart view is gone.
                window.Session.SelectSheet(data.Id);
                Invoke(window, "RefreshShell", "test");
                window.IsChartsheetViewShowing.Should().BeFalse();
                FindChartsheetHost(window).Should().BeNull();

                // Move Chart back to the worksheet: the (now empty) chartsheet still shows without crashing.
                window.Session.ExecuteReviewCommand(new MoveChartCommand(chartsheetId, chart.Id, data.Id)).Success.Should().BeTrue();
                window.Session.SelectSheet(chartsheetId);
                Invoke(window, "RefreshShell", "test");
                window.IsChartsheetViewShowing.Should().BeTrue();
                window.Session.ExecuteReviewCommand(new MoveChartCommand(data.Id, chart.Id, chartsheetId)).Success.Should().BeTrue();

                // And again, to prove repeated switching is stable.
                window.Session.SelectSheet(chartsheetId);
                Invoke(window, "RefreshShell", "test");
                window.IsChartsheetViewShowing.Should().BeTrue();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Grid? FindChartsheetHost(MainWindow window) =>
        window.GetField<ContentControl>("_sheetGridHost").Content as Grid is { } grid &&
        grid.GetType().Name == "ChartsheetViewHost"
            ? grid
            : null;

    private static void Invoke(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    private static T GetField<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
}

internal static class AvaloniaChartsheetDisplayTestExtensions
{
    public static T GetField<T>(this MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
}
