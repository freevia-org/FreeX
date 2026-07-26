using FluentAssertions;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Axes;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R90-render-chart-axis-titles-5-2: Excel's Format Axis &gt; Labels "Interval between labels"
/// (<c>&lt;c:tickLblSkip&gt;</c>) and "Interval between tick marks" (<c>&lt;c:tickMarkSkip&gt;</c>) are read
/// into <see cref="ChartModel.XAxisLabelSkip"/>/<see cref="ChartModel.XAxisTickMarkSkip"/> by
/// <c>XlsxChartAxisReader</c> and written back by <c>XlsxChartXmlWriter.Axes</c>, but the WPF/OxyPlot
/// renderer never consulted them: every category tick and label was drawn regardless. The category
/// axes it builds now thin their tick labels and tick marks the way Excel does.
/// </summary>
public sealed partial class ChartRendererTests
{
    private static readonly string[] SixCategoryLabels = ["C0", "C1", "C2", "C3", "C4", "C5"];

    /// <summary>Stand-in surface the plot model is really rendered onto.</summary>
    private static readonly OxyRect CategoryAxisPlotArea = new(0, 0, 600, 400);

    /// <summary>
    /// A headless <see cref="IRenderContext"/> that records the text OxyPlot actually draws. Rendering
    /// for real (rather than poking at axis internals) is what resolves the axis's actual tick step
    /// from the plot area, and it is the only way to see which tick labels genuinely reach the canvas.
    /// </summary>
    private sealed class RecordingRenderContext : RenderContextBase
    {
        public List<string> DrawnText { get; } = [];

        public override void DrawText(
            ScreenPoint p,
            string text,
            OxyColor fill,
            string fontFamily,
            double fontSize,
            double fontWeight,
            double rotation,
            OxyPlot.HorizontalAlignment horizontalAlignment,
            OxyPlot.VerticalAlignment verticalAlignment,
            OxySize? maxSize)
        {
            if (!string.IsNullOrEmpty(text))
                DrawnText.Add(text);
        }

        private int _clipCount;

        public override int ClipCount => _clipCount;

        public override void PushClip(OxyRect clippingRectangle) => _clipCount++;

        public override void PopClip() => _clipCount--;

        public override OxySize MeasureText(string text, string fontFamily, double fontSize, double fontWeight) =>
            new((text?.Length ?? 0) * fontSize * 0.5, fontSize);

        public override void DrawLine(
            IList<ScreenPoint> points,
            OxyColor stroke,
            double thickness,
            EdgeRenderingMode edgeRenderingMode,
            double[]? dashArray,
            LineJoin lineJoin)
        {
        }

        public override void DrawPolygon(
            IList<ScreenPoint> points,
            OxyColor fill,
            OxyColor stroke,
            double thickness,
            EdgeRenderingMode edgeRenderingMode,
            double[]? dashArray,
            LineJoin lineJoin)
        {
        }

        public override void DrawEllipse(
            OxyRect rect,
            OxyColor fill,
            OxyColor stroke,
            double thickness,
            EdgeRenderingMode edgeRenderingMode)
        {
        }
    }

