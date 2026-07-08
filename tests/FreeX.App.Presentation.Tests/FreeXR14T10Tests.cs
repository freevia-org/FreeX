using System.Collections.Generic;
using System.Linq;

using FluentAssertions;

using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Round-14 fix bucket T10 — R14-text-to-columns-dedup-2: the WPF host apply path
/// (<see cref="TextToColumnsApplyPlanner.BuildEdits(Sheet, GridRange, string)"/> and friends) and the
/// Avalonia host apply path (<see cref="TextToColumnsDialogPlanner.MapToEdits"/> /
/// <see cref="TextToColumnsApplyPlanner.MapResultToEdits"/>) must produce byte-identical results for the
/// same workbook and the same Text to Columns operation: each field trimmed of leading/trailing
/// whitespace (matching Excel's General-format behavior), and a shorter row's stale trailing cells left
/// untouched rather than overwritten with blanks. Before the fix, the Avalonia path trimmed nothing and
/// force-filled every column up to the widest row, so "a, b, c" / "x, y" produced " b"/" c"/blank-D2 on
/// Avalonia while WPF produced "b"/"c"/untouched-D2.
/// </summary>
public sealed class FreeXR14T10Tests
{
    [Fact]
    public void TextToColumns_WindowsAndAvaloniaApplyPaths_ProduceIdenticalEditsForSameInput()
    {
        var workbook = new Workbook("Ttc");
        var sheet = workbook.AddSheet("Data");
        // Source column B (col 2), rows 1..2: row 1 has 3 comma-delimited fields with padding spaces,
        // row 2 has only 2 fields (fewer than the widest row).
        var range = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 2, 2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("a, b, c"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("x, y"));

        // Windows host path: TextToColumnsApplyPlanner.BuildEdits reads straight from the sheet.
        var windowsEdits = TextToColumnsApplyPlanner.BuildEdits(sheet, range, ",");

        // Avalonia host path: TextToColumnsPlanner.Plan (pure split) feeding TextToColumnsDialogPlanner.MapToEdits.
        var options = TextToColumnsOptions.Delimited(",", textQualifier: null);
        var result = TextToColumnsPlanner.Plan(["a, b, c", "x, y"], options);
        var avaloniaEdits = TextToColumnsDialogPlanner.MapToEdits(sheet.Id, result, range);

        ToTextMap(avaloniaEdits).Should().BeEquivalentTo(ToTextMap(windowsEdits),
            "the two host shells must realize the identical Text to Columns operation identically");

        // Pin the actual Excel-matching values: leading/trailing whitespace trimmed from every field...
        var edits = ToTextMap(avaloniaEdits);
        edits[new CellAddress(sheet.Id, 1, 2)].Should().Be("a");
        edits[new CellAddress(sheet.Id, 1, 3)].Should().Be("b");
        edits[new CellAddress(sheet.Id, 1, 4)].Should().Be("c");
        edits[new CellAddress(sheet.Id, 2, 2)].Should().Be("x");
        edits[new CellAddress(sheet.Id, 2, 3)].Should().Be("y");

        // ...and the shorter row's stale trailing cell (D2) is left untouched, not overwritten with a blank.
        avaloniaEdits.Should().NotContain(e => e.Address == new CellAddress(sheet.Id, 2, 4));
        windowsEdits.Should().NotContain(e => e.Address == new CellAddress(sheet.Id, 2, 4));
    }

    private static Dictionary<CellAddress, string?> ToTextMap(
        IEnumerable<(CellAddress Address, Cell NewCell)> edits) =>
        edits.ToDictionary(e => e.Address, e => (e.NewCell.Value as TextValue)?.Value);
}
