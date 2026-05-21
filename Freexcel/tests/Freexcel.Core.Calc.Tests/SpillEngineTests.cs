using Freexcel.Core.Model;
using Freexcel.Core.Formula;
using FluentAssertions;

namespace Freexcel.Core.Calc.Tests;

public class SpillEngineTests
{
    private static Sheet MakeSheet() => new Sheet(SheetId.New(), "S");

    [Fact]
    public void SetSpillRange_WritesValuesToAdjacentCells()
    {
        var sheet = MakeSheet();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        var cells = new ScalarValue[2, 2]
        {
            { new NumberValue(1), new NumberValue(2) },
            { new NumberValue(3), new NumberValue(4) }
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells));

        sheet.GetValue(1, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void IsSpillBlocked_OccupiedCell_ReturnsTrue()
    {
        var sheet = MakeSheet();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(99));
        sheet.IsSpillBlocked(anchor, 2, 2).Should().BeTrue();
    }

    [Fact]
    public void ClearSpillRange_RemovesSpillValues()
    {
        var sheet = MakeSheet();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        var cells = new ScalarValue[1, 3]
        {
            { new NumberValue(1), new NumberValue(2), new NumberValue(3) }
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(2));

        sheet.ClearSpillRange(anchor);
        sheet.GetValue(1, 2).Should().Be(new BlankValue());
    }

    [Fact]
    public void SetSpillRange_BlockedByData_OriginalPreserved()
    {
        var sheet = MakeSheet();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(99));
        var cells = new ScalarValue[2, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) }
        };
        bool blocked = sheet.IsSpillBlocked(anchor, 2, 1);
        blocked.Should().BeTrue();
        sheet.GetValue(2, 1).Should().Be(new NumberValue(99));
    }

    [Fact]
    public void IsSpillBlocked_DifferentAnchorSpill_ReturnsTrue()
    {
        var sheet = MakeSheet();
        var firstAnchor = new CellAddress(sheet.Id, 1, 2);
        sheet.SetSpillRange(firstAnchor, new RangeValue(new ScalarValue[2, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) }
        }));

        var secondAnchor = new CellAddress(sheet.Id, 1, 1);
        sheet.IsSpillBlocked(secondAnchor, 2, 2).Should().BeTrue();
    }

    // ── RecalcEngine spill integration ────────────────────────────────────────

    private static (RecalcEngine engine, Workbook wb) MakeEngine()
    {
        var graph     = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine    = new RecalcEngine(graph, evaluator);
        var wb        = new Workbook();
        wb.AddSheet("Sheet1");
        return (engine, wb);
    }

    [Fact]
    public void Recalc_SequenceFormula_SpillsToAdjacentCells()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(3)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Recalc_SequenceFormula_DoesNotTreatOwnPreviousSpillAsBlocked()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(3)");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [anchor]);
        engine.Recalculate(wb, [anchor]);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Recalc_SequenceBlocked_SetsSpillError()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(99));
        sheet.SetFormula(anchor, "SEQUENCE(3)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        sheet.GetValue(1, 1).Should().Be(ErrorValue.Spill);
        sheet.GetValue(2, 1).Should().Be(new NumberValue(99));
        sheet.GetValue(3, 1).Should().Be(new BlankValue());
    }

    [Fact]
    public void Recalc_BlockedSequenceAfterBlockerCleared_WritesSpillValues()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        var blocker = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(blocker, new NumberValue(99));
        sheet.SetFormula(anchor, "SEQUENCE(3)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        sheet.ClearCell(blocker);
        engine.Recalculate(wb, [anchor]);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Recalc_FormulaChangedFromSpillToScalar_ClearsOldSpillValues()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(3)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        sheet.SetFormula(anchor, "42");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(42));
        sheet.GetValue(2, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(3, 1).Should().Be(BlankValue.Instance);
    }
}
