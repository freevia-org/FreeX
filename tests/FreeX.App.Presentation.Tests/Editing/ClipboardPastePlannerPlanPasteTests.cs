using FluentAssertions;
using FreeX.App.Presentation.Editing;

namespace FreeX.App.Presentation.Tests.Editing;

/// <summary>
/// Regression coverage for group E-clipboard-text finding P1: a transient OS clipboard read
/// failure must never be silently reinterpreted as "clipboard unchanged" (which would risk a
/// stale internal-clipboard paste over content the user just copied elsewhere). Callers that can
/// distinguish a failed read from a successful-but-empty one should use
/// <see cref="ClipboardPastePlanner.PlanPaste"/> and get an explicit <see cref="ClipboardPastePlan.ReadFailed"/>
/// signal instead of guessing.
/// </summary>
public sealed class ClipboardPastePlannerPlanPasteTests
{
    [Fact]
    public void PlanPaste_ReadFailedTakesPriorityOverInternalClipboardFallback()
    {
        ClipboardPastePlanner.PlanPaste(
                internalClipboardText: "FreeX copy",
                currentClipboardText: null,
                clipboardReadFailed: true)
            .Should()
            .Be(ClipboardPastePlan.ReadFailed, "a failed OS clipboard read must never be treated as an unchanged clipboard");
    }

    [Fact]
    public void PlanPaste_SuccessfulNullReadStillFallsBackToInternalClipboard()
    {
        // A read that succeeded with no text (empty/non-text clipboard) is the historical
        // "clipboard unchanged" fallback and must keep working exactly as before.
        ClipboardPastePlanner.PlanPaste(
                internalClipboardText: "FreeX copy",
                currentClipboardText: null,
                clipboardReadFailed: false)
            .Should()
            .Be(ClipboardPastePlan.UseInternalClipboard);
    }

    [Fact]
    public void PlanPaste_MatchingClipboardTextUsesInternalClipboard()
    {
        ClipboardPastePlanner.PlanPaste(
                internalClipboardText: "FreeX copy",
                currentClipboardText: "FreeX copy",
                clipboardReadFailed: false)
            .Should()
            .Be(ClipboardPastePlan.UseInternalClipboard);
    }

    [Fact]
    public void PlanPaste_ChangedClipboardTextUsesExternalClipboardText()
    {
        ClipboardPastePlanner.PlanPaste(
                internalClipboardText: "FreeX copy",
                currentClipboardText: "External app copy",
                clipboardReadFailed: false)
            .Should()
            .Be(ClipboardPastePlan.UseExternalClipboardText);
    }

    [Fact]
    public void PlanPaste_NoInternalClipboardUsesExternalClipboardText()
    {
        ClipboardPastePlanner.PlanPaste(
                internalClipboardText: null,
                currentClipboardText: "Some external text",
                clipboardReadFailed: false)
            .Should()
            .Be(ClipboardPastePlan.UseExternalClipboardText);
    }

    [Fact]
    public void PlanPaste_ReadFailedTakesPriorityEvenWithNoInternalClipboard()
    {
        ClipboardPastePlanner.PlanPaste(
                internalClipboardText: null,
                currentClipboardText: null,
                clipboardReadFailed: true)
            .Should()
            .Be(ClipboardPastePlan.ReadFailed);
    }
}
