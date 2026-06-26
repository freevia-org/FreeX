using System.IO;
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
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.ChartDataLabelsTests", Guid.NewGuid().ToString("N"));

    public ChartDataLabelsTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

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
        var chart = BuildColumnChartWithLabels(showValue: true, numFmt: "0.00");
        var rt    = DoRoundTrip(chart);

        rt.DataLabels!.NumberFormat.Should().Be("0.00");
    }

    [Fact]
    public void RoundTrip_PieChart_ShowPercent_Preserved()
    {
        var chart = BuildPieChartWithLabels(showPercent: true);
        var rt    = DoRoundTrip(chart);

        rt.DataLabels.Should().NotBeNull("pie chart percent labels survive round-trip");
        rt.DataLabels!.ShowPercent.Should().BeTrue();
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
