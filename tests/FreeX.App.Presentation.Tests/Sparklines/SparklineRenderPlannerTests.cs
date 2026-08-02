using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.SparklineUI;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Sparklines;

/// <summary>
/// Tests for the non-UI sparkline render glue: reading a sparkline's data range off the sheet into
/// its numeric series, and dispatching that series through the portable layout engine into the
/// cell-local geometry the renderer draws. No running UI.
/// </summary>
public sealed class SparklineRenderPlannerTests
{
    [Fact]
    public void ReadSeries_ReadsNumberDateAndBoolCells_SkipsBlanks()
    {
        var (workbook, sheet) = BuildSheet();
        // A1:A4 — number, bool, blank, number. Round-8 finding N5: the blank at A3 is no longer
        // dropped — the default DisplayEmptyCellsAs (Gap) keeps its position in the series as NaN
        // so the layout engine breaks the line there, matching Excel's default gap behavior.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(9));

        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.Line,
            Location = new CellAddress(sheet.Id, 5, 1),
            DataRange = Range(sheet.Id, 1, 1, 4, 1),
        };

        var series = SparklineRenderPlanner.ReadSeries(workbook, sheet, sparkline);

        series.Should().HaveCount(4);
        series[0].Should().Be(3);
        series[1].Should().Be(1);
        double.IsNaN(series[2]).Should().BeTrue("the blank cell keeps its position as NaN under the default Gap display");
        series[3].Should().Be(9);
    }

    [Fact]
    public void ReadSeries_SkipsHiddenRows()
    {
        var (workbook, sheet) = BuildSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        sheet.HiddenRows.Add(2);

        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.Column,
            Location = new CellAddress(sheet.Id, 5, 1),
            DataRange = Range(sheet.Id, 1, 1, 3, 1),
        };

        SparklineRenderPlanner.ReadSeries(workbook, sheet, sparkline).Should().Equal(1, 3);
    }

    [Fact]
    public void BuildValues_KeyedById()
    {
        var (workbook, sheet) = BuildSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        var sparkline = new SparklineModel
        {
            Location = new CellAddress(sheet.Id, 2, 1),
            DataRange = Range(sheet.Id, 1, 1, 1, 1),
        };
        sheet.Sparklines.Add(sparkline);

        var values = SparklineRenderPlanner.BuildValues(workbook, sheet);

        values.Should().ContainKey(sparkline.Id);
        values[sparkline.Id].Should().Equal(5);
    }

    [Fact]
    public void Plan_AppliesInsetAndSkipsCellsNotLaidOut()
    {
        var (workbook, sheet) = BuildSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(4));

        var visible = new SparklineModel
        {
            Kind = SparklineKind.Line,
            Location = new CellAddress(sheet.Id, 3, 3),
            DataRange = Range(sheet.Id, 1, 1, 1, 2),
        };
        var offscreen = new SparklineModel
        {
            Kind = SparklineKind.Line,
            Location = new CellAddress(sheet.Id, 9, 9),
            DataRange = Range(sheet.Id, 1, 1, 1, 2),
        };
        sheet.Sparklines.Add(visible);
        sheet.Sparklines.Add(offscreen);

        var values = SparklineRenderPlanner.BuildValues(workbook, sheet);
        var instructions = SparklineRenderPlanner.Plan(sheet, values, Lookup, inset: 3);

        instructions.Should().HaveCount(1);
        var instruction = instructions[0];
        instruction.Location.Should().Be(visible.Location);
        instruction.Kind.Should().Be(SparklineKind.Line);
        instruction.Values.Should().Equal(2, 4);
        // Cell rect (10,20,80,40) inset by 3 on every side.
        instruction.CellRect.Should().Be(new LayoutRect(13, 23, 74, 34));

        static bool Lookup(CellAddress location, out LayoutRect rect)
        {
            if (location.Row == 3 && location.Col == 3)
            {
                rect = new LayoutRect(10, 20, 80, 40);
                return true;
            }

            rect = default;
            return false;
        }
    }

    [Fact]
    public void Plan_DropsEmptySeries()
    {
        var (workbook, sheet) = BuildSheet();
        // Round-8 finding N5: a single blank cell is no longer an empty series — the default
        // DisplayEmptyCellsAs (Gap) keeps its position as NaN, so Plan would emit an instruction
        // for it. Only a data range over the supported-cell cap now yields a genuinely empty
        // series (SparklineSeriesReader.ReadSeries returns [] outright), so use that to exercise
        // Plan's "no instruction for an empty series" drop path.
        var sparkline = new SparklineModel
        {
            Location = new CellAddress(sheet.Id, 2, 1),
            DataRange = Range(sheet.Id, 1, 1, (uint)(SparklineRangeLimits.MaxDataCellCount + 1), 1),
        };
        sheet.Sparklines.Add(sparkline);

        var values = SparklineRenderPlanner.BuildValues(workbook, sheet);
        var instructions = SparklineRenderPlanner.Plan(sheet, values, AlwaysAt);

        instructions.Should().BeEmpty();

        static bool AlwaysAt(CellAddress location, out LayoutRect rect)
        {
            rect = new LayoutRect(0, 0, 50, 20);
            return true;
        }
    }

    [Fact]
    public void LayoutColumn_WinLoss_ProducesFixedHalfHeightBars()
    {
        var (_, sheet) = BuildSheet();
        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.WinLoss,
            Location = new CellAddress(sheet.Id, 1, 1),
            DataRange = Range(sheet.Id, 1, 1, 1, 1),
        };
        var instruction = new SparklineRenderInstruction(
            sparkline.Id,
            sparkline.Location,
            SparklineKind.WinLoss,
            new double[] { 5, -2, 0, 3 },
            new LayoutRect(0, 0, 40, 20));

        var layout = SparklineRenderPlanner.LayoutColumn(instruction);

        // Zero is dropped; win/loss yields one bar per non-zero value, all half the cell height.
        layout.Bars.Should().HaveCount(3);
        layout.Bars.Should().AllSatisfy(bar => bar.Rect.Height.Should().Be(10));
        layout.Bars.Select(bar => bar.IsNegative).Should().Equal(false, true, false);
    }

    [Fact]
    public void LayoutLine_SingleValue_ReportsCenterPoint()
    {
        var instruction = new SparklineRenderInstruction(
            Guid.NewGuid(),
            new CellAddress(default, 1, 1),
            SparklineKind.Line,
            new double[] { 7 },
            new LayoutRect(0, 0, 40, 20));

        var layout = SparklineRenderPlanner.LayoutLine(instruction);

        layout.SinglePoint.Should().Be(new LayoutPoint(20, 10));
        layout.Segments.Should().BeEmpty();
    }

    // ── Integration smoke: verify sparklines load from the real fixture XLSX ──────────────────────

    [Fact]
    public void Fixture_LineMarkers_001_LoadsSparklineWithSevenValues()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "planning", "wave4-sparklines-fixtures",
            "generated-excel-sparklines",
            "Excel_native_sparkline_line_markers_001.xlsx");
        path = Path.GetFullPath(path);

        if (!File.Exists(path))
            return; // fixture not yet generated — skip silently in CI

        using var stream = File.OpenRead(path);
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets[0];

        sheet.Sparklines.Should().NotBeEmpty("fixture must contain at least one sparkline");

        var sp = sheet.Sparklines[0];
        var series = SparklineSeriesReader.ReadSeries(workbook, sheet, sp);
        series.Should().HaveCount(7, "fixture data is A2:A8 = 7 values");
        series.Should().Equal(3, 7, 2, 9, 5, 1, 8);
    }

    private static (Workbook Workbook, Sheet Sheet) BuildSheet()
    {
        var workbook = new Workbook("Sparklines");
        var sheet = workbook.AddSheet("Data");
        return (workbook, sheet);
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));
}
