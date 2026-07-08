using System.IO;
using FluentAssertions;
using FreeX.Core.Model;
using FreeX.Core.Calc;
using FreeX.Core.Formula;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-13 fix bucket S11.
/// </summary>
public sealed class FreeXR13S11Tests
{
    // R13-formula-array-cse-3 (MED): a dynamic-array formula that is currently #SPILL!-blocked
    // (e.g. =SEQUENCE(3) in A1 while A2 already holds a value) used to be saved as a PLAIN
    // single-cell formula, because at save time the anchor has no live spill extent
    // (TryGetSpillExtent is false -- RecalcEngine.ClearSpillRange ran before the #SPILL! was set)
    // and no provisional array extent either (this is a freshly-typed formula, not one loaded from
    // a file). XlsxFileAdapter's loader demotes any reloaded formula without HasArrayFormula
    // (t="array") to legacy Implicit mode permanently, so after reopening the file and clearing
    // the blocker, Excel would re-spill {1;2;3} but FreeX stayed collapsed to a single value
    // resolved via implicit intersection. The fix writes a currently-blocked dynamic-array
    // formula as a single-cell array formula (t="array" ref=anchor) so it reloads as Dynamic and
    // correctly re-spills once unblocked.
    [Fact]
    public void SpillBlockedDynamicArrayFormula_SavedAndReloaded_ReSpillsAfterBlockerCleared()
    {
        var workbook = new Workbook("SpillBlockedRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);

        sheet.SetCell(a1, Cell.FromFormula("SEQUENCE(3)"));
        sheet.SetCell(a2, new NumberValue(99));

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        // Sanity check: A2 blocks A1's spill, so the anchor evaluates to #SPILL! before saving.
        sheet.GetCell(a1)!.Value.Should().Be(ErrorValue.Spill,
            "A2 already holds a value inside SEQUENCE(3)'s 3-row spill range");

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(stream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedAnchor = reloadedSheet.GetCell(1, 1);

        reloadedAnchor.Should().NotBeNull();
        reloadedAnchor!.FormulaText.Should().Be("SEQUENCE(3)");
        reloadedAnchor.ArrayMode.Should().Be(FormulaArrayMode.Dynamic,
            "a #SPILL!-blocked dynamic array formula must round-trip as Dynamic, not be demoted to " +
            "legacy Implicit mode just because it had no live spill extent at save time");

        // Clear the blocker and recalculate: Excel re-spills {1;2;3} once the range is free again.
        reloadedSheet.ClearCell(new CellAddress(reloadedSheet.Id, 2, 1));
        var reloadEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        reloadEngine.RecalculateAllFormulas(reloaded);

        reloadedSheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        reloadedSheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        reloadedSheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }
}
