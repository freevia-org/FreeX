using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

public class FindReplaceTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandBus CommandBus) Setup()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(id => new TestCommandContext(workbook));
        return (workbook, sheet, commandBus);
    }

    [Fact]
    public void Find_MatchesByDisplayText()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("hello"));

        var results = FindReplaceService.Find(wb, "hello");

        results.Should().HaveCount(1);
        results[0].Address.Should().Be(a1);
        results[0].MatchedText.Should().Be("hello");
    }

    [Fact]
    public void Find_MatchCase_DoesNotMatchWrongCase()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("hello"));

        var results = FindReplaceService.Find(wb, "HELLO", matchCase: true);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Find_EntireCell_DoesNotMatchPartial()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("hello world"));

        var results = FindReplaceService.Find(wb, "hello", matchEntireCell: true);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Find_SearchFormulas_FindsFormulaText()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "SUM(B1:B5)");

        var results = FindReplaceService.Find(wb, "SUM", searchFormulas: true);

        results.Should().HaveCount(1);
        results[0].Address.Should().Be(a1);
    }

    [Fact]
    public void Find_OptionsLimitScopeOrderAndLookInComments()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var b1 = new CellAddress(sheet1.Id, 1, 2);
        var a2 = new CellAddress(sheet1.Id, 2, 1);
        var sheet2Cell = new CellAddress(sheet2.Id, 1, 1);
        sheet1.SetCell(b1, new TextValue("needle in B1"));
        sheet1.SetCell(a2, new TextValue("needle in A2"));
        sheet2.SetCell(sheet2Cell, new TextValue("needle elsewhere"));
        sheet1.Comments[a2] = "needle note";
        sheet1.ThreadedComments[b1] = new ThreadedComment("needle thread")
        {
            Replies =
            [
                new CommentReply("needle thread reply", "Codex"),
                new CommentReply("other reply", "FreeX")
            ]
        };

        var valueResults = FindReplaceService.Find(
            workbook,
            "needle",
            new FindOptions(Within: FindWithin.Sheet, CurrentSheetId: sheet1.Id, SearchOrder: FindSearchOrder.ByColumns));

        valueResults.Select(result => result.Address).Should().Equal(a2, b1);
        valueResults.Select(result => result.Target).Should().OnlyContain(target => target == FindResultTarget.Cell);

        var noteResults = FindReplaceService.Find(
            workbook,
            "needle note",
            new FindOptions(Within: FindWithin.Sheet, CurrentSheetId: sheet1.Id, LookIn: FindLookIn.Notes));

        noteResults.Should().ContainSingle().Which.Address.Should().Be(a2);
        noteResults.Single().Target.Should().Be(FindResultTarget.Note);

        var commentResults = FindReplaceService.Find(
            workbook,
            "needle thread",
            new FindOptions(Within: FindWithin.Sheet, CurrentSheetId: sheet1.Id, LookIn: FindLookIn.Comments));

        commentResults.Select(result => result.Address).Should().Equal(b1, b1);
        commentResults.Select(result => result.MatchedText).Should().Equal("needle thread", "needle thread reply");
        commentResults.Select(result => result.Target).Should().Equal(
            FindResultTarget.ThreadedComment,
            FindResultTarget.ThreadedCommentReply);
        commentResults.Select(result => result.ReplyIndex).Should().Equal(null, 0);
    }

    // R71-commands-find-replace-4-3: a SelectionScope pinned to the sheet that was active
    // when Find & Replace was opened must only constrain the search while Within == Sheet.
    // Excel treats selection-scoping as a within-sheet concept: switching Within to Workbook
    // must search every sheet, ignoring the stale, sheet-pinned selection entirely.
    [Fact]
    public void Find_WithinWorkbook_IgnoresSheetPinnedSelectionScope_FindsAllSheets()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var s1Match = new CellAddress(sheet1.Id, 1, 1); // A1, inside the captured scope
        var s1OutsideScope = new CellAddress(sheet1.Id, 10, 1); // A10, outside the captured scope
        var s2Match = new CellAddress(sheet2.Id, 1, 1); // Sheet2!A1
        sheet1.SetCell(s1Match, new TextValue("cat"));
        sheet1.SetCell(s1OutsideScope, new TextValue("cat"));
        sheet2.SetCell(s2Match, new TextValue("cat"));

        // A Sheet1 A1:A5 selection, captured at dialog-open time and pinned to Sheet1.
        var selectionScope = new[] { new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 5, 1)) };

        var workbookResults = FindReplaceService.Find(
            workbook,
            "cat",
            new FindOptions(Within: FindWithin.Workbook, SelectionScope: selectionScope));

        // The stale Sheet1-pinned selection scope must be ignored entirely: every "cat" on
        // both sheets is found, including the Sheet1 cell outside the selection.
        workbookResults.Select(r => r.Address).Should().BeEquivalentTo([s1Match, s1OutsideScope, s2Match]);
    }

    [Fact]
    public void Find_WithinSheet_StillHonorsSelectionScope()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var s1Match = new CellAddress(sheet1.Id, 1, 1); // A1, inside the captured scope
        var s1OutsideScope = new CellAddress(sheet1.Id, 10, 1); // A10, outside the captured scope
        var s2Match = new CellAddress(sheet2.Id, 1, 1); // Sheet2!A1
        sheet1.SetCell(s1Match, new TextValue("cat"));
        sheet1.SetCell(s1OutsideScope, new TextValue("cat"));
        sheet2.SetCell(s2Match, new TextValue("cat"));

        var selectionScope = new[] { new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 5, 1)) };

        var sheetResults = FindReplaceService.Find(
            workbook,
            "cat",
            new FindOptions(Within: FindWithin.Sheet, CurrentSheetId: sheet1.Id, SelectionScope: selectionScope));

        // Within=Sheet keeps the selection-scope restriction: only the in-range Sheet1 match.
        sheetResults.Select(r => r.Address).Should().Equal(s1Match);

        // An empty scope list (Count<=1, i.e. the "no restricting selection" case the pattern
        // match "{ Count: > 0 }" guards) is not a restricting selection at all -- Excel: a lone
        // active cell with no multi-cell selection searches the whole sheet. This path is
        // unaffected by the Within-gated fix and must remain unchanged.
        var emptyScopeResults = FindReplaceService.Find(
            workbook,
            "cat",
            new FindOptions(Within: FindWithin.Sheet, CurrentSheetId: sheet1.Id, SelectionScope: Array.Empty<GridRange>()));

        emptyScopeResults.Select(r => r.Address).Should().Equal(s1Match, s1OutsideScope);
    }

    [Fact]
    public void Find_OptionsCanRequireMatchingCellFormat()
    {
        var (wb, sheet, _) = Setup();
        var boldStyle = wb.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(255, 255, 0) });
        var yellowOnlyStyle = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 255, 0) });
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new TextValue("needle"));
        sheet.SetCell(a2, new TextValue("needle"));
        sheet.SetCell(a3, new TextValue("needle"));
        sheet.GetCell(a1)!.StyleId = boldStyle;
        sheet.GetCell(a2)!.StyleId = yellowOnlyStyle;

        var results = FindReplaceService.Find(
            wb,
            "needle",
            new FindOptions(RequiredFormat: new StyleDiff(Bold: true, FillColor: new CellColor(255, 255, 0))));

        results.Select(result => result.Address).Should().Equal(a1);
    }

    [Fact]
    public void ReplaceAll_ReplacesValueCells()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("foo"));

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "foo", "bar");

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("bar"));
    }

    [Fact]
    public void ReplaceAll_DoesNotReplaceFormulaCells()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "SUM(B1:B5)");

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "SUM", "MAX");

        count.Should().Be(0);
        sheet.GetCell(a1)!.FormulaText.Should().Be("SUM(B1:B5)");
    }

    [Fact]
    public void ReplaceAll_WithFormulaLookIn_ReplacesFormulaTextAndSupportsUndoRedo()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetFormula(a1, "SUM(B1:B5)");
        // "SUM literal" contains no "SUM" substring for this cell to be an unrelated distractor;
        // the formula-cell replacement is what this test verifies (constant-cell replacement in
        // Formulas mode is covered separately by ReplaceAll_WithFormulaLookIn_AlsoReplacesConstantCells).
        sheet.SetCell(a2, new TextValue("unrelated literal"));

        var count = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "SUM",
            "MAX",
            new FindOptions(LookIn: FindLookIn.Formulas));

        count.Should().Be(1);
        sheet.GetCell(a1)!.FormulaText.Should().Be("MAX(B1:B5)");
        sheet.GetCell(a2)!.Value.Should().Be(new TextValue("unrelated literal"));

        commandBus.Undo(wb.Id).Success.Should().BeTrue();
        sheet.GetCell(a1)!.FormulaText.Should().Be("SUM(B1:B5)");

        commandBus.Redo(wb.Id).Success.Should().BeTrue();
        sheet.GetCell(a1)!.FormulaText.Should().Be("MAX(B1:B5)");
    }

    [Fact]
    public void ReplaceAll_WithFormulaLookIn_AlsoReplacesConstantCells()
    {
        // Excel semantics: "Look in: Formulas" is the ONLY replace mode Excel offers, and it
        // replaces constants too — Find and Replace must agree on what counts as a match.
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetFormula(a1, "SUM(B1:B5)");
        sheet.SetCell(a2, new TextValue("SUM literal"));

        var found = FindReplaceService.Find(wb, "SUM", new FindOptions(LookIn: FindLookIn.Formulas));
        found.Should().HaveCount(2);

        var count = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "SUM",
            "MAX",
            new FindOptions(LookIn: FindLookIn.Formulas));

        count.Should().Be(2);
        sheet.GetCell(a1)!.FormulaText.Should().Be("MAX(B1:B5)");
        sheet.GetCell(a2)!.Value.Should().Be(new TextValue("MAX literal"));
    }

    [Fact]
    public void ReplaceAll_ReplacesSubstring_InValueCells()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("foobar"));

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "foo", "baz");

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("bazbar"));
    }

    [Fact]
    public void ReplaceAll_HonorsSheetScope()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var commandBus = new CommandBus(id => new TestCommandContext(workbook));
        var a1 = new CellAddress(sheet1.Id, 1, 1);
        var a2 = new CellAddress(sheet2.Id, 1, 1);
        sheet1.SetCell(a1, new TextValue("foo"));
        sheet2.SetCell(a2, new TextValue("foo"));

        var count = FindReplaceService.ReplaceAll(
            workbook,
            commandBus,
            "foo",
            "bar",
            new FindOptions(Within: FindWithin.Sheet, CurrentSheetId: sheet1.Id));

        count.Should().Be(1);
        sheet1.GetCell(a1)!.Value.Should().Be(new TextValue("bar"));
        sheet2.GetCell(a2)!.Value.Should().Be(new TextValue("foo"));
    }

    [Fact]
    public void ReplaceAll_AppliesReplacementFormatToChangedCellsOnly()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new TextValue("foo"));
        sheet.SetCell(a2, new TextValue("other"));

        var count = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "foo",
            "bar",
            replacementFormat: new StyleDiff(Bold: true, FillColor: new CellColor(255, 255, 0)));

        count.Should().Be(1);
        var replacedStyle = wb.GetStyle(sheet.GetCell(a1)!.StyleId);
        replacedStyle.Bold.Should().BeTrue();
        replacedStyle.FillColor.Should().Be(new CellColor(255, 255, 0));
        wb.GetStyle(sheet.GetCell(a2)!.StyleId).Bold.Should().BeFalse();
    }

    [Fact]
    public void ReplaceAll_WithNotesAndCommentsLookIn_ReplacesTextSurfaces()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("foo value"));
        sheet.Comments[a1] = "foo note";
        sheet.ThreadedComments[b1] = new ThreadedComment("foo root", "Quinn")
        {
            Replies = [new CommentReply("foo reply", "Codex")]
        };

        var notes = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "foo",
            "bar",
            new FindOptions(LookIn: FindLookIn.Notes));
        var comments = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "foo",
            "bar",
            new FindOptions(LookIn: FindLookIn.Comments));

        notes.Should().Be(1);
        comments.Should().Be(2);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("foo value"));
        sheet.Comments[a1].Should().Be("bar note");
        sheet.ThreadedComments[b1].Text.Should().Be("bar root");
        sheet.ThreadedComments[b1].Replies.Single().Text.Should().Be("bar reply");

        commandBus.Undo(wb.Id).Success.Should().BeTrue();
        sheet.ThreadedComments[b1].Text.Should().Be("foo root");
        sheet.ThreadedComments[b1].Replies.Single().Text.Should().Be("foo reply");

        commandBus.Undo(wb.Id).Success.Should().BeTrue();
        sheet.Comments[a1].Should().Be("foo note");
    }

    // ── Fix 2: formula replace clears stale cached Value ──────────────────────

    [Fact]
    public void ReplaceAll_WithFormulaLookIn_ClearsStaleValueAfterReplace()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        // Simulate a cell that has been evaluated: formula "SUM(1,2)" with cached Value 3.
        var cell = Cell.FromFormula("SUM(1,2)");
        cell.Value = new NumberValue(3);
        sheet.SetCell(a1, cell);

        FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "SUM",
            "MAX",
            new FindOptions(LookIn: FindLookIn.Formulas));

        // After replace the formula text should be updated.
        sheet.GetCell(a1)!.FormulaText.Should().Be("MAX(1,2)");
        // The cached Value must be cleared (BlankValue) — not the stale result — so that
        // any display before recalculation does not show a wrong number.
        sheet.GetCell(a1)!.Value.Should().Be(BlankValue.Instance);
    }

    // ── Fix 3: number-skip perf optimization ──────────────────────────────────

    [Fact]
    public void Find_NumericSearch_StillFindsNumberCells()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(42));

        // A plain digit-only search must still match number cells.
        var results = FindReplaceService.Find(wb, "42");

        results.Should().HaveCount(1);
        results[0].Address.Should().Be(a1);
    }

    [Fact]
    public void Find_PatternWithWildcard_NumberCellsNotSkipped()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        // Use a value whose invariant rendering contains '*' so a literal substring match works.
        // The point of this test is not that '*' acts as a glob, but that CanSearchTextMatchNumber
        // returns true for patterns that contain '*', so number cells are NOT pre-filtered out.
        // Verify via the helper directly.
        sheet.SetCell(a1, new NumberValue(42));

        // A pattern with a wildcard character ('*' or '?') must not trigger the number-skip
        // optimization — CanSearchTextMatchNumber must return true for such patterns.
        FindReplaceService.CanSearchTextMatchNumber("4*").Should().BeTrue(
            "wildcard patterns must not skip number cells");
        FindReplaceService.CanSearchTextMatchNumber("?2").Should().BeTrue(
            "wildcard patterns must not skip number cells");

        // Confirm a numeric substring (no wildcards) still finds the cell.
        var results = FindReplaceService.Find(wb, "42");
        results.Should().HaveCount(1);
        results[0].Address.Should().Be(a1);
    }

    [Fact]
    public void Find_PlainTextSearch_SkipsNumberCells()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new NumberValue(42));
        sheet.SetCell(a2, new TextValue("foo42"));

        // A plain text search with a non-numeric character ('f') cannot match any number
        // cell; the result must still include the text cell but must not return the number cell.
        var results = FindReplaceService.Find(wb, "foo");

        results.Should().HaveCount(1);
        results[0].Address.Should().Be(a2);
    }

    [Fact]
    public void Find_PlainTextSearch_SkipNumberCells_SameResultsAsFullScan()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        // Mix of number and text cells, none of which match "hello".
        var addresses = new[]
        {
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 2, 1),
        };
        sheet.SetCell(addresses[0], new NumberValue(100));
        sheet.SetCell(addresses[1], new TextValue("world"));
        sheet.SetCell(addresses[2], new NumberValue(3.14));

        var results = FindReplaceService.Find(workbook, "hello");

        results.Should().BeEmpty();
    }

    [Fact]
    public void CanSearchTextMatchNumber_ReturnsCorrectly()
    {
        // Patterns that can match numbers (digits, sign, decimal, exponent, wildcards).
        FindReplaceService.CanSearchTextMatchNumber("42").Should().BeTrue();
        FindReplaceService.CanSearchTextMatchNumber("3.14").Should().BeTrue();
        FindReplaceService.CanSearchTextMatchNumber("-1").Should().BeTrue();
        FindReplaceService.CanSearchTextMatchNumber("1E+10").Should().BeTrue();
        // Wildcards are handled separately in Find_PatternWithWildcard_NumberCellsNotSkipped.

        // Plain text patterns that can never appear in a number.
        FindReplaceService.CanSearchTextMatchNumber("foo").Should().BeFalse();
        FindReplaceService.CanSearchTextMatchNumber("hello world").Should().BeFalse();
        FindReplaceService.CanSearchTextMatchNumber("SUM").Should().BeFalse();
    }

    // ── F11: number-format-aware display text in Values search ───────────────

    [Fact]
    public void Find_Values_UsesAppliedNumberFormat_PercentCell()
    {
        // Regression: a cell with value 0.5 formatted as "0%" displays "50%" to the user.
        // Searching Values for "50%" must find it; searching "0.5" must NOT (invariant rendering
        // is not what the user sees).
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);

        var percentStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "0%" });
        var cell = Cell.FromValue(new NumberValue(0.5));
        cell.StyleId = percentStyle;
        sheet.SetCell(a1, cell);

        // Should find when searching the formatted display text.
        var results = FindReplaceService.Find(wb, "50%");
        results.Should().ContainSingle().Which.Address.Should().Be(a1);

        // Should NOT find when searching the invariant raw value (user does not see "0.5").
        var rawResults = FindReplaceService.Find(wb, "0.5");
        rawResults.Should().BeEmpty("invariant raw value must not be returned in Values mode");
    }

    [Fact]
    public void Find_Values_UsesAppliedNumberFormat_CurrencyCell()
    {
        // A cell with value 1000 formatted as "$#,##0.00" displays "$1,000.00".
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);

        var currencyStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var cell = Cell.FromValue(new NumberValue(1000));
        cell.StyleId = currencyStyle;
        sheet.SetCell(a1, cell);

        var results = FindReplaceService.Find(wb, "$1,000.00");
        results.Should().ContainSingle().Which.Address.Should().Be(a1);
    }

    [Fact]
    public void Find_Values_NumberFormat_TextCellUnchanged()
    {
        // Text cells must still be found by their text content regardless of number format.
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("hello"));

        var results = FindReplaceService.Find(wb, "hello");
        results.Should().ContainSingle().Which.Address.Should().Be(a1);
    }

    [Fact]
    public void TryReplaceAll_ReturnsCommandFailureInsteadOfCountingRejectedEdits()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("foo"));
        var commandBus = new RejectingCommandBus("The sheet is protected.");

        var result = FindReplaceService.TryReplaceAll(workbook, commandBus, "foo", "bar");

        result.ReplacedCount.Should().Be(0);
        result.Failure.Should().Be(new CommandOutcome(false, "The sheet is protected."));
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("foo"));
    }
}

file sealed class RejectingCommandBus(string message) : ICommandBus
{
    public CommandOutcome Execute(WorkbookId workbookId, IWorkbookCommand command) => new(false, message);
    public CommandOutcome ExecuteRepeatable(WorkbookId workbookId, Func<IWorkbookCommand> commandFactory) => new(false, message);
    public CommandOutcome Undo(WorkbookId workbookId) => new(false, message);
    public CommandOutcome Redo(WorkbookId workbookId) => new(false, message);
    public bool CanUndo(WorkbookId workbookId) => false;
    public bool CanRedo(WorkbookId workbookId) => false;
    public CommandOutcome RepeatLast(WorkbookId workbookId) => new(false, message);
    public bool CanRepeat(WorkbookId workbookId) => false;
    public int GetUndoStackDepth(WorkbookId workbookId) => 0;
}
