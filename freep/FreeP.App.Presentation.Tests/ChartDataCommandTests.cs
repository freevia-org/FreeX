using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

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
}
