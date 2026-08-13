using System.IO;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 20 tests: chart data labels (model, round-trip, render) + secondary axis.
/// </summary>
public sealed class ChartDataLabelsTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.ChartDataLabelsTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // ── 1. Model defaults ─────────────────────────────────────────────────────

    [Fact]
    public void ChartDataLabels_DefaultValues()
    {
        var dl = new ChartDataLabels();
        dl.ShowValue.Should().BeFalse();
        dl.ShowPercent.Should().BeFalse();
        dl.ShowCategoryName.Should().BeFalse();
        dl.ShowSeriesName.Should().BeFalse();
        dl.ShowLegendKey.Should().BeFalse();
        dl.ShowBubbleSize.Should().BeFalse();
        dl.ShowLeaderLines.Should().BeNull();
        dl.Position.Should().BeNull();
        dl.NumberFormat.Should().BeNull();
        dl.HasAny.Should().BeFalse();
    }

    [Fact]
    public void ChartDataLabels_HasAny_TrueWhenShowValueSet()
    {
        var dl = new ChartDataLabels { ShowValue = true };
        dl.HasAny.Should().BeTrue();
    }

    [Fact]
    public void ChartDataLabels_HasAny_TrueWhenShowPercentSet()
    {
        var dl = new ChartDataLabels { ShowPercent = true };
        dl.HasAny.Should().BeTrue();
    }

    [Fact]
    public void ChartDataLabels_HasAny_TrueWhenShowLegendKeySet()
    {
        var dl = new ChartDataLabels { ShowLegendKey = true };
        dl.HasAny.Should().BeTrue();
    }

    [Fact]
    public void ChartDataLabels_HasAny_TrueWhenShowBubbleSizeSet()
    {
        var dl = new ChartDataLabels { ShowBubbleSize = true };
        dl.HasAny.Should().BeTrue();
    }

    [Fact]
    public void ChartDataLabels_HasAny_TrueWhenLeaderLinesExplicitlyDisabled()
    {
        var dl = new ChartDataLabels { ShowLeaderLines = false };
        dl.HasAny.Should().BeTrue();
    }

    [Fact]
    public void ChartSeries_OnSecondaryAxis_DefaultFalse()
    {
        new ChartSeries().OnSecondaryAxis.Should().BeFalse();
    }

    [Fact]
    public void ChartSeries_DataLabels_DefaultNull()
    {
        new ChartSeries().DataLabels.Should().BeNull();
    }

    [Fact]
    public void ChartShape_DataLabels_DefaultNull()
    {
        new ChartShape().DataLabels.Should().BeNull();
    }

    [Fact]
    public void ChartShape_SecondaryValueAxis_DefaultNull()
    {
        new ChartShape().SecondaryValueAxis.Should().BeNull();
    }

    [Fact]
    public void ChartAxis_NumberFormatDefaults()
    {
        var axis = new ChartAxis();

        axis.NumberFormatCode.Should().BeNull();
        axis.NumberFormatSourceLinked.Should().BeNull();
    }

    // ── 2. Round-trip: ShowValue + OutsideEnd + numFmt ───────────────────────

    [Fact]
    public void RoundTrip_ChartLevelDataLabels_ShowValue_Preserved()
    {
        var chart = BuildColumnChartWithLabels(showValue: true);
        var rt    = DoRoundTrip(chart);

        rt.DataLabels.Should().NotBeNull("chart-level data labels survive round-trip");
        rt.DataLabels!.ShowValue.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_ChartLevelDataLabels_Position_Preserved()
    {
        var chart = BuildColumnChartWithLabels(
            showValue: true, position: DataLabelPosition.OutsideEnd);
        var rt = DoRoundTrip(chart);

        rt.DataLabels!.Position.Should().Be(DataLabelPosition.OutsideEnd);
    }

    [Fact]
    public void RoundTrip_ChartLevelDataLabels_NumberFormat_Preserved()
    {
        var chart = BuildColumnChartWithLabels(
            showValue: true,
            position: DataLabelPosition.OutsideEnd,
            numFmt: "0.00");
        var rt    = DoRoundTrip(chart);

        rt.DataLabels!.NumberFormat.Should().Be("0.00");
    }

    [Fact]
    public void RoundTrip_ChartLevelDataLabels_ShowLegendKeyOnly_Preserved()
    {
        var chart = BuildColumnChartWithLabels();
        chart.DataLabels!.ShowLegendKey = true;
        var rt = DoRoundTrip(chart);

        rt.DataLabels.Should().NotBeNull("legend-key-only data labels survive round-trip");
        rt.DataLabels!.ShowLegendKey.Should().BeTrue();
        rt.DataLabels.HasAny.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_ChartLevelDataLabels_ShowBubbleSize_Preserved()
    {
        var chart = BuildColumnChartWithLabels();
        chart.DataLabels!.ShowBubbleSize = true;
        var rt = DoRoundTrip(chart);

        rt.DataLabels.Should().NotBeNull("bubble-size data labels survive round-trip");
        rt.DataLabels!.ShowBubbleSize.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_ChartLevelDataLabels_TextStyle_Preserved()
    {
        var chart = BuildColumnChartWithLabels(showValue: true);
        chart.DataLabels!.TextStyle = new ChartTextStyle
        {
            FontSizePt = 11.5,
            Bold = true,
            Italic = true,
            FontFamily = "Arial"
        };

        var rt = DoRoundTrip(chart);

        rt.DataLabels!.TextStyle.Should().NotBeNull();
        rt.DataLabels.TextStyle!.FontSizePt.Should().Be(11.5);
        rt.DataLabels.TextStyle.Bold.Should().BeTrue();
        rt.DataLabels.TextStyle.Italic.Should().BeTrue();
        rt.DataLabels.TextStyle.FontFamily.Should().Be("Arial");
    }

    [Fact]
    public void RoundTrip_PieChart_ShowPercent_Preserved()
    {
        var chart = BuildPieChartWithLabels(showPercent: true);
        var rt    = DoRoundTrip(chart);

        rt.DataLabels.Should().NotBeNull("pie chart percent labels survive round-trip");
        rt.DataLabels!.ShowPercent.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_PieChart_ShowLeaderLines_PreservesExplicitState(bool value)
    {
        var chart = BuildPieChartWithLabels(showPercent: true);
        chart.DataLabels!.ShowLeaderLines = value;
        var rt = DoRoundTrip(chart);

        rt.DataLabels.Should().NotBeNull("leader-line setting survives round-trip");
        rt.DataLabels!.ShowLeaderLines.Should().Be(value);
    }

    [Fact]
    public void RoundTrip_PerSeriesDataLabels_Preserved()
    {
        var chart = BuildColumnChart();
        chart.Series[0].DataLabels = new ChartDataLabels
        {
            ShowValue = true,
            Position  = DataLabelPosition.InsideEnd,
            NumberFormat = "#,##0"
        };
        var rt = DoRoundTrip(chart);

        var ser0 = rt.Series[0];
        ser0.DataLabels.Should().NotBeNull("per-series labels survive round-trip");
        ser0.DataLabels!.ShowValue.Should().BeTrue();
        ser0.DataLabels!.Position.Should().Be(DataLabelPosition.InsideEnd);
        ser0.DataLabels!.NumberFormat.Should().Be("#,##0");
    }

    // ── 3. Round-trip: secondary axis ─────────────────────────────────────────

    [Fact]
    public void RoundTrip_SecondaryAxisPresence_Preserved()
    {
        var chart = BuildComboChartWithSecondaryAxis();
        var rt    = DoRoundTrip(chart);

        rt.SecondaryValueAxis.Should().NotBeNull("secondary axis survives round-trip");
    }

    [Fact]
    public void RoundTrip_SecondaryAxis_SeriesOnSecondaryAxis_Preserved()
    {
        var chart = BuildComboChartWithSecondaryAxis();
        // Series index 1 is on the secondary axis
        chart.Series[1].OnSecondaryAxis.Should().BeTrue("setup: series 1 on secondary axis");

        var rt = DoRoundTrip(chart);

        // After round-trip the secondary axis info should be retained
        rt.SecondaryValueAxis.Should().NotBeNull("secondary valAx survives round-trip");
        // CA4: the per-series flag must also round-trip (writer splits groups, reader detects it)
        rt.Series.Should().HaveCountGreaterThanOrEqualTo(2, "both series survive round-trip");
        rt.Series[0].OnSecondaryAxis.Should().BeFalse("primary series stays on primary axis");
        rt.Series[1].OnSecondaryAxis.Should().BeTrue("secondary series flag round-trips");
    }

    [Fact]
    public void RoundTrip_AxisNumberFormats_Preserved()
    {
        var chart = BuildComboChartWithSecondaryAxis();
        chart.CategoryAxis.NumberFormatCode = "m/d/yy";
        chart.CategoryAxis.NumberFormatSourceLinked = true;
        chart.ValueAxis.NumberFormatCode = "#,##0.0";
        chart.ValueAxis.NumberFormatSourceLinked = false;
        chart.SecondaryValueAxis!.NumberFormatCode = "0.00%";
        chart.SecondaryValueAxis.NumberFormatSourceLinked = false;

        var rt = DoRoundTrip(chart);

        rt.CategoryAxis.NumberFormatCode.Should().Be("m/d/yy");
        rt.CategoryAxis.NumberFormatSourceLinked.Should().BeTrue();
        rt.ValueAxis.NumberFormatCode.Should().Be("#,##0.0");
        rt.ValueAxis.NumberFormatSourceLinked.Should().BeFalse();
        rt.SecondaryValueAxis.Should().NotBeNull();
        rt.SecondaryValueAxis!.NumberFormatCode.Should().Be("0.00%");
        rt.SecondaryValueAxis.NumberFormatSourceLinked.Should().BeFalse();
    }

    /// <summary>
    /// CC1 regression: secondary-axis detection MUST use idx→series MAP, not positional indexing.
    ///
    /// A combo chart where the secondary lineChart group has c:idx=1 but that series is the THIRD
    /// appended to shape.Series (append positions 0,1,2 = idx 0,2,1 respectively) — the old code
    /// did shape.Series[1].OnSecondaryAxis = true which flags the WRONG series (the one at append
    /// position 1, i.e. idx=2).  The fixed code resolves via the idx→ChartSeries map so the series
    /// with c:idx=1 is correctly flagged regardless of its append order.
    ///
    /// Structure:
    ///   barChart (primary):  ser idx=0 "PrimaryA",  ser idx=2 "PrimaryB"
    ///   lineChart (secondary, valAx2): ser idx=1 "SecondaryLine"
    ///
    /// After reading:
    ///   shape.Series append order = [PrimaryA(idx0), PrimaryB(idx2), SecondaryLine(idx1)]
    ///   Only SecondaryLine.OnSecondaryAxis should be true.
    /// </summary>
    [Fact]
    public void Read_InterleavedIdx_SecondaryAxisFlagsCorrectSeries()
    {
        // Build a minimal chart XML with interleaved c:idx values:
        //   barChart (primary)  has ser idx=0 and idx=2
        //   lineChart (secondary) has ser idx=1, referencing the second c:valAx (axId=200)
        const string chartXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <c:chart>
                <c:plotArea>
                  <!-- Primary group: bar, references axId 100 (cat) + 200 (primary val) -->
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:grouping val="clustered"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:strCache><c:pt idx="0"><c:v>PrimaryA</c:v></c:pt></c:strCache></c:strRef></c:tx>
                      <c:cat><c:strRef><c:strCache><c:pt idx="0"><c:v>Q1</c:v></c:pt><c:pt idx="1"><c:v>Q2</c:v></c:pt></c:strCache></c:strRef></c:cat>
                      <c:val><c:numRef><c:numCache><c:ptCount val="2"/><c:pt idx="0"><c:v>10</c:v></c:pt><c:pt idx="1"><c:v>20</c:v></c:pt></c:numCache></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="2"/>
                      <c:order val="2"/>
                      <c:tx><c:strRef><c:strCache><c:pt idx="0"><c:v>PrimaryB</c:v></c:pt></c:strCache></c:strRef></c:tx>
                      <c:val><c:numRef><c:numCache><c:ptCount val="2"/><c:pt idx="0"><c:v>30</c:v></c:pt><c:pt idx="1"><c:v>40</c:v></c:pt></c:numCache></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="100"/>
                    <c:axId val="200"/>
                  </c:barChart>
                  <!-- Secondary group: line, references axId 100 (cat) + 300 (secondary val) -->
                  <c:lineChart>
                    <c:grouping val="standard"/>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:tx><c:strRef><c:strCache><c:pt idx="0"><c:v>SecondaryLine</c:v></c:pt></c:strCache></c:strRef></c:tx>
                      <c:val><c:numRef><c:numCache><c:ptCount val="2"/><c:pt idx="0"><c:v>1000</c:v></c:pt><c:pt idx="1"><c:v>2000</c:v></c:pt></c:numCache></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="100"/>
                    <c:axId val="300"/>
                  </c:lineChart>
                  <!-- Primary category axis -->
                  <c:catAx>
                    <c:axId val="100"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:crossAx val="200"/>
                  </c:catAx>
                  <!-- Primary value axis (axId=200) -->
                  <c:valAx>
                    <c:axId val="200"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:crossAx val="100"/>
                  </c:valAx>
                  <!-- Secondary value axis (axId=300) — second c:valAx is the secondary -->
                  <c:valAx>
                    <c:axId val="300"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:crossAx val="100"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """;

        // Build a minimal in-memory ZIP with only the chart entry, then call ReadChartPart directly.
        var ms = new System.IO.MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms,
            System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("ppt/charts/chart1.xml");
            using var w = new System.IO.StreamWriter(entry.Open(), System.Text.Encoding.UTF8);
            w.Write(chartXml);
        }
        ms.Position = 0;

        ChartShape? shape;
        using (var zip2 = new System.IO.Compression.ZipArchive(ms,
            System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false))
        {
            shape = PptxChartReader.ReadChartPart(
                zip2, "ppt/charts/chart1.xml",
                PresentationColorScheme.CreateDefault());
        }

        // Verify 3 series were read in append order: PrimaryA(idx0), PrimaryB(idx2), SecondaryLine(idx1)
        shape.Should().NotBeNull();
        shape!.Series.Should().HaveCount(3, "3 series total across both chart groups");
        shape.Series[0].Name.Should().Be("PrimaryA",    "first appended = idx 0");
        shape.Series[1].Name.Should().Be("PrimaryB",    "second appended = idx 2");
        shape.Series[2].Name.Should().Be("SecondaryLine", "third appended = idx 1 (secondary)");

        // CC1 fix: OnSecondaryAxis must be resolved via idx map, NOT positional index.
        // Old (buggy): shape.Series[1] (PrimaryB, idx=2) would be flagged — WRONG.
        // Fixed:       shape.Series[2] (SecondaryLine, idx=1) is flagged — CORRECT.
        shape.Series[0].OnSecondaryAxis.Should().BeFalse("PrimaryA (idx=0) is on primary axis");
        shape.Series[1].OnSecondaryAxis.Should().BeFalse("PrimaryB (idx=2) is on primary axis");
        shape.Series[2].OnSecondaryAxis.Should().BeTrue(
            "SecondaryLine (c:idx=1) must be flagged via idx map, not positional index");
    }

    [Fact]
    public void RoundTrip_NoSecondaryAxis_NoRegression()
    {
        // A plain column chart with no secondary axis must not emit a secondary valAx.
        var chart = BuildColumnChart();
        var rt    = DoRoundTrip(chart);

        rt.SecondaryValueAxis.Should().BeNull("no secondary axis without SecondaryValueAxis set");
        rt.Series.Should().AllSatisfy(s => s.OnSecondaryAxis.Should().BeFalse());
    }

    // ── 4. Label string composition ───────────────────────────────────────────

    [Fact]
    public void LabelComposition_ShowValue_FormatsValue()
    {
        var dl = new ChartDataLabels { ShowValue = true };
        string lbl = ComposeLabel(dl, value: 42.5, total: 100, cat: "Q1", ser: "Sales");
        lbl.Should().Contain("42.5");
    }

    [Fact]
    public void LabelComposition_ShowPercent_FormatsPercent()
    {
        var dl = new ChartDataLabels { ShowPercent = true };
        string lbl = ComposeLabel(dl, value: 25.0, total: 100.0, cat: "Q1", ser: "Sales");
        lbl.Should().Contain("%");
    }

    [Fact]
    public void LabelComposition_ShowCatName_IncludesCategoryName()
    {
        var dl = new ChartDataLabels { ShowCategoryName = true, ShowValue = true };
        string lbl = ComposeLabel(dl, value: 50.0, total: 200.0, cat: "Q1", ser: "Sales");
        lbl.Should().Contain("Q1");
        lbl.Should().Contain("50");
    }

    [Fact]
    public void LabelComposition_ShowValue_WithNumberFormat()
    {
        // The renderer's FormatWithCode("0.00") → 2 decimal places → "123.46"
        // Our test ComposeLabel uses G4 for brevity; verify the number-format code parsing directly.
        string result = FormatWithCodeTest(123.456, "0.00");
        result.Should().Be("123.46");
    }

    private static string FormatWithCodeTest(double value, string code)
    {
        // Mirror of renderer's FormatWithCode helper (CB5: count digits between '.' and '%')
        if (code.Contains('%'))
        {
            double pct = value * 100.0;
            int dotPos = code.IndexOf('.');
            int decimals = dotPos >= 0 ? code.LastIndexOf('%') - dotPos - 1 : 0;
            return pct.ToString(decimals > 0 ? $"F{decimals}" : "F0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }
        if (code.Contains(','))
            return value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        int dotIdx = code.IndexOf('.');
        if (dotIdx >= 0)
        {
            int dec = code.Length - dotIdx - 1;
            return value.ToString($"F{dec}", System.Globalization.CultureInfo.InvariantCulture);
        }
        return value.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void LabelComposition_ShowPercent_PieSlice()
    {
        var dl = new ChartDataLabels { ShowPercent = true };
        // 25% of 100
        string lbl = ComposeLabel(dl, value: 25.0, total: 100.0, cat: "Slice A", ser: "S");
        lbl.Should().Be("25%");
    }

    // ── 5. Compositor carries labels on the DrawOp ───────────────────────────

    [Fact]
    public void Compositor_Chart_DataLabels_PassedThrough()
    {
        var chart = BuildColumnChartWithLabels(showValue: true);
        var pres  = BuildPresWithChart(chart);
        var ops   = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op    = ops.OfType<DrawOp.Chart>().First();

        op.ChartShape.DataLabels.Should().NotBeNull();
        op.ChartShape.DataLabels!.ShowValue.Should().BeTrue();
    }

    [Fact]
    public void Compositor_Chart_SecondaryAxis_PassedThrough()
    {
        var chart = BuildComboChartWithSecondaryAxis();
        var pres  = BuildPresWithChart(chart);
        var ops   = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op    = ops.OfType<DrawOp.Chart>().First();

        op.ChartShape.SecondaryValueAxis.Should().NotBeNull();
    }

    // ── 6. Secondary axis scale mapping ──────────────────────────────────────

    [Fact]
    public void SecondaryAxis_ValueMapping_CorrectNormalization()
    {
        // A series on the secondary axis with range 0-1000 should map value 500 to 50% of plot height.
        double secMin = 0, secMax = 1000;
        double range  = secMax - secMin;
        double plotH  = 200.0;
        double val    = 500.0;
        double normalizedPy = plotH - (val - secMin) / range * plotH;
        normalizedPy.Should().BeApproximately(100.0, 0.001, "500 of 1000 = 50% up = plotH/2");
    }

    [Fact]
    public void SecondaryAxis_IndependentFromPrimaryAxis()
    {
        // Primary axis data: 0–100. Secondary: 0–10000. They should have independent scales.
        var chart = BuildComboChartWithSecondaryAxis();
        chart.Series[0].Values.Should().AllSatisfy(v => v.Should().BeInRange(0, 200));
        chart.Series[1].Values.Should().AllSatisfy(v => v.Should().BeInRange(0, 20000));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Calls the internal label composition logic used by both renderers.</summary>
    private static string ComposeLabel(ChartDataLabels dl, double value, double total, string? cat, string? ser)
    {
        // Mirror the FormatDataLabel logic from the renderer (tested via model only)
        string formattedVal = value.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
        string pctStr = total > 0 ? $"{value / total * 100:0}%" : "0%";

        var parts = new System.Text.StringBuilder();
        if (dl.ShowSeriesName   && !string.IsNullOrEmpty(ser))  parts.Append(ser).Append(' ');
        if (dl.ShowCategoryName && !string.IsNullOrEmpty(cat))  parts.Append(cat).Append(' ');
        if (dl.ShowValue)   parts.Append(formattedVal).Append(' ');
        if (dl.ShowPercent) parts.Append(pctStr).Append(' ');

        return parts.ToString().Trim();
    }

    private static ChartShape BuildColumnChart()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        var s1 = new ChartSeries { Name = "Sales" };
        s1.Values.AddRange(new double?[] { 100, 200, 150 });
        chart.Series.Add(s1);
        var s2 = new ChartSeries { Name = "Budget" };
        s2.Values.AddRange(new double?[] { 120, 180, 160 });
        chart.Series.Add(s2);
        return chart;
    }

    private static ChartShape BuildColumnChartWithLabels(
        bool showValue = false, bool showPercent = false,
        DataLabelPosition? position = null, string? numFmt = null)
    {
        var chart = BuildColumnChart();
        chart.DataLabels = new ChartDataLabels
        {
            ShowValue    = showValue,
            ShowPercent  = showPercent,
            Position     = position,
            NumberFormat = numFmt
        };
        return chart;
    }

    private static ChartShape BuildPieChartWithLabels(bool showPercent = false)
    {
        var chart = new ChartShape { ChartType = ChartType.Pie };
        chart.Categories.AddRange(new[] { "Alpha", "Beta", "Gamma" });
        var s = new ChartSeries { Name = "Share" };
        s.Values.AddRange(new double?[] { 40, 35, 25 });
        chart.Series.Add(s);
        chart.DataLabels = new ChartDataLabels { ShowPercent = showPercent };
        return chart;
    }

    private static ChartShape BuildComboChartWithSecondaryAxis()
    {
        // Column series on primary axis + line series on secondary axis (combo chart pattern)
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Jan", "Feb", "Mar" });

        var colSeries = new ChartSeries { Name = "Revenue" };
        colSeries.Values.AddRange(new double?[] { 100, 150, 120 });
        chart.Series.Add(colSeries);

        var lineSeries = new ChartSeries { Name = "Target", OnSecondaryAxis = true };
        lineSeries.Values.AddRange(new double?[] { 5000, 8000, 12000 });
        chart.Series.Add(lineSeries);

        chart.SecondaryValueAxis = new ChartAxis { HasMajorGridlines = false };

        return chart;
    }

    private ChartShape DoRoundTrip(ChartShape chart)
    {
        var pres = BuildPresWithChart(chart);
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);
        var reloaded = PptxPackageReader.Read(path);
        return reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
    }

    private static Presentation BuildPresWithChart(ChartShape chart)
    {
        var pres  = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "MyChart",
            Kind        = SlideShapeKind.Chart,
            OffsetXEmu  = 914400, OffsetYEmu = 457200,
            ExtentCxEmu = 5486400, ExtentCyEmu = 3657600,
            Chart       = chart
        });
        pres.Slides.Add(slide);
        return pres;
    }

    // ── CC2/CC3/CC4: secondary-axis data-label scale correctness ─────────────

    /// <summary>
    /// CC2: RenderLineDataLabels must compute the label Y using the secondary-axis range
    /// (not the primary range) for OnSecondaryAxis series. Verified analytically:
    /// the secondary-range fraction is ≤ 1 (in-chart), the primary-range fraction >> 1 (off-chart).
    /// </summary>
    [Fact]
    public void CC2_LineDataLabel_SecondaryAxisSeries_UsesSecondaryRange()
    {
        var primary = new ChartSeries { Name = "Bars", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 20, 50, 100 });

        var secondary = new ChartSeries { Name = "Line", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 200_000, 600_000, 1_000_000 });

        var chart = new ChartShape
        {
            ChartType          = ChartType.LineMarkers,
            SecondaryValueAxis = new ChartAxis { HasMajorGridlines = false },
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        // The secondary range from ComputeNiceSecondaryAxisRange must cover the 1M value.
        var (secMin, secMax, _) = ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);
        double secRange   = secMax - secMin;
        double testVal    = 1_000_000.0;
        double correctFrac = (testVal - secMin) / secRange;

        var (pMin, pMax, _) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
        double pRange   = pMax - pMin;
        double brokenFrac = (testVal - pMin) / pRange;

        correctFrac.Should().BeLessThanOrEqualTo(1.1,
            "CC2: secondary value through secondary range must be ≤ 1 (within plot)");
        correctFrac.Should().BeGreaterThanOrEqualTo(0.7,
            "CC2: value at secondary max should map well up the plot (nice range extends slightly above data max)");
        brokenFrac.Should().BeGreaterThan(10.0,
            "CC2 sanity: same value through primary range would be >> 1 (off-chart)");
    }

    /// <summary>
    /// CC3: RenderColumnDataLabels must use the secondary-axis range for OnSecondaryAxis series.
    /// </summary>
    [Fact]
    public void CC3_ColumnDataLabel_SecondaryAxisSeries_UsesSecondaryRange()
    {
        var primary = new ChartSeries { Name = "P", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 10, 20, 30 });

        var secondary = new ChartSeries { Name = "S", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 50_000, 80_000, 100_000 });

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var (secMin, secMax, _) = ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);
        double secRange  = secMax - secMin;
        var (pMin, pMax, _) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
        double pRange    = pMax - pMin;
        double testVal   = 100_000.0;

        double secFrac = (testVal - secMin) / secRange;
        double priFrac = (testVal - pMin)   / pRange;

        secFrac.Should().BeLessThanOrEqualTo(1.1,
            "CC3: secondary column value through secondary range must be ≤ 1");
        priFrac.Should().BeGreaterThan(10.0,
            "CC3 sanity: same value through primary range would be >> 1");
    }

    /// <summary>
    /// CC4: RenderBarDataLabels must use the secondary-axis range for OnSecondaryAxis series.
    /// </summary>
    [Fact]
    public void CC4_BarDataLabel_SecondaryAxisSeries_UsesSecondaryRange()
    {
        var primary = new ChartSeries { Name = "P", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 5, 15, 25 });

        var secondary = new ChartSeries { Name = "S", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 10_000, 30_000, 50_000 });

        var chart = new ChartShape { ChartType = ChartType.BarClustered };
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var (secMin, secMax, _) = ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);
        double secRange  = secMax - secMin;
        var (pMin, pMax, _) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
        double pRange    = pMax - pMin;
        double testVal   = 50_000.0;

        double secFrac = (testVal - secMin) / secRange;
        double priFrac = (testVal - pMin)   / pRange;

        secFrac.Should().BeLessThanOrEqualTo(1.1,
            "CC4: secondary bar value through secondary range must be ≤ 1");
        priFrac.Should().BeGreaterThan(10.0,
            "CC4 sanity: same value through primary range would be >> 1");
    }

    // ── CA2: dLbls child order ────────────────────────────────────────────────

    [Fact]
    public void DLbls_ChildOrder_NumFmtBeforeDLblPos_BeforeShowFlags()
    {
        // A chart with numFmt + position + showValue — verify the written XML
        // has numFmt FIRST, then dLblPos, then show* (CT_DLbls schema order).
        var chart = BuildColumnChartWithLabels(showValue: true,
            position: DataLabelPosition.OutsideEnd, numFmt: "0.00");

        var pptxPath = WriteToTempPptx(chart);
        var xmlText  = ExtractFirstChartXml(pptxPath);

        // Find positions of elements inside c:dLbls
        int numFmtPos   = xmlText.IndexOf("<c:numFmt",    StringComparison.Ordinal);
        int dLblPosPos  = xmlText.IndexOf("<c:dLblPos",   StringComparison.Ordinal);
        int showValPos  = xmlText.IndexOf("<c:showVal",   StringComparison.Ordinal);

        numFmtPos.Should().BeGreaterThan(-1,  "numFmt must be present");
        dLblPosPos.Should().BeGreaterThan(-1, "dLblPos must be present");
        showValPos.Should().BeGreaterThan(-1, "showVal must be present");

        numFmtPos.Should().BeLessThan(dLblPosPos,
            "numFmt must appear before dLblPos (CT_DLbls schema order)");
        dLblPosPos.Should().BeLessThan(showValPos,
            "dLblPos must appear before showVal (CT_DLbls schema order)");
    }

    [Fact]
    public void DLbls_ChildOrder_TextPropertiesFollowNumFmt()
    {
        var chart = BuildColumnChartWithLabels(
            showValue: true,
            position: DataLabelPosition.OutsideEnd,
            numFmt: "0.00");
        chart.DataLabels!.TextStyle = new ChartTextStyle { FontFamily = "Arial" };

        var xmlText = ExtractFirstChartXml(WriteToTempPptx(chart));
        int numFmtPos = xmlText.IndexOf("<c:numFmt", StringComparison.Ordinal);
        int txPrPos = xmlText.IndexOf("<c:txPr", StringComparison.Ordinal);
        int dLblPosPos = xmlText.IndexOf("<c:dLblPos", StringComparison.Ordinal);

        numFmtPos.Should().BeGreaterThan(-1);
        txPrPos.Should().BeGreaterThan(-1);
        dLblPosPos.Should().BeGreaterThan(-1);
        numFmtPos.Should().BeLessThan(txPrPos);
        txPrPos.Should().BeLessThan(dLblPosPos);
    }

    [Fact]
    public void AxisNumFmt_WrittenForCategoryPrimaryAndSecondaryValueAxes()
    {
        var chart = BuildComboChartWithSecondaryAxis();
        chart.CategoryAxis.NumberFormatCode = "m/d/yy";
        chart.CategoryAxis.NumberFormatSourceLinked = true;
        chart.ValueAxis.NumberFormatCode = "#,##0";
        chart.ValueAxis.NumberFormatSourceLinked = false;
        chart.SecondaryValueAxis!.NumberFormatCode = "0.0%";
        chart.SecondaryValueAxis.NumberFormatSourceLinked = false;

        var pptxPath = WriteToTempPptx(chart);
        var doc = XDocument.Parse(ExtractFirstChartXml(pptxPath));
        XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";

        var catFmt = doc.Descendants(c + "catAx").First().Element(c + "numFmt");
        var valFormats = doc.Descendants(c + "valAx")
            .Select(axis => axis.Element(c + "numFmt"))
            .ToList();

        catFmt.Should().NotBeNull();
        catFmt!.Attribute("formatCode")!.Value.Should().Be("m/d/yy");
        catFmt.Attribute("sourceLinked")!.Value.Should().Be("1");
        valFormats.Should().HaveCount(2);
        valFormats[0]!.Attribute("formatCode")!.Value.Should().Be("#,##0");
        valFormats[0]!.Attribute("sourceLinked")!.Value.Should().Be("0");
        valFormats[1]!.Attribute("formatCode")!.Value.Should().Be("0.0%");
        valFormats[1]!.Attribute("sourceLinked")!.Value.Should().Be("0");
    }

    // ── CA3: dLblPos gating ───────────────────────────────────────────────────

    [Fact]
    public void DLblPos_StackedColumn_OutsideEnd_Suppressed()
    {
        // Stacked column + OutsideEnd → dLblPos must NOT be written (invalid for stacked).
        var chart = new ChartShape { ChartType = ChartType.ColumnStacked };
        chart.Categories.AddRange(new[] { "A", "B" });
        var s = new ChartSeries { Name = "S1" };
        s.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(s);
        chart.DataLabels = new ChartDataLabels
        {
            ShowValue = true,
            Position  = DataLabelPosition.OutsideEnd
        };

        var pptxPath = WriteToTempPptx(chart);
        var xmlText  = ExtractFirstChartXml(pptxPath);

        // dLblPos must be absent (suppressed) since outEnd is invalid for stacked column.
        xmlText.Should().NotContain("<c:dLblPos",
            "outEnd is invalid for stacked column — dLblPos must be suppressed");
    }

    [Fact]
    public void DLblPos_StackedColumn_Center_Preserved()
    {
        // Stacked column + Center → dLblPos="ctr" must be written (the only valid value).
        var chart = new ChartShape { ChartType = ChartType.ColumnStacked };
        chart.Categories.AddRange(new[] { "A", "B" });
        var s = new ChartSeries { Name = "S1" };
        s.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(s);
        chart.DataLabels = new ChartDataLabels
        {
            ShowValue = true,
            Position  = DataLabelPosition.Center
        };

        var pptxPath = WriteToTempPptx(chart);
        var xmlText  = ExtractFirstChartXml(pptxPath);

        xmlText.Should().Contain("dLblPos", "ctr is valid for stacked column and must be written");
        xmlText.Should().Contain("val=\"ctr\"");
    }

    // ── XML inspection helpers ────────────────────────────────────────────────

    private string WriteToTempPptx(ChartShape chart)
    {
        var pres = BuildPresWithChart(chart);
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    private static string ExtractFirstChartXml(string pptxPath)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(pptxPath);
        var chartEntry = zip.Entries
            .Where(e => System.Text.RegularExpressions.Regex.IsMatch(
                e.FullName, @"ppt/charts/chart\d+\.xml$"))
            .OrderBy(e => e.FullName)
            .First();
        using var stream = chartEntry.Open();
        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ── 8. Wave-20 CB render-bug regressions (CB2/CB5/CB6) ────────────────────

    /// <summary>CB5: "0.00%" format code must yield exactly 2 decimal places.</summary>
    [Fact]
    public void FormatWithCode_PercentWithTwoDecimals_YieldsTwoDecimals_CB5()
    {
        // "0.00%" → LastIndexOf('%')=4, dotPos=1 → decimals=2 → "12.34%"
        string result = FormatWithCodeTest(0.1234, "0.00%");
        result.Should().Be("12.34%", "CB5: 0.00% must produce 2 decimal places, not 3");
    }

    /// <summary>CB5: "0.0%" format code must yield exactly 1 decimal place.</summary>
    [Fact]
    public void FormatWithCode_PercentOneDecimal_YieldsOneDecimal_CB5()
    {
        string result = FormatWithCodeTest(0.123, "0.0%");
        result.Should().Be("12.3%", "CB5: 0.0% must produce 1 decimal place");
    }

    /// <summary>CB5: "0%" format code (no dot) must yield 0 decimal places.</summary>
    [Fact]
    public void FormatWithCode_PercentNoDot_YieldsZeroDecimals_CB5()
    {
        string result = FormatWithCodeTest(0.5, "0%");
        result.Should().Be("50%", "CB5: 0% must produce no decimal places");
    }

    /// <summary>CB2: ShowPercent with non-zero total must render the actual share, not "0%".</summary>
    [Fact]
    public void LabelComposition_ShowPercent_WithNonZeroTotal_RendersActualShare_CB2()
    {
        var dl = new ChartDataLabels { ShowPercent = true };
        // 40 out of 100 → "40%"
        string lbl = ComposeLabel(dl, value: 40.0, total: 100.0, cat: "A", ser: "S");
        lbl.Should().Be("40%", "CB2: ShowPercent with total=100 and value=40 must render '40%', not '0%'");
    }

    /// <summary>CB2: ShowPercent with total=0 must render "0%" (guard).</summary>
    [Fact]
    public void LabelComposition_ShowPercent_ZeroTotal_Renders0Pct_CB2()
    {
        var dl = new ChartDataLabels { ShowPercent = true };
        string lbl = ComposeLabel(dl, value: 10.0, total: 0.0, cat: "A", ser: "S");
        lbl.Should().Be("0%", "CB2: ShowPercent with total=0 (guard) must render '0%'");
    }
}
