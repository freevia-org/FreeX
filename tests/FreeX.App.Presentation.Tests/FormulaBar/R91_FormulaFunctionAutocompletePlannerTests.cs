using FluentAssertions;
using FreeX.App.Presentation.FormulaBar;

namespace FreeX.App.Presentation.Tests.FormulaBar;

/// <summary>
/// R91-formula-editing-assist-5-1: there was no function-name AutoComplete while typing a formula
/// (typing "=SU" never offered SUM/SUBTOTAL/SUMIF/...). These tests exercise the new portable
/// planner behind that feature directly (the nearest unit-testable seam for a WPF/Avalonia popup).
/// </summary>
public sealed class R91_FormulaFunctionAutocompletePlannerTests
{
    private static readonly string[] SampleFunctionNames =
        ["SUM", "SUBTOTAL", "SUMIF", "SUMIFS", "SUMPRODUCT", "SUBSTITUTE", "AVERAGE", "IF"];

    [Fact]
    public void ShouldShowAutocomplete_TypingFunctionPrefix_ReturnsTokenAndPrefix()
    {
        var shown = FormulaFunctionAutocompletePlanner.ShouldShowAutocomplete(
            "=SU", caretIndex: 3, out var tokenStart, out var tokenLength, out var prefix);

        shown.Should().BeTrue();
        tokenStart.Should().Be(1);
        tokenLength.Should().Be(2);
        prefix.Should().Be("SU");
    }

    [Fact]
    public void BuildCandidates_FiltersByPrefixCaseInsensitiveAndSortsAlphabetically()
    {
        var candidates = FormulaFunctionAutocompletePlanner.BuildCandidates("su", SampleFunctionNames);

        candidates.Should().Equal("SUBSTITUTE", "SUBTOTAL", "SUM", "SUMIF", "SUMIFS", "SUMPRODUCT");
    }

    [Fact]
    public void BuildCandidates_IncludesDefinedAndTableNamesAlongsideFunctions()
    {
        var candidates = FormulaFunctionAutocompletePlanner.BuildCandidates(
            "Sale",
            functionNames: SampleFunctionNames,
            definedNames: ["SalesTotal"],
            tableNames: ["SalesTable"]);

        candidates.Should().Equal("SalesTable", "SalesTotal");
    }

    [Fact]
    public void Commit_ReplacesTypedPrefixWithNameAndOpeningParen()
    {
        var (text, caretIndex) = FormulaFunctionAutocompletePlanner.Commit(
            "=SU", tokenStart: 1, tokenLength: 2, chosenName: "SUM", isFunction: true);

        text.Should().Be("=SUM(");
        caretIndex.Should().Be(5);
    }

    [Fact]
    public void Commit_MidFormula_PreservesTextAfterToken()
    {
        var (text, caretIndex) = FormulaFunctionAutocompletePlanner.Commit(
            "=1+SU+2", tokenStart: 3, tokenLength: 2, chosenName: "SUM", isFunction: true);

        text.Should().Be("=1+SUM(+2");
        caretIndex.Should().Be(7);
    }

    [Fact]
    public void MoveSelection_ArrowDownFromNoSelection_SelectsFirst()
    {
        FormulaFunctionAutocompletePlanner.MoveSelection(currentIndex: -1, candidateCount: 3, delta: 1)
            .Should().Be(0);
    }

    [Fact]
    public void MoveSelection_WrapsAroundAtEitherEnd()
    {
        FormulaFunctionAutocompletePlanner.MoveSelection(currentIndex: 2, candidateCount: 3, delta: 1)
            .Should().Be(0);
        FormulaFunctionAutocompletePlanner.MoveSelection(currentIndex: 0, candidateCount: 3, delta: -1)
            .Should().Be(2);
    }

    // ── No-regression siblings ──────────────────────────────────────────────

