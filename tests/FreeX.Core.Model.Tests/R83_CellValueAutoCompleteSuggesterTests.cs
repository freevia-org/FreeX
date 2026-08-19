using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R83-app-flashfill-autocomplete-5-2: Options > Advanced showed "Enable AutoComplete for cell
/// values" permanently checked and disabled with no backing feature anywhere in the codebase.
/// These tests cover the pure suggestion logic (<see cref="CellValueAutoCompleteSuggester.Suggest"/>)
/// and the column-scan that gathers candidates from a live <see cref="Sheet"/>
/// (<see cref="CellValueAutoCompleteSuggester.CollectContiguousColumnTextEntries"/>) that now back
/// the option.
/// </summary>
public sealed class R83_CellValueAutoCompleteSuggesterTests
{
    // --- Suggest: pure matching logic -----------------------------------------------------

    [Fact]
    public void Suggest_UniquePrefixMatch_ReturnsFullCandidate()
    {
        // FAILS before the fix existed (the type didn't exist at all); once implemented, typing
        // "Cal" against a column containing "California" must suggest the full word, matching the
        // finding's failure scenario verbatim.
        var result = CellValueAutoCompleteSuggester.Suggest(["California"], "Cal");

        result.Should().Be("California");
    }

    [Fact]
    public void Suggest_NoMatchingCandidate_ReturnsNull()
    {
        // No-regression sibling: an unrelated column never manufactures a bogus suggestion.
        var result = CellValueAutoCompleteSuggester.Suggest(["Oregon", "Washington"], "Cal");

        result.Should().BeNull();
    }

    [Fact]
    public void Suggest_AmbiguousPrefix_ReturnsNull()
    {
        // Excel refuses to guess between two different completions of the same prefix.
        var result = CellValueAutoCompleteSuggester.Suggest(["California", "Calgary"], "Cal");

        result.Should().BeNull();
    }

    [Fact]
    public void Suggest_SameCandidateRepeated_IsNotAmbiguous()
    {
        var result = CellValueAutoCompleteSuggester.Suggest(["California", "california"], "Cal");

        result.Should().Be("California");
    }

    [Fact]
    public void Suggest_IsCaseInsensitive()
    {
        var result = CellValueAutoCompleteSuggester.Suggest(["CALIFORNIA"], "cal");

        result.Should().Be("CALIFORNIA");
    }

    [Fact]
    public void Suggest_TypedTextAlreadyEqualsCandidate_ReturnsNull()
    {
        // Nothing left to complete once the typed text already matches a full candidate.
        var result = CellValueAutoCompleteSuggester.Suggest(["California"], "California");

        result.Should().BeNull();
    }

    [Fact]
    public void Suggest_EmptyTypedText_ReturnsNull()
    {
        var result = CellValueAutoCompleteSuggester.Suggest(["California"], "");

        result.Should().BeNull();
    }

    [Fact]
    public void Suggest_BlankAndNullCandidatesAreIgnored()
    {
        var result = CellValueAutoCompleteSuggester.Suggest([null, "", "California"], "Cal");

        result.Should().Be("California");
    }

    // --- CollectContiguousColumnTextEntries: live-sheet column scan -----------------------

    [Fact]
    public void Collect_ScansContiguousTextAboveAndBelow_StoppingAtBlank()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // A1 California, A2 (editing, no value yet), A3 Colorado, A4 blank, A5 Connecticut (unreachable: gap at A4)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("California"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Colorado"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Connecticut"));

        var entries = CellValueAutoCompleteSuggester.CollectContiguousColumnTextEntries(
            sheet, new CellAddress(sheet.Id, 2, 1));

