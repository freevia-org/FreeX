using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Wave 9B — unit tests for chart-data commands and EditingSession chart API.
/// </summary>
public sealed class ChartDataCommandTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────────

    private static (Presentation p, PresentationCommandBus bus, uint chartShapeId) MakeChartPresentation()
    {
        var p    = new Presentation();
        var slide = new Slide();

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });

        var s1 = new ChartSeries { Name = "Sales" };
        s1.Values.AddRange(new double?[] { 100, 200, 150 });
        chart.Series.Add(s1);

        var s2 = new ChartSeries { Name = "Budget" };
        s2.Values.AddRange(new double?[] { 120, 180, 160 });
        chart.Series.Add(s2);

        var shape = new SlideShape
        {
            Id          = 1,
            Name        = "Chart1",
            Kind        = SlideShapeKind.Chart,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 3657600,
            Chart       = chart
        };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var bus = new PresentationCommandBus(p);
        return (p, bus, shape.Id);
    }

    private static EditingSession MakeSession()
    {
        var (p, bus, shapeId) = MakeChartPresentation();
        var session = new EditingSession(p, bus);
        session.Select(shapeId);
        return session;
    }

    private static (Presentation p, PresentationCommandBus bus, uint chartShapeId) MakeGroupedChartPresentation()
    {
        var (p, bus, chartShapeId) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes.Single(shape => shape.Id == chartShapeId);
        p.Slides[0].Shapes.Remove(chart);

        var group = new SlideShape { Id = 99, Name = "Group", Kind = SlideShapeKind.Group };
        group.Children.Add(chart);
        p.Slides[0].Shapes.Add(group);
        return (p, bus, chartShapeId);

    }

    private static EditingSession MakeGroupedSession()
    {
        var (p, bus, chartShapeId) = MakeChartPresentation();
        var slide = p.Slides[0];
        var chart = slide.Shapes.Single(shape => shape.Id == chartShapeId);
        slide.Shapes.Remove(chart);
        slide.Shapes.Add(new SlideShape
        {
            Id = 10,
            Name = "Group",
            Kind = SlideShapeKind.Group,
            Children = { chart },
        });

        var session = new EditingSession(p, bus);
        session.Select(chartShapeId);
        return session;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // SetChartCellValueCommand
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SetChartCellValue_Apply_UpdatesValue()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new SetChartCellValueCommand(0, id, seriesIndex: 0, categoryIndex: 1, value: 999.0));
        p.Slides[0].Shapes[0].Chart!.Series[0].Values[1].Should().Be(999.0);
    }

    [Fact]
    public void SetChartCellValue_GroupedChart_UpdatesValueAndSupportsUndo()
    {
        var (p, bus, id) = MakeGroupedChartPresentation();
        var chart = p.Slides[0].Shapes[0].Children[0].Chart!;

        bus.Execute(new SetChartCellValueCommand(0, id, 0, 1, 999.0));
        chart.Series[0].Values[1].Should().Be(999.0);

        bus.Undo();
        chart.Series[0].Values[1].Should().Be(200.0);
    }

    [Fact]
    public void SetChartCellValue_Apply_MarksChartWorkbookForRegeneration()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;

        chart.RegenerateWorkbookOnSave.Should().BeFalse();
        bus.Execute(new SetChartCellValueCommand(0, id, seriesIndex: 0, categoryIndex: 1, value: 999.0));

        chart.RegenerateWorkbookOnSave.Should().BeTrue();
    }

    [Fact]
    public void SetChartCellValue_Apply_MarksOnlyTargetChartWorkbookForRegeneration()
    {
        var (p, bus, id) = MakeChartPresentation();
        var neighborChart = new ChartShape { ChartType = ChartType.Scatter };
        var neighborSeries = new ChartSeries { Name = "Neighbor" };
        neighborSeries.XValues.AddRange(new double?[] { 1, 2, 3 });
        neighborSeries.Values.AddRange(new double?[] { 10, 20, 30 });
        neighborChart.Series.Add(neighborSeries);
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "NeighborChart",
            Kind = SlideShapeKind.Chart,
            Chart = neighborChart
        });

        bus.Execute(new SetChartCellValueCommand(0, id, seriesIndex: 0, categoryIndex: 1, value: 999.0));

        p.Slides[0].Shapes[0].Chart!.RegenerateWorkbookOnSave.Should().BeTrue();
        p.Slides[0].Shapes[1].Chart!.RegenerateWorkbookOnSave.Should().BeFalse();
    }

    [Fact]
    public void SetChartCellValue_Revert_RestoresOldValue()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new SetChartCellValueCommand(0, id, seriesIndex: 0, categoryIndex: 1, value: 999.0));
        bus.Undo();
        p.Slides[0].Shapes[0].Chart!.Series[0].Values[1].Should().Be(200.0);
    }

    [Fact]
    public void SetChartCellValue_Redo_ReappliesNewValue()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new SetChartCellValueCommand(0, id, seriesIndex: 0, categoryIndex: 0, value: 42.0));
        bus.Undo();
        bus.Redo();
        p.Slides[0].Shapes[0].Chart!.Series[0].Values[0].Should().Be(42.0);
    }

    [Fact]
    public void SetChartCellValue_OutOfRange_IsIgnored()
    {
        var (p, bus, id) = MakeChartPresentation();
        var act = () => bus.Execute(new SetChartCellValueCommand(0, id, seriesIndex: 99, categoryIndex: 0, value: 1.0));
        act.Should().NotThrow();
        p.Slides[0].Shapes[0].Chart!.Series.Should().HaveCount(2, "no series added on out-of-range");
    }

    [Fact]
    public void SetChartCellValue_ProtectedData_IsIgnoredAndDoesNotMarkWorkbook()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ChartDataProtected = true;

        bus.Execute(new SetChartCellValueCommand(0, id, seriesIndex: 0, categoryIndex: 1, value: 999.0));

        chart.Series[0].Values[1].Should().Be(200.0);
        chart.RegenerateWorkbookOnSave.Should().BeFalse();
    }

    [Fact]
    public void EditingSession_ChartProtection_ExposesDataAndFormattingCapabilities()
    {
        var session = MakeSession();
        session.CanEditSelectedChartData.Should().BeTrue();
        session.CanEditSelectedChartFormatting.Should().BeTrue();

        session.SelectedChart!.ChartDataProtected = true;
        session.CanEditSelectedChartData.Should().BeFalse();
        session.CanEditSelectedChartFormatting.Should().BeTrue();

        session.SelectedChart.ChartFormattingProtected = true;
        session.CanEditSelectedChartFormatting.Should().BeFalse();

        session.SelectedChart.ChartObjectProtected = true;
        session.CanEditSelectedChartData.Should().BeFalse();
        session.CanEditSelectedChartFormatting.Should().BeFalse();
    }

    [Fact]
    public void EditingSession_ChangeSelectedChartType_ProtectedDataReturnsFalse()
    {
        var session = MakeSession();
        session.SelectedChart!.ChartDataProtected = true;

        session.ChangeSelectedChartType(ChartType.Line).Should().BeFalse();
        session.SelectedChart.ChartType.Should().Be(ChartType.ColumnClustered);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // SetChartCategoryLabelCommand
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SetChartCategoryLabel_Apply_UpdatesLabel()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new SetChartCategoryLabelCommand(0, id, categoryIndex: 0, label: "Q1-2026"));
        p.Slides[0].Shapes[0].Chart!.Categories[0].Should().Be("Q1-2026");
    }

    [Fact]
    public void SetChartCategoryLabel_Revert_RestoresOldLabel()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new SetChartCategoryLabelCommand(0, id, categoryIndex: 0, label: "Q1-2026"));
        bus.Undo();
        p.Slides[0].Shapes[0].Chart!.Categories[0].Should().Be("Q1");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // SetChartSeriesNameCommand
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SetChartSeriesName_Apply_UpdatesName()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new SetChartSeriesNameCommand(0, id, seriesIndex: 1, name: "Forecast"));
        p.Slides[0].Shapes[0].Chart!.Series[1].Name.Should().Be("Forecast");
    }

    [Fact]
    public void SetChartSeriesName_Revert_RestoresName()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new SetChartSeriesNameCommand(0, id, seriesIndex: 1, name: "Forecast"));
        bus.Undo();
        p.Slides[0].Shapes[0].Chart!.Series[1].Name.Should().Be("Budget");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // AddChartSeriesCommand
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AddChartSeries_Apply_AppendsSeries()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new AddChartSeriesCommand(0, id, "Actuals"));
        p.Slides[0].Shapes[0].Chart!.Series.Should().HaveCount(3);
        p.Slides[0].Shapes[0].Chart!.Series[2].Name.Should().Be("Actuals");
    }

    [Fact]
    public void AddChartSeries_NewSeries_HasOneValuePerCategory()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new AddChartSeriesCommand(0, id, "New"));
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Series[2].Values.Should().HaveCount(chart.Categories.Count, "matrix rectangular after add");
    }

    [Fact]
    public void AddChartSeries_BubbleSeedsCoordinatesAndUndoRemovesThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ChartType = ChartType.Bubble;

        bus.Execute(new AddChartSeriesCommand(0, id, "New Bubble Series"));

        chart.Series[^1].Values.Should().HaveCount(3);
        chart.Series[^1].XValues.Should().Equal(1.0, 2.0, 3.0);
        chart.Series[^1].BubbleSizes.Should().Equal(1.0, 1.0, 1.0);

        bus.Undo();
        chart.Series.Should().HaveCount(2);
    }

    [Fact]
    public void AddChartSeries_Revert_RemovesSeries()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new AddChartSeriesCommand(0, id, "New"));
        bus.Undo();
        p.Slides[0].Shapes[0].Chart!.Series.Should().HaveCount(2);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // RemoveChartSeriesCommand
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RemoveChartSeries_Apply_RemovesCorrectSeries()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new RemoveChartSeriesCommand(0, id, seriesIndex: 0));
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Series.Should().HaveCount(1);
        chart.Series[0].Name.Should().Be("Budget");
    }

    [Fact]
    public void RemoveChartSeries_Revert_RestoresSeries()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new RemoveChartSeriesCommand(0, id, seriesIndex: 0));
        bus.Undo();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Series.Should().HaveCount(2);
        chart.Series[0].Name.Should().Be("Sales");
    }

    [Fact]
    public void MoveChartSeries_ApplyAndUndo_PreservesSeriesPayload()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        var fill = new ThemeAwareColor(new SrgbColor(0x12, 0x34, 0x56));
        chart.Series[1].FillColor = fill;
        chart.Series[1].XValues.AddRange(new double?[] { 10, 20, 30 });

        bus.Execute(new MoveChartSeriesCommand(0, id, sourceIndex: 1, targetIndex: 0));

        chart.Series[0].Name.Should().Be("Budget");
        chart.Series[0].FillColor.Should().Be(fill);
        chart.Series[0].XValues.Should().Equal(new double?[] { 10, 20, 30 });
        bus.Undo();
        chart.Series[0].Name.Should().Be("Sales");
        chart.Series[1].Name.Should().Be("Budget");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // AddChartCategoryCommand
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AddChartCategory_Apply_AppendsCategoryAndGrowsAllSeries()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new AddChartCategoryCommand(0, id, "Q4"));
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Categories.Should().HaveCount(4);
        chart.Categories[3].Should().Be("Q4");
        chart.Series[0].Values.Should().HaveCount(4, "series 0 stays rectangular");
        chart.Series[1].Values.Should().HaveCount(4, "series 1 stays rectangular");
    }

    [Fact]
    public void AddChartCategory_Revert_RemovesLastCategoryAndValue()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new AddChartCategoryCommand(0, id, "Q4"));
        bus.Undo();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Categories.Should().HaveCount(3);
        chart.Series[0].Values.Should().HaveCount(3);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // RemoveChartCategoryCommand
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RemoveChartCategory_Apply_RemovesCategoryAndOneValueFromEachSeries()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new RemoveChartCategoryCommand(0, id, categoryIndex: 1));
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Categories.Should().HaveCount(2);
        chart.Series[0].Values.Should().HaveCount(2);
        chart.Series[1].Values.Should().HaveCount(2);
    }

    [Fact]
    public void RemoveChartCategory_Revert_RestoresCategoryAndValues()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new RemoveChartCategoryCommand(0, id, categoryIndex: 1));
        bus.Undo();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Categories.Should().HaveCount(3);
        chart.Categories[1].Should().Be("Q2");
        chart.Series[0].Values[1].Should().Be(200.0);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // ReplaceChartDataCommand (batch)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AddChartCategory_BubbleMaintainsCoordinatesAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ChartType = ChartType.Bubble;
        foreach (var series in chart.Series)
        {
            series.XValues.AddRange([1.0, 2.0, 3.0]);
            series.BubbleSizes.AddRange([4.0, 5.0, 6.0]);
        }

        bus.Execute(new AddChartCategoryCommand(0, id, "Q4"));

        chart.Categories.Should().EndWith("Q4");
        chart.Series.Should().AllSatisfy(series =>
        {
            series.Values.Should().HaveCount(4);
            series.XValues.Should().Equal(1.0, 2.0, 3.0, 4.0);
            series.BubbleSizes.Should().Equal(4.0, 5.0, 6.0, 1.0);
        });

        bus.Undo();
        chart.Categories.Should().HaveCount(3);
        chart.Series.Should().AllSatisfy(series =>
        {
            series.XValues.Should().Equal(1.0, 2.0, 3.0);
            series.BubbleSizes.Should().Equal(4.0, 5.0, 6.0);
        });
    }

    [Fact]
    public void RemoveChartCategory_ScatterRemovesCoordinatesAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ChartType = ChartType.Scatter;
        foreach (var series in chart.Series)
            series.XValues.AddRange([10.0, 20.0, 30.0]);

        bus.Execute(new RemoveChartCategoryCommand(0, id, categoryIndex: 1));

        chart.Categories.Should().Equal("Q1", "Q3");
        chart.Series.Should().AllSatisfy(series =>
            series.XValues.Should().Equal(10.0, 30.0));

        bus.Undo();
        chart.Categories.Should().Equal("Q1", "Q2", "Q3");
        chart.Series.Should().AllSatisfy(series =>
            series.XValues.Should().Equal(10.0, 20.0, 30.0));
    }

    [Fact]
    public void ReplaceChartData_Apply_ReplacesAllData()
    {
        var (p, bus, id) = MakeChartPresentation();
        var cats    = new[] { "Jan", "Feb" };
        var names   = new[] { "Rev" };
        var vals    = new[] { new[] { 10.0, 20.0 } };
        bus.Execute(new ReplaceChartDataCommand(0, id, cats, names, vals.Select(v => (IEnumerable<double>)v)));

        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Categories.Should().Equal("Jan", "Feb");
        chart.Series.Should().HaveCount(1);
        chart.Series[0].Name.Should().Be("Rev");
        chart.Series[0].Values[0].Should().Be(10.0);
        chart.Series[0].Values[1].Should().Be(20.0);
    }

    [Fact]
    public void ReplaceChartData_Revert_RestoresOriginalData()
    {
        var (p, bus, id) = MakeChartPresentation();
        bus.Execute(new ReplaceChartDataCommand(
            0, id,
            new[] { "X" },
            new[] { "S" },
            new[] { new[] { 1.0 } }.Select(v => (IEnumerable<double>)v)));

        bus.Undo();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Categories.Should().Equal("Q1", "Q2", "Q3");
        chart.Series.Should().HaveCount(2);
        chart.Series[0].Name.Should().Be("Sales");
        chart.Series[0].Values[0].Should().Be(100.0);
    }

    [Fact]
    public void SetChartCellValue_Revert_RestoresMissingPointAsGap()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Series[0].Values[1] = null;

        bus.Execute(new SetChartCellValueCommand(0, id, 0, 1, 250.0));
        chart.Series[0].Values[1].Should().Be(250.0);

        bus.Undo();
        chart.Series[0].Values[1].Should().BeNull("undo must preserve an authored chart gap");
    }

    [Fact]
    public void ReplaceChartData_WithChartType_ChangesTypeAndUndoRestoresIt()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;

        chart.ChartType.Should().Be(ChartType.ColumnClustered);
        bus.Execute(new ReplaceChartDataCommand(
            0,
            id,
            chart.Categories,
            chart.Series.Select(series => series.Name),
            chart.Series.Select(series => (IEnumerable<double?>)series.Values),
            ChartType.LineMarkers));

        chart.ChartType.Should().Be(ChartType.LineMarkers);

        using var package = new MemoryStream();
        PptxPackageWriter.Write(p, package);
        package.Position = 0;
        PptxPackageReader.Read(package)
            .Slides[0].Shapes[0].Chart!.ChartType
            .Should().Be(ChartType.LineMarkers);

        bus.Undo();
        chart.ChartType.Should().Be(ChartType.ColumnClustered);
    }

    [Fact]
    public void ReplaceChartData_ToScatter_CreatesCoordinatesAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;

        chart.Series[0].XValues.Should().BeEmpty();
        bus.Execute(new ReplaceChartDataCommand(
            0,
            id,
            chart.Categories,
            chart.Series.Select(series => series.Name),
            chart.Series.Select(series => (IEnumerable<double?>)series.Values),
            ChartType.Bubble));

        chart.ChartType.Should().Be(ChartType.Bubble);
        chart.ScatterStyle.Should().Be(ScatterStyle.LineMarker);
        foreach (var series in chart.Series)
        {
            series.XValues.Should().HaveCount(3);
            series.BubbleSizes.Should().HaveCount(3);
        }

        bus.Undo();
        chart.ChartType.Should().Be(ChartType.ColumnClustered);
        foreach (var series in chart.Series)
        {
            series.XValues.Should().BeEmpty();
            series.BubbleSizes.Should().BeEmpty();
        }
    }

    [Fact]
    public void ReplaceChartData_WithScatterCoordinates_RoundTripsAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        var values = new[]
        {
            new double?[] { 10.0, 20.0, 30.0 },
            new double?[] { 5.0, 15.0, 25.0 },
        };
        var xValues = new[]
        {
            new double?[] { 0.5, 1.5, 2.5 },
            new double?[] { 1.0, 2.0, 3.0 },
        };
        var bubbleSizes = new[]
        {
            new double?[] { 4.0, 6.0, 8.0 },
            new double?[] { 3.0, 5.0, 7.0 },
        };

        bus.Execute(new ReplaceChartDataCommand(
            0,
            id,
            new[] { "P1", "P2", "P3" },
            new[] { "Revenue", "Cost" },
            values.Select(row => (IEnumerable<double?>)row),
            ChartType.Bubble,
            xValues.Select(row => (IEnumerable<double?>)row),
            bubbleSizes.Select(row => (IEnumerable<double?>)row)));

        chart.ChartType.Should().Be(ChartType.Bubble);
        chart.Series[0].XValues.Should().Equal(xValues[0]);
        chart.Series[1].XValues.Should().Equal(xValues[1]);
        chart.Series[0].BubbleSizes.Should().Equal(bubbleSizes[0]);

        using var package = new MemoryStream();
        PptxPackageWriter.Write(p, package);
        package.Position = 0;
        var reread = PptxPackageReader.Read(package)
            .Slides[0].Shapes[0].Chart!;
        reread.ChartType.Should().Be(ChartType.Bubble);
        reread.Series[0].XValues.Should().Equal(xValues[0]);
        reread.Series[1].BubbleSizes.Should().Equal(bubbleSizes[1]);

        bus.Undo();
        chart.ChartType.Should().Be(ChartType.ColumnClustered);
        chart.Series.Should().AllSatisfy(series =>
        {
            series.XValues.Should().BeEmpty();
            series.BubbleSizes.Should().BeEmpty();
        });
    }

    [Fact]
    public void ReplaceChartData_Revert_RestoresPreviousWorkbookRegenerationFlag()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;

        chart.RegenerateWorkbookOnSave.Should().BeFalse();
        bus.Execute(new ReplaceChartDataCommand(
            0, id,
            new[] { "X" },
            new[] { "S" },
            new[] { new[] { 1.0 } }.Select(v => (IEnumerable<double>)v)));
        chart.RegenerateWorkbookOnSave.Should().BeTrue();

        bus.Undo();

        chart.RegenerateWorkbookOnSave.Should().BeFalse();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // EditingSession chart API
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EditingSession_SelectedChart_ReturnsChartWhenChartSelected()
    {
        var sess = MakeSession();
        sess.SelectedChart.Should().NotBeNull();
    }

    [Fact]
    public void EditingSession_GroupedChart_UsesRecursiveDataAndFormattingCommands()
    {
        var sess = MakeGroupedSession();

        sess.SelectedChart.Should().NotBeNull();
        sess.SetChartValue(0, 1, 999.0);
        sess.SetChartCategory(0, "H1");
        sess.ApplyChartTextOptions(new ChartTextOptions("Arial", 18, true, null, null));

        sess.SelectedChart!.Series[0].Values[1].Should().Be(999.0);
        sess.SelectedChart.Categories[0].Should().Be("H1");
        sess.SelectedChart.TextStyle.Should().Match<ChartTextStyle>(style =>
            style.FontFamily == "Arial" && style.FontSizePt == 18 && style.Bold == true);

        sess.Undo();
        sess.SelectedChart.TextStyle.Should().BeNull();
        sess.Undo();
        sess.SelectedChart.Categories[0].Should().Be("Q1");
        sess.Undo();
        sess.SelectedChart.Series[0].Values[1].Should().Be(200.0);
    }

    [Fact]
    public void EditingSession_SelectedChart_ReturnsNullWhenNothingSelected()
    {
        var (p, bus, _) = MakeChartPresentation();
        var sess = new EditingSession(p, bus);
        // No shape selected.
        sess.SelectedChart.Should().BeNull();
    }

    [Fact]
    public void EditingSession_SetChartValue_UpdatesModel()
    {
        var sess = MakeSession();
        sess.SetChartValue(seriesIndex: 0, categoryIndex: 2, value: 777.0);
        sess.SelectedChart!.Series[0].Values[2].Should().Be(777.0);
    }

    [Fact]
    public void EditingSession_SetChartValue_IsUndoable()
    {
        var sess = MakeSession();
        sess.SetChartValue(0, 0, 555.0);
        sess.Undo();
        sess.SelectedChart!.Series[0].Values[0].Should().Be(100.0);
    }

    [Fact]
    public void EditingSession_SetChartCategory_UpdatesLabel()
    {
        var sess = MakeSession();
        sess.SetChartCategory(0, "H1");
        sess.SelectedChart!.Categories[0].Should().Be("H1");
    }

    [Fact]
    public void EditingSession_SetChartSeriesName_UpdatesName()
    {
        var sess = MakeSession();
        sess.SetChartSeriesName(1, "Forecast");
        sess.SelectedChart!.Series[1].Name.Should().Be("Forecast");
    }

    [Fact]
    public void EditingSession_AddChartSeries_KeepsMatrixRectangular()
    {
        var sess = MakeSession();
        sess.AddChartSeries("NewSeries");
        var chart = sess.SelectedChart!;
        foreach (var s in chart.Series)
            s.Values.Should().HaveCount(chart.Categories.Count, "matrix stays rectangular");
    }

    [Fact]
    public void EditingSession_RemoveChartSeries_IsUndoable()
    {
        var sess = MakeSession();
        sess.RemoveChartSeries(0);
        sess.SelectedChart!.Series.Should().HaveCount(1);
        sess.Undo();
        sess.SelectedChart!.Series.Should().HaveCount(2);
    }

    [Fact]
    public void EditingSession_MoveChartSeries_IsUndoable()
    {
        var sess = MakeSession();
        sess.MoveChartSeries(1, 0);
        sess.SelectedChart!.Series[0].Name.Should().Be("Budget");
        sess.Undo();
        sess.SelectedChart.Series[0].Name.Should().Be("Sales");
    }

    [Fact]
    public void EditingSession_AddChartCategory_GrowsAllSeries()
    {
        var sess = MakeSession();
        sess.AddChartCategory("Q4");
        var chart = sess.SelectedChart!;
        foreach (var s in chart.Series)
            s.Values.Should().HaveCount(chart.Categories.Count);
    }

    [Fact]
    public void EditingSession_RemoveChartCategory_ShrinksAllSeries()
    {
        var sess = MakeSession();
        sess.RemoveChartCategory(2);
        var chart = sess.SelectedChart!;
        chart.Categories.Should().HaveCount(2);
        foreach (var s in chart.Series)
            s.Values.Should().HaveCount(2);
    }

    [Fact]
    public void EditingSession_ReplaceChartData_IsSingleUndoStep()
    {
        var sess = MakeSession();
        sess.ReplaceChartData(
            new[] { "A", "B" },
            new[] { "Only" },
            new[] { new[] { 1.0, 2.0 } }.Select(v => (IEnumerable<double>)v));

        // One undo should restore everything.
        sess.Undo();
        sess.SelectedChart!.Categories.Should().Equal("Q1", "Q2", "Q3");
        sess.SelectedChart.Series.Should().HaveCount(2);
    }

    [Fact]
    public void EditingSession_ChangeSelectedChartType_SeedsCoordinatesAndUndoRestoresType()
    {
        var sess = MakeSession();

        sess.ChangeSelectedChartType(ChartType.Scatter).Should().BeTrue();
        var scatter = sess.SelectedChart!;
        scatter.ChartType.Should().Be(ChartType.Scatter);
        scatter.Series.Should().OnlyContain(series => series.XValues.Count == scatter.Categories.Count);
        scatter.Series.SelectMany(series => series.XValues).Should().NotContainNulls();

        sess.Undo();
        sess.SelectedChart!.ChartType.Should().Be(ChartType.ColumnClustered);
        sess.SelectedChart.Series.Should().OnlyContain(series => series.XValues.Count == 0);
    }

    [Fact]
    public void EditingSession_ChangedEvent_FiredOnChartEdit()
    {
        var sess = MakeSession();
        int fired = 0;
        sess.Changed += () => fired++;
        sess.SetChartValue(0, 0, 1.0);
        fired.Should().BeGreaterThan(0);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // W6 — ReplaceChartDataCommand undo must preserve gap (null) values
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// W6 regression: a gap (null at index 2) in the original data must survive
    /// ReplaceChartData + Undo — it must come back as null, not 0.0.
    /// </summary>
    [Fact]
    public void W6_ReplaceChartData_Undo_RestoresGapAsNull()
    {
        var p     = new Presentation();
        var slide = new Slide();

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });

        var s1 = new ChartSeries { Name = "Gap Series" };
        // Q3 is a gap (null) — the reader would produce this for a missing <c:pt idx="2">
        s1.Values.AddRange(new double?[] { 10.0, 20.0, null });
        chart.Series.Add(s1);

        var shape = new SlideShape
        {
            Id = 1, Name = "C", Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 1, ExtentCyEmu = 1,
            Chart = chart
        };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);
        var bus = new PresentationCommandBus(p);

        // Replace with new data (no gap in new data).
        bus.Execute(new ReplaceChartDataCommand(
            0, 1,
            new[] { "Q1", "Q2", "Q3" },
            new[] { "Gap Series" },
            new[] { new double?[] { 11.0, 22.0, 33.0 } }.Select(r => (IEnumerable<double?>)r)));

        // Undo → original gap must be restored.
        bus.Undo();

        var restoredValues = p.Slides[0].Shapes[0].Chart!.Series[0].Values;
        restoredValues.Should().HaveCount(3);
        restoredValues[0].Should().Be(10.0);
        restoredValues[1].Should().Be(20.0);
        restoredValues[2].Should().BeNull("gap was null before the edit and must be null after undo, not 0.0");
    }

    /// <summary>
    /// W6 regression: verifies that the nullable overload on EditingSession also preserves gaps.
    /// </summary>
    [Fact]
    public void W6_EditingSession_ReplaceChartData_NullableOverload_PreservesGaps()
    {
        var p     = new Presentation();
        var slide = new Slide();

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "A", "B", "C" });

        var s1 = new ChartSeries { Name = "S1" };
        s1.Values.AddRange(new double?[] { 1.0, null, 3.0 });  // B is a gap
        chart.Series.Add(s1);

        var shape = new SlideShape
        {
            Id = 2, Name = "C2", Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 1, ExtentCyEmu = 1,
            Chart = chart
        };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);
        var bus  = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);
        sess.Select(2);

        // Apply via nullable overload.
        sess.ReplaceChartData(
            new[] { "A", "B", "C" },
            new[] { "S1" },
            new[] { new double?[] { 9.0, null, 7.0 } }.Select(r => (IEnumerable<double?>)r));

        // The new data has a gap at B.
        sess.SelectedChart!.Series[0].Values[1].Should().BeNull("null passed in nullable overload stays null");

        // Undo restores original gap.
        sess.Undo();
        sess.SelectedChart.Series[0].Values[1].Should().BeNull("original gap at B survives undo");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // W7 — Dialog working-copy nullability (tested at command/payload level)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// W7 regression: simulates "open dialog, make no value changes, press OK" —
    /// the payload issued to ReplaceChartDataCommand must preserve the original null.
    ///
    /// This tests the command/model path; the WPF dialog UI itself is tested in
    /// ChartDataDialogTests (StaFact / WPF-required project).
    /// </summary>
    [Fact]
    public void W7_ReplaceChartData_WithNullPayload_GapPreservedInModel()
    {
        var p     = new Presentation();
        var slide = new Slide();

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "X", "Y" });

        var s = new ChartSeries { Name = "Sparse" };
        s.Values.AddRange(new double?[] { 5.0, null });  // Y is a gap
        chart.Series.Add(s);

        var shape = new SlideShape
        {
            Id = 3, Name = "C3", Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 1, ExtentCyEmu = 1,
            Chart = chart
        };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);
        var bus = new PresentationCommandBus(p);

        // Simulate: dialog read the chart values (ToList() preserving nulls), pressed OK
        // with no edits — the same nullable payload goes back into the command.
        var workingValues = chart.Series.Select(sr => sr.Values.ToList()).ToList();  // List<List<double?>>
        bus.Execute(new ReplaceChartDataCommand(
            0, 3,
            chart.Categories.ToList(),
            chart.Series.Select(sr => sr.Name),
            workingValues.Select(sv => (IEnumerable<double?>)sv)));

        // Gap must still be null after "no-op" dialog OK.
        p.Slides[0].Shapes[0].Chart!.Series[0].Values[1]
            .Should().BeNull("a gap that the dialog didn't touch must remain null after OK");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // W8 — Series FillColor / PointColors restored on remove-then-undo
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// W8 regression: a series with a custom FillColor that is removed during the dialog
    /// edit (the new payload has fewer series) must have its FillColor restored on Undo.
    /// </summary>
    [Fact]
    public void W8_ReplaceChartData_Undo_RestoresFillColor()
    {
        var p     = new Presentation();
        var slide = new Slide();

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Q1", "Q2" });

        var customColor = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00));

        var s1 = new ChartSeries { Name = "Red Series", FillColor = customColor };
        s1.Values.AddRange(new double?[] { 10.0, 20.0 });
        chart.Series.Add(s1);

        var s2 = new ChartSeries { Name = "Normal Series" };
        s2.Values.AddRange(new double?[] { 5.0, 15.0 });
        chart.Series.Add(s2);

        var shape = new SlideShape
        {
            Id = 4, Name = "C4", Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 1, ExtentCyEmu = 1,
            Chart = chart
        };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);
        var bus = new PresentationCommandBus(p);

        // Edit: remove s1 (keep only "Normal Series") — simulates user deleting the first series.
        bus.Execute(new ReplaceChartDataCommand(
            0, 4,
            new[] { "Q1", "Q2" },
            new[] { "Normal Series" },
            new[] { new double?[] { 5.0, 15.0 } }.Select(r => (IEnumerable<double?>)r)));

        chart.Series.Should().HaveCount(1, "only one series after edit");

        // Undo → s1 (with its FillColor) must come back.
        bus.Undo();

        var restored = p.Slides[0].Shapes[0].Chart!;
        restored.Series.Should().HaveCount(2, "both series restored after undo");
        restored.Series[0].Name.Should().Be("Red Series");
        restored.Series[0].FillColor.Should().NotBeNull("FillColor must be restored on undo");
        restored.Series[0].FillColor!.Resolved.R.Should().Be(0xFF, "restored FillColor is red");
    }

    /// <summary>
    /// W8 regression: a series with per-point PointColors (pie chart style) that is removed
    /// during an edit must have its PointColors restored on Undo.
    /// </summary>
    [Fact]
    public void W8_ReplaceChartData_Undo_RestoresPointColors()
    {
        var p     = new Presentation();
        var slide = new Slide();

        var chart = new ChartShape { ChartType = ChartType.Pie };
        chart.Categories.AddRange(new[] { "Slice A", "Slice B", "Slice C" });

        var ptColor = new ThemeAwareColor(new SrgbColor(0x00, 0x80, 0x00));
        var s = new ChartSeries { Name = "Pie" };
        s.Values.AddRange(new double?[] { 40.0, 35.0, 25.0 });
        s.PointColors[1] = ptColor;  // Slice B has a custom point color
        chart.Series.Add(s);

        var shape = new SlideShape
        {
            Id = 5, Name = "C5", Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 1, ExtentCyEmu = 1,
            Chart = chart
        };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);
        var bus = new PresentationCommandBus(p);

        // Edit: replace all values (series count unchanged, but values differ).
        bus.Execute(new ReplaceChartDataCommand(
            0, 5,
            new[] { "Slice A", "Slice B", "Slice C" },
            new[] { "Pie" },
            new[] { new double?[] { 50.0, 30.0, 20.0 } }.Select(r => (IEnumerable<double?>)r)));

        // Undo → PointColors must be restored.
        bus.Undo();

        var restored = p.Slides[0].Shapes[0].Chart!.Series[0];
        restored.PointColors.Should().ContainKey(1, "point color at index 1 must survive undo");
        restored.PointColors[1].Resolved.G.Should().Be(0x80, "point color is green");
    }

    // ─── BV2: scatter/bubble X value axis axPos ───────────────────────────────

    /// <summary>
    /// BV2: scatter and bubble charts emit two c:valAx elements (X axis axId=1, Y axis axId=2).
    /// The X axis must have axPos="b" (bottom), the Y axis axPos="l" (left).
    /// Both at "l" → malformed layout that can trigger PowerPoint's repair prompt.
    /// </summary>
    [Theory]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.Bubble)]
    public void BV2_ScatterBubble_XValAx_HasAxPosBottom_YValAx_HasAxPosLeft(ChartType chartType)
    {
        var p     = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape { ChartType = chartType };
        if (chartType == ChartType.Scatter)
        {
            var s = new ChartSeries { Name = "Series1" };
            s.XValues.AddRange(new double?[] { 1, 2, 3 });
            s.Values.AddRange(new double?[]  { 4, 5, 6 });
            chart.Series.Add(s);
        }
        else // Bubble
        {
            var s = new ChartSeries { Name = "Bubbles" };
            s.XValues.AddRange(new double?[]     { 1, 2, 3 });
            s.Values.AddRange(new double?[]      { 2, 4, 1 });
            s.BubbleSizes.AddRange(new double?[] { 5, 10, 3 });
            chart.Series.Add(s);
        }
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "C", Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 5000000, ExtentCyEmu = 3000000,
            Chart = chart
        });
        p.Slides.Add(slide);

        // Write to in-memory stream and inspect the chart XML directly.
        using var ms = new System.IO.MemoryStream();
        PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        var chartEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/charts/chart", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        chartEntry.Should().NotBeNull("chart XML must exist in the PPTX");
        using var entryStream = chartEntry!.Open();
        var doc = System.Xml.Linq.XDocument.Load(entryStream);
        System.Xml.Linq.XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";

        // Collect all c:valAx elements and their axId + axPos values.
        var valAxEls = doc.Descendants(c + "valAx").ToList();
        valAxEls.Should().HaveCount(2, $"{chartType} must emit exactly two c:valAx elements");

        // axId=1 → X axis → axPos must be "b"
        var xAx = valAxEls.FirstOrDefault(el =>
            el.Element(c + "axId")?.Attribute("val")?.Value == "1");
        xAx.Should().NotBeNull("X value axis (axId=1) must be present");
        xAx!.Element(c + "axPos")?.Attribute("val")?.Value
            .Should().Be("b",
            $"BV2: {chartType} X value axis (axId=1) must have axPos=\"b\" (bottom), not \"l\"");

        // axId=2 → Y axis → axPos must be "l"
        var yAx = valAxEls.FirstOrDefault(el =>
            el.Element(c + "axId")?.Attribute("val")?.Value == "2");
        yAx.Should().NotBeNull("Y value axis (axId=2) must be present");
        yAx!.Element(c + "axPos")?.Attribute("val")?.Value
            .Should().Be("l",
            $"BV2: {chartType} Y value axis (axId=2) must have axPos=\"l\" (left)");
    }

    [Fact]
    public void SetChartDisplayOptions_ChangesRoundTripFieldsAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Title = "Old title";
        chart.HasAutomaticTitle = true;
        chart.Legend = LegendPosition.Right;
        chart.DataLabels = new ChartDataLabels
        {
            ShowCategoryName = true,
            ShowValue = false,
            Position = DataLabelPosition.Center,
            NumberFormat = "0.0",
        };
        chart.CategoryAxis.HasMajorGridlines = true;
        chart.ValueAxis.HasMajorGridlines = false;
        chart.BarGapWidthPercent = 180;
        chart.BarOverlapPercent = -20;
        chart.DisplayBlanksAs = ChartDisplayBlanksAs.Gap;
        chart.ShowDataLabelsOverMaximum = null;
        chart.VaryColors = false;

        bus.Execute(new SetChartDisplayOptionsCommand(
            0,
            id,
            new ChartDisplayOptions(
                "Revenue",
                LegendPosition.Bottom,
                true,
                DataLabelPosition.OutsideEnd,
                false,
                true,
                true,
                true,
                false,
                true,
                "0.0%",
                " | ",
                40,
                55,
                ChartDisplayBlanksAs.Zero,
                true,
                true,
                true,
                LabelTextStyle: new ChartTextStyle
                {
                    FontFamily = "Aptos",
                    FontSizePt = 9,
                    Bold = true,
                    Italic = false,
                    Color = new ThemeAwareColor(SrgbColor.FromRgb(0x2F5496)),
                },
                ShowBubbleSize: true,
                TitleOverlay: true,
                PlotVisibleOnly: false,
                RoundedCorners: true)));

        chart.Title.Should().Be("Revenue");
        chart.HasAutomaticTitle.Should().BeFalse();
        chart.TitleOverlay.Should().BeTrue();
        chart.PlotVisibleOnly.Should().BeFalse();
        chart.RoundedCorners.Should().BeTrue();
        chart.Legend.Should().Be(LegendPosition.Bottom);
        chart.DataLabels!.ShowCategoryName.Should().BeTrue("existing label components are preserved");
        chart.DataLabels.ShowValue.Should().BeTrue();
        chart.DataLabels.ShowPercent.Should().BeTrue();
        chart.DataLabels.ShowSeriesName.Should().BeFalse();
        chart.DataLabels.ShowLegendKey.Should().BeTrue();
        chart.DataLabels.ShowBubbleSize.Should().BeTrue();
        chart.DataLabels.NumberFormat.Should().Be("0.0%");
        chart.DataLabels.Separator.Should().Be(" | ");
        chart.DataLabels.Position.Should().Be(DataLabelPosition.OutsideEnd);
        chart.DataLabels.TextStyle.Should().NotBeNull();
        chart.DataLabels.TextStyle!.FontFamily.Should().Be("Aptos");
        chart.DataLabels.TextStyle.FontSizePt.Should().Be(9);
        chart.DataLabels.TextStyle.Bold.Should().BeTrue();
        chart.DataLabels.TextStyle.Italic.Should().BeFalse();
        chart.DataLabels.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));
        chart.CategoryAxis.HasMajorGridlines.Should().BeFalse();
        chart.ValueAxis.HasMajorGridlines.Should().BeTrue();
        chart.BarGapWidthPercent.Should().Be(40);
        chart.BarOverlapPercent.Should().Be(55);
        chart.DisplayBlanksAs.Should().Be(ChartDisplayBlanksAs.Zero);
        chart.ShowDataLabelsOverMaximum.Should().BeTrue();
        chart.VaryColors.Should().BeTrue();
        chart.LegendOverlay.Should().BeTrue();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var reloaded = PptxPackageReader.Read(stream);
        var roundTripped = reloaded.Slides[0].Shapes[0].Chart!;
        roundTripped.Title.Should().Be("Revenue");
        roundTripped.TitleOverlay.Should().BeTrue();
        roundTripped.PlotVisibleOnly.Should().BeFalse();
        roundTripped.RoundedCorners.Should().BeTrue();
        roundTripped.Legend.Should().Be(LegendPosition.Bottom);
        roundTripped.DataLabels!.ShowValue.Should().BeTrue();
        roundTripped.DataLabels.ShowPercent.Should().BeTrue();
        roundTripped.DataLabels.ShowCategoryName.Should().BeTrue();
        roundTripped.DataLabels.ShowLegendKey.Should().BeTrue();
        roundTripped.DataLabels.ShowBubbleSize.Should().BeTrue();
        roundTripped.DataLabels.NumberFormat.Should().Be("0.0%");
        roundTripped.DataLabels.Separator.Should().Be(" | ");
        roundTripped.DataLabels.TextStyle.Should().NotBeNull();
        roundTripped.DataLabels.TextStyle!.FontFamily.Should().Be("Aptos");
        roundTripped.DataLabels.TextStyle.FontSizePt.Should().Be(9);
        roundTripped.DataLabels.TextStyle.Bold.Should().BeTrue();
        roundTripped.DataLabels.TextStyle.Italic.Should().BeFalse();
        roundTripped.DataLabels.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));
        roundTripped.BarGapWidthPercent.Should().Be(40);
        roundTripped.BarOverlapPercent.Should().Be(55);
        roundTripped.DisplayBlanksAs.Should().Be(ChartDisplayBlanksAs.Zero);
        roundTripped.ShowDataLabelsOverMaximum.Should().BeTrue();
        roundTripped.VaryColors.Should().BeTrue();
        roundTripped.LegendOverlay.Should().BeTrue();
        roundTripped.CategoryAxis.HasMajorGridlines.Should().BeFalse();
        roundTripped.ValueAxis.HasMajorGridlines.Should().BeTrue();

        bus.Undo();
        chart.Title.Should().Be("Old title");
        chart.HasAutomaticTitle.Should().BeTrue();
        chart.TitleOverlay.Should().BeNull();
        chart.PlotVisibleOnly.Should().BeNull();
        chart.RoundedCorners.Should().BeNull();
        chart.Legend.Should().Be(LegendPosition.Right);
        chart.DataLabels!.ShowValue.Should().BeFalse();
        chart.DataLabels.ShowCategoryName.Should().BeTrue();
        chart.DataLabels.ShowPercent.Should().BeFalse();
        chart.DataLabels.ShowSeriesName.Should().BeFalse();
        chart.DataLabels.ShowLegendKey.Should().BeFalse();
        chart.DataLabels.ShowBubbleSize.Should().BeFalse();
        chart.DataLabels.NumberFormat.Should().Be("0.0");
        chart.DataLabels.Separator.Should().BeNull();
        chart.DataLabels.Position.Should().Be(DataLabelPosition.Center);
        chart.DataLabels.TextStyle.Should().BeNull();
        chart.CategoryAxis.HasMajorGridlines.Should().BeTrue();
        chart.ValueAxis.HasMajorGridlines.Should().BeFalse();
        chart.BarGapWidthPercent.Should().Be(180);
        chart.BarOverlapPercent.Should().Be(-20);
        chart.DisplayBlanksAs.Should().Be(ChartDisplayBlanksAs.Gap);
        chart.ShowDataLabelsOverMaximum.Should().BeNull();
        chart.VaryColors.Should().BeFalse();
        chart.LegendOverlay.Should().BeNull();
        chart.HasHighLowLines.Should().BeTrue();
    }

    [Fact]
    public void SetChartDisplayOptions_ChangesChartStyleAndUndoRestoresIt()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.StyleId = 2;

        bus.Execute(new SetChartDisplayOptionsCommand(
            0,
            id,
            new ChartDisplayOptions(
                null,
                null,
                false,
                DataLabelPosition.OutsideEnd,
                false,
                false,
                StyleId: 102)));

        chart.StyleId.Should().Be(102);
        bus.Undo();
        chart.StyleId.Should().Be(2);
        bus.Redo();
        chart.StyleId.Should().Be(102);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!.StyleId.Should().Be(102);
    }

    [Fact]
    public void SetChartDisplayOptions_StockHighLowLines_RoundTripsAndUndo()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ChartType = ChartType.Stock;
        chart.HasHighLowLines = true;

        bus.Execute(new SetChartDisplayOptionsCommand(
            0,
            id,
            new ChartDisplayOptions(
                null,
                null,
                false,
                DataLabelPosition.BestFit,
                false,
                false,
                HighLowLines: false)));

        chart.HasHighLowLines.Should().BeFalse();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var reloaded = PptxPackageReader.Read(stream);
        reloaded.Slides[0].Shapes[0].Chart!.HasHighLowLines.Should().BeFalse();

        bus.Undo();
        chart.HasHighLowLines.Should().BeTrue();
    }

    [Fact]
    public void SetChartDisplayOptions_CreatesDataLabelsForNonValueComponents()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.DataLabels = null;

        bus.Execute(new SetChartDisplayOptionsCommand(
            0,
            id,
            new ChartDisplayOptions(
                null,
                null,
                false,
                DataLabelPosition.BestFit,
                false,
                false,
                false,
                true,
                false,
                false,
                null,
                " / ",
                LabelTextStyle: new ChartTextStyle { FontFamily = "Aptos", FontSizePt = 9 })));

        chart.DataLabels.Should().NotBeNull();
        chart.DataLabels!.ShowCategoryName.Should().BeTrue();
        chart.DataLabels.ShowValue.Should().BeFalse();
        chart.DataLabels.Separator.Should().Be(" / ");
        chart.DataLabels.TextStyle.Should().NotBeNull();
        chart.DataLabels.TextStyle!.FontFamily.Should().Be("Aptos");
    }

    [Fact]
    public void SetChartDataTableOptions_ChangesRoundTripFieldsAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.DataTable = new ChartDataTableSettings
        {
            ShowHorizontalBorder = true,
            ShowVerticalBorder = false,
            ShowOutlineBorder = true,
            ShowLegendKeys = false,
        };

        bus.Execute(new SetChartDataTableOptionsCommand(
            0,
            id,
            new ChartDataTableOptions(true, false, true, false, true)));

        chart.DataTable.Should().NotBeNull();
        chart.DataTable!.ShowHorizontalBorder.Should().BeFalse();
        chart.DataTable.ShowVerticalBorder.Should().BeTrue();
        chart.DataTable.ShowOutlineBorder.Should().BeFalse();
        chart.DataTable.ShowLegendKeys.Should().BeTrue();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var reloaded = PptxPackageReader.Read(stream);
        var roundTripped = reloaded.Slides[0].Shapes[0].Chart!.DataTable;
        roundTripped.Should().NotBeNull();
        roundTripped!.ShowHorizontalBorder.Should().BeFalse();
        roundTripped.ShowVerticalBorder.Should().BeTrue();
        roundTripped.ShowOutlineBorder.Should().BeFalse();
        roundTripped.ShowLegendKeys.Should().BeTrue();

        bus.Undo();
        chart.DataTable.Should().NotBeNull();
        chart.DataTable!.ShowHorizontalBorder.Should().BeTrue();
        chart.DataTable.ShowVerticalBorder.Should().BeFalse();
        chart.DataTable.ShowOutlineBorder.Should().BeTrue();
        chart.DataTable.ShowLegendKeys.Should().BeFalse();
    }

    [Fact]
    public void SetChartDataTableOptions_AppliesAuthoredFillBorderAndTextStyle()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.DataTable = new ChartDataTableSettings();

        bus.Execute(new SetChartDataTableOptionsCommand(
            0,
            id,
            new ChartDataTableOptions(true, true, true, true, false,
                "#F2F2F2", "#4472C4", 1.25, "#112233", 9, "Aptos", true, false)));

        var dataTable = chart.DataTable!;
        ((ShapeFill.Solid)dataTable.BackgroundFill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xF2F2F2));
        var border = (ShapeOutline.Visible)dataTable.BorderOutline!;
        border.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
        border.WidthPt.Should().Be(1.25);
        dataTable.TextStyle!.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x112233));
        dataTable.TextStyle.FontSizePt.Should().Be(9);
        dataTable.TextStyle.FontFamily.Should().Be("Aptos");
        dataTable.TextStyle.Bold.Should().BeTrue();
        dataTable.TextStyle.Italic.Should().BeFalse();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!.DataTable;
        roundTripped.Should().NotBeNull();
        ((ShapeFill.Solid)roundTripped!.BackgroundFill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xF2F2F2));
        ((ShapeOutline.Visible)roundTripped.BorderOutline!).WidthPt.Should().Be(1.25);
        roundTripped.TextStyle!.FontFamily.Should().Be("Aptos");
        roundTripped.TextStyle.FontSizePt.Should().Be(9);
    }

    [Fact]
    public void SetChartDataTableOptions_BlankStyleFieldsPreserveExistingAdvancedStyles()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        var gradient = new ShapeFill.Gradient(new[]
        {
            new GradientStop(0, new ThemeAwareColor(SrgbColor.FromRgb(0xFFFFFF))),
            new GradientStop(1, new ThemeAwareColor(SrgbColor.FromRgb(0xD9E2F3))),
        });
        var textStyle = new ChartTextStyle { FontFamily = "Calibri", FontSizePt = 11, Bold = true };
        var gradientBorder = new ShapeOutline.GradientVisible(gradient, 1.5);
        chart.DataTable = new ChartDataTableSettings
        {
            BackgroundFill = gradient,
            BorderOutline = gradientBorder,
            TextStyle = textStyle,
        };

        bus.Execute(new SetChartDataTableOptionsCommand(
            0, id, new ChartDataTableOptions(true, false, true, false, true)));

        chart.DataTable!.BackgroundFill.Should().BeSameAs(gradient);
        chart.DataTable.BorderOutline.Should().BeSameAs(gradientBorder);
        chart.DataTable.TextStyle!.FontFamily.Should().Be("Calibri");
        chart.DataTable.TextStyle.FontSizePt.Should().Be(11);
        chart.DataTable.TextStyle.Bold.Should().BeTrue();
    }

    [Fact]
    public void SetChartAxisOptions_ChangesRoundTripFieldsAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ValueAxis.Title = "Old axis";
        chart.ValueAxis.TitleStyle = new ChartTextStyle
        {
            FontFamily = "Calibri",
            FontSizePt = 11,
            Bold = false,
        };
        chart.ValueAxis.Delete = true;
        chart.ValueAxis.Min = 0;
        chart.ValueAxis.Max = 200;
        chart.ValueAxis.MajorUnit = 50;
        chart.ValueAxis.NumberFormatCode = "0";
        chart.ValueAxis.NumberFormatSourceLinked = true;
        chart.ValueAxis.HasMajorGridlines = true;
        chart.ValueAxis.MajorTickMark = ChartTickMark.Cross;
        chart.ValueAxis.MinorTickMark = ChartTickMark.Out;
        chart.ValueAxis.TickLabelPosition = ChartTickLabelPosition.High;
        chart.ValueAxis.Crosses = ChartAxisCrossing.Max;
        chart.ValueAxis.CrossesAt = 40;
        chart.ValueAxis.CrossBetween = ChartCrossBetween.Between;
        chart.ValueAxis.LabelAlignment = ChartLabelAlignment.Left;
        chart.ValueAxis.LabelOffsetPercent = 20;
        chart.ValueAxis.NoMultiLevelLabels = false;
        chart.ValueAxis.AutoCrossing = true;

        bus.Execute(new SetChartAxisOptionsCommand(
            0,
            id,
            new ChartAxisOptions(
                ChartAxisKind.Value, "Revenue", 10, 90, 10, 5, "$#,##0", false,
                ChartTickMark.Out, ChartTickMark.In, ChartTickLabelPosition.NextTo,
                ChartAxisCrossing.Min, 10, false,
                ChartCrossBetween.MidCat, ChartLabelAlignment.Right,
                35, true, false, true, true,
                new ChartTextStyle
                {
                    FontFamily = "Aptos Display",
                    FontSizePt = 15,
                    Bold = true,
                    Italic = true,
                    Color = new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
                })));

        chart.ValueAxis.Title.Should().Be("Revenue");
        chart.ValueAxis.Delete.Should().BeTrue();
        chart.ValueAxis.Min.Should().Be(10);
        chart.ValueAxis.Max.Should().Be(90);
        chart.ValueAxis.MajorUnit.Should().Be(10);
        chart.ValueAxis.MinorUnit.Should().Be(5);
        chart.ValueAxis.NumberFormatCode.Should().Be("$#,##0");
        chart.ValueAxis.NumberFormatSourceLinked.Should().BeFalse();
        chart.ValueAxis.HasMajorGridlines.Should().BeFalse();
        chart.ValueAxis.HasMinorGridlines.Should().BeTrue();
        chart.ValueAxis.MajorTickMark.Should().Be(ChartTickMark.Out);
        chart.ValueAxis.MinorTickMark.Should().Be(ChartTickMark.In);
        chart.ValueAxis.TickLabelPosition.Should().Be(ChartTickLabelPosition.NextTo);
        chart.ValueAxis.Crosses.Should().BeNull();
        chart.ValueAxis.CrossesAt.Should().Be(10);
        chart.ValueAxis.CrossBetween.Should().Be(ChartCrossBetween.MidCat);
        chart.ValueAxis.LabelAlignment.Should().Be(ChartLabelAlignment.Right);
        chart.ValueAxis.LabelOffsetPercent.Should().Be(35);
        chart.ValueAxis.NoMultiLevelLabels.Should().BeTrue();
        chart.ValueAxis.AutoCrossing.Should().BeFalse();
        chart.ValueAxis.ReverseOrder.Should().BeTrue();
        chart.ValueAxis.TitleStyle!.FontFamily.Should().Be("Aptos Display");
        chart.ValueAxis.TitleStyle.FontSizePt.Should().Be(15);
        chart.ValueAxis.TitleStyle.Bold.Should().BeTrue();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.ValueAxis.Title.Should().Be("Revenue");
        roundTripped.ValueAxis.Delete.Should().BeTrue();
        roundTripped.ValueAxis.Min.Should().Be(10);
        roundTripped.ValueAxis.Max.Should().Be(90);
        roundTripped.ValueAxis.MajorUnit.Should().Be(10);
        roundTripped.ValueAxis.MinorUnit.Should().Be(5);
        roundTripped.ValueAxis.TitleStyle!.FontFamily.Should().Be("Aptos Display");
        roundTripped.ValueAxis.TitleStyle.FontSizePt.Should().Be(15);
        roundTripped.ValueAxis.TitleStyle.Bold.Should().BeTrue();
        roundTripped.ValueAxis.TitleStyle.Italic.Should().BeTrue();
        roundTripped.ValueAxis.TitleStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        roundTripped.ValueAxis.NumberFormatCode.Should().Be("$#,##0");
        roundTripped.ValueAxis.HasMajorGridlines.Should().BeFalse();
        roundTripped.ValueAxis.HasMinorGridlines.Should().BeTrue();
        roundTripped.ValueAxis.MajorTickMark.Should().Be(ChartTickMark.Out);
        roundTripped.ValueAxis.MinorTickMark.Should().Be(ChartTickMark.In);
        roundTripped.ValueAxis.TickLabelPosition.Should().Be(ChartTickLabelPosition.NextTo);
        roundTripped.ValueAxis.Crosses.Should().BeNull();
        roundTripped.ValueAxis.CrossesAt.Should().Be(10);
        roundTripped.ValueAxis.CrossBetween.Should().Be(ChartCrossBetween.MidCat);
        roundTripped.ValueAxis.LabelAlignment.Should().Be(ChartLabelAlignment.Right);
        roundTripped.ValueAxis.LabelOffsetPercent.Should().Be(35);
        roundTripped.ValueAxis.NoMultiLevelLabels.Should().BeTrue();
        roundTripped.ValueAxis.AutoCrossing.Should().BeFalse();
        roundTripped.ValueAxis.ReverseOrder.Should().BeTrue();

        bus.Undo();
        chart.ValueAxis.Title.Should().Be("Old axis");
        chart.ValueAxis.TitleStyle!.FontFamily.Should().Be("Calibri");
        chart.ValueAxis.TitleStyle.FontSizePt.Should().Be(11);
        chart.ValueAxis.TitleStyle.Bold.Should().BeFalse();
        chart.ValueAxis.Delete.Should().BeTrue();
        chart.ValueAxis.Min.Should().Be(0);
        chart.ValueAxis.Max.Should().Be(200);
        chart.ValueAxis.MajorUnit.Should().Be(50);
        chart.ValueAxis.NumberFormatCode.Should().Be("0");
        chart.ValueAxis.NumberFormatSourceLinked.Should().BeTrue();
        chart.ValueAxis.HasMajorGridlines.Should().BeTrue();
        chart.ValueAxis.HasMinorGridlines.Should().BeFalse();
        chart.ValueAxis.MajorTickMark.Should().Be(ChartTickMark.Cross);
        chart.ValueAxis.MinorTickMark.Should().Be(ChartTickMark.Out);
        chart.ValueAxis.TickLabelPosition.Should().Be(ChartTickLabelPosition.High);
        chart.ValueAxis.Crosses.Should().Be(ChartAxisCrossing.Max);
        chart.ValueAxis.CrossesAt.Should().Be(40);
        chart.ValueAxis.CrossBetween.Should().Be(ChartCrossBetween.Between);
        chart.ValueAxis.LabelAlignment.Should().Be(ChartLabelAlignment.Left);
        chart.ValueAxis.LabelOffsetPercent.Should().Be(20);
        chart.ValueAxis.NoMultiLevelLabels.Should().BeFalse();
        chart.ValueAxis.AutoCrossing.Should().BeTrue();
        chart.ValueAxis.ReverseOrder.Should().BeFalse();
    }

    [Fact]
    public void SetChartAxisOptions_CreatesAndUndoRemovesSecondaryAxis()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.SecondaryValueAxis.Should().BeNull();

        bus.Execute(new SetChartAxisOptionsCommand(
            0,
            id,
            new ChartAxisOptions(
                ChartAxisKind.SecondaryValue, "Margin", 0, 100, 25, null, "0%", false)));

        chart.SecondaryValueAxis.Should().NotBeNull();
        chart.SecondaryValueAxis!.Title.Should().Be("Margin");
        chart.SecondaryValueAxis.Max.Should().Be(100);
        chart.SecondaryValueAxis.NumberFormatCode.Should().Be("0%");

        bus.Undo();
        chart.SecondaryValueAxis.Should().BeNull();
    }

    [Fact]
    public void SetChartSeriesOptions_ChangesRoundTripFieldsAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Series[1].SmoothLine = true;
        chart.Series[1].OnSecondaryAxis = true;
        chart.Series[1].InvertIfNegative = true;
        chart.Series[1].LineStyle = new ChartLineStyle
        {
            Color = new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4)),
            WidthPt = 1.5,
            Dash = OutlineDash.Dash,
        };
        chart.Series[1].MarkerStyle = new ChartMarkerStyle
        {
            Symbol = ChartMarkerSymbol.Circle,
            SizePt = 6,
            NoStroke = true,
        };
        chart.Series[1].Fill = new ShapeFill.Gradient(
            [
                new GradientStop(0, new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4))),
                new GradientStop(1, new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79))),
            ]);

        bus.Execute(new SetChartSeriesOptionsCommand(
            0,
            id,
            new ChartSeriesOptions(
                1, false, false, 2.25, ChartMarkerSymbol.Diamond, 8,
                new ThemeAwareColor(SrgbColor.FromRgb(0xC00000)),
                null,
                new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
                OutlineDash.DashDot,
                true,
                new ChartDataLabels
                {
                    ShowValue = true,
                    ShowCategoryName = true,
                    ShowLegendKey = true,
                    Position = DataLabelPosition.InsideEnd,
                    NumberFormat = "0.0%",
                    Separator = " | ",
                    TextStyle = new ChartTextStyle
                    {
                        FontFamily = "Aptos",
                        FontSizePt = 9,
                        Bold = true,
                        Italic = false,
                        Color = new ThemeAwareColor(SrgbColor.FromRgb(0x2F5496)),
                    },
                    ShowBubbleSize = true,
                },
                ErrorBars: new ChartErrorBars
                {
                    Direction = ChartErrorDirection.X,
                    BarType = ChartErrorBarType.Minus,
                    ValueType = ChartErrorValueType.Percentage,
                    Value = 7.5,
                    NoEndCap = true,
                },
                Trendline: new ChartTrendline
                {
                    Type = ChartTrendlineType.Polynomial,
                    PolynomialOrder = 3,
                    Forward = 1.5,
                    Backward = 0.5,
                    DisplayEquation = true,
                },
                InvertIfNegative: false)));

        var series = chart.Series[1];
        series.SmoothLine.Should().BeFalse();
        series.OnSecondaryAxis.Should().BeFalse();
        series.InvertIfNegative.Should().BeFalse();
        series.FillColor!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        series.Fill.Should().BeNull();
        series.LineStyle!.WidthPt.Should().Be(2.25);
        series.LineStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        series.LineStyle.Dash.Should().Be(OutlineDash.DashDot);
        series.LineStyle.NoFill.Should().BeTrue();
        series.DataLabels.Should().NotBeNull();
        series.DataLabels!.ShowValue.Should().BeTrue();
        series.DataLabels.ShowCategoryName.Should().BeTrue();
        series.DataLabels.ShowLegendKey.Should().BeTrue();
        series.DataLabels.ShowBubbleSize.Should().BeTrue();
        series.ErrorBars.Should().NotBeNull();
        series.ErrorBars!.Direction.Should().Be(ChartErrorDirection.X);
        series.ErrorBars.BarType.Should().Be(ChartErrorBarType.Minus);
        series.ErrorBars.ValueType.Should().Be(ChartErrorValueType.Percentage);
        series.ErrorBars.Value.Should().Be(7.5);
        series.ErrorBars.NoEndCap.Should().BeTrue();
        series.Trendline.Should().NotBeNull();
        series.Trendline!.Type.Should().Be(ChartTrendlineType.Polynomial);
        series.Trendline.PolynomialOrder.Should().Be(3);
        series.Trendline.Forward.Should().Be(1.5);
        series.Trendline.Backward.Should().Be(0.5);
        series.Trendline.DisplayEquation.Should().BeTrue();
        series.DataLabels.Position.Should().Be(DataLabelPosition.InsideEnd);
        series.DataLabels.NumberFormat.Should().Be("0.0%");
        series.DataLabels.Separator.Should().Be(" | ");
        series.MarkerStyle!.Symbol.Should().Be(ChartMarkerSymbol.Diamond);
        series.MarkerStyle.SizePt.Should().Be(8);
        series.MarkerStyle.NoStroke.Should().BeTrue();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.Series[1].SmoothLine.Should().BeFalse();
        roundTripped.Series[1].OnSecondaryAxis.Should().BeFalse();
        roundTripped.Series[1].InvertIfNegative.Should().BeFalse();
        roundTripped.Series[1].FillColor!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        roundTripped.Series[1].Fill.Should().BeNull();
        roundTripped.Series[1].LineStyle!.WidthPt.Should().Be(2.25);
        roundTripped.Series[1].LineStyle!.Color.Should().BeNull();
        roundTripped.Series[1].LineStyle!.Dash.Should().Be(OutlineDash.DashDot);
        roundTripped.Series[1].LineStyle!.NoFill.Should().BeTrue();
        roundTripped.Series[1].DataLabels.Should().NotBeNull();
        var roundTrippedLabels = roundTripped.Series[1].DataLabels!;
        roundTrippedLabels.ShowValue.Should().BeTrue();
        roundTrippedLabels.ShowCategoryName.Should().BeTrue();
        roundTrippedLabels.ShowLegendKey.Should().BeTrue();
        roundTrippedLabels.Position.Should().Be(DataLabelPosition.InsideEnd);
        roundTrippedLabels.NumberFormat.Should().Be("0.0%");
        roundTrippedLabels.Separator.Should().Be(" | ");
        roundTrippedLabels.TextStyle.Should().NotBeNull();
        roundTrippedLabels.TextStyle!.FontFamily.Should().Be("Aptos");
        roundTrippedLabels.TextStyle.FontSizePt.Should().Be(9);
        roundTrippedLabels.TextStyle.Bold.Should().BeTrue();
        roundTrippedLabels.TextStyle.Italic.Should().BeFalse();
        roundTrippedLabels.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));
        roundTripped.Series[1].ErrorBars.Should().NotBeNull();
        var roundTrippedErrorBars = roundTripped.Series[1].ErrorBars!;
        roundTrippedErrorBars.Direction.Should().Be(ChartErrorDirection.X);
        roundTrippedErrorBars.BarType.Should().Be(ChartErrorBarType.Minus);
        roundTrippedErrorBars.ValueType.Should().Be(ChartErrorValueType.Percentage);
        roundTrippedErrorBars.Value.Should().Be(7.5);
        roundTrippedErrorBars.NoEndCap.Should().BeTrue();
        var roundTrippedTrendline = roundTripped.Series[1].Trendline;
        roundTrippedTrendline.Should().NotBeNull();
        roundTrippedTrendline!.Type.Should().Be(ChartTrendlineType.Polynomial);
        roundTrippedTrendline.PolynomialOrder.Should().Be(3);
        roundTrippedTrendline.Forward.Should().Be(1.5);
        roundTrippedTrendline.Backward.Should().Be(0.5);
        roundTrippedTrendline.DisplayEquation.Should().BeTrue();
        var roundTrippedMarker = roundTripped.Series[1].MarkerStyle!;
        roundTrippedMarker.Symbol.Should().Be(ChartMarkerSymbol.Diamond);
        roundTrippedMarker.SizePt.Should().Be(8);

        bus.Undo();
        series.SmoothLine.Should().BeTrue();
        series.OnSecondaryAxis.Should().BeTrue();
        series.InvertIfNegative.Should().BeTrue();
        var revertedLine = series.LineStyle!;
        var revertedMarker = series.MarkerStyle!;
        revertedLine.WidthPt.Should().Be(1.5);
        revertedLine.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
        revertedLine.Dash.Should().Be(OutlineDash.Dash);
        revertedLine.NoFill.Should().BeFalse();
        revertedMarker.Symbol.Should().Be(ChartMarkerSymbol.Circle);
        revertedMarker.SizePt.Should().Be(6);
        revertedMarker.NoStroke.Should().BeTrue();
        series.FillColor.Should().BeNull();
        series.Fill.Should().BeOfType<ShapeFill.Gradient>();
        series.DataLabels.Should().BeNull();
        series.ErrorBars.Should().BeNull();
        series.Trendline.Should().BeNull();
    }

    [Fact]
    public void SetChartSeriesOptions_CreatesSecondaryAxisForNewComboAndUndoRemovesIt()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.SecondaryValueAxis.Should().BeNull();
        chart.Series[1].OnSecondaryAxis.Should().BeFalse();

        bus.Execute(new SetChartSeriesOptionsCommand(
            0,
            id,
            new ChartSeriesOptions(
                1, false, true, null, ChartMarkerSymbol.Auto, null)));

        chart.Series[1].OnSecondaryAxis.Should().BeTrue();
        chart.SecondaryValueAxis.Should().NotBeNull();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.SecondaryValueAxis.Should().NotBeNull();
        roundTripped.Series[1].OnSecondaryAxis.Should().BeTrue();

        bus.Undo();
        chart.Series[1].OnSecondaryAxis.Should().BeFalse();
        chart.SecondaryValueAxis.Should().BeNull();
    }

    [Fact]
    public void SetChartSeriesOptions_AuthorsComboOverrideAndUndoRestoresIt()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;

        bus.Execute(new SetChartSeriesOptionsCommand(
            0,
            id,
            new ChartSeriesOptions(
                1, false, false, null, ChartMarkerSymbol.Auto, null,
                OverrideChartType: ChartType.LineMarkers)));

        chart.Series[1].OverrideChartType.Should().Be(ChartType.LineMarkers);
        chart.Series[1].OnSecondaryAxis.Should().BeTrue();
        chart.SecondaryValueAxis.Should().NotBeNull();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.Series[1].OverrideChartType.Should().Be(ChartType.LineMarkers);
        roundTripped.Series[1].OnSecondaryAxis.Should().BeTrue();

        bus.Undo();
        chart.Series[1].OverrideChartType.Should().BeNull();
        chart.Series[1].OnSecondaryAxis.Should().BeFalse();
        chart.SecondaryValueAxis.Should().BeNull();
    }

    [Fact]
    public void SetChartBubbleOptions_ChangesRoundTripFieldsAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ChartType = ChartType.Bubble;
        chart.BubbleScalePercent = 100;
        chart.BubbleSizeRepresents = BubbleSizeRepresentation.Area;
        chart.ShowNegativeBubbles = false;

        bus.Execute(new SetChartBubbleOptionsCommand(
            0,
            id,
            new ChartBubbleOptions(175, BubbleSizeRepresentation.Width, true)));

        chart.BubbleScalePercent.Should().Be(175);
        chart.BubbleSizeRepresents.Should().Be(BubbleSizeRepresentation.Width);
        chart.ShowNegativeBubbles.Should().BeTrue();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.ChartType.Should().Be(ChartType.Bubble);
        roundTripped.BubbleScalePercent.Should().Be(175);
        roundTripped.BubbleSizeRepresents.Should().Be(BubbleSizeRepresentation.Width);
        roundTripped.ShowNegativeBubbles.Should().BeTrue();

        bus.Undo();
        chart.BubbleScalePercent.Should().Be(100);
        chart.BubbleSizeRepresents.Should().Be(BubbleSizeRepresentation.Area);
        chart.ShowNegativeBubbles.Should().BeFalse();
    }

    [Fact]
    public void SetChartPieOptions_ChangesRoundTripFieldsAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ChartType = ChartType.Doughnut;
        chart.FirstSliceAngleDegrees = 20;
        chart.DoughnutHolePercent = 40;

        bus.Execute(new SetChartPieOptionsCommand(0, id, new ChartPieOptions(225, 68)));

        chart.FirstSliceAngleDegrees.Should().Be(225);
        chart.DoughnutHolePercent.Should().Be(68);
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.ChartType.Should().Be(ChartType.Doughnut);
        roundTripped.FirstSliceAngleDegrees.Should().Be(225);
        roundTripped.DoughnutHolePercent.Should().Be(68);

        bus.Undo();
        chart.FirstSliceAngleDegrees.Should().Be(20);
        chart.DoughnutHolePercent.Should().Be(40);
    }

    [Fact]
    public void SetChartPieOptions_AuthorsOfPieSettingsAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ChartType = ChartType.OfPie;
        chart.OfPieType = OfPieType.Pie;
        chart.OfPieSplitType = OfPieSplitType.Auto;

        bus.Execute(new SetChartPieOptionsCommand(
            0,
            id,
            new ChartPieOptions(0, 40, OfPieType.Bar, OfPieSplitType.Custom, 2, 75, new[] { 1, 3 })));

        chart.OfPieType.Should().Be(OfPieType.Bar);
        chart.OfPieSplitType.Should().Be(OfPieSplitType.Custom);
        chart.OfPieSplitPosition.Should().Be(2);
        chart.OfPieSecondPieSizePercent.Should().Be(75);
        chart.OfPieCustomPointIndices.Should().Equal(1, 3);

        bus.Undo();
        chart.OfPieType.Should().Be(OfPieType.Pie);
        chart.OfPieSplitType.Should().Be(OfPieSplitType.Auto);
        chart.OfPieSplitPosition.Should().BeNull();
        chart.OfPieSecondPieSizePercent.Should().BeNull();
        chart.OfPieCustomPointIndices.Should().BeEmpty();
    }

    [Fact]
    public void OfPieChart_PreservesNativeFamilyAndSplitControlsThroughPptxRoundTrip()
    {
        var (p, _, _) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ChartType = ChartType.OfPie;
        chart.OfPieType = OfPieType.Bar;
        chart.OfPieSplitType = OfPieSplitType.Percent;
        chart.OfPieSplitPosition = 60.5;
        chart.OfPieSecondPieSizePercent = 75;
        chart.OfPieSeriesLinesSpecified = true;
        chart.BarGapWidthPercent = 120;
        chart.RegenerateWorkbookOnSave = true;

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        using (var document = PresentationDocument.Open(stream, false))
        {
            var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
            var chartPart = document.PresentationPart!.SlideParts
                .SelectMany(slidePart => slidePart.ChartParts)
                .Single();
            validator.Validate(chartPart)
                .Where(error => error.ErrorType == ValidationErrorType.Schema)
                .Should().BeEmpty();
        }
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;

        roundTripped.ChartType.Should().Be(ChartType.OfPie);
        roundTripped.OfPieType.Should().Be(OfPieType.Bar);
        roundTripped.OfPieSplitType.Should().Be(OfPieSplitType.Percent);
        roundTripped.OfPieSplitPosition.Should().Be(60.5);
        roundTripped.OfPieSecondPieSizePercent.Should().Be(75);
        roundTripped.OfPieSeriesLinesSpecified.Should().BeTrue();
        roundTripped.BarGapWidthPercent.Should().Be(120);
        roundTripped.Series.Select(series => series.Name).Should().Equal("Sales", "Budget");
    }

    [Fact]
    public void OfPieChart_PreservesCustomSecondPiePointIndicesThroughPptxRoundTrip()
    {
        var (p, _, _) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ChartType = ChartType.OfPie;
        chart.OfPieType = OfPieType.Pie;
        chart.OfPieSplitType = OfPieSplitType.Custom;
        chart.OfPieCustomPointIndices.AddRange(new[] { 1, 3 });
        chart.RegenerateWorkbookOnSave = true;

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        using (var document = PresentationDocument.Open(stream, false))
        {
            var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
            var chartPart = document.PresentationPart!.SlideParts
                .SelectMany(slidePart => slidePart.ChartParts)
                .Single();
            validator.Validate(chartPart)
                .Where(error => error.ErrorType == ValidationErrorType.Schema)
                .Should().BeEmpty();
        }

        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.OfPieSplitType.Should().Be(OfPieSplitType.Custom);
        roundTripped.OfPieCustomPointIndices.Should().Equal(1, 3);
    }

    [Fact]
    public void SetChartPlotStyleOptions_ChangesRoundTripFieldsAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ChartType = ChartType.Scatter;
        chart.ScatterStyle = ScatterStyle.Marker;
        chart.RadarStyle = RadarStyle.Standard;

        bus.Execute(new SetChartPlotStyleOptionsCommand(
            0, id, new ChartPlotStyleOptions(ScatterStyle.SmoothMarker, RadarStyle.Filled)));

        chart.ScatterStyle.Should().Be(ScatterStyle.SmoothMarker);
        chart.RadarStyle.Should().Be(RadarStyle.Filled);
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.ChartType.Should().Be(ChartType.Scatter);
        roundTripped.ScatterStyle.Should().Be(ScatterStyle.SmoothMarker);

        bus.Undo();
        chart.ScatterStyle.Should().Be(ScatterStyle.Marker);
        chart.RadarStyle.Should().Be(RadarStyle.Standard);
    }

    [Fact]
    public void SetChartPlotStyleOptions_RadarRoundTripsStyle()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.ChartType = ChartType.Radar;
        chart.RadarStyle = RadarStyle.Standard;

        bus.Execute(new SetChartPlotStyleOptionsCommand(
            0, id, new ChartPlotStyleOptions(ScatterStyle.Marker, RadarStyle.Filled)));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.ChartType.Should().Be(ChartType.Radar);
        roundTripped.RadarStyle.Should().Be(RadarStyle.Filled);
    }

    [Fact]
    public void SetChartPointOptions_ChangesRoundTripFieldsAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.Series[0].PointColors[1] = new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4));
        chart.Series[0].PointStyles[1] = new ChartPointStyle
        {
            StrokeColor = new ThemeAwareColor(SrgbColor.FromRgb(0x808080)),
            StrokeWidthPt = 0.75,
            Marker = new ChartMarkerStyle { Symbol = ChartMarkerSymbol.Circle, SizePt = 5 },
        };

        bus.Execute(new SetChartPointOptionsCommand(
            0,
            id,
            new ChartPointOptions(
                0,
                1,
                new ThemeAwareColor(SrgbColor.FromRgb(0xC00000)),
                null,
                null,
                2.25,
                ChartMarkerSymbol.Diamond,
                8,
                new ChartDataLabels
                {
                    ShowValue = true,
                    ShowCategoryName = true,
                    ShowLegendKey = true,
                    Position = DataLabelPosition.InsideEnd,
                    NumberFormat = "0.0%",
                    Separator = " | ",
                    TextStyle = new ChartTextStyle
                    {
                        FontFamily = "Aptos",
                        FontSizePt = 9,
                        Bold = true,
                        Italic = false,
                        Color = new ThemeAwareColor(SrgbColor.FromRgb(0x2F5496)),
                    },
                    ShowBubbleSize = true,
                    ShowLeaderLines = true,
                },
                ExplosionPercent: 35)));

        var style = chart.Series[0].PointStyles[1];
        chart.Series[0].PointColors[1].Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        style.FillColor!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        style.StrokeColor.Should().BeNull();
        style.StrokeWidthPt.Should().Be(2.25);
        style.Marker!.Symbol.Should().Be(ChartMarkerSymbol.Diamond);
        style.Marker.SizePt.Should().Be(8);
        style.ExplosionPercent.Should().Be(35);
        style.DataLabels.Should().NotBeNull();
        style.DataLabels!.ShowValue.Should().BeTrue();
        style.DataLabels.ShowCategoryName.Should().BeTrue();
        style.DataLabels.ShowLegendKey.Should().BeTrue();
        style.DataLabels.ShowBubbleSize.Should().BeTrue();
        style.DataLabels.ShowLeaderLines.Should().BeTrue();
        style.DataLabels.Position.Should().Be(DataLabelPosition.InsideEnd);
        style.DataLabels.TextStyle.Should().NotBeNull();
        style.DataLabels.TextStyle!.FontFamily.Should().Be("Aptos");
        style.DataLabels.TextStyle.FontSizePt.Should().Be(9);
        style.DataLabels.TextStyle.Bold.Should().BeTrue();
        style.DataLabels.TextStyle.Italic.Should().BeFalse();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        var roundTrippedSeries = roundTripped.Series[0];
        roundTrippedSeries.PointColors[1].Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        roundTrippedSeries.PointStyles[1].FillColor!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        roundTrippedSeries.PointStyles[1].StrokeWidthPt.Should().Be(2.25);
        roundTrippedSeries.PointStyles[1].Marker!.Symbol.Should().Be(ChartMarkerSymbol.Diamond);
        roundTrippedSeries.PointStyles[1].ExplosionPercent.Should().Be(35);
        var roundTrippedLabels = roundTrippedSeries.PointStyles[1].DataLabels;
        roundTrippedLabels.Should().NotBeNull();
        roundTrippedLabels!.ShowValue.Should().BeTrue();
        roundTrippedLabels.ShowCategoryName.Should().BeTrue();
        roundTrippedLabels.ShowLegendKey.Should().BeTrue();
        roundTrippedLabels.ShowLeaderLines.Should().BeTrue();
        roundTrippedLabels.Position.Should().Be(DataLabelPosition.InsideEnd);
        roundTrippedLabels.NumberFormat.Should().Be("0.0%");
        roundTrippedLabels.Separator.Should().Be(" | ");
        roundTrippedLabels.TextStyle.Should().NotBeNull();
        roundTrippedLabels.TextStyle!.FontFamily.Should().Be("Aptos");
        roundTrippedLabels.TextStyle.FontSizePt.Should().Be(9);
        roundTrippedLabels.TextStyle.Bold.Should().BeTrue();
        roundTrippedLabels.TextStyle.Italic.Should().BeFalse();
        roundTrippedLabels.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));

        bus.Undo();
        chart.Series[0].PointColors[1].Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
        chart.Series[0].PointStyles[1].StrokeColor!.Resolved.Should().Be(SrgbColor.FromRgb(0x808080));
        chart.Series[0].PointStyles[1].StrokeWidthPt.Should().Be(0.75);
        chart.Series[0].PointStyles[1].Marker!.Symbol.Should().Be(ChartMarkerSymbol.Circle);
        chart.Series[0].PointStyles[1].ExplosionPercent.Should().BeNull();
        chart.Series[0].PointStyles[1].DataLabels.Should().BeNull();
    }

    [Fact]
    public void ChartDataLabels_TextStyleOnly_IsAValidAuthoredOverride()
    {
        var labels = new ChartDataLabels
        {
            TextStyle = new ChartTextStyle { FontFamily = "Aptos", FontSizePt = 9 },
        };

        labels.HasAny.Should().BeTrue();
    }

    [Fact]
    public void SetChartLayoutOptions_ChangesRoundTripFieldsAndUndoRestoresThem()
    {
        var (p, bus, id) = MakeChartPresentation();
        var chart = p.Slides[0].Shapes[0].Chart!;
        chart.PlotAreaManualLayout = new ChartManualLayout
        {
            LayoutTarget = "inner",
            X = 0.1,
            Y = 0.2,
            Width = 0.8,
            Height = 0.7,
        };

        bus.Execute(new SetChartLayoutOptionsCommand(
            0,
            id,
            new ChartLayoutOptions(
                ChartLayoutTarget.PlotArea,
                "outer",
                ChartManualLayoutMode.Edge,
                ChartManualLayoutMode.Factor,
                ChartManualLayoutMode.Factor,
                ChartManualLayoutMode.Edge,
                12,
                0.15,
                0.75,
                24)));

        var layout = chart.PlotAreaManualLayout!;
        layout.LayoutTarget.Should().Be("outer");
        layout.XMode.Should().Be(ChartManualLayoutMode.Edge);
        layout.Y.Should().Be(0.15);
        layout.Width.Should().Be(0.75);
        layout.HeightMode.Should().Be(ChartManualLayoutMode.Edge);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.PlotAreaManualLayout!.LayoutTarget.Should().Be("outer");
        roundTripped.PlotAreaManualLayout.XMode.Should().Be(ChartManualLayoutMode.Edge);
        roundTripped.PlotAreaManualLayout.X.Should().Be(12);
        roundTripped.PlotAreaManualLayout.HeightMode.Should().Be(ChartManualLayoutMode.Edge);

        bus.Undo();
        chart.PlotAreaManualLayout!.LayoutTarget.Should().Be("inner");
        chart.PlotAreaManualLayout.X.Should().Be(0.1);
        chart.PlotAreaManualLayout.Height.Should().Be(0.7);
    }
}
