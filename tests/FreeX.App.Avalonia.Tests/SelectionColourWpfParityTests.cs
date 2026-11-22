using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// WPF draws floating-object selection chrome (outline, resize handles, rotation grip, selected
/// chart-element outline) in #207AC5 blue; only the cell-range selection is Excel green. Gridlines are
/// a single 1-DIP line per shared cell edge in RGB(220,220,220) (GridView.GridLineBrush / MakePen(...,1)).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class SelectionColourWpfParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static readonly Color WpfObjectBlue = Color.FromRgb(0x20, 0x7A, 0xC5);

    [Fact]
    public async Task ChartSelectionAdorner_UsesWpfBlueAndOneAndAHalfDipBorder()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var adorner = Invoke(window, "CreateChartSelectionAdorner", 200d, 100d).Should().BeOfType<Canvas>().Subject;
                var border = adorner.Children[0].Should().BeOfType<Border>().Subject;
                ColourOf(border.BorderBrush).Should().Be(WpfObjectBlue);
                border.BorderThickness.Left.Should().Be(1.5);
                foreach (var handle in adorner.Children.Skip(1).Cast<Border>())
                {
                    ColourOf(handle.BorderBrush).Should().Be(WpfObjectBlue);
                    ColourOf(handle.Background).Should().Be(Colors.White);
                }
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DrawingObjectSelectionAdorner_UsesWpfBlueForOutlineHandlesAndRotationGrip()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var adorner = Invoke(window, "CreateDrawingObjectSelectionAdorner", 120d, 80d, 0d).Should().BeOfType<Canvas>().Subject;
                ((Border)adorner.Children[0]).BorderBrush.Should().NotBeNull();
                ColourOf(((Border)adorner.Children[0]).BorderBrush).Should().Be(WpfObjectBlue);
                ColourOf(((Border)adorner.Children[1]).Background).Should().Be(WpfObjectBlue);
                ColourOf(((global::Avalonia.Controls.Shapes.Ellipse)adorner.Children[2]).Stroke).Should().Be(WpfObjectBlue);
                foreach (var handle in adorner.Children.Skip(3).Cast<Border>())
                    ColourOf(handle.BorderBrush).Should().Be(WpfObjectBlue);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void CellRangeSelection_StaysExcelGreen_AndGridLineMatchesWpf220()
    {
        ColourOfStatic("SelectionBorder").Should().Be(Color.FromRgb(33, 115, 70));
        ColourOfStatic("GridLine").Should().Be(Color.FromRgb(220, 220, 220));
    }

    [Fact]
    public async Task CellBorder_PaintsOnlyRightAndBottomEdgeSoSharedGridlineIsOneDip()
    {
        await Session.Dispatch(() =>
        {
            var method = typeof(MainWindow).GetMethod("CreateCellBorder", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var zoom in new[] { 1d, 2d })
            {
                var args = method.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
                var names = method.GetParameters().Select(p => p.Name).ToList();
                args[names.IndexOf("text")] = "x";
                args[names.IndexOf("foreground")] = Brushes.Black;
                args[names.IndexOf("textAlignment")] = global::Avalonia.Media.TextAlignment.Left;
                args[names.IndexOf("verticalAlignment")] = global::Avalonia.Layout.VerticalAlignment.Center;
                args[names.IndexOf("textWrapping")] = TextWrapping.NoWrap;
                args[names.IndexOf("fontWeight")] = FontWeight.Normal;
                args[names.IndexOf("fontStyle")] = FontStyle.Normal;
                args[names.IndexOf("fontSize")] = 11d;
                args[names.IndexOf("selected")] = false;
                args[names.IndexOf("zoomFactor")] = zoom;
                var border = (Border)method.Invoke(null, args)!;
                border.BorderThickness.Should().Be(new global::Avalonia.Thickness(0, 0, zoom, zoom));
                border.Padding.Should().Be(new global::Avalonia.Thickness(zoom, zoom, 0, 0),
                    "the content slot keeps the previous all-sides inset");
                ColourOf(border.BorderBrush).Should().Be(Color.FromRgb(220, 220, 220));
            }
        }, CancellationToken.None);
    }

    private static object? Invoke(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    private static Color ColourOf(IBrush? brush) => ((ISolidColorBrush)brush!).Color;

    private static Color ColourOfStatic(string field) =>
        ColourOf((IBrush)typeof(MainWindow).GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!);
}
