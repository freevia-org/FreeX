using System.Reflection;
using System.Threading;
using Avalonia.Headless;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless runtime proof that the Chart Design/Combo Chart ribbon route performs the same immediate
/// shared mutation as WPF, rather than opening the Avalonia-only dialog and falling into its unsupported
/// status guard.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaComboChartRuntimeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ComboChartRibbonCommand_TogglesTheSelectedChartThroughSharedCommandPath()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ComboToggleFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Series A"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Series B"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(20));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(25));

            var chart = new ChartModel
            {
                Type = ChartType.Column,
                FirstColIsCategories = true,
                ComboLineSeriesIndexes = [1],
                DataRange = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 6, 3)),
            };
            sheet.Charts.Add(chart);

            InvokePrivate(window, "SelectChart", chart);
            var commands = (IReadOnlyDictionary<string, Action>)InvokePrivate(
                window, "BuildContextualTabCommands")!;

            chart.UseComboLineForSecondarySeries.Should().BeFalse();
            commands["Combo Chart"]();

            chart.UseComboLineForSecondarySeries.Should().BeTrue();
            window.Session.CanUndo.Should().BeTrue();

            commands["Combo Chart"]();
            chart.UseComboLineForSecondarySeries.Should().BeFalse();

            // WPF can also turn off a loaded combo chart whose source now exposes only one data
            // series. The Avalonia dialog's SupportsCombo gate rejects that state, but WPF's
            // immediate ComboToggle intentionally remains available while the overlay is active.
            var oneSeriesChart = new ChartModel
            {
                Type = ChartType.Column,
                FirstColIsCategories = true,
                UseComboLineForSecondarySeries = true,
                ComboLineSeriesIndexes = [1],
                DataRange = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 6, 2)),
            };
            sheet.Charts.Add(oneSeriesChart);
            InvokePrivate(window, "SelectChart", oneSeriesChart);
            commands["Combo Chart"]();
            oneSeriesChart.UseComboLineForSecondarySeries.Should().BeFalse();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    private static object? InvokePrivate(MainWindow window, string methodName, params object[] args) =>
        typeof(MainWindow)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(window, args);
}
