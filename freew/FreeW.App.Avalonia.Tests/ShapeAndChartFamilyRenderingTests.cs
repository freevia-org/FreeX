using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using AvaloniaDrawing = global::Avalonia.Media.Drawing;
using AvaloniaDrawingGroup = global::Avalonia.Media.DrawingGroup;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// The Avalonia shell shares the preset-shape outlines and the chart scenes with WPF: every gallery
/// shape and every new chart family must lay out and paint here without throwing.
/// </summary>
public sealed class ShapeAndChartFamilyRenderingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task Dispatch(Action action) => Session.Dispatch(action, CancellationToken.None);

    private static int PaintedGeometryCount(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 1100));
        view.Arrange(new Rect(0, 0, 816, 1100));
        view.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        var drawingGroup = new AvaloniaDrawingGroup();
        using (var context = drawingGroup.Open())
            view.Render(context);
        return Flatten(drawingGroup).OfType<GeometryDrawing>().Count();
    }

    private static IEnumerable<AvaloniaDrawing> Flatten(AvaloniaDrawingGroup group)
    {
        foreach (var child in group.Children)
        {
            if (child is AvaloniaDrawingGroup nested)
            {
                foreach (var inner in Flatten(nested))
                    yield return inner;
            }
            else
            {
                yield return child;
            }
        }
    }

    private static TextDocument DocumentWith(Run run)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(run);
        document.Blocks.Add(paragraph);
        return document;
    }

    [Fact]
    public async Task Every_gallery_shape_paints_floating_and_inline()
    {
        await Dispatch(() =>
        {
            foreach (var item in PresetShapeCatalog.Items)
            {
                var inline = PresetShapeCatalog.CreateShape(item);
                var paintInline = () => PaintedGeometryCount(DocumentWith(Run.FromShape(inline)));
                paintInline.Should().NotThrow($"inline '{item.Id}'");

                var floating = PresetShapeCatalog.CreateShape(item);
                floating.Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square, HorizontalOffsetPt = 20, VerticalOffsetPt = 20 };
                var paintFloating = () => PaintedGeometryCount(DocumentWith(Run.FromShape(floating)));
                paintFloating.Should().NotThrow($"floating '{item.Id}'");
            }
        });
    }

    [Fact]
    public async Task Floating_preset_shape_paints_its_outline_geometry()
    {
        await Dispatch(() =>
        {
            PresetShapeCatalog.TryGetItem("Star5", out var item).Should().BeTrue();
            var star = PresetShapeCatalog.CreateShape(item!);
            star.Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square, HorizontalOffsetPt = 20, VerticalOffsetPt = 20 };

            PaintedGeometryCount(DocumentWith(Run.FromShape(star))).Should().BeGreaterThan(0);
        });
    }

    public static IEnumerable<object[]> FamilyCharts()
    {
        foreach (var kind in new[] { ChartKind.Column, ChartKind.Bar, ChartKind.Line, ChartKind.Area })
        {
            yield return [kind, ChartGrouping.Stacked];
            yield return [kind, ChartGrouping.PercentStacked];
        }

        foreach (var kind in new[] { ChartKind.Radar, ChartKind.Bubble, ChartKind.Stock, ChartKind.Combo })
            yield return [kind, ChartGrouping.Standard];
    }

    [Theory]
    [MemberData(nameof(FamilyCharts))]
    public async Task Chart_family_paints_inline_and_floating(ChartKind kind, ChartGrouping grouping)
    {
        await Dispatch(() =>
        {
            Chart Build()
            {
                var chart = new Chart { Kind = kind, Grouping = grouping, Title = "Family", ShowLegend = true };
                chart.Categories.AddRange(["1", "2", "3"]);
                chart.Series.Add(new ChartSeries("High", [12, 14, 13]));
                chart.Series.Add(new ChartSeries("Low", [8, 9, 7]));
                chart.Series.Add(new ChartSeries("Close", [10, 13, 9]) { PlotAsLine = kind == ChartKind.Combo, UseSecondaryAxis = kind == ChartKind.Combo });
                ChartKindNormalizer.Normalize(chart);
                return chart;
            }

            PaintedGeometryCount(DocumentWith(Run.FromChart(Build()))).Should().BeGreaterThan(0);

            var floating = Build();
            floating.Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square, HorizontalOffsetPt = 20, VerticalOffsetPt = 20 };
            PaintedGeometryCount(DocumentWith(Run.FromChart(floating))).Should().BeGreaterThan(0);
        });
    }
}
