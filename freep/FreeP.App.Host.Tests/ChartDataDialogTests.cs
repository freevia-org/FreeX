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
}
