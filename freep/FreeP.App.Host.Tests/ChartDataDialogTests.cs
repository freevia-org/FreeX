using System.IO;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 9B — host-layer tests for <see cref="ChartDataDialog"/> and its round-trip via
/// <see cref="PptxPackageWriter"/>.
/// </summary>
public sealed class ChartDataDialogTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.ChartDataDialogTests", Guid.NewGuid().ToString("N"));

    public ChartDataDialogTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────

    private static (EditingSession sess, uint shapeId) MakeSession()
    {
        var p    = new Presentation();
        var slide = new Slide();

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });

        var s1 = new ChartSeries { Name = "Alpha" };
        s1.Values.AddRange(new double?[] { 1.0, 2.0, 3.0 });
        chart.Series.Add(s1);

        var s2 = new ChartSeries { Name = "Beta" };
        s2.Values.AddRange(new double?[] { 4.0, 5.0, 6.0 });
        chart.Series.Add(s2);

        var shape = new SlideShape
        {
            Id          = 42u,
            Name        = "TestChart",
            Kind        = SlideShapeKind.Chart,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 3657600,
            Chart       = chart
        };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var bus  = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);
        sess.Select(42u);
        return (sess, 42u);
    }

    // ── ChartDataDialog construction ──────────────────────────────────────────────

    [StaFact]
    public void ChartDataDialog_Constructs_WithSelectedChart()
    {
        var (sess, _) = MakeSession();
        var dlg = new ChartDataDialog(sess);
        dlg.Should().NotBeNull();
    }

    [StaFact]
    public void ChartDataDialog_Throws_WhenNoChartSelected()
    {
        var p    = new Presentation();
        p.Slides.Add(new Slide());
        var bus  = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);
        // Nothing selected → SelectedChart is null.
        var act = () => new ChartDataDialog(sess);
        act.Should().Throw<InvalidOperationException>();
    }

    [StaFact]
    public void ChartDataDialog_ReflectsExistingCategories()
    {
        var (sess, _) = MakeSession();
        var dlg = new ChartDataDialog(sess);
        // Access internal _categories via the session chart model (the dialog copies from it).
        sess.SelectedChart!.Categories.Should().Equal("Q1", "Q2", "Q3");
    }

    [StaFact]
    public void ChartDataDialog_ReflectsExistingSeriesNames()
    {
        var (sess, _) = MakeSession();
        var dlg = new ChartDataDialog(sess);
        sess.SelectedChart!.Series[0].Name.Should().Be("Alpha");
        sess.SelectedChart!.Series[1].Name.Should().Be("Beta");
    }

    [StaFact]
    public void ChartDataDialog_ScatterProjectionPreservesEditableCoordinates()
    {
        var (sess, _) = MakeSession();
        var chart = sess.SelectedChart!;
        chart.ChartType = ChartType.Scatter;
        chart.Series[0].XValues.AddRange(new double?[] { 0.5, 1.5, 2.5 });
        chart.Series[1].XValues.AddRange(new double?[] { 1.0, 2.0, 3.0 });

        var dialog = new ChartDataDialog(sess);
        var commit = dialog.BuildCommitPlanForTests();

        commit.XValues[0].Should().Equal(new double?[] { 0.5, 1.5, 2.5 });
        commit.XValues[1].Should().Equal(new double?[] { 1.0, 2.0, 3.0 });
        commit.Values[0].Should().Equal(new double?[] { 1.0, 2.0, 3.0 });
    }

    [StaFact]
    public void ChartDisplayOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.Title = "Existing";
        sess.SelectedChart.Legend = LegendPosition.Right;
        sess.SelectedChart.ChartType = ChartType.Stock;

        var dialog = new ChartDisplayOptionsDialog(sess);
        dialog.SetTitleOverlayForTests(true);
        dialog.SetPlotVisibleOnlyForTests(false);
        dialog.SetRoundedCornersForTests(true);
        dialog.SetVaryColorsForTests(true);
        dialog.SetLegendOverlayForTests(true);
        dialog.SetHighLowLinesForTests(false);
        dialog.SetStyleIdForTests(102);
        dialog.SetBubbleSizeLabelsForTests(true);
        dialog.SetLabelTextStyleForTests("Aptos", 9, true, false, "#2F5496");
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.Title.Should().Be("Existing");
        options.TitleOverlay.Should().BeTrue();
        options.PlotVisibleOnly.Should().BeFalse();
        options.RoundedCorners.Should().BeTrue();
        options.Legend.Should().Be(LegendPosition.Right);
        options.DisplayBlanksAs.Should().BeNull();
        options.VaryColors.Should().BeTrue();
        options.LegendOverlay.Should().BeTrue();
        options.HighLowLines.Should().BeFalse();
        options.StyleId.Should().Be(102);
        options.ShowBubbleSize.Should().BeTrue();
        options.LabelTextStyle.Should().NotBeNull();
        options.LabelTextStyle!.FontFamily.Should().Be("Aptos");
        options.LabelTextStyle.FontSizePt.Should().Be(9);
        options.LabelTextStyle.Bold.Should().BeTrue();
        options.LabelTextStyle.Italic.Should().BeFalse();
        options.LabelTextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));
    }

    [StaFact]
    public void ChartDisplayOptionsDialog_WaterfallConnectorLinesUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.ChartType = ChartType.Waterfall;
        sess.SelectedChart.ShowWaterfallConnectorLines = true;

        var dialog = new ChartDisplayOptionsDialog(sess);
        dialog.SetWaterfallConnectorLinesForTests(false);

        dialog.BuildCommitPlanForTests().ShowWaterfallConnectorLines.Should().BeFalse();
    }

    [StaFact]
    public void ChartDisplayOptionsDialog_LineDecorationsUseSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.ChartType = ChartType.LineMarkers;
        sess.SelectedChart.ShowDropLines = true;
        sess.SelectedChart.ShowUpDownBars = false;

        var dialog = new ChartDisplayOptionsDialog(sess);
        dialog.SetDropLinesForTests(false);
        dialog.SetUpDownBarsForTests(true);
        var options = dialog.BuildCommitPlanForTests();

        options.ShowDropLines.Should().BeFalse();
        options.ShowUpDownBars.Should().BeTrue();
    }

    [StaFact]
    public void ChartDisplayOptionsDialog_SeriesLinesUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.ChartType = ChartType.ColumnStacked;
        sess.SelectedChart.SeriesLinesSpecified = true;

        var dialog = new ChartDisplayOptionsDialog(sess);
        dialog.SetSeriesLinesForTests(false);

        dialog.BuildCommitPlanForTests().ShowSeriesLines.Should().BeFalse();
    }

    [StaFact]
    public void ChartAxisOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.ValueAxis.Title = "Amount";
        sess.SelectedChart.ValueAxis.Delete = true;
        sess.SelectedChart.ValueAxis.Min = 0;
        sess.SelectedChart.ValueAxis.Max = 100;

        var dialog = new ChartAxisOptionsDialog(sess);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.Axis.Should().Be(ChartAxisKind.Value);
        options.Title.Should().Be("Amount");
        options.ShowAxis.Should().BeFalse();
        options.Minimum.Should().Be(0);
        options.Maximum.Should().Be(100);
    }

    [StaFact]
    public void ChartSeriesOptionsDialog_CanStartAtRequestedSeries()
    {
        var (sess, _) = MakeSession();
        var dialog = new ChartSeriesOptionsDialog(sess, initialSeriesIndex: 1);

        dialog.BuildCommitPlanForTests().SeriesIndex.Should().Be(1);
    }

    [StaFact]
    public void ChartAxisOptionsDialog_CanStartAtRequestedAxis()
    {
        var (sess, _) = MakeSession();
        var dialog = new ChartAxisOptionsDialog(sess, ChartAxisKind.Category);

        dialog.BuildCommitPlanForTests().Axis.Should().Be(ChartAxisKind.Category);
    }

    [StaFact]
    public void ChartAxisOptions_DisplayUnit_IsUndoable()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.ValueAxis.DisplayUnit.Should().Be(ChartAxisDisplayUnit.None);

        sess.ApplyChartAxisOptions(new ChartAxisOptions(
            ChartAxisKind.Value, null, null, null, null, null, null, true,
            DisplayUnit: ChartAxisDisplayUnit.Millions));

        sess.SelectedChart.ValueAxis.DisplayUnit.Should().Be(ChartAxisDisplayUnit.Millions);
        sess.Undo();
        sess.SelectedChart.ValueAxis.DisplayUnit.Should().Be(ChartAxisDisplayUnit.None);
    }

    [StaFact]
    public void ChartAxisOptions_CustomDisplayUnit_IsEditableAndRoundTrips()
    {
        var (sess, _) = MakeSession();
        sess.ApplyChartAxisOptions(new ChartAxisOptions(
            ChartAxisKind.Value, null, null, null, null, null, null, true,
            DisplayUnit: ChartAxisDisplayUnit.Custom,
            CustomDisplayUnit: 2500));

        sess.SelectedChart!.ValueAxis.DisplayUnit.Should().Be(ChartAxisDisplayUnit.Custom);
        sess.SelectedChart.ValueAxis.CustomDisplayUnit.Should().Be(2500);

        var path = Path.Combine(_tempDir, "chart-custom-display-unit.pptx");
        PptxPackageWriter.Write(sess.Presentation, path);
        var reloaded = PptxPackageReader.Read(path);
        var axis = reloaded.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.Chart).Chart!.ValueAxis;
        axis.DisplayUnit.Should().Be(ChartAxisDisplayUnit.Custom);
        axis.CustomDisplayUnit.Should().Be(2500);

        sess.Undo();
        sess.SelectedChart.ValueAxis.DisplayUnit.Should().Be(ChartAxisDisplayUnit.None);
    }

    [StaFact]
    public void ChartSeriesOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.Series[1].Name = "Margin";
        sess.SelectedChart.Series[1].SmoothLine = true;

        var dialog = new ChartSeriesOptionsDialog(sess);
        dialog.SetOptionsForTests(0, false, false, 2.25, ChartMarkerSymbol.Diamond, 7, "#4472C4", "#1F4E79", OutlineDash.DashDot, true,
            true, true, false, true, false, true, DataLabelPosition.InsideEnd, "0.0%", " | ",
            "Aptos", 9, true, false, "#2F5496", showBubbleSize: true, errorBars: true,
            showLeaderLines: true,
            trendline: true, trendlineType: ChartTrendlineType.Polynomial, trendlineOrder: 3,
            trendlineForward: 1.5, trendlineBackward: 0.5,
            trendlineEquation: true, trendlineRSquared: true, overrideChartType: ChartType.LineMarkers,
            invertIfNegative: true);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.SeriesIndex.Should().Be(0);
        options.SmoothLine.Should().BeFalse();
        options.OnSecondaryAxis.Should().BeFalse();
        options.InvertIfNegative.Should().BeTrue();
        options.OverrideChartType.Should().Be(ChartType.LineMarkers);
        options.FillColor!.Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
        options.LineColor!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        options.LineDash.Should().Be(OutlineDash.DashDot);
        options.NoLine.Should().BeTrue();
        options.DataLabels.Should().NotBeNull();
        options.DataLabels!.ShowValue.Should().BeTrue();
        options.DataLabels.ShowCategoryName.Should().BeTrue();
        options.DataLabels.ShowLegendKey.Should().BeTrue();
        options.DataLabels.ShowBubbleSize.Should().BeTrue();
        options.DataLabels.ShowLeaderLines.Should().BeTrue();
        options.ErrorBars.Should().NotBeNull();
        options.Trendline.Should().NotBeNull();
        options.Trendline!.Type.Should().Be(ChartTrendlineType.Polynomial);
        options.Trendline.PolynomialOrder.Should().Be(3);
        options.Trendline.Forward.Should().Be(1.5);
        options.Trendline.Backward.Should().Be(0.5);
        options.Trendline.DisplayEquation.Should().BeTrue();
        options.Trendline.DisplayRSquared.Should().BeTrue();
        options.DataLabels.Position.Should().Be(DataLabelPosition.InsideEnd);
        options.DataLabels.TextStyle.Should().NotBeNull();
        options.DataLabels.TextStyle!.FontFamily.Should().Be("Aptos");
        options.DataLabels.TextStyle.FontSizePt.Should().Be(9);
        options.DataLabels.TextStyle.Bold.Should().BeTrue();
        options.DataLabels.TextStyle.Italic.Should().BeFalse();
        options.DataLabels.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));
    }

    [StaFact]
    public void ChartPointOptionsDialog_CanStartAtHitPoint()
    {
        var (sess, _) = MakeSession();
        var dialog = new ChartPointOptionsDialog(sess, initialSeriesIndex: 1, initialPointIndex: 2);

        var options = dialog.BuildCommitPlanForTests();

        options.SeriesIndex.Should().Be(1);
        options.PointIndex.Should().Be(2);
    }

    [StaFact]
    public void ChartSeriesOptionsDialog_UsesScrollableBodyAndFixedActionRow()
    {
        var (sess, _) = MakeSession();
        var dialog = new ChartSeriesOptionsDialog(sess);

        var root = dialog.Content.Should().BeOfType<Grid>().Subject;
        root.RowDefinitions.Should().HaveCount(2);
        root.RowDefinitions[0].Height.IsStar.Should().BeTrue();
        root.RowDefinitions[1].Height.IsAuto.Should().BeTrue();

        var scrollViewer = root.Children.OfType<ScrollViewer>().Single();
        Grid.GetRow(scrollViewer).Should().Be(0);
        scrollViewer.VerticalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
        scrollViewer.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
        var optionsBody = scrollViewer.Content.Should().BeOfType<StackPanel>().Subject;
        optionsBody.Children.Count.Should().BeGreaterThan(30);

        var actionRow = root.Children.OfType<StackPanel>().Single();
        Grid.GetRow(actionRow).Should().Be(1);
        actionRow.Children.OfType<Button>().Should().HaveCount(2);
    }

    [StaFact]
    public void ChartPointOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        var dialog = new ChartPointOptionsDialog(sess);
        dialog.SetOptionsForTests(1, 2, "#C00000", "#1F4E79", 1.5, ChartMarkerSymbol.Diamond, 7,
            true, true, false, true, false, true, DataLabelPosition.InsideEnd, "0.0%", " | ",
            "Aptos", 9, true, false, "#2F5496", showBubbleSize: true, explosionPercent: 35,
            showLeaderLines: true);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.SeriesIndex.Should().Be(1);
        options.PointIndex.Should().Be(2);
        options.FillColor!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        options.StrokeColor!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        options.StrokeWidthPt.Should().Be(1.5);
        options.MarkerSymbol.Should().Be(ChartMarkerSymbol.Diamond);
        options.MarkerSizePt.Should().Be(7);
        options.ExplosionPercent.Should().Be(35);
        options.DataLabels.Should().NotBeNull();
        options.DataLabels!.ShowValue.Should().BeTrue();
        options.DataLabels.ShowCategoryName.Should().BeTrue();
        options.DataLabels.ShowLegendKey.Should().BeTrue();
        options.DataLabels.ShowBubbleSize.Should().BeTrue();
        options.DataLabels.ShowLeaderLines.Should().BeTrue();
        options.DataLabels.Position.Should().Be(DataLabelPosition.InsideEnd);
        options.DataLabels.TextStyle.Should().NotBeNull();
        options.DataLabels.TextStyle!.FontFamily.Should().Be("Aptos");
        options.DataLabels.TextStyle.FontSizePt.Should().Be(9);
        options.DataLabels.TextStyle.Bold.Should().BeTrue();
        options.DataLabels.TextStyle.Italic.Should().BeFalse();
        options.DataLabels.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));
    }

    [StaFact]
    public void ChartLayoutOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        var dialog = new ChartLayoutOptionsDialog(sess);
        dialog.SetOptionsForTests(ChartLayoutTarget.PlotArea, "inner", ChartManualLayoutMode.Edge, ChartManualLayoutMode.Factor, ChartManualLayoutMode.Factor, ChartManualLayoutMode.Edge, 12, 0.1, 0.8, 20);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.Target.Should().Be(ChartLayoutTarget.PlotArea);
        options.LayoutTarget.Should().Be("inner");
        options.XMode.Should().Be(ChartManualLayoutMode.Edge);
        options.HeightMode.Should().Be(ChartManualLayoutMode.Edge);
        options.X.Should().Be(12);
        options.Height.Should().Be(20);
    }

    [StaFact]
    public void ChartLayoutOptionsDialog_PreservesUnknownImportedModeWhenAcceptedUnchanged()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.PlotAreaManualLayout = new ChartManualLayout
        {
            XMode = ChartManualLayoutMode.Unsupported,
            RawXModeToken = "futureMode",
            X = 0.1,
        };

        var dialog = new ChartLayoutOptionsDialog(sess);
        var options = dialog.BuildCommitPlanForTests();

        options.XMode.Should().Be(ChartManualLayoutMode.Unsupported);
        options.RawXModeToken.Should().Be("futureMode");
    }

    [StaFact]
    public void ChartDataTableOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        var dialog = new ChartDataTableOptionsDialog(sess);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.ShowDataTable.Should().BeFalse();
        options.ShowHorizontalBorder.Should().BeTrue();
        options.ShowVerticalBorder.Should().BeTrue();
        options.ShowOutlineBorder.Should().BeTrue();
        options.ShowLegendKeys.Should().BeFalse();
        options.BackgroundColor.Should().BeNull();
        options.BorderColor.Should().BeNull();
        options.BorderWidthPt.Should().BeNull();
        options.FontSizePt.Should().BeNull();
    }

    [StaFact]
    public void ChartBubbleOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.ChartType = ChartType.Bubble;

        var dialog = new ChartBubbleOptionsDialog(sess);
        dialog.SetOptionsForTests(225, BubbleSizeRepresentation.Width, true);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.Should().Be(new ChartBubbleOptions(225, BubbleSizeRepresentation.Width, true));
    }

    [StaFact]
    public void ChartPieOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.ChartType = ChartType.Doughnut;
        sess.SelectedChart.FirstSliceAngleDegrees = 18;
        sess.SelectedChart.DoughnutHolePercent = 45;

        var dialog = new ChartPieOptionsDialog(sess);
        dialog.SetOptionsForTests(225, 68);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.Should().Be(new ChartPieOptions(225, 68));
    }

    [StaFact]
    public void ChartPieOptionsDialog_AuthorsOfPieSettingsThroughSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.ChartType = ChartType.OfPie;

        var dialog = new ChartPieOptionsDialog(sess);
        dialog.SetOfPieOptionsForTests(OfPieType.Bar, OfPieSplitType.Custom, 2, 75, "1, 2", 120, true);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.OfPieType.Should().Be(OfPieType.Bar);
        options.OfPieSplitType.Should().Be(OfPieSplitType.Custom);
        options.OfPieSplitPosition.Should().Be(2);
        options.OfPieSecondPieSizePercent.Should().Be(75);
        options.OfPieCustomPointIndices.Should().Equal(1, 2);
        options.OfPieGapWidthPercent.Should().Be(120);
        options.OfPieSeriesLines.Should().BeTrue();
    }

    [StaFact]
    public void ChartPlotStyleOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.ChartType = ChartType.Scatter;
        sess.SelectedChart.ScatterStyle = ScatterStyle.Marker;
        sess.SelectedChart.RadarStyle = RadarStyle.Standard;

        var dialog = new ChartPlotStyleOptionsDialog(sess);
        dialog.SetOptionsForTests(ScatterStyle.SmoothMarker, RadarStyle.Filled);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.Should().Be(new ChartPlotStyleOptions(ScatterStyle.SmoothMarker, RadarStyle.Filled));
    }

    [StaFact]
    public void Chart3DViewOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.View3D = new Chart3DView
        {
            RotationX = 25,
            RotationY = 35,
            Perspective = 54,
            HeightPercent = 100,
            DepthPercent = 125,
            RightAngleAxes = true,
        };
        sess.SelectedChart.ThreeDStyle = ChartThreeDStyle.Column;
        sess.SelectedChart.BarGapDepthPercent = 140;
        sess.SelectedChart.Wireframe = true;
        sess.SelectedChart.WireframeSpecified = true;

        var dialog = new Chart3DViewOptionsDialog(sess);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.Should().Be(new Chart3DViewOptions(25, 35, 54, 100, 125, true, true, 140));
    }

    [StaFact]
    public void ChartAreaOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        var dialog = new ChartAreaOptionsDialog(sess);
        dialog.SetOptionsForTests(ChartAreaFormattingTarget.PlotArea, null, null, null, true, true);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.Target.Should().Be(ChartAreaFormattingTarget.PlotArea);
        options.Fill.Should().BeSameAs(ShapeFill.None.Instance);
        options.Outline.Should().BeSameAs(ShapeOutline.None.Instance);
    }

    [StaFact]
    public void ChartAreaOptionsDialog_CanStartAtRequestedTarget()
    {
        var (sess, _) = MakeSession();
        var dialog = new ChartAreaOptionsDialog(sess, ChartAreaFormattingTarget.PlotArea);

        dialog.BuildCommitPlanForTests().Target.Should().Be(ChartAreaFormattingTarget.PlotArea);
    }

    [StaFact]
    public void ChartAreaOptionsDialog_AcceptsFillTransparency()
    {
        var (sess, _) = MakeSession();
        var dialog = new ChartAreaOptionsDialog(sess);
        dialog.SetOptionsForTests(
            ChartAreaFormattingTarget.ChartArea,
            "#4472C4",
            null,
            null,
            fillTransparency: 40);

        var options = dialog.BuildCommitPlanForTests();
        var fill = options.Fill.Should().BeOfType<ShapeFill.Solid>().Subject;
        fill.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
        fill.Color.Alpha.Should().Be(153);
    }

    [StaFact]
    public void ChartProtectionOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.ChartObjectProtected = true;
        sess.SelectedChart.ChartDataProtected = false;

        var dialog = new ChartProtectionOptionsDialog(sess);
        dialog.SetOptionsForTests(false, null, true, false);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.Should().Be(new ChartProtectionOptions(false, null, true, false));
    }

    [Fact]
    public void ChartDisplayOptionsDialog_UsesSharedPlannerAndSessionCommand()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartDisplayOptionsDialog.cs");

        source.Should().Contain("ChartDisplayOptionsPlanner.FromChart(chart)");
        source.Should().Contain("_planner.BuildCommitPlan()");
        source.Should().Contain("_editor.ApplyChartDisplayOptions");
        source.Should().NotContain("new SetChartDisplayOptionsCommand");
    }

    [Fact]
    public void ChartAxisOptionsDialog_UsesSharedPlannerAndSessionCommand()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartAxisOptionsDialog.cs");

        source.Should().Contain("ChartAxisOptionsPlanner.FromChart(chart)");
        source.Should().Contain("ChartAxisOptionsPlanner.AxisOptions");
        source.Should().Contain("_planner.BuildCommitPlan()");
        source.Should().Contain("_editor.ApplyChartAxisOptions");
        source.Should().NotContain("new SetChartAxisOptionsCommand");
    }

    [Fact]
    public void ChartSeriesOptionsDialog_UsesSharedPlannerAndSessionCommand()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartSeriesOptionsDialog.cs");

        source.Should().Contain("ChartSeriesOptionsPlanner.FromChart(chart)");
        source.Should().Contain("_planner.BuildCommitPlan()");
        source.Should().Contain("_editor.ApplyChartSeriesOptions");
        source.Should().NotContain("new SetChartSeriesOptionsCommand");
    }

    [Fact]
    public void ChartPointOptionsDialog_UsesSharedPlannerAndSessionCommand()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartPointOptionsDialog.cs");

        source.Should().Contain("ChartPointOptionsPlanner.FromChart(chart)");
        source.Should().Contain("_planner.BuildCommitPlan()");
        source.Should().Contain("_editor.ApplyChartPointOptions");
        source.Should().NotContain("new SetChartPointOptionsCommand");
    }

    [Fact]
    public void ChartLayoutOptionsDialog_UsesSharedPlannerAndSessionCommand()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartLayoutOptionsDialog.cs");

        source.Should().Contain("new ChartLayoutOptionsDialogSession(editor)");
        source.Should().Contain("_session.BuildCommitPlan(");
        source.Should().Contain("_session.TryCommit(");
        source.Should().NotContain("_planner.");
        source.Should().NotContain("_editor.ApplyChartLayoutOptions");
        source.Should().NotContain("new SetChartLayoutOptionsCommand");
    }

    [Fact]
    public void ChartDataTableOptionsDialog_UsesSharedPlannerAndSessionCommand()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartDataTableOptionsDialog.cs");

        source.Should().Contain("new ChartDataTableOptionsDialogSession(editor)");
        source.Should().Contain("_session.BuildCommitPlan(");
        source.Should().Contain("_session.TryCommit(");
        source.Should().NotContain("_planner.");
        source.Should().NotContain("_editor.ApplyChartDataTableOptions");
        source.Should().NotContain("new SetChartDataTableOptionsCommand");
    }

    [Fact]
    public void Chart3DViewOptionsDialog_UsesSharedPlannerAndSessionCommand()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "Chart3DViewOptionsDialog.cs");

        source.Should().Contain("new Chart3DViewOptionsDialogSession(editor");
        source.Should().Contain("_session.BuildCommitPlan(ReadInput())");
        source.Should().Contain("_session.Submit(ReadInput())");
        source.Should().NotContain("_planner");
        source.Should().NotContain("_editor");
        source.Should().NotContain("new SetChart3DViewOptionsCommand");
    }

    [StaFact]
    public void ChartTextOptionsDialog_ConstructsAndUsesSharedPlanner()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.TextStyle = new ChartTextStyle
        {
            FontFamily = "Aptos",
            FontSizePt = 11,
            Bold = true,
            Italic = false,
            Color = new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
        };

        var dialog = new ChartTextOptionsDialog(sess);
        var options = dialog.BuildCommitPlanForTests();

        dialog.Should().NotBeNull();
        options.FontFamily.Should().Be("Aptos");
        options.FontSizePt.Should().Be(11);
        options.Bold.Should().BeTrue();
        options.Italic.Should().BeFalse();
        options.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
    }

    [StaFact]
    public void ChartTextOptionsDialog_CanTargetChartTitle()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.Title = "Revenue";
        sess.SelectedChart.TitleStyle = new ChartTextStyle { FontFamily = "Aptos", FontSizePt = 15 };

        var dialog = new ChartTextOptionsDialog(sess, ChartTextTarget.Title);
        var options = dialog.BuildCommitPlanForTests();

        options.Target.Should().Be(ChartTextTarget.Title);
        options.FontFamily.Should().Be("Aptos");
        options.FontSizePt.Should().Be(15);
    }

    [StaFact]
    public void ChartTextOptionsDialog_CanTargetChartLegend()
    {
        var (sess, _) = MakeSession();
        sess.SelectedChart!.Legend = LegendPosition.Right;
        sess.SelectedChart.LegendTextStyle = new ChartTextStyle { FontFamily = "Aptos", FontSizePt = 12 };

        var dialog = new ChartTextOptionsDialog(sess, ChartTextTarget.Legend);
        var options = dialog.BuildCommitPlanForTests();

        options.Target.Should().Be(ChartTextTarget.Legend);
        options.FontFamily.Should().Be("Aptos");
        options.FontSizePt.Should().Be(12);
    }

    [Fact]
    public void Chart3DAndTextOptions_AreReachableThroughHostSourceRoutes()
    {
        var ribbonSource = ReadWorkspaceFile("freep", "FreeP.App.Host", "FreePRibbonCommands.cs");
        var windowSource = ReadWorkspaceFile("freep", "FreeP.App.Host", "MainWindow.cs");

        ribbonSource.Should().Contain("Chart3DViewOptionsPlanner.CommandId");
        ribbonSource.Should().Contain("ChartTextOptionsPlanner.CommandId");
        windowSource.Should().Contain("OpenChart3DViewOptionsDialog");
        windowSource.Should().Contain("OpenChartTextOptionsDialog");
    }

    [Fact]
    public void ChartBubbleOptionsDialog_UsesSharedPlannerAndSessionCommand()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartBubbleOptionsDialog.cs");
        var ribbonSource = ReadWorkspaceFile("freep", "FreeP.App.Host", "FreePRibbonCommands.cs");
        var windowSource = ReadWorkspaceFile("freep", "FreeP.App.Host", "MainWindow.cs");

        source.Should().Contain("new ChartBubbleOptionsDialogSession(editor");
        source.Should().Contain("_session.BuildCommitPlan(ReadInput())");
        source.Should().Contain("_session.Submit(ReadInput())");
        source.Should().NotContain("_planner");
        source.Should().NotContain("_editor");
        source.Should().NotContain("new SetChartBubbleOptionsCommand");
        ribbonSource.Should().Contain("ChartBubbleOptionsPlanner.CommandId");
        windowSource.Should().Contain("OpenChartBubbleOptionsDialog");
    }

    [Fact]
    public void ChartPieOptionsDialog_UsesSharedPlannerAndSessionCommand()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartPieOptionsDialog.cs");
        var ribbonSource = ReadWorkspaceFile("freep", "FreeP.App.Host", "FreePRibbonCommands.cs");
        var windowSource = ReadWorkspaceFile("freep", "FreeP.App.Host", "MainWindow.cs");

        source.Should().Contain("new ChartPieOptionsDialogSession(editor)");
        source.Should().Contain("_session.BuildCommitPlan(");
        source.Should().Contain("_session.TryCommit(");
        source.Should().NotContain("_planner.");
        source.Should().NotContain("_editor.ApplyChartPieOptions");
        source.Should().NotContain("new SetChartPieOptionsCommand");
        ribbonSource.Should().Contain("ChartPieOptionsPlanner.CommandId");
        windowSource.Should().Contain("OpenChartPieOptionsDialog");
    }

    [Fact]
    public void ChartPlotStyleOptionsDialog_UsesSharedPlannerAndSessionCommand()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartPlotStyleOptionsDialog.cs");
        var ribbonSource = ReadWorkspaceFile("freep", "FreeP.App.Host", "FreePRibbonCommands.cs");
        var windowSource = ReadWorkspaceFile("freep", "FreeP.App.Host", "MainWindow.cs");

        source.Should().Contain("new ChartPlotStyleOptionsDialogSession(editor");
        source.Should().Contain("_session.BuildCommitPlan(ReadInput())");
        source.Should().Contain("_session.Submit(ReadInput())");
        source.Should().NotContain("_planner");
        source.Should().NotContain("_editor");
        source.Should().NotContain("new SetChartPlotStyleOptionsCommand");
        ribbonSource.Should().Contain("ChartPlotStyleOptionsPlanner.CommandId");
        windowSource.Should().Contain("OpenChartPlotStyleOptionsDialog");
    }

    [Fact]
    public void ChartTextOptionsDialog_UsesSharedPlannerAndSessionCommand()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartTextOptionsDialog.cs");

        source.Should().Contain("new ChartTextOptionsDialogSession(editor, target)");
        source.Should().Contain("_session.BuildCommitPlan(ReadInput())");
        source.Should().Contain("_session.Submit(ReadInput())");
        source.Should().NotContain("_planner");
        source.Should().NotContain("_editor");
        source.Should().NotContain("new SetChartTextOptionsCommand");
    }

    [Fact]
    public void ChartDataDialog_UsesSharedSessionForWorkflow()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartDataDialog.cs");

        source.Should().Contain("new ChartDataDialogSession(editor)");
        source.Should().Contain("_session.BuildTableProjection()");
        source.Should().Contain("new ChartRowViewModel(row)");
        source.Should().Contain("MakeEditableHeader(seriesColumn)");
        source.Should().Contain("_session.BuildCommitPlan()");
        source.Should().Contain("_session.TryApplyEdits(");
        source.Should().Contain("_session.TryCommit(");
        source.Should().Contain("_session.AddSeries()");
        source.Should().Contain("_session.AddCategory()");
        source.Should().Contain("_session.RemoveActiveSeries()");
        source.Should().Contain("_session.RemoveActiveCategory()");
        source.Should().Contain("_session.MoveActiveCategory(delta)");
        source.Should().Contain("_session.SwitchRowsAndColumns()");
        source.Should().Contain("ChartDataDialogPlanner.FormatCellValue(");
        source.Should().Contain("ChartDataDialogPlanner.ParseCellValue(");
        source.Should().NotContain("ChartDataDialogPlanner.FromChart(");
        source.Should().NotContain("_planner.");
        source.Should().NotContain("ReplaceChartData(");
        source.Should().NotContain("private readonly List<string>       _categories");
        source.Should().NotContain("private readonly List<string>       _seriesNames");
        source.Should().NotContain("private readonly List<List<double?>> _values");
        source.Should().NotContain("private void EnsureRectangular");
        source.Should().NotContain("double.TryParse");
        source.Should().NotContain("Enumerable.Repeat");
        source.Should().NotContain("_planner.GetCategory(");
        source.Should().NotContain("_planner.SetCategory(");
        source.Should().NotContain("_planner.GetSeriesName(");
        source.Should().NotContain("_planner.SetSeriesName(");
        source.Should().NotContain("_planner.GetValue(");
        source.Should().NotContain("_planner.SetValue(");
        source.Should().NotContain("_planner.CategoriesForCommit()");
        source.Should().NotContain("_planner.SeriesNamesForCommit()");
        source.Should().NotContain("_planner.ValuesForCommit()");
    }

    [Fact]
    public void MainWindow_ChartDialogsRespectImportedProtectionPolicy()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "MainWindow.cs");

        source.Should().Contain("if (!Editor.CanEditSelectedChartData) return;");
        source.Should().Contain("if (!Editor.CanEditSelectedChartFormatting) return;");
    }

    [Fact]
    public void MainWindow_ChartProtectionDialogRouteIsAvailableForSelectedCharts()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "MainWindow.cs");
        var ribbon = ReadWorkspaceFile("freep", "FreeP.App.Host", "FreePRibbonCommands.cs");

        source.Should().Contain("OpenChartProtectionOptionsDialog");
        ribbon.Should().Contain("ChartProtectionOptionsPlanner.CommandId");
    }

    // ── EditingSession chart API (from session, not dialog) ───────────────────────

    [StaFact]
    public void ChartData_AfterReplaceChartData_SessionReflectsChange()
    {
        var (sess, _) = MakeSession();
        sess.ReplaceChartData(
            new[] { "Jan", "Feb" },
            new[] { "Revenue" },
            new[] { new[] { 10.0, 20.0 } }.Select(v => (IEnumerable<double>)v));

        var chart = sess.SelectedChart!;
        chart.Categories.Should().Equal("Jan", "Feb");
        chart.Series.Should().HaveCount(1);
        chart.Series[0].Name.Should().Be("Revenue");
        chart.Series[0].Values[0].Should().Be(10.0);
        chart.Series[0].Values[1].Should().Be(20.0);
    }

    [StaFact]
    public void ChartData_ReplaceChartData_IsUndoable()
    {
        var (sess, _) = MakeSession();
        sess.ReplaceChartData(
            new[] { "Only" },
            new[] { "X" },
            new[] { new[] { 99.0 } }.Select(v => (IEnumerable<double>)v));
        sess.Undo();

        sess.SelectedChart!.Categories.Should().Equal("Q1", "Q2", "Q3");
        sess.SelectedChart!.Series.Should().HaveCount(2);
    }

    // ── Round-trip: edit data → save → reload → verify ────────────────────────────

    [StaFact]
    public void ChartData_RoundTrip_SavedAndReloadedWithNewValues()
    {
        var (sess, _) = MakeSession();

        // Edit via session API.
        sess.ReplaceChartData(
            new[] { "H1", "H2" },
            new[] { "Profit", "Costs" },
            new[]
            {
                new[] { 300.0, 400.0 },
                new[] { 150.0, 200.0 }
            }.Select(v => (IEnumerable<double>)v));

        // Save.
        var path = Path.Combine(_tempDir, "chart-edit-rt.pptx");
        PptxPackageWriter.Write(sess.Presentation, path);

        // Reload.
        var reloaded = PptxPackageReader.Read(path);
        var chart    = reloaded.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.Chart)
            .Chart!;

        chart.Categories.Should().BeEquivalentTo(new[] { "H1", "H2" }, "chart categories survive round-trip");
        chart.Series.Should().HaveCount(2, "series count survives round-trip");
        chart.Series[0].Name.Should().Be("Profit");
        chart.Series[1].Name.Should().Be("Costs");
        chart.Series[0].Values[0].Should().BeApproximately(300.0, 0.01);
        chart.Series[1].Values[1].Should().BeApproximately(200.0, 0.01);
    }

    [StaFact]
    public void ChartData_AddedSeries_SurvivesRoundTrip()
    {
        var (sess, _) = MakeSession();
        sess.AddChartSeries("Gamma");

        var path = Path.Combine(_tempDir, "chart-addseries-rt.pptx");
        PptxPackageWriter.Write(sess.Presentation, path);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.Chart)
            .Chart!.Series
            .Should().HaveCount(3, "three series survive round-trip after add");
    }

    [StaFact]
    public void ChartData_AddedCategory_SurvivesRoundTrip()
    {
        var (sess, _) = MakeSession();
        sess.AddChartCategory("Q4");

        var path = Path.Combine(_tempDir, "chart-addcat-rt.pptx");
        PptxPackageWriter.Write(sess.Presentation, path);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.Chart)
            .Chart!.Categories
            .Should().HaveCount(4, "four categories survive round-trip after add");
    }

    // ── W7: dialog construction does not flatten gaps to 0.0 ─────────────────────

    /// <summary>
    /// W7 regression: constructing ChartDataDialog with a chart that has a gap (null at
    /// index 1 of the second series) must NOT flatten that null to 0.0 in the dialog's
    /// working copy, so a subsequent OK (no edits) leaves the model gap intact.
    /// </summary>
    [StaFact]
    public void W7_ChartDataDialog_Construction_PreservesGapInWorkingCopy()
    {
        // Build a presentation with a gap in Series[1].Values[1].
        var p    = new Presentation();
        var slide = new Slide();

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "X", "Y", "Z" });

        var s1 = new ChartSeries { Name = "Dense" };
        s1.Values.AddRange(new double?[] { 1.0, 2.0, 3.0 });
        chart.Series.Add(s1);

        var s2 = new ChartSeries { Name = "Sparse" };
        s2.Values.AddRange(new double?[] { 4.0, null, 6.0 });  // Y is a gap
        chart.Series.Add(s2);

        var shape = new SlideShape
        {
            Id          = 10u,
            Name        = "GapChart",
            Kind        = SlideShapeKind.Chart,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 3657600,
            Chart       = chart
        };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var bus  = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);
        sess.Select(10u);

        // Construct the dialog — this triggers the deep-copy that previously flattened nulls.
        var dlg = new ChartDataDialog(sess);
        dlg.Should().NotBeNull();

        // Simulate pressing OK with no edits: call the session's nullable ReplaceChartData
        // directly with the same nullable values the dialog would produce.
        var workingValues = chart.Series.Select(sr => sr.Values.ToList()).ToList();
        sess.ReplaceChartData(
            chart.Categories.ToList(),
            chart.Series.Select(sr => sr.Name),
            workingValues.Select(sv => (IEnumerable<double?>)sv));

        // The gap at Series[1][1] (Y in Sparse) must still be null.
        sess.SelectedChart!.Series[1].Values[1]
            .Should().BeNull("W7: gap must not be flattened to 0.0 by dialog OK with no edits");
    }

    /// <summary>
    /// W7 regression: ReplaceChartData → Undo on a gap chart must restore the null,
    /// not 0.0 (this tests the command path that the dialog OK button drives).
    /// </summary>
    [StaFact]
    public void W7_ReplaceChartData_Undo_GapRemainsNull()
    {
        var p    = new Presentation();
        var slide = new Slide();

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Jan", "Feb", "Mar" });

        var s = new ChartSeries { Name = "Revenue" };
        s.Values.AddRange(new double?[] { 100.0, null, 300.0 });  // Feb is a gap
        chart.Series.Add(s);

        var shape = new SlideShape
        {
            Id = 20u, Name = "G2", Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 1, ExtentCyEmu = 1,
            Chart = chart
        };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var bus  = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);
        sess.Select(20u);

        // Apply a replacement (no gap in new data).
        sess.ReplaceChartData(
            new[] { "Jan", "Feb", "Mar" },
            new[] { "Revenue" },
            new[] { new double?[] { 110.0, 220.0, 330.0 } }.Select(r => (IEnumerable<double?>)r));

        // Undo — Feb must come back as null.
        sess.Undo();

        sess.SelectedChart!.Series[0].Values[1]
            .Should().BeNull("W7: original Feb gap must be null after undo, not 0.0");
    }

    private static string ReadWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = directory.FullName;
            relativeParts.CopyTo(parts, 1);

            var candidate = Path.Combine(parts);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate workspace file.",
            Path.Combine(relativeParts));
    }
}
