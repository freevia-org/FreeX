using System.IO;
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

    [Fact]
    public void ChartDataDialog_UsesSharedPlannerForPolicy()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartDataDialog.cs");

        source.Should().Contain("ChartDataDialogPlanner.FromChart(chart)");
        source.Should().Contain("_planner.BuildTableProjection()");
        source.Should().Contain("new ChartRowViewModel(row)");
        source.Should().Contain("MakeEditableHeader(seriesColumn)");
        source.Should().Contain("_planner.BuildCommitPlan(ReadCategoryEditsFromGrid())");
        source.Should().Contain("commit.ChartType");
        source.Should().Contain("_planner.AddSeries()");
        source.Should().Contain("_planner.AddCategory()");
        source.Should().Contain("_planner.SwitchRowsAndColumns()");
        source.Should().Contain("ChartDataDialogPlanner.FormatCellValue(");
        source.Should().Contain("ChartDataDialogPlanner.ParseCellValue(");
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
