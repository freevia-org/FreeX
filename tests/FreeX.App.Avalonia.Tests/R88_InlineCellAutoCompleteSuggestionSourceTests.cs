using System;
using System.Linq;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-88 regression test for finding R88-app-autocomplete-picklist-5-2 (MED): the Avalonia shell's
/// inline cell editor never implemented Excel's "AutoComplete for cell values" -- typing "app" into a
/// cell below an existing "Apple" text entry left exactly "app" in the cell with no suggestion ever
/// offered, regardless of the AutoComplete option, because <c>CreateInlineCellEditor</c>'s
/// <c>editor.TextChanged</c> handler only forwarded the text to the formula box and never consulted
/// <see cref="FreeX.Core.Commands.CellValueAutoCompleteSuggester"/>. The WPF host implemented this same
/// feature in MainWindow.Editing.cs's ApplyCellValueAutoCompleteSuggestion (R83); this fix ports the
/// same suggester call into the Avalonia in-cell editor's TextChanged handler.
/// </summary>
public sealed class R88_InlineCellAutoCompleteSuggestionSourceTests
{
    private static string ReadMainWindowSource() =>
        TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("src", "FreeX.App.Avalonia", "MainWindow.cs");

    /// <summary>Isolates the inline cell editor's TextChanged handler body.</summary>
    private static string ExtractTextChangedHandlerSource(string source)
    {
        const string start = "editor.TextChanged += (_, _) =>";
        const string end = "editor.KeyDown += (_, args) => InlineCellEditor_KeyDown(address, editor, args);";

        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, "the inline cell editor's TextChanged handler must still exist");

        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex, "the TextChanged handler must still be immediately followed by the KeyDown wiring");

        return source[startIndex..endIndex];
    }

    [Fact]
    public void InlineCellEditorTextChanged_InvokesAutoCompleteSuggestion()
    {
        var handlerSource = ExtractTextChangedHandlerSource(ReadMainWindowSource());

        // This is the exact regression: before the fix, the handler only forwarded text to the
        // formula box / formula-reference-entry state and never called into the AutoComplete
        // suggestion path at all.
        handlerSource.Should().Contain(
            "ApplyInlineCellValueAutoCompleteSuggestion(address, editor)",
            "typing a plain text entry in the inline cell editor must offer a column AutoComplete " +
            "suggestion the same way the WPF host's inline/formula-bar editor already does");
    }

    [Fact]
    public void InlineCellEditorTextChanged_StillForwardsTextToFormulaBoxAndHighlights_NoRegression()
    {
        // No-regression sibling: adding the AutoComplete call must not have displaced the handler's
        // pre-existing responsibilities (formula box sync, reference-entry state, highlight refresh).
        var handlerSource = ExtractTextChangedHandlerSource(ReadMainWindowSource());

        handlerSource.Should().Contain("_formulaBox.Text = _inlineCellEditText;");
        handlerSource.Should().Contain("ClearFormulaReferenceEntrySpan();");
        handlerSource.Should().Contain("UpdateFormulaRangeEntryStateAfterTextChanged(_inlineCellEditText);");
        handlerSource.Should().Contain("RefreshFormulaReferenceHighlights();");
        handlerSource.Should().Contain("RefreshFormulaReferenceGridHighlights();");
    }

    [Fact]
    public void ApplyInlineCellValueAutoCompleteSuggestion_GatesOnOptionRangeModeAndForwardCaret()
    {
        var source = ReadMainWindowSource();
        var sessionSource = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Presentation", "FormulaBar", "FormulaRangeEditingSession.cs");
        var methodStart = source.IndexOf("private void ApplyInlineCellValueAutoCompleteSuggestion(", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0, "the ported AutoComplete method must exist");

        var methodEnd = source.IndexOf("private void BeginInlineCellEdit(", methodStart, StringComparison.Ordinal);
        methodEnd.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..methodEnd];

        // Mirrors the WPF host's gating: option enabled, not mid-formula/range-entry, caret at the
        // end with nothing selected, then the shared portable suggester (no reinvented matching
        // logic) collects candidates and offers a suggestion.
        method.Should().Contain("EnableAutoCompleteForCellValues");
        method.Should().Contain("_formulaRangeEditingSession.PlanCellValueAutocomplete(");
        sessionSource.Should().Contain("ShouldOfferCellValueAutoComplete(enabled)");
        sessionSource.Should().Contain("IsFormulaText(text)");
        sessionSource.Should().Contain("selectionLength != 0");
        sessionSource.Should().Contain("caretIndex != text.Length");
        sessionSource.Should().Contain("CellValueAutoCompleteSuggester.CollectContiguousColumnTextEntries(");
        sessionSource.Should().Contain("CellValueAutoCompleteSuggester.Suggest(candidates, text)");
    }

    [Fact]
    public void InlineCellEditorKeyDown_BackspaceAndDeleteSuppressNextSuggestion_NoRegression()
    {
        // No-regression sibling: Backspace/Delete must still be able to reject a live suggestion
        // (Excel behavior) rather than the AutoComplete instantly re-offering the same completion.
        var source = ReadMainWindowSource();
        var sessionSource = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Presentation", "FormulaBar", "FormulaRangeEditingSession.cs");

        source.Should().Contain("if (args.Key is Key.Back or Key.Delete)");
        source.Should().Contain("_formulaRangeEditingSession.SuppressNextCellValueAutocomplete();");
        sessionSource.Should().Contain("_suppressNextCellValueAutocomplete = true;");
    }
}