    [Fact]
    public void ShouldShowAutocomplete_JustAfterOpeningParen_ReturnsFalse_NoRegression()
    {
        // Once the user has typed the full name plus "(", Excel's function-name popup disappears
        // (the signature-help tooltip takes over instead -- see FormulaSignatureHelpPlanner).
        var shown = FormulaFunctionAutocompletePlanner.ShouldShowAutocomplete(
            "=SUM(", caretIndex: 5, out _, out _, out _);

        shown.Should().BeFalse();
    }

    [Fact]
    public void ShouldShowAutocomplete_NonFormulaText_ReturnsFalse_NoRegression()
    {
        var shown = FormulaFunctionAutocompletePlanner.ShouldShowAutocomplete(
            "SU", caretIndex: 2, out _, out _, out _);

        shown.Should().BeFalse();
    }

    [Fact]
    public void BuildCandidates_EmptyPrefix_ReturnsEmpty_NoRegression()
    {
        FormulaFunctionAutocompletePlanner.BuildCandidates("", SampleFunctionNames).Should().BeEmpty();
    }

    [Fact]
    public void MoveSelection_NoCandidates_ReturnsNoSelection_NoRegression()
    {
        FormulaFunctionAutocompletePlanner.MoveSelection(currentIndex: -1, candidateCount: 0, delta: 1)
            .Should().Be(-1);
    }

    // ── R114: defined-name/table-name candidates must not get a trailing "(" ───────────────────
    // FormulaFunctionAutocompletePlanner.BuildCandidates() deliberately merges built-in function
    // names with workbook defined names and structured-table names into one candidate list (see
    // BuildCandidates_IncludesDefinedAndTableNamesAlongsideFunctions above), but only function names
    // are callable. Committing a defined name or table name must insert the bare name -- never
    // "SalesTotal(" -- or the formula is left with an unbalanced parenthesis.

    [Fact]
    public void R114_Commit_DefinedNameCandidate_InsertsBareNameWithNoOpeningParen()
    {
        var (text, caretIndex) = FormulaFunctionAutocompletePlanner.Commit(
            "=Sale", tokenStart: 1, tokenLength: 4, chosenName: "SalesTotal", isFunction: false);

        text.Should().Be("=SalesTotal");
        caretIndex.Should().Be(11);
    }

    [Fact]
    public void R114_Commit_TableNameCandidate_MidFormula_InsertsBareNameWithNoOpeningParen()
    {
        var (text, caretIndex) = FormulaFunctionAutocompletePlanner.Commit(
            "=SUM(Sale)", tokenStart: 5, tokenLength: 4, chosenName: "SalesTable", isFunction: false);

        text.Should().Be("=SUM(SalesTable)");
        caretIndex.Should().Be(15);
    }

    [Fact]
    public void R114_Commit_FunctionCandidate_StillAppendsOpeningParen_NoRegression()
    {
        var (text, caretIndex) = FormulaFunctionAutocompletePlanner.Commit(
            "=Sale", tokenStart: 1, tokenLength: 4, chosenName: "SUM", isFunction: true);

        text.Should().Be("=SUM(");
        caretIndex.Should().Be(5);
    }

    [Fact]
    public void R114_IsFunctionCandidate_MatchesBuiltInFunctionNameCaseInsensitively()
    {
        FormulaFunctionAutocompletePlanner.IsFunctionCandidate("sum", SampleFunctionNames)
            .Should().BeTrue();
    }

    [Fact]
    public void R114_IsFunctionCandidate_DefinedNameNotInFunctionList_ReturnsFalse()
    {
        FormulaFunctionAutocompletePlanner.IsFunctionCandidate("SalesTotal", SampleFunctionNames)
            .Should().BeFalse();
    }

    [Fact]
    public void R114_IsFunctionCandidate_NullFunctionNames_ReturnsFalse_NoRegression()
    {
        FormulaFunctionAutocompletePlanner.IsFunctionCandidate("SUM", null)
            .Should().BeFalse();
    }
}
