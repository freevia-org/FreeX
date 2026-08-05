using FluentAssertions;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class DialogRangeSelectionControllerTests
{
    [Fact]
    public void Begin_ReplacesActiveSessionThroughRestoreTransitionBeforeActivatingNext()
    {
        var controller = new DialogRangeSelectionController<string>();
        var transitions = new List<DialogRangeSelectionTransition<string>>();

        var first = controller.Begin(
            "first",
            "original",
            DialogRangeSelectionFormat.Range,
            collapseDialog: false,
            ownerWasEnabled: false,
            transitions.Add);
        var second = controller.Begin(
            "second",
            "next",
            DialogRangeSelectionFormat.StartCell,
            collapseDialog: true,
            ownerWasEnabled: true,
            transitions.Add);

        transitions.Should().ContainSingle();
        transitions[0].State.Should().BeSameAs(first);
        transitions[0].RestoreDialog.Should().BeTrue();
        transitions[0].RestoreOriginalText.Should().BeTrue();
        transitions[0].ApplySelection.Should().BeFalse();
        controller.Active.Should().BeSameAs(second);
        second.OriginalText.Should().Be("next");
        second.CollapseDialog.Should().BeTrue();
        second.OwnerWasEnabled.Should().BeTrue();
    }

    [Fact]
    public void DecideKey_HandlesEnterAndEscapeOnlyWhileSessionIsActive()
    {
        var controller = new DialogRangeSelectionController<object>();

        controller.DecideKey(DialogRangeSelectionKey.Enter)
            .Should().Be(DialogRangeSelectionKeyDecision.Ignore);
        controller.Begin(
            new object(),
            originalText: null,
            DialogRangeSelectionFormat.Range,
            collapseDialog: false,
            ownerWasEnabled: true,
            _ => throw new InvalidOperationException("No previous session expected."));

        controller.DecideKey(DialogRangeSelectionKey.Other)
            .Should().Be(DialogRangeSelectionKeyDecision.Ignore);
        controller.DecideKey(DialogRangeSelectionKey.Enter)
            .Should().Be(DialogRangeSelectionKeyDecision.Apply);
        controller.DecideKey(DialogRangeSelectionKey.Escape)
            .Should().Be(DialogRangeSelectionKeyDecision.Cancel);
    }

    [Fact]
    public void Complete_ProjectsApplyAndOriginalTextSemanticsAndClearsSession()
    {
        var controller = new DialogRangeSelectionController<string>();
        var range = CreateRange();
        var state = controller.Begin(
            "context",
            "before",
            DialogRangeSelectionFormat.DataValidationFormula,
            collapseDialog: true,
            ownerWasEnabled: false,
            _ => throw new InvalidOperationException("No previous session expected."));

        var transition = controller.Complete(range, applySelection: true);

        transition.Should().NotBeNull();
        transition!.State.Should().BeSameAs(state);
        transition.ApplySelection.Should().BeTrue();
        transition.RestoreOriginalText.Should().BeFalse();
        transition.RestoreDialog.Should().BeTrue();
        transition.SelectedRange.Should().Be(range);
        controller.IsActive.Should().BeFalse();
        controller.Complete(range, applySelection: true).Should().BeNull();
    }

    [Fact]
    public void CompleteCancel_RestoresOriginalTextWithoutApplyingSelection()
    {
        var controller = StartController();

        var transition = controller.Complete(CreateRange(), applySelection: false);

        transition.Should().NotBeNull();
        transition!.ApplySelection.Should().BeFalse();
        transition.RestoreOriginalText.Should().BeTrue();
        transition.State.OriginalText.Should().Be("before");
        transition.RestoreDialog.Should().BeTrue();
    }

    [Fact]
    public void CompleteApplyWithoutSelection_DoesNotApplyOrRestoreOriginalText()
    {
        var controller = StartController();

        var transition = controller.Complete(selectedRange: null, applySelection: true);

        transition.Should().NotBeNull();
        transition!.ApplySelection.Should().BeFalse();
        transition.RestoreOriginalText.Should().BeFalse();
        transition.RestoreDialog.Should().BeTrue();
    }

    [Fact]
    public void Cancel_PreservesRequestedRendererRestorationFlags()
    {
        var controller = StartController();

        var transition = controller.Cancel(restoreDialog: false, restoreOriginalText: false);

        transition.Should().NotBeNull();
        transition!.RestoreDialog.Should().BeFalse();
        transition.RestoreOriginalText.Should().BeFalse();
        transition.ApplySelection.Should().BeFalse();
        transition.SelectedRange.Should().BeNull();
        controller.IsActive.Should().BeFalse();
    }

    private static DialogRangeSelectionController<string> StartController()
    {
        var controller = new DialogRangeSelectionController<string>();
        controller.Begin(
            "context",
            "before",
            DialogRangeSelectionFormat.Range,
            collapseDialog: false,
            ownerWasEnabled: true,
            _ => throw new InvalidOperationException("No previous session expected."));
        return controller;
    }

    private static GridRange CreateRange()
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 4, 4));
    }
}