        entries.Should().BeEquivalentTo(["California", "Colorado"]);
    }

    [Fact]
    public void Collect_NonTextCellsDoNotBreakTheScanButAreNotCandidates()
    {
        // No-regression sibling: a number sitting between two text entries in the column is
        // skipped as a candidate but does not end the contiguous run the way a blank does.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("California"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Colorado"));

        var entries = CellValueAutoCompleteSuggester.CollectContiguousColumnTextEntries(
            sheet, new CellAddress(sheet.Id, 3, 1));

        entries.Should().BeEquivalentTo(["California", "Colorado"]);
    }

    [Fact]
    public void Collect_EmptyColumn_ReturnsEmpty()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // Populate an unrelated cell so the sheet has a used range, but column A stays empty.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("unrelated"));

        var entries = CellValueAutoCompleteSuggester.CollectContiguousColumnTextEntries(
            sheet, new CellAddress(sheet.Id, 5, 1));

        entries.Should().BeEmpty();
    }

    [Fact]
    public void Collect_SpillMemberInColumn_DoesNotTruncateScan_AndIncludesSpillTextAsCandidate()
    {
        // spill-overlay-root F11: a spill member cell has no entry in Sheet's _cells dictionary
        // (only in the spill overlay), so sheet.GetCell(row, col) returns null for it. Before the
        // fix, the scan treated that null exactly like a real blank cell and stopped there,
        // silently dropping every real entry beyond it (here, "Connecticut" past the spill) and
        // never offering the spilled text ("Boston"/"Boulder") itself as a candidate.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // A1 = California: ordinary text entry above everything.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("California"));

        // A3 = spill anchor (e.g. a transposed TEXTSPLIT formula), spilling text down into A3:A4
        // exactly like RecalcEngine does: the anchor's own cell.Value is the first spill slot,
        // and SetSpillRange populates the remaining member(s) in the overlay only.
        var anchor = new CellAddress(sheet.Id, 3, 1);
        var anchorCell = Cell.FromFormula("TEXTSPLIT(\"Boston,Boulder\", \",\")");
        anchorCell.Value = new TextValue("Boston");
        sheet.SetCell(anchor, anchorCell);
        var spillCells = new ScalarValue[2, 1]
        {
            { new TextValue("ignored-anchor-slot") }, // SetSpillRange ignores slot [0,0]
            { new TextValue("Boulder") },
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillCells)); // spills to A3:A4

        // A5 = Connecticut: real text sitting past the spill member, in the same contiguous run.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Connecticut"));

        // User is editing A2, between A1 and the spill anchor.
        var entries = CellValueAutoCompleteSuggester.CollectContiguousColumnTextEntries(
            sheet, new CellAddress(sheet.Id, 2, 1));

        entries.Should().BeEquivalentTo(["California", "Boston", "Boulder", "Connecticut"]);
    }

    [Fact]
    public void Collect_RealBlankCellStillStopsTheScan_EvenNearASpill()
    {
        // No-regression sibling: switching the scan from GetCell to GetValue must not turn a
        // genuinely empty cell into something that no longer stops the walk. GetValue returns
        // BlankValue.Instance for an unset cell just as it does for a spill-vacated one, so the
        // contiguous-run boundary behavior for ordinary blanks is unchanged.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("California"));
        // A2 left genuinely blank (never set).
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Colorado"));

        var entries = CellValueAutoCompleteSuggester.CollectContiguousColumnTextEntries(
            sheet, new CellAddress(sheet.Id, 3, 1));

        // Scanning up from row 3 hits the blank A2 immediately and stops -- "California" (row 1)
        // is unreachable. The downward scan is unaffected and still finds "Colorado" at row 4,
        // exactly as it did before the fix.
        entries.Should().BeEquivalentTo(["Colorado"]);
    }

    [Fact]
    public void Collect_ThenSuggest_EndToEndMatchesFindingsScenario()
    {
        // End-to-end: A1 = "California", user editing A2 and has typed "Cal" -> suggests
        // "California", exactly the finding's failure scenario.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("California"));

        var entries = CellValueAutoCompleteSuggester.CollectContiguousColumnTextEntries(
            sheet, new CellAddress(sheet.Id, 2, 1));
        var suggestion = CellValueAutoCompleteSuggester.Suggest(entries, "Cal");

        suggestion.Should().Be("California");
    }
}
