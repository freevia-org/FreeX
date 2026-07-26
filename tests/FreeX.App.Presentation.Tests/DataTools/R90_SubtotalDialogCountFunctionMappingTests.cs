using FluentAssertions;
using FreeX.App.Presentation.DataTools;
using FreeX.Core.Commands;

namespace FreeX.App.Presentation.Tests.DataTools;

// R90-commands-subtotal-outline-5-1: real Excel's Subtotal dialog "Count" behaves like COUNTA
// (SUBTOTAL function_num 3 — counts text and numbers) and "Count Numbers" behaves like COUNT
// (function_num 2 — numeric only). FreeX had these swapped: choosing "Count" produced
// SUBTOTAL(2,...), which silently returns 0 on an all-text column instead of the correct count.
// Exercised through the real dialog entry point: SubtotalDialogPlanner.CreateFunctionChoices
// supplies the exact FunctionText tokens ("Count" / "CountA") the Subtotal dialog wires to its
// "Count" / "Count Numbers" labels, and SubtotalDialogInputParser.TryParse is what
// SubtotalCommand's caller uses to turn that dialog text into the functionNumber it applies.
public sealed class R90_SubtotalDialogCountFunctionMappingTests
{
    [Fact]
    public void R90_TryParse_DialogCountLabelToken_MapsToCountA_FunctionNumber3()
    {
        // "Count" is the FunctionText token CreateFunctionChoices wires to the dialog's plain
        // "Count" label (see SubtotalDialogPlannerTests / CreateFunctionChoices_UsesSharedFunctionTokens).
        SubtotalDialogInputParser.TryParse(
                groupColumnText: "0",
                subtotalColumnsText: "1",
                functionText: "Count",
                replaceCurrentSubtotals: true,
                pageBreakBetweenGroups: false,
                summaryBelowData: true,
                out var result,
                out var issue)
            .Should().BeTrue(issue.ToString());

        result.FunctionNumber.Should().Be(3, "Excel's Subtotal dialog 'Count' behaves like COUNTA (counts text too)");
    }

    [Fact]
    public void R90_TryParse_DialogCountNumbersLabelToken_MapsToCount_FunctionNumber2()
    {
        // "CountA" is the FunctionText token CreateFunctionChoices wires to the dialog's
        // "Count Numbers" label.
        SubtotalDialogInputParser.TryParse(
                groupColumnText: "0",
                subtotalColumnsText: "1",
                functionText: "CountA",
                replaceCurrentSubtotals: true,
                pageBreakBetweenGroups: false,
                summaryBelowData: true,
                out var result,
                out var issue)
            .Should().BeTrue(issue.ToString());

        result.FunctionNumber.Should().Be(2, "Excel's Subtotal dialog 'Count Numbers' behaves like COUNT (numeric only)");
    }

    // No-regression sibling: an unrelated function token (Sum) must still map to its own,
    // untouched function number through the same real entry point.
    [Fact]
    public void R90_TryParse_DialogSumLabelToken_StillMapsToSum_FunctionNumber9()
    {
        SubtotalDialogInputParser.TryParse(
                groupColumnText: "0",
                subtotalColumnsText: "1",
                functionText: "sum",
                replaceCurrentSubtotals: true,
                pageBreakBetweenGroups: false,
                summaryBelowData: true,
                out var result,
                out var issue)
            .Should().BeTrue(issue.ToString());

        result.FunctionNumber.Should().Be(9);
    }

    [Fact]
    public void R90_CreateFunctionChoices_StillWiresCountAndCountNumbersToExpectedTokens()
    {
        // Locks the dialog-facing token wiring itself (SubtotalDialogPlanner side of the pipeline)
        // so a future edit there can't silently re-break the mapping this test pins.
        var choices = SubtotalDialogPlanner.CreateFunctionChoices();

        choices.Single(c => c.Label == "Count").FunctionText.Should().Be("Count");
        choices.Single(c => c.Label == "Count Numbers").FunctionText.Should().Be("CountA");
    }
}
