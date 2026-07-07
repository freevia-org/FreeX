using System.Reflection;
using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round-12 fix bucket Q6 regression test.
///   - R12-recalc-dependency-deep-2: a formula that references the same large (&gt;8-cell) range twice
///     (e.g. B1 = SUM(A1:A100)+COUNT(A1:A100)) must not permanently leave a stale, empty
///     <c>RangeDependencyIndex</c> registered for the sheet once that formula's dependencies are cleared.
///     A stale non-zero <c>_rangeDependentsBySheet</c> entry disables the single-root-exact-chain and
///     single-leaf-exact-dependent recalc fast paths for the rest of the session.
/// </summary>
public class FreeXR12Q6Tests
{
    [Fact]
    public void ClearFormulaDependencies_AfterFormulaReferencesSameRangeTwice_FullyClearsRangeDependencyIndex()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        for (var row = 1u; row <= 100; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var formulaCell = new CellAddress(sheet.Id, 1, 2);
        // Same range (A1:A100) referenced twice by one formula - this is the exact repro from the finding.
        sheet.SetFormula(formulaCell, "SUM(A1:A100)+COUNT(A1:A100)");
        engine.RecalculateAllFormulas(workbook);

        var graph = GetGraphField(engine);
        var rangeDependentsBySheet = GetRangeDependentsBySheet(graph);
        rangeDependentsBySheet.Should().ContainKey(sheet.Id,
            "registering a range-precedent formula must record a RangeDependencyIndex for its sheet");

        // Clear this cell's formula dependencies, mirroring what WorkbookCellEditService/MainWindow.Editing
        // do when a formula cell is overwritten with a plain value.
        engine.ClearFormulaDependencies(formulaCell);

        rangeDependentsBySheet.Should().NotContainKey(sheet.Id,
            "clearing the only formula that referenced this sheet's ranges must fully unregister the " +
            "sheet's RangeDependencyIndex, not leave an empty index with a stale non-zero Count that " +
            "permanently disables the exact-chain recalc fast paths");
    }

    [Fact]
    public void RangeDependencyIndex_Count_ReturnsToZero_AfterDuplicateRangeReferenceRemoved()
    {
        // Directly reproduces the finding's exact repro against the RangeDependencyIndex named in it:
        // adding the same (range, dependent) pair twice (as SetDependenciesCore does for a formula that
        // lists the same range twice) then removing it twice (as ClearDependencies does) must leave
        // Count at exactly 0, not stuck at 1 forever.
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 100, 1)); // A1:A100, >8 cells
        var dependent = new CellAddress(sheetId, 1, 2);
        var dependency = new RangeDependency(range, dependent);

        var index = new RangeDependencyIndex();
        index.Add(dependency); // first occurrence: SUM(A1:A100)
        index.Add(dependency); // second occurrence of the SAME range: COUNT(A1:A100)
        index.Count.Should().Be(1, "two occurrences of the same (range, dependent) pair are one logical dependency");

        index.Remove(dependency); // ClearDependencies' first loop iteration
        index.Remove(dependency); // ClearDependencies' second loop iteration (duplicate range entry)

        index.Count.Should().Be(0,
            "removing both duplicate range entries for the same dependent must fully clear the index, " +
            "not leave Count stuck at 1 with an empty index still registered");
    }

    private static DependencyGraph GetGraphField(RecalcEngine engine)
    {
        var field = typeof(RecalcEngine).GetField("_graph", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (DependencyGraph)field!.GetValue(engine)!;
    }

    // RangeDependencyIndex is `internal` (InternalsVisibleTo covers this test assembly), so the
    // dictionary can be cast directly without reflecting over its value type too.
    private static Dictionary<SheetId, RangeDependencyIndex> GetRangeDependentsBySheet(DependencyGraph graph)
    {
        var field = typeof(DependencyGraph).GetField(
            "_rangeDependentsBySheet", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (Dictionary<SheetId, RangeDependencyIndex>)field!.GetValue(graph)!;
    }
}
