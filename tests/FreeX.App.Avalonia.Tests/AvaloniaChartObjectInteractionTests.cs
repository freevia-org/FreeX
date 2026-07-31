using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;

using FluentAssertions;

using FreeX.App.Presentation.DrawingInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Focused headless proof for the Avalonia chart object interaction surface: visible resize handles
/// and the single undoable SetChartBoundsCommand used by move and resize release.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaChartObjectInteractionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ChartSelectionAdorner_RendersEightResizeHandlesAtCornersAndEdges()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var adorner = InvokePrivate(window, "CreateChartSelectionAdorner", 200d, 100d)
                    .Should().BeOfType<Canvas>().Subject;

                adorner.Children.Should().HaveCount(9, "the border plus eight chart resize handles must be rendered");
                var expectedCenters = new[]
                {
                    (0d, 0d), (100d, 0d), (200d, 0d),
                    (0d, 50d), (200d, 50d),
                    (0d, 100d), (100d, 100d), (200d, 100d),
                };

                for (var index = 0; index < expectedCenters.Length; index++)
                {
                    var handle = adorner.Children[index + 1].Should().BeOfType<Border>().Subject;
                    handle.Width.Should().Be(9);
                    handle.Height.Should().Be(9);
                    Canvas.GetLeft(handle).Should().BeApproximately(expectedCenters[index].Item1 - 4.5, 0.001);
                    Canvas.GetTop(handle).Should().BeApproximately(expectedCenters[index].Item2 - 4.5, 0.001);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ChartDragCommit_UpdatesBoundsAndUndoRestoresPreviousBounds()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                sheet.ShowHeadings = false;
                var chart = new ChartModel
                {
                    Type = ChartType.Column,
                    DataRange = new GridRange(
                        new CellAddress(sheet.Id, 1, 1),
                        new CellAddress(sheet.Id, 4, 2)),
                    Left = 20,
                    Top = 30,
                    Width = 400,
                    Height = 300,
                };
                sheet.Charts.Add(chart);

                var container = new Grid { Width = 480, Height = 270 };
                Canvas.SetLeft(container, 72);
                Canvas.SetTop(container, 96);
                var session = CreateChartDragSession(chart, container, ObjectDragKind.ResizeSE);

                InvokePrivate(window, "CommitChartDrag", session, container);

                chart.Left.Should().Be(72);
                chart.Top.Should().Be(96);
                chart.Width.Should().Be(480);
                chart.Height.Should().Be(270);
                window.Session.CanUndo.Should().BeTrue("a chart drag release must execute one undoable bounds command");

                window.Session.UndoLastEdit().Success.Should().BeTrue();
                chart.Left.Should().Be(20);
                chart.Top.Should().Be(30);
                chart.Width.Should().Be(400);
                chart.Height.Should().Be(300);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static object CreateChartDragSession(
        ChartModel chart,
        Control container,
        ObjectDragKind kind)
    {
        var type = typeof(MainWindow).GetNestedType("ChartDragSession", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ChartDragSession was not found");
        var session = RuntimeHelpers.GetUninitializedObject(type);

        SetProperty(session, "Chart", chart);
        SetProperty(session, "Container", container);
        SetProperty(session, "Kind", kind);
        SetProperty(session, "StartCanvasRect", new LayoutRect(20, 30, 400, 300));
        SetProperty(session, "StartPointerInCanvas", new global::Avalonia.Point(0, 0));
        return session;
    }

    private static void SetProperty(object target, string name, object value) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.SetValue(target, value);

    private static object? InvokePrivate(MainWindow window, string methodName, params object[] args) =>
        typeof(MainWindow)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(window, args);
}
