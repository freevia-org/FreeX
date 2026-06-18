using FreeX.Core.Model;
using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

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
    public void Recalc_DynamicArrayArithmetic_PreservesSpill()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(3)+10");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(11));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(12));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(13));
    }

    [Fact]
    public void Recalc_TopLevelFunctionRangeResult_PreservesSpill()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(3));
        var anchor = new CellAddress(sheet.Id, 1, 4);
        sheet.SetFormula(anchor, "IF(TRUE,B1:B3,C1:C3)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 4).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 4).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Recalc_DynamicRowRangeArithmetic_SpillsFromFirstElement()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        sheet.SetCell(new CellAddress(sheet.Id, 7, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 2), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 3), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 15, 2), new NumberValue(10));
        var formula = new CellAddress(sheet.Id, 15, 5);
        sheet.SetFormula(formula, "A7:C7*B15");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [formula]);

        sheet.GetValue(15, 5).Should().Be(new NumberValue(20));
        sheet.GetValue(15, 6).Should().Be(new NumberValue(30));
        sheet.GetValue(15, 7).Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Recalc_DynamicColumnRangeArithmetic_SpillsFromFirstElement()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(7));
        var formula = new CellAddress(sheet.Id, 3, 4);
        sheet.SetFormula(formula, "A1:A3+10");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [formula]);

        sheet.GetValue(3, 4).Should().Be(new NumberValue(12));
        sheet.GetValue(4, 4).Should().Be(new NumberValue(15));
        sheet.GetValue(5, 4).Should().Be(new NumberValue(17));
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

    [Fact]
    public void Recalc_FormulaChangedFromSpillToFormulaError_ClearsOldSpillValues()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(3)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        sheet.SetFormula(anchor, "0^(-1)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        sheet.GetValue(1, 1).Should().Be(ErrorValue.DivByZero);
        sheet.GetValue(2, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(3, 1).Should().Be(BlankValue.Instance);
    }

    // ── Spill-target dependency (the cross-anchor ordering bug) ───────────────
    // Regression test for: formula cell B references a spill-target cell T that is populated
    // by a different formula anchor A.  In a full RecalculateAllFormulas call, A and B may be
    // topologically unordered (T is not a formula cell, so it contributes 0 to B's in-degree),
    // causing B to read T as blank before A has spilled.  The fix adds a second evaluation pass.

    [Fact]
    public void RecalculateAllFormulas_FormulaReferencingSpillTarget_SeesSpilledValue()
    {
        // Arrange: anchor A1 spills SEQUENCE(3) → A1=1, A2=2, A3=3
        //          formula C1 = A2  (references a spill-target, not a formula cell)
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();

        var anchor = new CellAddress(sheet.Id, 1, 1);  // A1 — spill anchor
        var reader = new CellAddress(sheet.Id, 1, 3);  // C1 — reads spill target A2

        sheet.SetFormula(anchor, "SEQUENCE(3)");
        sheet.SetFormula(reader, "A2");  // A2 is a spill target of anchor A1

        // Act: full recalc (this is the code path that had the ordering bug)
        engine.RecalculateAllFormulas(wb);

        // Assert: anchor spilled correctly
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1), "A1 is anchor value");
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2), "A2 is spilled value");
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3), "A3 is spilled value");

        // Assert: C1 picked up the spilled value, not blank
        sheet.GetValue(1, 3).Should().Be(new NumberValue(2),
            "C1 = A2 must see the spilled value (2), not blank — ordering bug regression");
    }

    [Fact]
    public void RecalculateAllFormulas_CrossSheetFormulaReferencingSpillTarget_SeesSpilledValue()
    {
        // Arrange: two-sheet scenario (mirrors Calendar↔Calc real-world case)
        //   Sheet1!A1: SEQUENCE(3)  → spills A1=1, A2=2, A3=3
        //   Sheet2!B1: =Sheet1!A3   → cross-sheet reference to a spill target
        var graph     = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine    = new RecalcEngine(graph, evaluator);
        var wb        = new Workbook();
        wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2");

        var sheet1 = wb.GetSheet("Sheet1")!;
        var sheet2 = wb.GetSheet("Sheet2")!;

        var anchor = new CellAddress(sheet1.Id, 1, 1);  // Sheet1!A1
        var reader = new CellAddress(sheet2.Id, 1, 2);  // Sheet2!B1

        sheet1.SetFormula(anchor, "SEQUENCE(3)");
        sheet2.SetFormula(reader, "Sheet1!A3");  // Sheet1!A3 is a spill target

        engine.RecalculateAllFormulas(wb);

        sheet1.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet1.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet1.GetValue(3, 1).Should().Be(new NumberValue(3));

        sheet2.GetValue(1, 2).Should().Be(new NumberValue(3),
            "Sheet2!B1 = Sheet1!A3 must see the cross-sheet spilled value (3)");
    }

    // Mirrors Spill Formulae!C190 = SORT(ANCHORARRAY(C184)) where C184 itself spills.
    [Fact]
    public void RecalculateAllFormulas_SortOverAnchorArrayOfSpillingAnchor_SortsSpilledValues()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();

        // Source block B1:D3 (col B = 12, 11, 13).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(121));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(11));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(111));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(13));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(131));

        var anchor = new CellAddress(sheet.Id, 1, 6);   // F1 spills B1:D3 (3x2)
        var reader = new CellAddress(sheet.Id, 1, 10);  // J1 = SORT(ANCHORARRAY(F1))
        sheet.SetFormula(anchor, "B1:D3");
        sheet.SetFormula(reader, "SORT(ANCHORARRAY(F1))");

        engine.RecalculateAllFormulas(wb);

        sheet.GetValue(1, 6).Should().Be(new NumberValue(12), "F1 spills the source block's top-left");
        sheet.GetValue(1, 10).Should().Be(new NumberValue(11),
            "SORT(ANCHORARRAY(F1)) must sort the spilled block ascending by col 1, putting 11 first");
        sheet.GetValue(2, 10).Should().Be(new NumberValue(12));
        sheet.GetValue(3, 10).Should().Be(new NumberValue(13));
    }

    // Mirrors Spill Formulae!D197 = SUMIFS(..., ANCHORARRAY(<spill>), ...): array criteria from a
    // spilled range must make SUMIFS spill one sum per criterion, with the anchor cell holding the
    // top-left scalar (not a RangeValue).
    [Fact]
    public void RecalculateAllFormulas_SumIfsWithAnchorArrayCriteria_SpillsOneSumPerCriterion()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();

        // Data: category in A, amount in B.
        (uint, string, double)[] data =
        {
            (1, "x", 1), (2, "y", 2), (3, "x", 3), (4, "z", 4), (5, "y", 5),
        };
        foreach (var (r, cat, amt) in data)
        {
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new TextValue(cat));
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(amt));
        }

        // D1 spills the distinct categories {x; y; z}; F1 = SUMIFS(B, A, ANCHORARRAY(D1)).
        var critAnchor = new CellAddress(sheet.Id, 1, 4);  // D1
        var sumAnchor = new CellAddress(sheet.Id, 1, 6);   // F1
        sheet.SetFormula(critAnchor, "UNIQUE(A1:A5)");
        sheet.SetFormula(sumAnchor, "SUMIFS(B1:B5,A1:A5,ANCHORARRAY(D1))");

        engine.RecalculateAllFormulas(wb);

        // x -> 1+3 = 4, y -> 2+5 = 7, z -> 4
        sheet.GetValue(1, 6).Should().Be(new NumberValue(4),
            "SUMIFS over spilled array criteria must spill; the anchor holds the first sum (x=4) as a scalar");
        sheet.GetValue(2, 6).Should().Be(new NumberValue(7), "y = 2+5");
        sheet.GetValue(3, 6).Should().Be(new NumberValue(4), "z = 4");
    }
}
