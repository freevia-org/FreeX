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
}
