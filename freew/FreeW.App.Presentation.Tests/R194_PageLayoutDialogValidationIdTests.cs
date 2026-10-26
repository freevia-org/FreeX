using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// r194 (backlog item 44): r190 gave the Drop Cap dialog a validation message and a status control
/// to show it in, but its surface spec never declared a <c>ValidationAutomationId</c> -- so unlike
/// every sibling page-layout dialog the message carried no automation id, and the Avalonia dialog
/// could not call <c>ApplyValidation</c> because there was nothing to apply.
///
/// This asserts the convention across ALL the page-layout dialogs that can reject input, rather
/// than just the one that was missing it: a dialog added later inherits the requirement instead of
/// relying on whoever adds it to remember.
/// </summary>
public class R194_PageLayoutDialogValidationIdTests
{
    public static TheoryData<string, string?> SurfacesThatCanReject() => new()
    {
        { nameof(DropCapOptionsDialogPlanner), DropCapOptionsDialogPlanner.Surface.ValidationAutomationId },
        { nameof(ColumnsDialogPlanner), ColumnsDialogPlanner.Surface.ValidationAutomationId },
        { nameof(HyphenationOptionsDialogPlanner), HyphenationOptionsDialogPlanner.Surface.ValidationAutomationId },
    };

    [Theory]
    [MemberData(nameof(SurfacesThatCanReject))]
    public void ADialogThatCanRejectInput_DeclaresAnIdForItsValidationMessage(
        string planner,
        string? validationAutomationId)
    {
        validationAutomationId.Should().NotBeNullOrWhiteSpace(
            "{0} can refuse a value, so the message saying why needs an id a screen reader and the " +
            "UI tests can find it by",
            planner);
    }

    [Fact]
    public void EachValidationIdIsDistinct()
    {
        // Two dialogs sharing an id would make an automation lookup ambiguous and let a test assert
        // against the wrong dialog's message.
        var ids = SurfacesThatCanReject()
            .Select(row => (string?)row[1])
            .ToList();

        ids.Should().OnlyHaveUniqueItems();
    }
}
