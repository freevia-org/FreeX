using System.Reflection;
using System.Threading;

using Avalonia.Headless;
using FluentAssertions;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Avalonia.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Functional proof for the legacy chart-axis command family. The commands are invoked through the
/// same contextual-tab dictionary used by the Avalonia shell, then inspected on the live ChartModel.
/// This prevents a dialog/source marker from counting as parity when the command does not persist or undo.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaChartAxisRuntimeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task LegacyAxisCommands_MutateAndUndoTheSelectedChart()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("AxisFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Values"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

            var chart = new ChartModel
            {
                Type = ChartType.Column,
                FirstColIsCategories = true,
                XAxisLabelFontSize = 14,
                XAxisGridlineThickness = 3,
                DataRange = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 3, 2)),
            };
            sheet.Charts.Add(chart);

            InvokePrivate(window, "SelectChart", chart);
            var commands = (IReadOnlyDictionary<string, Action>)InvokePrivate(
                window, "BuildContextualTabCommands")!;

            var expected = new[]
            {
                "X Axis Ticks", "Y Axis Ticks", "X Axis Label Font", "Y Axis Label Font",
                "X Axis Label Angle", "Y Axis Label Angle", "X Axis Line", "Y Axis Line",
                "X Axis Number Format", "Y Axis Number Format", "X Gridline Style", "Y Gridline Style",
                "X Log Scale", "Y Log Scale",
            };
            commands.Keys.Should().Contain(expected);

            var axisGroup = AvaloniaRibbonComposition.BuildDefinition().Tabs
                .Single(tab => tab.Id == "ChartFormatTab")
                .Groups.Single(group => group.Id == "ChartFormatLegacyAxesGroup");
            axisGroup.Controls.Select(control => control.Label).Should().Contain(expected);

            var originalFontSize = chart.XAxisLabelFontSize;
            commands["X Axis Label Font"]();
            chart.XAxisLabelFontSize.Should().NotBe(originalFontSize);
            window.Session.CanUndo.Should().BeTrue();
            window.Session.UndoLastEdit().Success.Should().BeTrue();
            chart.XAxisLabelFontSize.Should().Be(originalFontSize);

            ChartAxisPlanner.PlanQuickCommand(chart, useXAxis: true, ChartAxisQuickCommand.LabelAngle)
                .XAxisLabelAngle.Should().Be(-45);
            commands["X Axis Label Angle"]();
            chart.XAxisLabelAngle.Should().Be(-45);
            commands["X Axis Line"]();
            chart.XAxisLineThickness.Should().Be(1.5);
            commands["X Axis Number Format"]();
            chart.XAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.Number);
            commands["X Gridline Style"]();
            chart.XAxisGridlineThickness.Should().Be(1);
            commands["Y Log Scale"]();
            chart.YAxisLogScale.Should().BeTrue();
            window.Session.CanUndo.Should().BeTrue();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    private static object? InvokePrivate(MainWindow window, string methodName, params object[] args) =>
        typeof(MainWindow)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(window, args);
}
