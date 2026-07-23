using FluentAssertions;
using FreeX.App.Host;
using Xunit;

namespace FreeX.App.Host.Tests;

public sealed class FormulaEditInteractionPlannerTests
{
    [Fact]
    public void IsRangeEntryActive_RequiresFormulaTextAndPointMode()
    {
        FormulaEditInteractionPlanner.IsRangeEntryActive("=SUM(A1:A2)", pointMode: false)
            .Should().BeFalse();

        FormulaEditInteractionPlanner.IsRangeEntryActive("=SUM(", pointMode: true)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldCommitInlineArrows_CommitsOnlyNonFormulaText()
    {
        FormulaEditInteractionPlanner.ShouldCommitInlineArrows("abc", pointMode: false)
            .Should().BeTrue();

        FormulaEditInteractionPlanner.ShouldCommitInlineArrows("=SUM(A1:A2)", pointMode: false)
            .Should().BeFalse();

        FormulaEditInteractionPlanner.ShouldCommitInlineArrows("=SUM(", pointMode: true)
            .Should().BeFalse();
    }

    // R78-render-inplace-editor-5-1: F2 (Edit mode) puts the caret inside existing, non-formula
    // content -- arrow keys must reposition the caret, never commit -- whereas typing a fresh
    // character over the selection (Enter mode) still commits on a plain arrow key, matching real
    // Excel. Before the enteredViaEditKey parameter existed, ShouldCommitInlineArrows only checked
    // "is this formula text", so F2 on plain text ("Hello") behaved exactly like fresh typed entry
    // and every arrow press committed instead of moving the caret.
    [Fact]
    public void ShouldCommitInlineArrows_NeverCommitsWhenEnteredViaEditKeyEvenForNonFormulaText()
    {
        FormulaEditInteractionPlanner.ShouldCommitInlineArrows("Hello", pointMode: false, enteredViaEditKey: true)
            .Should().BeFalse();

        // Formula text and active range-entry already never commit regardless of entry mode.
        FormulaEditInteractionPlanner.ShouldCommitInlineArrows("=SUM(A1:A2)", pointMode: false, enteredViaEditKey: true)
            .Should().BeFalse();
    }

    // No-regression sibling: typed-entry (Enter mode, the default/explicit-false case) must keep
    // committing non-formula text on a plain arrow key -- only F2/double-click Edit-mode sessions
    // gained the new non-committing behavior.
    [Fact]
    public void ShouldCommitInlineArrows_StillCommitsWhenEnteredViaTypedOvertypeForNonFormulaText()
    {
        FormulaEditInteractionPlanner.ShouldCommitInlineArrows("Hello", pointMode: false, enteredViaEditKey: false)
            .Should().BeTrue();

        FormulaEditInteractionPlanner.ShouldCommitInlineArrows("=SUM(A1:A2)", pointMode: false, enteredViaEditKey: false)
            .Should().BeFalse();
    }

    [Fact]
    public void TogglePointMode_TogglesOnlyFormulaText()
    {
        FormulaEditInteractionPlanner.TogglePointMode("=A1", pointMode: false).Should().BeTrue();
        FormulaEditInteractionPlanner.TogglePointMode("=A1", pointMode: true).Should().BeFalse();
        FormulaEditInteractionPlanner.TogglePointMode("abc", pointMode: false).Should().BeFalse();
    }

    [Fact]
    public void ShouldStartPointModeFromTypedText_StartsOnlyForNewFormulaEntry()
    {
        FormulaEditInteractionPlanner.ShouldStartPointModeFromTypedText("=").Should().BeTrue();
        FormulaEditInteractionPlanner.ShouldStartPointModeFromTypedText("=SUM(").Should().BeFalse();
        FormulaEditInteractionPlanner.ShouldStartPointModeFromTypedText("text").Should().BeFalse();
    }
}
