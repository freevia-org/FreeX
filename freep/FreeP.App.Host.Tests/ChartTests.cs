using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 2b chart tests: model, I/O round-trip, and compositor.
/// </summary>
public sealed class ChartTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.ChartTests", Guid.NewGuid().ToString("N"));

    public ChartTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1. Model — SlideShapeKind.Chart = 5
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SlideShapeKind_Chart_HasValue5()
    {
        ((int)SlideShapeKind.Chart).Should().Be(5);
    }

    [Fact]
    public void ChartShape_DefaultValues()
    {
        var chart = new ChartShape();
        chart.ChartType.Should().Be(ChartType.ColumnClustered);
        chart.StyleId.Should().BeNull();
        chart.Title.Should().BeNull();
        chart.Categories.Should().BeEmpty();
        chart.Series.Should().BeEmpty();
        chart.Legend.Should().BeNull();
        chart.VaryColors.Should().BeFalse();
        chart.DisplayBlanksAs.Should().BeNull();
        chart.ShowDataLabelsOverMaximum.Should().BeNull();
        chart.BarGapWidthPercent.Should().BeNull();
        chart.BarOverlapPercent.Should().BeNull();
        chart.BarGapDepthPercent.Should().BeNull();
        chart.ThreeDStyle.Should().Be(ChartThreeDStyle.None);
        chart.View3D.Should().BeNull();
        chart.FirstSliceAngleDegrees.Should().BeNull();
        chart.BubbleScalePercent.Should().Be(100);
        chart.BubbleSizeRepresents.Should().Be(BubbleSizeRepresentation.Area);
        chart.ShowNegativeBubbles.Should().BeFalse();
    }

    [Fact]
    public void SlideCloner_ChartPreservesTypeSpecificChartMetadata()
    {
        var slide = new Slide();
        var chart = BuildDoughnutChart(holeSize: 65);
        chart.FirstSliceAngleDegrees = 135;
        chart.StyleId = 102;
        chart.BarGapWidthPercent = 25;
        chart.BarOverlapPercent = -40;
        chart.BarGapDepthPercent = 125;
        chart.ThreeDStyle = ChartThreeDStyle.Pie;
        chart.View3D = new Chart3DView
        {
            RotationX = 15,
            RotationY = 20,
            RightAngleAxes = false,
            Perspective = 30,
            HeightPercent = 100,
            DepthPercent = 100,
        };
        chart.DisplayBlanksAs = ChartDisplayBlanksAs.Span;
        chart.ShowDataLabelsOverMaximum = true;
        chart.ShowDropLines = true;
        chart.ShowUpDownBars = true;
        chart.UpDownBarGapWidthPercent = 180;
        chart.UpBarFill = new ShapeFill.Solid(new SrgbColor(0x11, 0x22, 0x33));
        chart.DownBarFill = new ShapeFill.Solid(new SrgbColor(0xAA, 0xBB, 0xCC));
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Chart,
            Chart = chart
        });

        var clone = SlideCloner.CloneSlide(slide);

        var clonedChart = clone.Shapes.Single().Chart!;
        clonedChart.Should().NotBeSameAs(chart);
        clonedChart.ChartType.Should().Be(ChartType.Doughnut);
        clonedChart.StyleId.Should().Be(102);
        clonedChart.DoughnutHolePercent.Should().Be(65);
        clonedChart.FirstSliceAngleDegrees.Should().Be(135);
        clonedChart.BarGapWidthPercent.Should().Be(25);
        clonedChart.BarOverlapPercent.Should().Be(-40);
        clonedChart.BarGapDepthPercent.Should().Be(125);
        clonedChart.ThreeDStyle.Should().Be(ChartThreeDStyle.Pie);
        clonedChart.View3D.Should().NotBeNull();
        clonedChart.View3D.Should().NotBeSameAs(chart.View3D);
        clonedChart.View3D!.RotationX.Should().Be(15);
        clonedChart.View3D.RotationY.Should().Be(20);
        clonedChart.View3D.RightAngleAxes.Should().BeFalse();
        clonedChart.View3D.Perspective.Should().Be(30);
        clonedChart.View3D.HeightPercent.Should().Be(100);
        clonedChart.View3D.DepthPercent.Should().Be(100);
        clonedChart.DisplayBlanksAs.Should().Be(ChartDisplayBlanksAs.Span);
        clonedChart.ShowDataLabelsOverMaximum.Should().BeTrue();
        clonedChart.ShowDropLines.Should().BeTrue();
        clonedChart.ShowUpDownBars.Should().BeTrue();
        clonedChart.UpDownBarGapWidthPercent.Should().Be(180);
        clonedChart.UpBarFill.Should().Be(chart.UpBarFill);
        clonedChart.DownBarFill.Should().Be(chart.DownBarFill);
    }

    [Fact]
    public void SlideCloner_ChartPreservesAuthoredSurfaceLayoutAndBubbleMetadata()
    {
        var slide = new Slide();
        var chart = BuildColumnChart();
        chart.ChartAreaFill = new ShapeFill.Solid(SrgbColor.FromRgb(0xF2F2F2));
        chart.ChartAreaOutline = new ShapeOutline.Visible(SrgbColor.FromRgb(0x7F7F7F), 1.25);
        chart.HasAutomaticTitle = true;
        chart.TitleOverlay = true;
        chart.PlotVisibleOnly = false;
        chart.PlotAreaManualLayout = new ChartManualLayout
        {
            LayoutTarget = "inner",
            XMode = ChartManualLayoutMode.Edge,
            YMode = ChartManualLayoutMode.Factor,
            WidthMode = ChartManualLayoutMode.Factor,
            HeightMode = ChartManualLayoutMode.Edge,
            X = 0.10,
            Y = 0.20,
            Width = 0.70,
            Height = 0.85,
        };
        chart.PlotAreaFill = new ShapeFill.Solid(SrgbColor.FromRgb(0xEAF2F8));
        chart.PlotAreaOutline = new ShapeOutline.Visible(SrgbColor.FromRgb(0x1F4E79), 0.75);
        chart.Legend = LegendPosition.Right;
        chart.LegendOverlay = true;
        chart.LegendManualLayout = new ChartManualLayout
        {
            LayoutTarget = "outer",
            X = 0.60,
            Y = 0.10,
            Width = 0.35,
            Height = 0.40,
        };
        chart.VaryColors = true;
        chart.BubbleScalePercent = 175;
        chart.BubbleSizeRepresents = BubbleSizeRepresentation.Width;
        chart.ShowNegativeBubbles = true;
        chart.PreservedChartSpaceExtensionsXml =
            "<c:extLst xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:ext uri=\"urn:freep:chart-test\" /></c:extLst>";
        chart.PreservedPivotSourceXml =
            "<c:pivotSource xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:name>PivotTable1</c:name><c:fmtId val=\"1\" /></c:pivotSource>";
        chart.ChartDate1904 = true;
        chart.ChartLanguage = "en-US";
        chart.PreservedChartProtectionXml =
            "<c:protection xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" chartObject=\"1\" data=\"0\" />";
        chart.ChartObjectProtected = true;
        chart.ChartDataProtected = false;
        chart.ChartFormattingProtected = true;
        chart.ChartSelectionProtected = true;
        slide.Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.Chart, Chart = chart });

        var clone = SlideCloner.CloneSlide(slide).Shapes.Single().Chart!;

        ((ShapeFill.Solid)clone.ChartAreaFill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xF2F2F2));
        ((ShapeOutline.Visible)clone.ChartAreaOutline!).WidthPt.Should().Be(1.25);
        clone.HasAutomaticTitle.Should().BeTrue();
        clone.TitleOverlay.Should().BeTrue();
        clone.PlotVisibleOnly.Should().BeFalse();
        clone.PlotAreaManualLayout.Should().NotBeSameAs(chart.PlotAreaManualLayout);
        clone.PlotAreaManualLayout!.LayoutTarget.Should().Be("inner");
        clone.PlotAreaManualLayout.XMode.Should().Be(ChartManualLayoutMode.Edge);
        clone.PlotAreaManualLayout.Height.Should().Be(0.85);
        ((ShapeFill.Solid)clone.PlotAreaFill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xEAF2F8));
        ((ShapeOutline.Visible)clone.PlotAreaOutline!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        clone.LegendOverlay.Should().BeTrue();
        clone.LegendManualLayout.Should().NotBeSameAs(chart.LegendManualLayout);
        clone.LegendManualLayout!.LayoutTarget.Should().Be("outer");
        clone.LegendManualLayout.Width.Should().Be(0.35);
        clone.VaryColors.Should().BeTrue();
        clone.BubbleScalePercent.Should().Be(175);
        clone.BubbleSizeRepresents.Should().Be(BubbleSizeRepresentation.Width);
        clone.ShowNegativeBubbles.Should().BeTrue();
        clone.PreservedChartSpaceExtensionsXml.Should().Be(chart.PreservedChartSpaceExtensionsXml);
        clone.PreservedPivotSourceXml.Should().Be(chart.PreservedPivotSourceXml);
        clone.ChartDate1904.Should().BeTrue();
        clone.ChartLanguage.Should().Be("en-US");
        clone.PreservedChartProtectionXml.Should().Be(chart.PreservedChartProtectionXml);
        clone.ChartObjectProtected.Should().BeTrue();
        clone.ChartDataProtected.Should().BeFalse();
        clone.ChartFormattingProtected.Should().BeTrue();
        clone.ChartSelectionProtected.Should().BeTrue();
    }

    [Fact]
    public void ChartSpaceExtensions_SurviveReadWriteAndReopen()
    {
        var sourcePath = WriteToPptx(BuildPresWithChart(BuildColumnChart()));
        var extension = XNamespace.Get("urn:freep:chart-extension-test");
        RewriteChartXml(sourcePath, 1, document =>
        {
            document.Root!.Add(new XElement(ChartNs + "extLst",
                new XElement(ChartNs + "ext",
                    new XAttribute("uri", "urn:freep:chart-extension"),
                    new XElement(extension + "state", new XAttribute("value", "keep")))));
        });

        var imported = PptxPackageReader.Read(sourcePath);
        var importedChart = imported.Slides[0].Shapes.Single().Chart!;
        importedChart.PreservedChartSpaceExtensionsXml.Should().Contain("urn:freep:chart-extension");
        importedChart.PreservedChartSpaceExtensionsXml.Should().Contain("value=\"keep\"");

        var savedPath = WriteToPptx(imported);
        using var archive = ZipFile.OpenRead(savedPath);
        var chartXml = LoadChartXml(archive, 1);
        var savedState = chartXml.Root!
            .Element(ChartNs + "extLst")
            ?.Descendants(extension + "state")
            .Single();
        savedState.Should().NotBeNull();
        savedState!.Attribute("value")?.Value.Should().Be("keep");
    }

    [Fact]
    public void SlideCloner_ChartPreservesAxisDisplayMetadata()
    {
        var chart = BuildColumnChart();
        chart.CategoryAxis.MajorTickMark = ChartTickMark.Out;
        chart.CategoryAxis.MinorTickMark = ChartTickMark.None;
        chart.CategoryAxis.TickLabelPosition = ChartTickLabelPosition.NextTo;
        chart.CategoryAxis.LabelOffsetPercent = 100;
        chart.CategoryAxis.NoMultiLevelLabels = false;
        chart.ValueAxis.MajorTickMark = ChartTickMark.Cross;

        var slide = new Slide();
        slide.Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.Chart, Chart = chart });
        var clone = SlideCloner.CloneSlide(slide).Shapes.Single().Chart!;

        clone.CategoryAxis.MajorTickMark.Should().Be(ChartTickMark.Out);
        clone.CategoryAxis.MinorTickMark.Should().Be(ChartTickMark.None);
        clone.CategoryAxis.TickLabelPosition.Should().Be(ChartTickLabelPosition.NextTo);
        clone.CategoryAxis.LabelOffsetPercent.Should().Be(100);
        clone.CategoryAxis.NoMultiLevelLabels.Should().BeFalse();
        clone.ValueAxis.MajorTickMark.Should().Be(ChartTickMark.Cross);
    }

    [Fact]
    public void SlideCloner_ChartPreservesSeriesAndPointAuthoredPayload()
    {
        var chart = BuildColumnChart();
        chart.ValueAxis.NumberFormatCode = "0.00%";
        chart.ValueAxis.NumberFormatSourceLinked = false;
        var series = chart.Series[0];
        series.Fill = new ShapeFill.Pattern(
            "diagStripe",
            new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33)),
            new ThemeAwareColor(new SrgbColor(0xEE, 0xDD, 0xCC)));
        series.SmoothLine = true;
        series.Trendline = new ChartTrendline
        {
            Type = ChartTrendlineType.Polynomial,
            PolynomialOrder = 3,
            Forward = 1.25,
            DisplayEquation = true,
            DisplayRSquared = true,
        };
        series.FormulaReferences.SeriesName = "Sheet1!$B$1";
        series.FormulaReferences.Category = "Sheet1!$A$2:$A$4";
        series.FormulaReferences.Values = "Sheet1!$B$2:$B$4";
        series.PointStyles[1] = new ChartPointStyle
        {
            Fill = new ShapeFill.Solid(new SrgbColor(0xAA, 0xBB, 0xCC)),
            DataLabels = new ChartDataLabels
            {
                Delete = true,
                ShowValue = true,
                NumberFormat = "0.0%",
            },
            Marker = new ChartMarkerStyle
            {
                Symbol = ChartMarkerSymbol.Diamond,
                Fill = new ShapeFill.Solid(new SrgbColor(0x12, 0x34, 0x56)),
            },
        };

        var slide = new Slide();
        slide.Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.Chart, Chart = chart });
        var clone = SlideCloner.CloneSlide(slide).Shapes.Single().Chart!;
        var clonedSeries = clone.Series[0];
        var clonedPoint = clonedSeries.PointStyles[1];

        clonedSeries.Fill.Should().BeOfType<ShapeFill.Pattern>();
        clonedSeries.SmoothLine.Should().BeTrue();
        clonedSeries.Trendline.Should().NotBeSameAs(series.Trendline);
        clonedSeries.Trendline!.Type.Should().Be(ChartTrendlineType.Polynomial);
        clonedSeries.Trendline.PolynomialOrder.Should().Be(3);
        clonedSeries.Trendline.DisplayEquation.Should().BeTrue();
        clonedSeries.FormulaReferences.SeriesName.Should().Be("Sheet1!$B$1");
        clonedSeries.FormulaReferences.Category.Should().Be("Sheet1!$A$2:$A$4");
        clonedSeries.FormulaReferences.Values.Should().Be("Sheet1!$B$2:$B$4");
        clonedPoint.Fill.Should().BeOfType<ShapeFill.Solid>();
        clonedPoint.DataLabels.Should().NotBeSameAs(series.PointStyles[1].DataLabels);
        clonedPoint.DataLabels!.Delete.Should().BeTrue();
        clonedPoint.DataLabels.ShowValue.Should().BeTrue();
        clonedPoint.DataLabels.NumberFormat.Should().Be("0.0%");
        clonedPoint.Marker!.Fill.Should().BeOfType<ShapeFill.Solid>();
        clone.ValueAxis.NumberFormatCode.Should().Be("0.00%");
        clone.ValueAxis.NumberFormatSourceLinked.Should().BeFalse();
    }

    [Fact]
    public void ChartSeries_DefaultValues()
    {
        var series = new ChartSeries();
        series.Name.Should().BeEmpty();
        series.FillColor.Should().BeNull();
        series.Values.Should().BeEmpty();
        series.PointColors.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. I/O round-trip — write a chart shape then read it back
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Chart_ShapeKindPreserved()
    {
        var pres = BuildPresWithChart(BuildColumnChart());
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var shape = reloaded.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Chart);
        shape.Should().NotBeNull("chart shape should survive round-trip");
    }

    [Fact]
    public void RoundTrip_Chart_AnchorPreserved()
    {
        var pres = BuildPresWithChart(BuildColumnChart());
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var shape = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart);
        shape.OffsetXEmu.Should().Be(914400,  "offset X preserved");
        shape.OffsetYEmu.Should().Be(457200,  "offset Y preserved");
        shape.ExtentCxEmu.Should().Be(5486400, "width preserved");
        shape.ExtentCyEmu.Should().Be(3657600, "height preserved");
    }

    [Fact]
    public void RoundTrip_Chart_TypePreserved()
    {
        var chart = BuildColumnChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var shape = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart);
        shape.Chart.Should().NotBeNull();
        shape.Chart!.ChartType.Should().Be(ChartType.ColumnClustered);
    }

    [Fact]
    public void RoundTrip_ComboChart_PreservesSecondaryLineOverride()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var inserted = editor.InsertComboChart();
        var path = WriteToPptx(presentation);
        var reloaded = PptxPackageReader.Read(path);

        var chart = reloaded.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Chart).Chart;
        chart.Should().NotBeNull();
        chart!.Series.Should().HaveCount(2);
        chart.Series[1].OverrideChartType.Should().Be(ChartType.LineMarkers);
        chart.Series[1].OnSecondaryAxis.Should().BeTrue();
        inserted.Chart!.Series[1].OverrideChartType.Should().Be(ChartType.LineMarkers);
    }

    [Fact]
    public void RoundTrip_Chart_AxisDisplayMetadataPreserved()
    {
        var chart = BuildColumnChart();
        chart.CategoryAxis.MajorTickMark = ChartTickMark.Out;
        chart.CategoryAxis.MinorTickMark = ChartTickMark.None;
        chart.CategoryAxis.TickLabelPosition = ChartTickLabelPosition.NextTo;
        chart.CategoryAxis.LabelOffsetPercent = 100;
        chart.CategoryAxis.NoMultiLevelLabels = false;
        chart.ValueAxis.MajorTickMark = ChartTickMark.Cross;
        chart.ValueAxis.MinorTickMark = ChartTickMark.In;
        chart.ValueAxis.TickLabelPosition = ChartTickLabelPosition.High;
        chart.ValueAxis.CrossBetween = ChartCrossBetween.MidCat;
        chart.CategoryAxis.AutoCrossing = false;
        chart.CategoryAxis.LabelAlignment = ChartLabelAlignment.Right;
        chart.CategoryAxis.Crosses = ChartAxisCrossing.Max;
        chart.ValueAxis.CrossesAt = 12.5;
        chart.ValueAxis.MajorUnit = 5;
        chart.ValueAxis.MinorUnit = 1;
        chart.ValueAxis.DisplayUnit = ChartAxisDisplayUnit.Millions;
        chart.ValueAxis.HasMinorGridlines = true;
        chart.CategoryAxis.ReverseOrder = true;
        chart.ValueAxis.ReverseOrder = true;

        var reloaded = PptxPackageReader.Read(WriteToPptx(BuildPresWithChart(chart)));
        var roundTripped = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;

        roundTripped.CategoryAxis.MajorTickMark.Should().Be(ChartTickMark.Out);
        roundTripped.CategoryAxis.MinorTickMark.Should().Be(ChartTickMark.None);
        roundTripped.CategoryAxis.TickLabelPosition.Should().Be(ChartTickLabelPosition.NextTo);
        roundTripped.CategoryAxis.LabelOffsetPercent.Should().Be(100);
        roundTripped.CategoryAxis.NoMultiLevelLabels.Should().BeFalse();
        roundTripped.ValueAxis.MajorTickMark.Should().Be(ChartTickMark.Cross);
        roundTripped.ValueAxis.MinorTickMark.Should().Be(ChartTickMark.In);
        roundTripped.ValueAxis.TickLabelPosition.Should().Be(ChartTickLabelPosition.High);
        roundTripped.ValueAxis.CrossBetween.Should().Be(ChartCrossBetween.MidCat);
        roundTripped.CategoryAxis.AutoCrossing.Should().BeFalse();
        roundTripped.CategoryAxis.LabelAlignment.Should().Be(ChartLabelAlignment.Right);
        roundTripped.CategoryAxis.Crosses.Should().Be(ChartAxisCrossing.Max);
        roundTripped.ValueAxis.CrossesAt.Should().Be(12.5);
        roundTripped.ValueAxis.MajorUnit.Should().Be(5);
        roundTripped.ValueAxis.MinorUnit.Should().Be(1);
        roundTripped.ValueAxis.DisplayUnit.Should().Be(ChartAxisDisplayUnit.Millions);
        roundTripped.ValueAxis.HasMinorGridlines.Should().BeTrue();
        roundTripped.CategoryAxis.ReverseOrder.Should().BeTrue();
        roundTripped.ValueAxis.ReverseOrder.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_Chart_AxisUnknownDisplayUnitToken_IsRetained()
    {
        var chart = BuildColumnChart();
        chart.ValueAxis.DisplayUnit = ChartAxisDisplayUnit.Unsupported;
        chart.ValueAxis.RawDisplayUnitToken = "customPowerUnit";

        var reloaded = PptxPackageReader.Read(WriteToPptx(BuildPresWithChart(chart)));
        var axis = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!.ValueAxis;

        axis.DisplayUnit.Should().Be(ChartAxisDisplayUnit.Unsupported);
        axis.RawDisplayUnitToken.Should().Be("customPowerUnit");
    }

    [Fact]
    public void RoundTrip_Chart_SeriesCountPreserved()
    {
        var chart = BuildColumnChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.Series.Should().HaveCount(2, "two series survive round-trip");
    }

    [Fact]
    public void RoundTrip_Chart_SeriesNamesPreserved()
    {
        var chart = BuildColumnChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.Series[0].Name.Should().Be("Sales");
        rt.Series[1].Name.Should().Be("Budget");
    }

    [Fact]
    public void RoundTrip_Chart_ValuesPreserved()
    {
        var chart = BuildColumnChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.Series[0].Values.Should().HaveCount(3);
        rt.Series[0].Values[0].Should().BeApproximately(100, 0.01);
        rt.Series[0].Values[1].Should().BeApproximately(200, 0.01);
        rt.Series[0].Values[2].Should().BeApproximately(150, 0.01);
    }

    [Fact]
    public void RoundTrip_Chart_CategoriesPreserved()
    {
        var chart = BuildColumnChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.Categories.Should().Equal(new[] { "Q1", "Q2", "Q3" });
    }

    [Fact]
    public void RoundTrip_Chart_TitlePreserved()
    {
        var chart = BuildColumnChart();
        chart.Title = "Quarterly Performance";
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.Title.Should().Be("Quarterly Performance");
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public void RoundTrip_Chart_TitleOverlay_PreservesPackageAndModel(bool overlay, string xmlValue)
    {
        var chart = BuildColumnChart();
        chart.Title = "Quarterly Performance";
        chart.TitleOverlay = overlay;
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var title = chartDoc.Root!.Element(ChartNs + "chart")!
                .Element(ChartNs + "title");
            title.Should().NotBeNull();
            title!.Element(ChartNs + "overlay")?.Attribute("val")?.Value.Should().Be(xmlValue);
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.Title.Should().Be("Quarterly Performance");
        rt.TitleOverlay.Should().Be(overlay);
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public void RoundTrip_Chart_PlotVisibleOnly_PreservesPackageAndModel(bool visibleOnly, string xmlValue)
    {
        var chart = BuildColumnChart();
        chart.PlotVisibleOnly = visibleOnly;
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var chartEl = chartDoc.Root!.Element(ChartNs + "chart")!;
            chartEl.Element(ChartNs + "plotVisOnly")?.Attribute("val")?.Value.Should().Be(xmlValue);
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.PlotVisibleOnly.Should().Be(visibleOnly);
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public void RoundTrip_Chart_RoundedCorners_PreservesPackageAndModel(bool roundedCorners, string xmlValue)
    {
        var chart = BuildColumnChart();
        chart.RoundedCorners = roundedCorners;
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            chartDoc.Root!.Element(ChartNs + "roundedCorners")?.Attribute("val")?.Value.Should().Be(xmlValue);
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.RoundedCorners.Should().Be(roundedCorners);
    }

    [Fact]
    public void RoundTrip_Chart_PivotSource_PreservesPackageAndModel()
    {
        var chart = BuildColumnChart();
        chart.PreservedPivotSourceXml =
            "<c:pivotSource xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:name>PivotTable1</c:name><c:fmtId val=\"1\" /></c:pivotSource>";
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var root = chartDoc.Root!;
            var pivotSource = root.Element(ChartNs + "pivotSource");
            pivotSource.Should().NotBeNull();
            root.Elements().Select(element => element.Name.LocalName)
                .Should().ContainInOrder("pivotSource", "chart");
            pivotSource!.Element(ChartNs + "name")?.Value.Should().Be("PivotTable1");
            pivotSource.Element(ChartNs + "fmtId")?.Attribute("val")?.Value.Should().Be("1");
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.PreservedPivotSourceXml.Should().Contain("PivotTable1");
        rt.PreservedPivotSourceXml.Should().Contain("fmtId");
    }

    [Fact]
    public void RoundTrip_ChartSpaceMetadata_PreservesDateLocaleAndProtection()
    {
        var chart = BuildColumnChart();
        chart.ChartDate1904 = true;
        chart.ChartLanguage = "fr-FR";
        chart.PreservedChartProtectionXml =
            "<c:protection xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" chartObject=\"1\" data=\"0\" formatting=\"1\" selection=\"1\" />";
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var root = LoadChartXml(archive, chartIndex: 1).Root!;
            root.Elements().Select(element => element.Name.LocalName)
                .Should().ContainInOrder("date1904", "lang", "protection", "chart");
            root.Element(ChartNs + "date1904")?.Attribute("val")?.Value.Should().Be("1");
            root.Element(ChartNs + "lang")?.Attribute("val")?.Value.Should().Be("fr-FR");
            root.Element(ChartNs + "protection")?.Attribute("chartObject")?.Value.Should().Be("1");
            root.Element(ChartNs + "protection")?.Attribute("data")?.Value.Should().Be("0");
            root.Element(ChartNs + "protection")?.Attribute("selection")?.Value.Should().Be("1");
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartDate1904.Should().BeTrue();
        rt.ChartLanguage.Should().Be("fr-FR");
        rt.PreservedChartProtectionXml.Should().Contain("formatting");
        rt.ChartObjectProtected.Should().BeTrue();
        rt.ChartDataProtected.Should().BeFalse();
        rt.ChartFormattingProtected.Should().BeTrue();
        rt.ChartSelectionProtected.Should().BeTrue();

        rt.ChartObjectProtected = false;
        rt.ChartDataProtected = true;
        rt.ChartFormattingProtected = false;
        rt.ChartSelectionProtected = false;
        var editedPath = WriteToPptx(BuildPresWithChart(rt));
        using (var archive = ZipFile.OpenRead(editedPath))
        {
            var protection = LoadChartXml(archive, chartIndex: 1).Root!
                .Element(ChartNs + "protection");
            protection.Should().NotBeNull();
            protection!.Attribute("chartObject")?.Value.Should().Be("0");
            protection.Attribute("data")?.Value.Should().Be("1");
            protection.Attribute("formatting")?.Value.Should().Be("0");
            protection.Attribute("selection")?.Value.Should().Be("0");
        }

        var edited = PptxPackageReader.Read(editedPath);
        var editedChart = edited.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        editedChart.ChartObjectProtected.Should().BeFalse();
        editedChart.ChartDataProtected.Should().BeTrue();
        editedChart.ChartFormattingProtected.Should().BeFalse();
        editedChart.ChartSelectionProtected.Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_ChartLevelMetadata_DefaultsStayAbsentAndNull()
    {
        var path = WriteToPptx(BuildPresWithChart(BuildColumnChart()));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var chartEl = chartDoc.Root!.Element(ChartNs + "chart")!;
            chartEl.Element(ChartNs + "dispBlanksAs").Should().BeNull();
            chartDoc.Root!.Element(ChartNs + "roundedCorners").Should().BeNull();
            chartDoc.Root!.Element(ChartNs + "pivotSource").Should().BeNull();
            chartDoc.Root!.Element(ChartNs + "date1904").Should().BeNull();
            chartDoc.Root!.Element(ChartNs + "lang").Should().BeNull();
            chartDoc.Root!.Element(ChartNs + "protection").Should().BeNull();
            chartEl.Element(ChartNs + "showDLblsOverMax").Should().BeNull();
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.DisplayBlanksAs.Should().BeNull();
        rt.RoundedCorners.Should().BeNull();
        rt.PreservedPivotSourceXml.Should().BeNull();
        rt.ChartDate1904.Should().BeNull();
        rt.ChartLanguage.Should().BeNull();
        rt.PreservedChartProtectionXml.Should().BeNull();
        rt.ShowDataLabelsOverMaximum.Should().BeNull();
    }

    [Theory]
    [InlineData(ChartDisplayBlanksAs.Span, "span")]
    [InlineData(ChartDisplayBlanksAs.Gap, "gap")]
    [InlineData(ChartDisplayBlanksAs.Zero, "zero")]
    public void RoundTrip_ChartLevelDisplayBlanksAs_PreservedInPackageAndModel(
        ChartDisplayBlanksAs modelValue,
        string xmlValue)
    {
        var chart = BuildColumnChart();
        chart.DisplayBlanksAs = modelValue;
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var chartEl = chartDoc.Root!.Element(ChartNs + "chart")!;
            chartEl.Element(ChartNs + "dispBlanksAs")
                ?.Attribute("val")
                ?.Value
                .Should()
                .Be(xmlValue);
            ChartChildIndex(chartEl, "plotVisOnly").Should().BeLessThan(ChartChildIndex(chartEl, "dispBlanksAs"));
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.DisplayBlanksAs.Should().Be(modelValue);
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public void RoundTrip_ChartLevelShowDataLabelsOverMaximum_PreservedInPackageAndModel(
        bool modelValue,
        string xmlValue)
    {
        var chart = BuildColumnChart();
        chart.DisplayBlanksAs = ChartDisplayBlanksAs.Gap;
        chart.ShowDataLabelsOverMaximum = modelValue;
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var chartEl = chartDoc.Root!.Element(ChartNs + "chart")!;
            chartEl.Element(ChartNs + "showDLblsOverMax")
                ?.Attribute("val")
                ?.Value
                .Should()
                .Be(xmlValue);
            ChartChildIndex(chartEl, "plotVisOnly").Should().BeLessThan(ChartChildIndex(chartEl, "dispBlanksAs"));
            ChartChildIndex(chartEl, "dispBlanksAs").Should().BeLessThan(ChartChildIndex(chartEl, "showDLblsOverMax"));
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.DisplayBlanksAs.Should().Be(ChartDisplayBlanksAs.Gap);
        rt.ShowDataLabelsOverMaximum.Should().Be(modelValue);
    }

    [Fact]
    public void Read_ChartLevelShowDataLabelsOverMaximum_BareElementMeansTrue()
    {
        var path = WriteToPptx(BuildPresWithChart(BuildColumnChart()));
        RewriteChartXml(path, chartIndex: 1, chartDoc =>
        {
            var chartEl = chartDoc.Root!.Element(ChartNs + "chart")!;
            chartEl.Element(ChartNs + "plotVisOnly")!
                .AddAfterSelf(new XElement(ChartNs + "showDLblsOverMax"));
        });

        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ShowDataLabelsOverMaximum.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_ColumnChart_GapWidthAndOverlapPreservedInPackageAndModel()
    {
        var chart = BuildColumnChart();
        chart.BarGapWidthPercent = 40;
        chart.BarOverlapPercent = 55;
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var barChart = chartDoc.Descendants(ChartNs + "barChart").Single();
            barChart.Element(ChartNs + "gapWidth")
                ?.Attribute("val")
                ?.Value
                .Should()
                .Be("40");
            barChart.Element(ChartNs + "overlap")
                ?.Attribute("val")
                ?.Value
                .Should()
                .Be("55");
            ChartChildIndex(barChart, "gapWidth").Should().BeLessThan(ChartChildIndex(barChart, "overlap"));
            ChartChildIndex(barChart, "overlap").Should().BeLessThan(ChartChildIndex(barChart, "axId"));
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().Be(ChartType.ColumnClustered);
        rt.BarGapWidthPercent.Should().Be(40);
        rt.BarOverlapPercent.Should().Be(55);
        rt.BarGapDepthPercent.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_ColumnChart_GapDepthPreservedInPackageAndModel()
    {
        var chart = BuildColumnChart();
        chart.BarGapWidthPercent = 40;
        chart.BarGapDepthPercent = 180;
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var bar3DChart = chartDoc.Descendants(ChartNs + "bar3DChart").Single();
            bar3DChart.Element(ChartNs + "gapWidth")
                ?.Attribute("val")
                ?.Value
                .Should()
                .Be("40");
            bar3DChart.Element(ChartNs + "gapDepth")
                ?.Attribute("val")
                ?.Value
                .Should()
                .Be("180");
            bar3DChart.Element(ChartNs + "overlap").Should().BeNull("c:bar3DChart does not use c:overlap");
            ChartChildIndex(bar3DChart, "gapWidth").Should().BeLessThan(ChartChildIndex(bar3DChart, "gapDepth"));
            ChartChildIndex(bar3DChart, "gapDepth").Should().BeLessThan(ChartChildIndex(bar3DChart, "axId"));
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().Be(ChartType.ColumnClustered);
        rt.BarGapWidthPercent.Should().Be(40);
        rt.BarGapDepthPercent.Should().Be(180);
    }

    [Fact]
    public void RoundTrip_Chart_View3D_PreservesSchemaOrderAndCameraSettings()
    {
        var chart = BuildColumnChart();
        chart.View3D = new Chart3DView
        {
            RotationX = 15,
            HeightPercent = 100,
            RotationY = 20,
            DepthPercent = 100,
            RightAngleAxes = false,
            Perspective = 30,
        };
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var chartEl = chartDoc.Root!.Element(ChartNs + "chart")!;
            ChartChildIndex(chartEl, "autoTitleDeleted").Should().BeLessThan(ChartChildIndex(chartEl, "view3D"));
            ChartChildIndex(chartEl, "view3D").Should().BeLessThan(ChartChildIndex(chartEl, "plotArea"));

            var view3D = chartEl.Element(ChartNs + "view3D")!;
            view3D.Elements().Select(element => element.Name.LocalName).Should().Equal(
                "rotX", "hPercent", "rotY", "depthPercent", "rAngAx", "perspective");
            view3D.Element(ChartNs + "rotX")!.Attribute("val")!.Value.Should().Be("15");
            view3D.Element(ChartNs + "hPercent")!.Attribute("val")!.Value.Should().Be("100");
            view3D.Element(ChartNs + "rotY")!.Attribute("val")!.Value.Should().Be("20");
            view3D.Element(ChartNs + "depthPercent")!.Attribute("val")!.Value.Should().Be("100");
            view3D.Element(ChartNs + "rAngAx")!.Attribute("val")!.Value.Should().Be("0");
            view3D.Element(ChartNs + "perspective")!.Attribute("val")!.Value.Should().Be("30");
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.View3D.Should().NotBeNull();
        rt.View3D!.RotationX.Should().Be(15);
        rt.View3D.HeightPercent.Should().Be(100);
        rt.View3D.RotationY.Should().Be(20);
        rt.View3D.DepthPercent.Should().Be(100);
        rt.View3D.RightAngleAxes.Should().BeFalse();
        rt.View3D.Perspective.Should().Be(30);
    }

    [Fact]
    public void RoundTrip_ChartAreaAndPlotAreaFormatting_PreservesSchemaPlacementAndModel()
    {
        var chart = BuildColumnChart();
        chart.ChartAreaFill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xF2F2F2)));
        chart.ChartAreaOutline = new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0x7F7F7F)), 1.25);
        chart.PlotAreaFill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xEAF2F8)));
        chart.PlotAreaOutline = new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)), 0.75);
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var chartSpace = chartDoc.Root!;
            var chartEl = chartSpace.Element(ChartNs + "chart")!;
            var chartSpPr = chartSpace.Element(ChartNs + "spPr");
            var plotArea = chartEl.Element(ChartNs + "plotArea")!;
            var plotSpPr = plotArea.Element(ChartNs + "spPr");

            chartSpPr.Should().NotBeNull();
            chartSpace.Elements().Select(element => element.Name.LocalName).Should().ContainInOrder("chart", "spPr");
            plotSpPr.Should().NotBeNull();
            plotArea.Elements().Last().Should().Be(plotSpPr);
        }

        var reloaded = PptxPackageReader.Read(path);
        var roundTripped = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        ((ShapeFill.Solid)roundTripped.ChartAreaFill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xF2F2F2));
        ((ShapeOutline.Visible)roundTripped.ChartAreaOutline!).WidthPt.Should().Be(1.25);
        ((ShapeFill.Solid)roundTripped.PlotAreaFill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xEAF2F8));
        ((ShapeOutline.Visible)roundTripped.PlotAreaOutline!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
    }

    [Fact]
    public void Read_PowerPointAuthoredBar3DChart_GapDepthClampedIntoModel()
    {
        var path = WriteToPptx(BuildPresWithChart(BuildColumnChart()));
        RewriteChartXml(path, chartIndex: 1, chartDoc =>
        {
            var barChart = chartDoc.Descendants(ChartNs + "barChart").Single();
            barChart.Name = ChartNs + "bar3DChart";
            barChart.Element(ChartNs + "overlap")?.Remove();
            var gapDepth = new XElement(ChartNs + "gapDepth", new XAttribute("val", "650"));
            var gapWidth = barChart.Element(ChartNs + "gapWidth");
            if (gapWidth is not null)
                gapWidth.AddAfterSelf(gapDepth);
            else
                barChart.Elements(ChartNs + "axId").First().AddBeforeSelf(gapDepth);
        });

        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().Be(ChartType.ColumnClustered);
        rt.BarGapDepthPercent.Should().Be(500);
    }

    [Fact]
    public void RoundTrip_PieChart_TypePreserved()
    {
        var chart = BuildPieChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().Be(ChartType.Pie);
    }

    [Fact]
    public void RoundTrip_PieChart_VaryColorsPreservedInPackageAndModel()
    {
        var chart = BuildPieChart();
        chart.VaryColors = true;
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var varyColors = chartDoc
                .Descendants(ChartNs + "pieChart")
                .Single()
                .Element(ChartNs + "varyColors");
            varyColors.Should().NotBeNull("PowerPoint-authored pie charts use c:varyColors for per-slice fallback colors");
            varyColors!.Attribute("val")!.Value.Should().Be("1");
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.VaryColors.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_PieChart_FirstSliceAnglePreservedInPackageAndModel()
    {
        var chart = BuildPieChart();
        chart.FirstSliceAngleDegrees = 270;
        chart.DataLabels = new ChartDataLabels { ShowPercent = true };
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var pieChart = chartDoc.Descendants(ChartNs + "pieChart").Single();
            pieChart.Element(ChartNs + "firstSliceAng")
                ?.Attribute("val")
                ?.Value
                .Should()
                .Be("270");
            ChartChildIndex(pieChart, "dLbls").Should().BeLessThan(ChartChildIndex(pieChart, "firstSliceAng"));
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().Be(ChartType.Pie);
        rt.FirstSliceAngleDegrees.Should().Be(270);
    }

    [Theory]
    [InlineData(ChartThreeDStyle.Pie, "pie3DChart")]
    [InlineData(ChartThreeDStyle.Line, "line3DChart")]
    [InlineData(ChartThreeDStyle.Area, "area3DChart")]
    [InlineData(ChartThreeDStyle.Column, "bar3DChart")]
    public void RoundTrip_Classic3DChartGroup_PreservedInPackageAndModel(
        ChartThreeDStyle threeDStyle,
        string expectedElementName)
    {
        var chart = threeDStyle switch
        {
            ChartThreeDStyle.Pie => BuildPieChart(),
            ChartThreeDStyle.Line => BuildLineChart(),
            ChartThreeDStyle.Area => BuildAreaChart(),
            _ => BuildColumnChart()
        };
        chart.ThreeDStyle = threeDStyle;

        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            chartDoc.Descendants(ChartNs + expectedElementName)
                .Should()
                .ContainSingle($"{expectedElementName} should survive writer selection");
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ThreeDStyle.Should().Be(threeDStyle);
    }

    [Fact]
    public void RoundTrip_PieChart_AbsentFirstSliceAngleStaysAbsentAndDefault()
    {
        var chart = BuildPieChart();
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            chartDoc.Descendants(ChartNs + "pieChart")
                .Single()
                .Element(ChartNs + "firstSliceAng")
                .Should()
                .BeNull();
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.FirstSliceAngleDegrees.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_LineChart_TypePreserved()
    {
        var chart = BuildLineChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().BeOneOf([ChartType.Line, ChartType.LineMarkers], "line charts round-trip as line variant");
    }

    [Fact]
    public void RoundTrip_LineChart_DropLinesPreservedInPackageAndModel()
    {
        var chart = BuildLineChart();
        chart.ShowDropLines = true;
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var lineChart = chartDoc.Descendants(ChartNs + "lineChart").Single();
            lineChart.Element(ChartNs + "dropLines").Should().NotBeNull();
            ChartChildIndex(lineChart, "dropLines").Should().BeLessThan(ChartChildIndex(lineChart, "axId"));
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ShowDropLines.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_LineChart_UpDownBarsPreservedInPackageAndModel()
    {
        var chart = BuildLineChart();
        chart.Series.Add(new ChartSeries { Name = "Baseline", Values = { 8, 12, 11 } });
        chart.ShowUpDownBars = true;
        chart.UpDownBarGapWidthPercent = 180;
        chart.UpBarFill = new ShapeFill.Solid(new SrgbColor(0x11, 0x22, 0x33));
        chart.DownBarFill = new ShapeFill.Solid(new SrgbColor(0xAA, 0xBB, 0xCC));
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var lineChart = chartDoc.Descendants(ChartNs + "lineChart").Single();
            var bars = lineChart.Element(ChartNs + "upDownBars");
            bars.Should().NotBeNull();
            bars!.Element(ChartNs + "gapWidth")!.Attribute("val")!.Value.Should().Be("180");
            bars.Element(ChartNs + "upBars")!.Element(ChartNs + "spPr")!.Element(DrawingNs + "solidFill")!
                .Element(DrawingNs + "srgbClr")!.Attribute("val")!.Value.Should().Be("112233");
            bars.Element(ChartNs + "downBars")!.Element(ChartNs + "spPr")!.Element(DrawingNs + "solidFill")!
                .Element(DrawingNs + "srgbClr")!.Attribute("val")!.Value.Should().Be("AABBCC");
            ChartChildIndex(lineChart, "upDownBars").Should().BeLessThan(ChartChildIndex(lineChart, "axId"));
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ShowUpDownBars.Should().BeTrue();
        rt.UpDownBarGapWidthPercent.Should().Be(180);
        ((ShapeFill.Solid)rt.UpBarFill!).Color.Resolved.Should().Be(new SrgbColor(0x11, 0x22, 0x33));
        ((ShapeFill.Solid)rt.DownBarFill!).Color.Resolved.Should().Be(new SrgbColor(0xAA, 0xBB, 0xCC));
    }

    [Fact]
    public void RoundTrip_LineChart_PreservesAuthoredSeriesAndPointStyle()
    {
        var chart = BuildLineChart();
        chart.ChartType = ChartType.LineMarkers;
        var series = chart.Series[0];
        series.LineStyle = new ChartLineStyle
        {
            Color = new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33)),
            WidthPt = 2.25,
            Dash = OutlineDash.DashDot
        };
        series.MarkerStyle = new ChartMarkerStyle
        {
            Symbol = ChartMarkerSymbol.Diamond,
            SizePt = 9,
            FillColor = new ThemeAwareColor(new SrgbColor(0xAA, 0xBB, 0xCC)),
            StrokeColor = new ThemeAwareColor(new SrgbColor(0x44, 0x55, 0x66)),
            StrokeWidthPt = 1.5
        };
        series.PointStyles[1] = new ChartPointStyle
        {
            FillColor = new ThemeAwareColor(new SrgbColor(0xEE, 0xDD, 0xCC)),
            StrokeColor = new ThemeAwareColor(new SrgbColor(0x77, 0x88, 0x99)),
            StrokeWidthPt = 3,
            Marker = new ChartMarkerStyle
            {
                Symbol = ChartMarkerSymbol.Square,
                SizePt = 12
            }
        };

        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var lineDash = chartDoc
                .Descendants(ChartNs + "ser")
                .First()
                .Element(ChartNs + "spPr")
                ?.Element(DrawingNs + "ln")
                ?.Element(DrawingNs + "prstDash")
                ?.Attribute("val")
                ?.Value;
            lineDash.Should().Be("dashDot", "authored series line dashes must be emitted in c:ser/c:spPr");
        }

        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        var styled = rt.Series[0];
        styled.LineStyle.Should().NotBeNull();
        styled.LineStyle!.Color!.Resolved.Should().Be(new SrgbColor(0x11, 0x22, 0x33));
        styled.LineStyle.WidthPt.Should().BeApproximately(2.25, 0.001);
        styled.LineStyle.Dash.Should().Be(OutlineDash.DashDot);
        styled.MarkerStyle.Should().NotBeNull();
        styled.MarkerStyle!.Symbol.Should().Be(ChartMarkerSymbol.Diamond);
        styled.MarkerStyle.SizePt.Should().Be(9);
        styled.MarkerStyle.FillColor!.Resolved.Should().Be(new SrgbColor(0xAA, 0xBB, 0xCC));
        styled.MarkerStyle.StrokeColor!.Resolved.Should().Be(new SrgbColor(0x44, 0x55, 0x66));
        styled.MarkerStyle.StrokeWidthPt.Should().BeApproximately(1.5, 0.001);
        styled.PointStyles.Should().ContainKey(1);
        styled.PointStyles[1].FillColor!.Resolved.Should().Be(new SrgbColor(0xEE, 0xDD, 0xCC));
        styled.PointStyles[1].StrokeColor!.Resolved.Should().Be(new SrgbColor(0x77, 0x88, 0x99));
        styled.PointStyles[1].StrokeWidthPt.Should().BeApproximately(3, 0.001);
        styled.PointStyles[1].Marker!.Symbol.Should().Be(ChartMarkerSymbol.Square);
        styled.PointStyles[1].Marker!.SizePt.Should().Be(12);
    }

    [Fact]
    public void RoundTrip_Chart_GradientFills_PreservedForSeriesPointAndMarker()
    {
        var chart = BuildColumnChart();
        var series = chart.Series[0];
        series.Fill = MakeGradient(0x10, 0x20, 0x30, 0xD0, 0xE0, 0xF0, 35);
        series.PointStyles[1] = new ChartPointStyle
        {
            Fill = MakeGradient(0x20, 0x40, 0x60, 0xF0, 0xA0, 0x40, 90)
        };
        series.MarkerStyle = new ChartMarkerStyle
        {
            Symbol = ChartMarkerSymbol.Circle,
            Fill = MakeGradient(0x40, 0x10, 0x80, 0xEE, 0xDD, 0xCC, 120)
        };

        var path = WriteToPptx(BuildPresWithChart(chart));
        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var ser = chartDoc.Descendants(ChartNs + "ser").First();
            ser.Element(ChartNs + "spPr")
                ?.Element(DrawingNs + "gradFill")
                .Should().NotBeNull("series gradient fill must be emitted under c:ser/c:spPr");
            ser.Elements(ChartNs + "dPt")
                .Single(dpt => dpt.Element(ChartNs + "idx")?.Attribute("val")?.Value == "1")
                .Element(ChartNs + "spPr")
                ?.Element(DrawingNs + "gradFill")
                .Should().NotBeNull("point gradient fill must be emitted under c:dPt/c:spPr");
            ser.Element(ChartNs + "marker")
                ?.Element(ChartNs + "spPr")
                ?.Element(DrawingNs + "gradFill")
                .Should().NotBeNull("marker gradient fill must be emitted under c:marker/c:spPr");
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        var rtSeries = rt.Series[0];

        rtSeries.Fill.Should().BeOfType<ShapeFill.Gradient>();
        ((ShapeFill.Gradient)rtSeries.Fill!).AngleDegrees.Should().BeApproximately(35, 0.01);

        rtSeries.PointStyles[1].Fill.Should().BeOfType<ShapeFill.Gradient>();
        ((ShapeFill.Gradient)rtSeries.PointStyles[1].Fill!).AngleDegrees.Should().BeApproximately(90, 0.01);

        rtSeries.MarkerStyle.Should().NotBeNull();
        rtSeries.MarkerStyle!.Fill.Should().BeOfType<ShapeFill.Gradient>();
        ((ShapeFill.Gradient)rtSeries.MarkerStyle.Fill!).AngleDegrees.Should().BeApproximately(120, 0.01);
    }

    [Fact]
    public void RoundTrip_Chart_PatternFills_PreservedForSeriesPointAndMarker()
    {
        var chart = BuildColumnChart();
        var series = chart.Series[0];
        series.Fill = MakePattern("diagStripe", 0x10, 0x20, 0x30, 0xF0, 0xF1, 0xF2);
        series.PointStyles[1] = new ChartPointStyle
        {
            Fill = MakePattern("cross", 0x20, 0x40, 0x60, 0xE0, 0xD0, 0xC0)
        };
        series.MarkerStyle = new ChartMarkerStyle
        {
            Symbol = ChartMarkerSymbol.Circle,
            Fill = MakePattern("pct50", 0x44, 0x55, 0x66, 0xAA, 0xBB, 0xCC)
        };

        var path = WriteToPptx(BuildPresWithChart(chart));
        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            chartDoc.Descendants(DrawingNs + "pattFill").Should().HaveCountGreaterThanOrEqualTo(3);
            chartDoc.Descendants(DrawingNs + "pattFill")
                .Select(e => e.Attribute("prst")?.Value)
                .Should()
                .Contain(new[] { "diagStripe", "cross", "pct50" });
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        var rtSeries = rt.Series[0];

        var seriesPattern = rtSeries.Fill.Should().BeOfType<ShapeFill.Pattern>().Subject;
        seriesPattern.Preset.Should().Be("diagStripe");
        seriesPattern.ForegroundColor.Resolved.Should().Be(new SrgbColor(0x10, 0x20, 0x30));
        seriesPattern.BackgroundColor.Resolved.Should().Be(new SrgbColor(0xF0, 0xF1, 0xF2));

        var pointPattern = rtSeries.PointStyles[1].Fill.Should().BeOfType<ShapeFill.Pattern>().Subject;
        pointPattern.Preset.Should().Be("cross");
        pointPattern.ForegroundColor.Resolved.Should().Be(new SrgbColor(0x20, 0x40, 0x60));
        pointPattern.BackgroundColor.Resolved.Should().Be(new SrgbColor(0xE0, 0xD0, 0xC0));

        rtSeries.MarkerStyle.Should().NotBeNull();
        var markerPattern = rtSeries.MarkerStyle!.Fill.Should().BeOfType<ShapeFill.Pattern>().Subject;
        markerPattern.Preset.Should().Be("pct50");
        markerPattern.ForegroundColor.Resolved.Should().Be(new SrgbColor(0x44, 0x55, 0x66));
        markerPattern.BackgroundColor.Resolved.Should().Be(new SrgbColor(0xAA, 0xBB, 0xCC));
    }

    [Fact]
    public void RoundTrip_TwoCharts_SameSlide()
    {
        var pres = new Presentation();
        var slide = new Slide();

        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "Chart1",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 3000000, ExtentCyEmu = 3000000,
            Chart = BuildColumnChart()
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 2, Name = "Chart2",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 3500000, OffsetYEmu = 0,
            ExtentCxEmu = 3000000, ExtentCyEmu = 3000000,
            Chart = BuildPieChart()
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var charts = reloaded.Slides[0].Shapes
            .Where(s => s.Kind == SlideShapeKind.Chart)
            .ToList();
        charts.Should().HaveCount(2, "both charts survive round-trip");
        charts[0].Chart!.ChartType.Should().Be(ChartType.ColumnClustered);
        charts[1].Chart!.ChartType.Should().Be(ChartType.Pie);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Compositor — chart produces DrawOp.Chart
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compositor_Chart_ProducesChartOp()
    {
        var pres = BuildPresWithChart(BuildColumnChart());
        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        ops.OfType<DrawOp.Chart>().Should().HaveCount(1, "compositor produces one DrawOp.Chart");
    }

    [Fact]
    public void Compositor_Chart_BoundsCorrect()
    {
        var pres = BuildPresWithChart(BuildColumnChart());
        var ops  = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op   = ops.OfType<DrawOp.Chart>().First();

        // 914400 EMU / 9525 = 96 DIP
        op.BoundsDip.X.Should().BeApproximately(96.0, 0.1);
        op.BoundsDip.Y.Should().BeApproximately(48.0, 0.1);
    }

    [Fact]
    public void Compositor_Chart_SeriesColorsResolved()
    {
        var pres = BuildPresWithChart(BuildColumnChart());
        var ops  = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op   = ops.OfType<DrawOp.Chart>().First();

        op.SeriesColors.Should().HaveCount(2, "one color per series");
    }

    [Fact]
    public void Compositor_Chart_ChartShapeReference()
    {
        var chart = BuildColumnChart();
        var pres  = BuildPresWithChart(chart);
        var ops   = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op    = ops.OfType<DrawOp.Chart>().First();

        op.ChartShape.Should().BeSameAs(chart, "compositor passes through the model reference");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Multiple slides with charts
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_TwoSlides_EachWithChart()
    {
        var pres = new Presentation();

        for (int si = 0; si < 2; si++)
        {
            var slide = new Slide();
            slide.Shapes.Add(new SlideShape
            {
                Id = 1, Name = $"Chart_Slide{si}",
                Kind = SlideShapeKind.Chart,
                OffsetXEmu = 914400, OffsetYEmu = 457200,
                ExtentCxEmu = 5486400, ExtentCyEmu = 3657600,
                Chart = BuildColumnChart()
            });
            pres.Slides.Add(slide);
        }

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides.Should().HaveCount(2);
        reloaded.Slides[0].Shapes.Should().Contain(s => s.Kind == SlideShapeKind.Chart);
        reloaded.Slides[1].Shapes.Should().Contain(s => s.Kind == SlideShapeKind.Chart);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. New distinct chart types (Wave 19B)
    // ──────────────────────────────────────────────────────────────────────────

    // ── 5a: Model defaults ──────────────────────────────────────────────────

    [Fact]
    public void ChartShape_DoughnutHolePercent_DefaultIs50()
    {
        new ChartShape().DoughnutHolePercent.Should().Be(50);
    }

    [Fact]
    public void ChartSeries_XValues_DefaultEmpty()
    {
        new ChartSeries().XValues.Should().BeEmpty();
    }

    [Fact]
    public void ChartSeries_BubbleSizes_DefaultEmpty()
    {
        new ChartSeries().BubbleSizes.Should().BeEmpty();
    }

    // ── 5b: Round-trip — doughnut ────────────────────────────────────────────

    [Fact]
    public void RoundTrip_DoughnutChart_TypePreserved()
    {
        var chart = BuildDoughnutChart();
        var pres  = BuildPresWithChart(chart);
        var path  = WriteToPptx(pres);
        var rt    = PptxPackageReader.Read(path).Slides[0].Shapes
                        .First(s => s.Kind == SlideShapeKind.Chart).Chart!;

        rt.ChartType.Should().Be(ChartType.Doughnut, "doughnut round-trips as Doughnut");
    }

    [Fact]
    public void RoundTrip_DoughnutChart_HoleSizePreserved()
    {
        var chart = BuildDoughnutChart(holeSize: 60);
        var pres  = BuildPresWithChart(chart);
        var path  = WriteToPptx(pres);
        var rt    = PptxPackageReader.Read(path).Slides[0].Shapes
                        .First(s => s.Kind == SlideShapeKind.Chart).Chart!;

        rt.DoughnutHolePercent.Should().Be(60, "hole size survives round-trip");
    }

    [Fact]
    public void RoundTrip_DoughnutChart_FirstSliceAnglePreservedInPackageAndModel()
    {
        var chart = BuildDoughnutChart(holeSize: 60);
        chart.FirstSliceAngleDegrees = 45;
        chart.DataLabels = new ChartDataLabels { ShowValue = true };
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var doughnutChart = chartDoc.Descendants(ChartNs + "doughnutChart").Single();
            doughnutChart.Element(ChartNs + "firstSliceAng")
                ?.Attribute("val")
                ?.Value
                .Should()
                .Be("45");
            ChartChildIndex(doughnutChart, "dLbls").Should().BeLessThan(ChartChildIndex(doughnutChart, "firstSliceAng"));
            ChartChildIndex(doughnutChart, "firstSliceAng").Should().BeLessThan(ChartChildIndex(doughnutChart, "holeSize"));
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().Be(ChartType.Doughnut);
        rt.FirstSliceAngleDegrees.Should().Be(45);
        rt.DoughnutHolePercent.Should().Be(60);
    }

    // ── 5c: Round-trip — scatter ─────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ScatterChart_TypePreserved()
    {
        var chart = BuildScatterChart();
        var pres  = BuildPresWithChart(chart);
        var path  = WriteToPptx(pres);
        var rt    = PptxPackageReader.Read(path).Slides[0].Shapes
                        .First(s => s.Kind == SlideShapeKind.Chart).Chart!;

        rt.ChartType.Should().Be(ChartType.Scatter, "scatter round-trips as Scatter");
    }

    [Fact]
    public void RoundTrip_ScatterChart_XValuesPreserved()
    {
        var chart = BuildScatterChart();
        var pres  = BuildPresWithChart(chart);
        var path  = WriteToPptx(pres);
        var rt    = PptxPackageReader.Read(path).Slides[0].Shapes
                        .First(s => s.Kind == SlideShapeKind.Chart).Chart!;

        rt.Series.Should().HaveCount(1);
        rt.Series[0].XValues.Should().HaveCount(3, "X values preserved");
        rt.Series[0].XValues[0].Should().BeApproximately(1.0, 0.01);
        rt.Series[0].Values[0].Should().BeApproximately(10.0, 0.01);
    }

    // ── 5d: Round-trip — radar ───────────────────────────────────────────────

    [Fact]
    public void RoundTrip_RadarChart_TypePreserved()
    {
        var chart = BuildRadarChart();
        var pres  = BuildPresWithChart(chart);
        var path  = WriteToPptx(pres);
        var rt    = PptxPackageReader.Read(path).Slides[0].Shapes
                        .First(s => s.Kind == SlideShapeKind.Chart).Chart!;

        rt.ChartType.Should().Be(ChartType.Radar, "radar round-trips as Radar");
    }

    [Fact]
    public void RoundTrip_RadarChart_CategoriesPreserved()
    {
        var chart = BuildRadarChart();
        var pres  = BuildPresWithChart(chart);
        var path  = WriteToPptx(pres);
        var rt    = PptxPackageReader.Read(path).Slides[0].Shapes
                        .First(s => s.Kind == SlideShapeKind.Chart).Chart!;

        rt.Categories.Should().Equal(new[] { "Speed", "Power", "Agility", "Stamina" });
    }

    // ── 5e: Round-trip — bubble ──────────────────────────────────────────────

    [Fact]
    public void RoundTrip_BubbleChart_TypePreserved()
    {
        var chart = BuildBubbleChart();
        var pres  = BuildPresWithChart(chart);
        var path  = WriteToPptx(pres);
        var rt    = PptxPackageReader.Read(path).Slides[0].Shapes
                        .First(s => s.Kind == SlideShapeKind.Chart).Chart!;

        rt.ChartType.Should().Be(ChartType.Bubble, "bubble round-trips as Bubble");
    }

    [Fact]
    public void RoundTrip_BubbleChart_BubbleSizesPreserved()
    {
        var chart = BuildBubbleChart();
        var pres  = BuildPresWithChart(chart);
        var path  = WriteToPptx(pres);
        var rt    = PptxPackageReader.Read(path).Slides[0].Shapes
                        .First(s => s.Kind == SlideShapeKind.Chart).Chart!;

        rt.Series[0].BubbleSizes.Should().HaveCount(3, "bubble sizes preserved");
        rt.Series[0].BubbleSizes[0].Should().BeApproximately(5.0, 0.01);
    }

    [Fact]
    public void RoundTrip_BubbleChart_SizingMetadataPreserved()
    {
        var chart = BuildBubbleChart();
        chart.BubbleScalePercent = 175;
        chart.BubbleSizeRepresents = BubbleSizeRepresentation.Width;
        chart.ShowNegativeBubbles = true;
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var bubbleChart = chartDoc.Descendants(ChartNs + "bubbleChart").Single();
            bubbleChart.Element(ChartNs + "bubbleScale")?.Attribute("val")?.Value.Should().Be("175");
            bubbleChart.Element(ChartNs + "sizeRepresents")?.Attribute("val")?.Value.Should().Be("w");
            bubbleChart.Element(ChartNs + "showNegBubbles")?.Attribute("val")?.Value.Should().Be("1");
        }

        var rt = PptxPackageReader.Read(path).Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.Chart).Chart!;

        rt.BubbleScalePercent.Should().Be(175);
        rt.BubbleSizeRepresents.Should().Be(BubbleSizeRepresentation.Width);
        rt.ShowNegativeBubbles.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_StockChart_TypePreservedInPackageAndModel()
    {
        var chart = BuildStockChart();
        chart.ShowUpDownBars = true;
        chart.UpDownBarGapWidthPercent = 180;
        chart.UpBarFill = new ShapeFill.Solid(new SrgbColor(0x11, 0x22, 0x33));
        chart.DownBarFill = new ShapeFill.Solid(new SrgbColor(0xAA, 0xBB, 0xCC));
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            chartDoc.Descendants(ChartNs + "stockChart").Should().ContainSingle(
                "stock charts should keep their PowerPoint chart family instead of downgrading to c:lineChart");
            chartDoc.Descendants(ChartNs + "hiLowLines").Should().ContainSingle(
                "new stock charts use the high-low rendering authored by PowerPoint");
            var bars = chartDoc.Descendants(ChartNs + "upDownBars").Should().ContainSingle().Subject;
            bars.Element(ChartNs + "gapWidth")!.Attribute("val")!.Value.Should().Be("180");
            bars.Element(ChartNs + "upBars")!.Element(ChartNs + "spPr")!.Element(DrawingNs + "solidFill")!
                .Element(DrawingNs + "srgbClr")!.Attribute("val")!.Value.Should().Be("112233");
            bars.Element(ChartNs + "downBars")!.Element(ChartNs + "spPr")!.Element(DrawingNs + "solidFill")!
                .Element(DrawingNs + "srgbClr")!.Attribute("val")!.Value.Should().Be("AABBCC");
            chartDoc.Descendants(ChartNs + "lineChart").Should().BeEmpty();
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().Be(ChartType.Stock);
        rt.HasHighLowLines.Should().BeTrue();
        rt.ShowUpDownBars.Should().BeTrue();
        rt.UpDownBarGapWidthPercent.Should().Be(180);
        ((ShapeFill.Solid)rt.UpBarFill!).Color.Resolved.Should().Be(new SrgbColor(0x11, 0x22, 0x33));
        ((ShapeFill.Solid)rt.DownBarFill!).Color.Resolved.Should().Be(new SrgbColor(0xAA, 0xBB, 0xCC));
        rt.Series.Should().HaveCount(4);
    }

    [Fact]
    public void RoundTrip_FunnelChart_TypeAndStagesPreservedInPackageAndModel()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Funnel,
            Categories = { "Awareness", "Interest", "Conversion" }
        };
        var series = new ChartSeries { Name = "Value" };
        series.Values.AddRange(new double?[] { 100, 60, 18 });
        chart.Series.Add(series);
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            chartDoc.Descendants(ChartNs + "funnelChart").Should().ContainSingle();
            chartDoc.Descendants(ChartNs + "catAx").Should().BeEmpty();
        }

        var rt = PptxPackageReader.Read(path).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().Be(ChartType.Funnel);
        rt.Categories.Should().Equal("Awareness", "Interest", "Conversion");
        rt.Series.Should().ContainSingle();
        rt.Series[0].Values.Should().Equal(100, 60, 18);
    }

    [Fact]
    public void RoundTrip_WaterfallChart_TypeAndIncrementsPreservedInPackageAndModel()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Waterfall,
            Categories = { "Start", "Reduction", "Growth" }
        };
        var series = new ChartSeries { Name = "Value" };
        series.Values.AddRange(new double?[] { 100, -30, 20 });
        chart.Series.Add(series);
        chart.ShowWaterfallConnectorLines = false;
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            chartDoc.Descendants(ChartNs + "waterfallChart").Should().ContainSingle();
            chartDoc.Descendants(ChartNs + "showConnectorLines").Single()
                .Attribute("val")!.Value.Should().Be("0");
            chartDoc.Descendants(ChartNs + "catAx").Should().ContainSingle();
        }

        var rt = PptxPackageReader.Read(path).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().Be(ChartType.Waterfall);
        rt.ShowWaterfallConnectorLines.Should().BeFalse();
        rt.Categories.Should().Equal("Start", "Reduction", "Growth");
        rt.Series.Should().ContainSingle();
        rt.Series[0].Values.Should().Equal(100, -30, 20);
    }

    [Theory]
    [InlineData(ChartType.Surface, "surfaceChart")]
    [InlineData(ChartType.Surface3D, "surface3DChart")]
    public void RoundTrip_SurfaceChart_TypePreservedInPackageAndModel(
        ChartType chartType,
        string expectedElementName)
    {
        var chart = BuildSurfaceChart(chartType);
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var surfaceChart = chartDoc.Descendants(ChartNs + expectedElementName).Should().ContainSingle(
                "surface charts should keep their PowerPoint chart family instead of downgrading to c:barChart").Subject;
            surfaceChart.Elements(ChartNs + "axId").Should().HaveCount(3);
            chartDoc.Descendants(ChartNs + "serAx").Should().ContainSingle(
                "surface charts need a series axis in addition to category and value axes");
            chartDoc.Descendants(ChartNs + "barChart").Should().BeEmpty();
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().Be(chartType);
        rt.Series.Should().HaveCount(2);
    }

    [Fact]
    public void RoundTrip_Surface3D_WireframeFalsePreservesExplicitTokenAndModel()
    {
        var chart = BuildSurfaceChart(ChartType.Surface3D);
        chart.WireframeSpecified = true;
        chart.Wireframe = false;
        var path = WriteToPptx(BuildPresWithChart(chart));

        using (var archive = ZipFile.OpenRead(path))
        {
            var chartDoc = LoadChartXml(archive, chartIndex: 1);
            var wireframe = chartDoc.Descendants(ChartNs + "surface3DChart")
                .Single()
                .Element(ChartNs + "wireframe");
            wireframe.Should().NotBeNull();
            wireframe!.Attribute("val")!.Value.Should().Be("0");
        }

        var reloaded = PptxPackageReader.Read(path);
        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.WireframeSpecified.Should().BeTrue();
        rt.Wireframe.Should().BeFalse();
    }

    // ── 5f: Compositor emits correct type ────────────────────────────────────

    [Fact]
    public void Compositor_DoughnutChart_ProducesChartOpWithDoughnutType()
    {
        var pres = BuildPresWithChart(BuildDoughnutChart());
        var ops  = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op   = ops.OfType<DrawOp.Chart>().First();

        op.ChartShape.ChartType.Should().Be(ChartType.Doughnut);
    }

    [Fact]
    public void Compositor_ScatterChart_ProducesChartOpWithScatterType()
    {
        var pres = BuildPresWithChart(BuildScatterChart());
        var ops  = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op   = ops.OfType<DrawOp.Chart>().First();

        op.ChartShape.ChartType.Should().Be(ChartType.Scatter);
    }

    [Fact]
    public void Compositor_RadarChart_ProducesChartOpWithRadarType()
    {
        var pres = BuildPresWithChart(BuildRadarChart());
        var ops  = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op   = ops.OfType<DrawOp.Chart>().First();

        op.ChartShape.ChartType.Should().Be(ChartType.Radar);
    }

    [Fact]
    public void Compositor_BubbleChart_ProducesChartOpWithBubbleType()
    {
        var pres = BuildPresWithChart(BuildBubbleChart());
        var ops  = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op   = ops.OfType<DrawOp.Chart>().First();

        op.ChartShape.ChartType.Should().Be(ChartType.Bubble);
    }

    [Theory]
    [InlineData(ChartType.Stock)]
    [InlineData(ChartType.Surface)]
    [InlineData(ChartType.Surface3D)]
    public void Compositor_StockAndSurfaceCharts_ProduceChartOpWithModeledType(ChartType chartType)
    {
        var chart = chartType == ChartType.Stock
            ? BuildStockChart()
            : BuildSurfaceChart(chartType);
        var pres = BuildPresWithChart(chart);
        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op = ops.OfType<DrawOp.Chart>().First();

        op.ChartShape.ChartType.Should().Be(chartType);
    }

    // ── 5g: Geometry helpers (pure-math, no renderer) ────────────────────────

    [Fact]
    public void Doughnut_SliceAngles_SumTo2Pi()
    {
        // All slices in a doughnut must sum to a full circle.
        var values = new double[] { 10, 20, 30, 40 };
        double total = values.Sum();
        double sumAngles = values.Sum(v => v / total * 2 * Math.PI);
        sumAngles.Should().BeApproximately(2 * Math.PI, 1e-9);
    }

    [Fact]
    public void Scatter_PointMapping_CorrectNormalization()
    {
        // Point at x=5, range=[0,10] → 50% across plot of width=200
        double xMin = 0, xMax = 10, plotW = 200;
        double px = (5.0 - xMin) / (xMax - xMin) * plotW;
        px.Should().BeApproximately(100.0, 0.001);
    }

    [Fact]
    public void Radar_SpokeAngle_CorrectForCategoryIndex()
    {
        // Category 0 = top (−90°), category 1 rotates by 360°/N
        int N = 4;
        double angle0 = -Math.PI / 2 + 2 * Math.PI * 0 / N;
        double angle1 = -Math.PI / 2 + 2 * Math.PI * 1 / N;
        angle0.Should().BeApproximately(-Math.PI / 2, 1e-9, "first spoke at top");
        angle1.Should().BeApproximately(0, 1e-9, "second spoke 90° clockwise");
    }

    [Fact]
    public void Bubble_Radius_ScalesWithSquareRoot()
    {
        // Bubble radius = sqrt(size/maxSize) * maxRadius
        double maxBubble = 100;
        double maxRadius = 40;
        double r50 = Math.Sqrt(50.0 / maxBubble) * maxRadius;
        double r100 = Math.Sqrt(100.0 / maxBubble) * maxRadius;
        r100.Should().BeApproximately(maxRadius, 0.001, "full-size bubble at maxRadius");
        r50.Should().BeLessThan(r100, "smaller size → smaller radius");
        r50.Should().BeApproximately(Math.Sqrt(0.5) * maxRadius, 0.001);
    }

    /// <summary>
    /// BV3: PowerPoint draws series 0 as the INNERMOST ring (nearest the hole), later series
    /// outward.  The old formula rOut - si*(ringW+ringGap) produced si=0 as outermost — reversed.
    /// The corrected formula is: innerR = rIn + si*(ringW+ringGap), outerR = innerR + ringW.
    /// This test verifies the math: si=0 must produce the smallest outerR and the largest si
    /// must produce the largest outerR.
    /// </summary>
    [Fact]
    public void BV3_Doughnut_MultiSeries_RingOrder_Series0IsInnermost()
    {
        // Reproduce the ring-radius arithmetic from RenderDoughnutChart.
        int serCount = 3;
        double rOut  = 100.0;
        double rIn   = 40.0;   // 40% hole
        double ringGap = rOut * 0.04;
        double ringW   = (rOut - rIn - (serCount - 1) * ringGap) / serCount;

        // Corrected formula: si=0 → innermost, si=serCount-1 → outermost
        double[] outerRadii = new double[serCount];
        for (int si = 0; si < serCount; si++)
        {
            double innerR = rIn + si * (ringW + ringGap);
            outerRadii[si] = innerR + ringW;
        }

        // series 0 must be smallest (innermost), series 2 must be largest (outermost)
        outerRadii[0].Should().BeLessThan(outerRadii[1],
            "BV3: series 0 must be the innermost ring (smaller outerR than series 1)");
        outerRadii[1].Should().BeLessThan(outerRadii[2],
            "BV3: series 1 must be inside series 2");
        outerRadii[serCount - 1].Should().BeApproximately(rOut, 1e-9,
            "the outermost ring of the last series must reach rOut");
    }

    // ── ID1/ID2: regenerated-workbook column layout + c:f formula ranges ────

    [Fact]
    public void RegeneratedWorkbook_ScatterChart_WritesXAndYColumns()
    {
        var chart = BuildScatterChart();
        chart.RegenerateWorkbookOnSave = true;
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);

        using var archive = ZipFile.OpenRead(path);
        var sheetXml = LoadWorkbookSheetXml(archive, chartIndex: 1);

        // X values (1, 2, 3) must appear — not an empty column A.
        sheetXml.Should().Contain(">1<");
        sheetXml.Should().Contain(">2<");
        sheetXml.Should().Contain(">3<");
        // Y values (10, 20, 15) must also appear.
        sheetXml.Should().Contain(">10<");
        sheetXml.Should().Contain(">20<");
        sheetXml.Should().Contain(">15<");
    }

    [Fact]
    public void RegeneratedWorkbook_BubbleChart_WritesXYAndSizeColumns()
    {
        var chart = BuildBubbleChart();
        chart.RegenerateWorkbookOnSave = true;
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);

        using var archive = ZipFile.OpenRead(path);
        var sheetDoc = LoadWorkbookSheetDoc(archive, chartIndex: 1);
        var cellValues = ExtractCellValues(sheetDoc);

        // X values (1, 3, 5), Y values (2, 4, 1), sizes (5, 15, 10) must all be present.
        foreach (var expected in new[] { "1", "3", "5", "2", "4", "10", "15" })
            cellValues.Should().Contain(expected, $"expected value '{expected}' to appear in the regenerated bubble workbook");

        // Three distinct columns are used for the single series (X, Y, size).
        var columns = cellValues.Count == 0
            ? new HashSet<string>()
            : sheetDoc.Descendants().Where(e => e.Name.LocalName == "c")
                .Select(c => new string(c.Attribute("r")!.Value.TakeWhile(char.IsLetter).ToArray()))
                .ToHashSet();
        columns.Should().HaveCount(3, "bubble chart with one series should use 3 columns (X, Y, size)");
    }

    [Fact]
    public void RegeneratedWorkbook_CategoryChart_UnchangedLayout()
    {
        // Regression: category charts must keep categories in col A and series values in cols B+.
        var chart = BuildColumnChart();
        chart.RegenerateWorkbookOnSave = true;
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);

        using var archive = ZipFile.OpenRead(path);
        var sheetDoc = LoadWorkbookSheetDoc(archive, chartIndex: 1);

        var rowTwo = sheetDoc.Descendants().First(e => e.Name.LocalName == "row" && e.Attribute("r")?.Value == "2");
        var cellA2 = rowTwo.Elements().First(c => c.Attribute("r")?.Value == "A2");
        cellA2.Attribute("t")?.Value.Should().Be("inlineStr", "col A must still hold the category label");
        cellA2.Descendants().First(e => e.Name.LocalName == "t").Value.Should().Be("Q1");
    }

    [Fact]
    public void RegeneratedChart_CategorySeries_HasCFormulaRanges()
    {
        var chart = BuildColumnChart(); // 2 series, 3 categories
        chart.RegenerateWorkbookOnSave = true;
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);

        using var archive = ZipFile.OpenRead(path);
        var chartDoc = LoadChartXml(archive, chartIndex: 1);

        var series = chartDoc.Descendants(ChartNs + "ser").ToList();
        series.Should().HaveCount(2);

        foreach (var ser in series)
        {
            var catF = ser.Element(ChartNs + "cat")?.Element(ChartNs + "strRef")?.Element(ChartNs + "f");
            catF.Should().NotBeNull("c:cat/c:strRef requires a c:f formula range");
            catF!.Value.Should().Be("ChartData!$A$2:$A$4");

            var valF = ser.Element(ChartNs + "val")?.Element(ChartNs + "numRef")?.Element(ChartNs + "f");
            valF.Should().NotBeNull("c:val/c:numRef requires a c:f formula range");
        }

        // Series 0 -> col B, Series 1 -> col C.
        series[0].Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("ChartData!$B$2:$B$4");
        series[1].Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("ChartData!$C$2:$C$4");
    }

    [Fact]
    public void RegeneratedChart_ScatterSeries_HasXValYValFormulaRanges()
    {
        var chart = BuildScatterChart(); // 1 series, 3 points
        chart.RegenerateWorkbookOnSave = true;
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);

        using var archive = ZipFile.OpenRead(path);
        var chartDoc = LoadChartXml(archive, chartIndex: 1);
        var ser = chartDoc.Descendants(ChartNs + "ser").Single();

        var xValF = ser.Element(ChartNs + "xVal")?.Element(ChartNs + "numRef")?.Element(ChartNs + "f");
        var yValF = ser.Element(ChartNs + "yVal")?.Element(ChartNs + "numRef")?.Element(ChartNs + "f");
        xValF.Should().NotBeNull("c:xVal/c:numRef requires a c:f formula range");
        yValF.Should().NotBeNull("c:yVal/c:numRef requires a c:f formula range");

        // X in col A, Y in col B for the single series (matches the workbook layout).
        xValF!.Value.Should().Be("ChartData!$A$2:$A$4");
        yValF!.Value.Should().Be("ChartData!$B$2:$B$4");
    }

    [Fact]
    public void RegeneratedChart_BubbleSeries_HasBubbleSizeFormulaRange()
    {
        var chart = BuildBubbleChart(); // 1 series, 3 points
        chart.RegenerateWorkbookOnSave = true;
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);

        using var archive = ZipFile.OpenRead(path);
        var chartDoc = LoadChartXml(archive, chartIndex: 1);
        var ser = chartDoc.Descendants(ChartNs + "ser").Single();

        var sizeF = ser.Element(ChartNs + "bubbleSize")?.Element(ChartNs + "numRef")?.Element(ChartNs + "f");
        sizeF.Should().NotBeNull("c:bubbleSize/c:numRef requires a c:f formula range");
        sizeF!.Value.Should().Be("ChartData!$C$2:$C$4");
    }

    [Fact]
    public void PreservedChart_DoesNotFabricateCFormulaRange()
    {
        // A chart that is NOT flagged for regeneration (no packageSnapshot either, e.g. a
        // freshly-authored chart with cached data only) must not get a c:f pointing at a
        // workbook that was never written.
        var chart = BuildColumnChart();
        chart.RegenerateWorkbookOnSave.Should().BeFalse("default/preserved charts must not regenerate");
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);

        using var archive = ZipFile.OpenRead(path);
        var chartDoc = LoadChartXml(archive, chartIndex: 1);
        var ser = chartDoc.Descendants(ChartNs + "ser").First();

        ser.Element(ChartNs + "val")?.Element(ChartNs + "numRef")?.Element(ChartNs + "f")
            .Should().BeNull("without a regenerated workbook there is nothing for a fabricated c:f to address");

        archive.Entries.Should().NotContain(e => e.FullName.StartsWith("ppt/embeddings/"),
            "no workbook should be written when RegenerateWorkbookOnSave is false and there's no source snapshot");
    }

    [Fact]
    public void PreservedChart_WritesModeledSourceFormulaRanges()
    {
        var chart = BuildColumnChart();
        chart.RegenerateWorkbookOnSave.Should().BeFalse();
        chart.Series[0].FormulaReferences.SeriesName = "Sheet1!$B$1";
        chart.Series[0].FormulaReferences.Category = "Sheet1!$A$2:$A$4";
        chart.Series[0].FormulaReferences.Values = "Sheet1!$B$2:$B$4";
        chart.Series[1].FormulaReferences.SeriesName = "Sheet1!$C$1";
        chart.Series[1].FormulaReferences.Category = "Sheet1!$A$2:$A$4";
        chart.Series[1].FormulaReferences.Values = "Sheet1!$C$2:$C$4";
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);

        using var archive = ZipFile.OpenRead(path);
        var chartDoc = LoadChartXml(archive, chartIndex: 1);
        var series = chartDoc.Descendants(ChartNs + "ser").ToList();

        series[0].Element(ChartNs + "tx")!.Element(ChartNs + "strRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("Sheet1!$B$1");
        series[0].Element(ChartNs + "cat")!.Element(ChartNs + "strRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("Sheet1!$A$2:$A$4");
        series[0].Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("Sheet1!$B$2:$B$4");
        series[1].Element(ChartNs + "tx")!.Element(ChartNs + "strRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("Sheet1!$C$1");
        series[1].Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("Sheet1!$C$2:$C$4");

        archive.Entries.Should().NotContain(e => e.FullName.StartsWith("ppt/embeddings/chartWorkbook"),
            "preserved formula references alone must not trigger workbook regeneration");
    }

    [Theory]
    [InlineData("06-charts.pptx")]
    [InlineData("18-chart-types.pptx")]
    [InlineData("19-chart-labels.pptx")]
    [InlineData("22-chart-baseline-depth.pptx")]
    public void RenderCompareChartCorpus_ImportsWorkbookFormulaReferences(string deckName)
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), deckName);
        var sourceFormulaCount = CountNonEmptyChartFormulas(deckPath);
        var presentation = PptxPackageReader.Read(deckPath);
        var chartFormulaReferences = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .SelectMany(shape => shape.Chart!.Series)
            .Select(series => series.FormulaReferences)
            .ToArray();

        chartFormulaReferences.Should().NotBeEmpty($"{deckName} should contain chart series");
        if (sourceFormulaCount == 0)
        {
            chartFormulaReferences.Should().OnlyContain(reference => !reference.HasAny,
                $"{deckName} only contains blank c:f placeholders and should not fabricate formulas");
            return;
        }

        chartFormulaReferences.Should().Contain(reference => reference.SeriesName != null,
            $"{deckName} should expose authored workbook formulas for series names");
        chartFormulaReferences.Should().Contain(reference =>
                reference.Values != null ||
                reference.YValues != null ||
                reference.BubbleSizes != null,
            $"{deckName} should expose authored workbook formulas for chart values");
    }

    // ── ID1/ID2 helpers ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("06-charts.pptx")]
    [InlineData("18-chart-types.pptx")]
    [InlineData("19-chart-labels.pptx")]
    [InlineData("22-chart-baseline-depth.pptx")]
    public void RenderCompareChartCorpus_ImportsVaryColorsDecision(string deckName)
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), deckName);
        var sourceVaryColors = ReadChartVaryColorsValues(deckPath);
        var imported = PptxPackageReader.Read(deckPath)
            .Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!.VaryColors)
            .ToArray();

        sourceVaryColors.Should().NotBeEmpty($"{deckName} should contain chart type varyColors decisions");
        imported.Should().NotBeEmpty($"{deckName} should import chart shapes");
        if (sourceVaryColors.Contains(true))
            imported.Should().Contain(true, $"{deckName} should expose authored c:varyColors val=1");
        if (sourceVaryColors.Contains(false))
            imported.Should().Contain(false, $"{deckName} should expose authored c:varyColors val=0/default");
    }

    [Fact]
    public void RenderCompareChartCorpus_ImportsDefaultTextStyleAndComboColorsBySeriesIndex()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "19-chart-labels.pptx");
        var charts = PptxPackageReader.Read(deckPath)
            .Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .ToArray();

        var textStyles = charts.Select(chart => chart.TextStyle).ToArray();
        textStyles.Should().NotContainNulls();
        textStyles.Cast<ChartTextStyle>().Select(style => style.FontSizePt)
            .Should().OnlyContain(fontSize => fontSize == 18.0);
        var combo = charts.Should().ContainSingle(chart =>
            chart.Series.Any(series => series.Name == "Units")).Subject;
        combo.Series.Select(series => series.FillColor!.SchemeColor!.Slot).Should().Equal(
            ThemeColorSlot.Accent1,
            ThemeColorSlot.Accent3,
            ThemeColorSlot.Accent2);
    }

    private static readonly XNamespace ChartNs =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace SheetNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XDocument LoadChartXml(ZipArchive archive, int chartIndex)
    {
        var entry = archive.GetEntry($"ppt/charts/chart{chartIndex}.xml")
            ?? throw new FileNotFoundException($"chart{chartIndex}.xml not found");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void RewriteChartXml(string packagePath, int chartIndex, Action<XDocument> rewrite)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update);
        var entry = archive.GetEntry($"ppt/charts/chart{chartIndex}.xml")
            ?? throw new FileNotFoundException($"chart{chartIndex}.xml not found");
        XDocument chartDoc;
        using (var stream = entry.Open())
        {
            chartDoc = XDocument.Load(stream);
        }

        rewrite(chartDoc);
        entry.Delete();
        var replacement = archive.CreateEntry($"ppt/charts/chart{chartIndex}.xml");
        using var replacementStream = replacement.Open();
        chartDoc.Save(replacementStream);
    }

    private static int ChartChildIndex(XElement chartElement, string localName)
    {
        var children = chartElement.Elements().ToList();
        var index = children.FindIndex(element => element.Name == ChartNs + localName);
        index.Should().BeGreaterThanOrEqualTo(0, $"{localName} should be present in {chartElement.Name.LocalName}");
        return index;
    }

    private static XDocument LoadWorkbookSheetDoc(ZipArchive archive, int chartIndex)
    {
        var workbookEntry = archive.GetEntry($"ppt/embeddings/chartWorkbook{chartIndex}.xlsx")
            ?? throw new FileNotFoundException($"chartWorkbook{chartIndex}.xlsx not found");
        using var workbookStream = workbookEntry.Open();
        using var workbookMemory = new MemoryStream();
        workbookStream.CopyTo(workbookMemory);
        workbookMemory.Position = 0;

        using var workbookArchive = new ZipArchive(workbookMemory, ZipArchiveMode.Read);
        var sheetEntry = workbookArchive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new FileNotFoundException("xl/worksheets/sheet1.xml not found in embedded workbook");
        using var sheetStream = sheetEntry.Open();
        return XDocument.Load(sheetStream);
    }

    private static string LoadWorkbookSheetXml(ZipArchive archive, int chartIndex) =>
        LoadWorkbookSheetDoc(archive, chartIndex).ToString(SaveOptions.DisableFormatting);

    private static List<string> ExtractCellValues(XDocument sheetDoc) =>
        sheetDoc.Descendants(SheetNs + "v").Select(v => v.Value).ToList();

    private static string FindCorpusDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "FreeP.RenderCompare", "corpus");
            if (File.Exists(Path.Combine(candidate, "06-charts.pptx")) &&
                File.Exists(Path.Combine(candidate, "18-chart-types.pptx")) &&
                File.Exists(Path.Combine(candidate, "19-chart-labels.pptx")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate tools/FreeP.RenderCompare/corpus chart decks.");
    }

    private static int CountNonEmptyChartFormulas(string deckPath)
    {
        using var archive = ZipFile.OpenRead(deckPath);
        return archive.Entries
            .Where(entry => entry.FullName.StartsWith("ppt/charts/chart", StringComparison.OrdinalIgnoreCase) &&
                            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .SelectMany(entry =>
            {
                using var stream = entry.Open();
                return XDocument.Load(stream)
                    .Descendants(ChartNs + "f")
                    .Select(formula => formula.Value)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
            })
            .Count();
    }

    // ── Helpers for new chart types ──────────────────────────────────────────

    private static bool[] ReadChartVaryColorsValues(string deckPath)
    {
        using var archive = ZipFile.OpenRead(deckPath);
        return archive.Entries
            .Where(entry => entry.FullName.StartsWith("ppt/charts/chart", StringComparison.OrdinalIgnoreCase) &&
                            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .SelectMany(entry =>
            {
                using var stream = entry.Open();
                return XDocument.Load(stream)
                    .Descendants(ChartNs + "varyColors")
                    .Select(element => element.Attribute("val")?.Value is "1" or "true")
                    .ToArray();
            })
            .ToArray();
    }

    private static ChartShape BuildDoughnutChart(int holeSize = 50)
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Doughnut,
            DoughnutHolePercent = holeSize
        };
        chart.Categories.AddRange(new[] { "Alpha", "Beta", "Gamma" });
        var s = new ChartSeries { Name = "Shares" };
        s.Values.AddRange(new double?[] { 40, 35, 25 });
        chart.Series.Add(s);
        return chart;
    }

    private static ChartShape BuildScatterChart()
    {
        var chart = new ChartShape
        {
            ChartType    = ChartType.Scatter,
            ScatterStyle = ScatterStyle.LineMarker
        };
        var s = new ChartSeries { Name = "Data" };
        s.XValues.AddRange(new double?[] { 1, 2, 3 });
        s.Values.AddRange(new double?[]  { 10, 20, 15 });
        chart.Series.Add(s);
        return chart;
    }

    private static ChartShape BuildRadarChart()
    {
        var chart = new ChartShape
        {
            ChartType   = ChartType.Radar,
            RadarStyle  = RadarStyle.Marker
        };
        chart.Categories.AddRange(new[] { "Speed", "Power", "Agility", "Stamina" });
        var s = new ChartSeries { Name = "Character A" };
        s.Values.AddRange(new double?[] { 80, 60, 90, 70 });
        chart.Series.Add(s);
        return chart;
    }

    private static ChartShape BuildBubbleChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Bubble };
        var s = new ChartSeries { Name = "Bubbles" };
        s.XValues.AddRange(new double?[]     { 1, 3, 5 });
        s.Values.AddRange(new double?[]      { 2, 4, 1 });
        s.BubbleSizes.AddRange(new double?[] { 5, 15, 10 });
        chart.Series.Add(s);
        return chart;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static ChartShape BuildStockChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Stock };
        chart.Categories.AddRange(new[] { "Day 1", "Day 2", "Day 3" });
        foreach (var (name, values) in new[]
        {
            ("Open", new double?[] { 10, 12, 11 }),
            ("High", new double?[] { 14, 16, 15 }),
            ("Low", new double?[] { 8, 9, 10 }),
            ("Close", new double?[] { 13, 11, 14 })
        })
        {
            var series = new ChartSeries { Name = name };
            series.Values.AddRange(values);
            chart.Series.Add(series);
        }

        return chart;
    }

    private static ChartShape BuildSurfaceChart(ChartType chartType)
    {
        if (chartType is not (ChartType.Surface or ChartType.Surface3D))
            throw new ArgumentOutOfRangeException(nameof(chartType), chartType, "Expected a surface chart type.");

        var chart = new ChartShape { ChartType = chartType };
        chart.Categories.AddRange(new[] { "North", "East", "South" });

        var low = new ChartSeries { Name = "Low Band" };
        low.Values.AddRange(new double?[] { 10, 20, 15 });
        chart.Series.Add(low);

        var high = new ChartSeries { Name = "High Band" };
        high.Values.AddRange(new double?[] { 30, 25, 35 });
        chart.Series.Add(high);

        return chart;
    }

    private static ChartShape BuildColumnChart()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });

        var s1 = new ChartSeries { Name = "Sales" };
        s1.Values.AddRange(new double?[] { 100, 200, 150 });
        chart.Series.Add(s1);

        var s2 = new ChartSeries { Name = "Budget" };
        s2.Values.AddRange(new double?[] { 120, 180, 160 });
        chart.Series.Add(s2);

        return chart;
    }

    private static ChartShape BuildPieChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Pie };
        chart.Categories.AddRange(new[] { "Alpha", "Beta", "Gamma" });

        var s = new ChartSeries { Name = "Share" };
        s.Values.AddRange(new double?[] { 40, 35, 25 });
        chart.Series.Add(s);

        return chart;
    }

    private static ChartShape BuildLineChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Line };
        chart.Categories.AddRange(new[] { "Jan", "Feb", "Mar" });

        var s = new ChartSeries { Name = "Trend" };
        s.Values.AddRange(new double?[] { 10, 20, 15 });
        chart.Series.Add(s);

        return chart;
    }

    private static ChartShape BuildAreaChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Area };
        chart.Categories.AddRange(new[] { "Jan", "Feb", "Mar" });

        var s = new ChartSeries { Name = "Trend" };
        s.Values.AddRange(new double?[] { 10, 20, 15 });
        chart.Series.Add(s);

        return chart;
    }

    private static ShapeFill.Gradient MakeGradient(
        byte startR,
        byte startG,
        byte startB,
        byte endR,
        byte endG,
        byte endB,
        double angleDegrees) =>
        new(
            new[]
            {
                new GradientStop(0.0, new ThemeAwareColor(new SrgbColor(startR, startG, startB))),
                new GradientStop(1.0, new ThemeAwareColor(new SrgbColor(endR, endG, endB)))
            },
            GradientKind.Linear,
            angleDegrees);

    private static ShapeFill.Pattern MakePattern(
        string preset,
        byte fgR,
        byte fgG,
        byte fgB,
        byte bgR,
        byte bgG,
        byte bgB) =>
        new(
            preset,
            new ThemeAwareColor(new SrgbColor(fgR, fgG, fgB)),
            new ThemeAwareColor(new SrgbColor(bgR, bgG, bgB)));

    private static Presentation BuildPresWithChart(ChartShape chart)
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "MyChart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 3657600,
            Chart = chart
        });
        pres.Slides.Add(slide);
        return pres;
    }

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }
}
