using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R160-formula-editing-F1: <c>FormulaSignatureHelpPlanner.Resolve</c> is caret-position-sensitive
/// -- it re-bolds whichever argument the caret currently sits inside -- but the Formula Bar's
/// <c>SelectionChanged</c> handler used to only call <c>ClearFormulaReferenceEntrySpanIfCaretLeftReference</c>,
/// never <c>RefreshFormulaSignatureHelp</c>. Moving the caret with no further keystroke (arrow
/// keys, or clicking to reposition) left the live signature tooltip showing whichever argument was
/// current at the last <c>TextChanged</c>, even after the caret moved into a different argument.
/// </summary>
public sealed class R160_FormulaSignatureHelpCaretMoveTests
{
    [Fact]
    public void FormulaBarCaretMove_WithoutTyping_RebindsBoldedArgumentToCaretPosition()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();

            const string formula = "=IF(A1>0,B1,C1)";
            harness.SetFormulaBarText(formula);
            // Caret just before the closing ")" (i.e. right after typing "...,C1", matching the
            // finding's gesture) sits inside the 3rd (optional) argument.
            harness.SetFormulaBarCaretIndex(formula.Length - 1);

            harness.Window.FormulaSignatureHelpIsOpenForTest.Should().BeTrue();
            harness.Window.FormulaSignatureHelpBoldArgumentForTest.Should().Be("[Value_if_false]");

            // Move the caret back into "A1>0" -- a caret-only move with no text change alongside
            // it, so only SelectionChanged fires, never TextChanged.
            harness.SetFormulaBarCaretIndex("=IF(A1>".Length);

            harness.Window.FormulaSignatureHelpIsOpenForTest.Should().BeTrue();
            harness.Window.FormulaSignatureHelpBoldArgumentForTest.Should().Be("Logical_test");
        });
    }

    [Fact]
    public void FormulaBarCaretMove_PastClosingParen_HidesSignatureHelpTooltip()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();

            const string formula = "=IF(A1>0,B1,C1)";
            harness.SetFormulaBarText(formula);
            harness.SetFormulaBarCaretIndex("=IF(A1>".Length);
            harness.Window.FormulaSignatureHelpIsOpenForTest.Should().BeTrue();

            // Sibling no-regression case: moving the caret past the call's closing parenthesis
            // (still no text change, so only SelectionChanged fires) must hide the tooltip rather
            // than leaving the stale "Logical_test" bolding on display -- FormulaSignatureHelpPlanner
            // already returns null once the caret is past the call (see the pinned
            // Resolve_AfterClosingParen_ReturnsNull_NoRegression planner test); this exercises that
            // through the caret-only-move UI path the fix now wires up.
            harness.SetFormulaBarCaretIndex(formula.Length);
            harness.Window.FormulaSignatureHelpIsOpenForTest.Should().BeFalse();
        });
    }
}
