using System.IO;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R174-formula-array-cse-1 (MED) -- sibling/no-regression coverage.
/// <para>
/// Fixing the genuine single-cell legacy CSE misclassification (see
/// R174_LegacyCseSingleCellArrayFormulaTests) requires XlsxFileAdapter to be able to tell that
/// case apart from FreeX's OWN currently-1x1 dynamic-array formulas, which
/// XlsxFileAdapter.Save.cs writes using the exact same single-cell "t=array ref=anchor"
/// representation (see R17_save_io_Tests.OneByOneDynamicArray_RoundTripsAsArrayFormula_AndReSpillsAfterEdit
/// and FreeXR13S11Tests.SpillBlockedDynamicArrayFormula_SavedAndReloaded_ReSpillsAfterBlockerCleared,
/// both of which must keep passing unmodified). The fix resolves the ambiguity at the point where
/// FreeX's OWN save creates it: XlsxFileAdapter.Save.cs now also stamps a real-Excel-style dynamic
/// array `cm` metadata marker (xl/metadata.xml, "XLDAPR") on exactly the cells it writes this way,
/// and the loader treats a 1x1 declared array-formula range WITHOUT that marker as a genuine
/// legacy CSE formula (confined) and WITH it as still-dynamic (unconfined) -- this test asserts
/// that second, still-dynamic half of that contract directly, alongside the same round-trip
/// re-spill behavior R17_save_io_Tests already covers.
/// </para>
/// </summary>
public sealed class R174_DynamicArrayCollapsedRoundTripTests
{
    [Fact]
    public void OneByOneDynamicArray_RoundTripsWithNoLegacyConfinement_AndReSpillsAfterEdit()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("R174DynamicArrayRoundTrip");
        var sheet = workbook.AddSheet("Data");

        // A1:A3 all equal -> UNIQUE(A1:A3) collapses to a single 1x1 result, exactly the shape
        // XlsxFileAdapter.Save.cs writes as a single-cell "t=array ref=anchor" formula.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(5));

        var anchor = new CellAddress(sheet.Id, 1, 3);
        sheet.SetFormula(anchor, "UNIQUE(A1:A3)");

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        sheet.TryGetSpillExtent(anchor, out var preSaveRows, out var preSaveCols).Should().BeTrue();
        preSaveRows.Should().Be(1u);
        preSaveCols.Should().Be(1u);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedAnchorCell = reloadedSheet.GetCell(1, 3)!;

        reloadedAnchorCell.FormulaText.Should().Be("UNIQUE(A1:A3)");
        reloadedAnchorCell.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        reloadedAnchorCell.LegacyArrayRows.Should().Be(0u,
            "FreeX's own round-tripped 1x1 dynamic-array formula must NOT be mistaken for a " +
            "genuine legacy CSE formula and confined -- it carries the real dynamic-array `cm` " +
            "metadata marker XlsxFileAdapter.Save.cs now stamps on it, which the fix for the " +
            "genuine-legacy-CSE case must respect");
        reloadedAnchorCell.LegacyArrayCols.Should().Be(0u);

        // Widen the input so UNIQUE produces two distinct values -> the reloaded formula must
        // still be able to re-spill into a second cell (same outcome R17_save_io_Tests pins).
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 2, 1), new NumberValue(9));
        var reloadEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        reloadEngine.RecalculateAllFormulas(reloaded);

        reloadedSheet.GetValue(1, 3).Should().Be(new NumberValue(5));
        reloadedSheet.GetValue(2, 3).Should().Be(new NumberValue(9));
    }
}