    private static ViewportModel SixCategoryViewport(SheetId sheetId) =>
        new(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Value"),
                Cell(2, 1, "C0"), Cell(2, 2, "10"),
                Cell(3, 1, "C1"), Cell(3, 2, "20"),
                Cell(4, 1, "C2"), Cell(4, 2, "30"),
                Cell(5, 1, "C3"), Cell(5, 2, "40"),
                Cell(6, 1, "C4"), Cell(6, 2, "50"),
                Cell(7, 1, "C5"), Cell(7, 2, "60")
            ],
            [],
            []);

    /// <summary>
    /// Builds and actually renders the plot model for a six-category chart of <paramref name="type"/>,
    /// then returns the category labels OxyPlot drew and the tick-mark values it would draw ticks at.
    /// </summary>
    private static (IReadOnlyList<string> Labels, IReadOnlyList<double> TickValues) RenderCategoryAxis(
        ChartType type,
        AxisPosition categoryAxisPosition,
        int labelSkip,
        int tickMarkSkip)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = type,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 7, 2)),
            ShowLegend = false,
            XAxisLabelSkip = labelSkip,
            XAxisTickMarkSkip = tickMarkSkip
        };

        var model = BuildPlotModel(chart, SixCategoryViewport(sheetId));
        var renderContext = new RecordingRenderContext();
        ((IPlotModel)model).Update(true);
        ((IPlotModel)model).Render(renderContext, CategoryAxisPlotArea);

        // Rendering has now resolved the axis's actual tick step, so the tick values it drew marks at
        // can be read back off the axis itself (the drawn tick segments are not otherwise attributable
        // to a specific axis in the recorded output).
        var axis = model.Axes.Should().ContainSingle(a => a.Position == categoryAxisPosition).Subject;
        axis.GetTickValues(out _, out var majorTickValues, out _);

        var categoryLabels = SixCategoryLabels.Where(renderContext.DrawnText.Contains).ToList();
        // Preserve draw order rather than category order so a mis-anchored skip cannot pass.
        var drawnCategoryLabels = renderContext.DrawnText.Where(SixCategoryLabels.Contains).ToList();
        drawnCategoryLabels.Should().Equal(categoryLabels, "labels are drawn in category order");
        return (drawnCategoryLabels, majorTickValues.ToList());
    }

    [Fact]
    public void CategoryAxis_DrawsOnlyEveryNthLabel_WhenXAxisLabelSkipIsSet()
    {
        var (labels, tickValues) = RenderCategoryAxis(
            ChartType.Column, AxisPosition.Bottom, labelSkip: 3, tickMarkSkip: 0);

        // Excel anchors the kept labels on the first category: the 1st and 4th of six.
        labels.Should().Equal("C0", "C3");
        // Label thinning must leave the tick marks alone.
        tickValues.Should().HaveCount(6);
    }

    [Fact]
    public void CategoryAxis_DrawsOnlyEveryNthTickMark_WhenXAxisTickMarkSkipIsSet()
    {
        var (labels, tickValues) = RenderCategoryAxis(
            ChartType.Column, AxisPosition.Bottom, labelSkip: 0, tickMarkSkip: 2);

        tickValues.Should().Equal(0, 2, 4);
        // Tick-mark thinning must leave the labels alone.
        labels.Should().Equal("C0", "C1", "C2", "C3", "C4", "C5");
    }

    [Fact]
    public void CategoryAxis_AppliesLabelAndTickMarkSkipsIndependently()
    {
        var (labels, tickValues) = RenderCategoryAxis(
            ChartType.Column, AxisPosition.Bottom, labelSkip: 3, tickMarkSkip: 2);

        labels.Should().Equal("C0", "C3");
        tickValues.Should().Equal(0, 2, 4);
    }

    [Fact]
    public void BarFamilyCategoryAxisOnTheLeft_HonorsTheSameXModelFields()
    {
        // XlsxChartAxisReader stores the category axis's skips on the X* fields even when the category
        // axis is the vertical one, so the bar family's left CategoryAxis must read them from there.
        var (labels, _) = RenderCategoryAxis(
            ChartType.Bar, AxisPosition.Left, labelSkip: 2, tickMarkSkip: 0);

        labels.Should().Equal("C0", "C2", "C4");
    }

    // ---- No-regression siblings: the default and the CT_Skip "1" spelling both draw everything ----

    [Fact]
    public void CategoryAxis_DrawsEveryLabelAndTickMark_ByDefault()
    {
        var (labels, tickValues) = RenderCategoryAxis(
            ChartType.Column, AxisPosition.Bottom, labelSkip: 0, tickMarkSkip: 0);

        labels.Should().Equal("C0", "C1", "C2", "C3", "C4", "C5");
        tickValues.Should().Equal(0, 1, 2, 3, 4, 5);
    }

    [Fact]
    public void CategoryAxis_TreatsSkipOfOneAsExcelsDefaultInterval()
    {
        // ECMA-376's CT_Skip defaults to 1 and Excel writes val="1" for "show every label", so 1 must
        // behave exactly like the unspecified 0 the model stores when the element is absent.
        var (labels, tickValues) = RenderCategoryAxis(
            ChartType.Column, AxisPosition.Bottom, labelSkip: 1, tickMarkSkip: 1);

        labels.Should().Equal("C0", "C1", "C2", "C3", "C4", "C5");
        tickValues.Should().Equal(0, 1, 2, 3, 4, 5);
    }

    [Fact]
    public void ValueAxis_IsUnaffectedByCategoryAxisSkips()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 7, 2)),
            ShowLegend = false,
            XAxisLabelSkip = 3,
            XAxisTickMarkSkip = 2
        };

        var model = BuildPlotModel(chart, SixCategoryViewport(sheetId));
        ((IPlotModel)model).Update(true);
        ((IPlotModel)model).Render(new RecordingRenderContext(), CategoryAxisPlotArea);

        var valueAxis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Left).Subject;
        valueAxis.GetTickValues(out var labelValues, out var tickValues, out _);
        labelValues.Should().HaveCountGreaterThan(2);
        tickValues.Should().HaveSameCount(labelValues);
    }
}
