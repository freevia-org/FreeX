namespace Free.Shared.AppServices.Tests;

public sealed class PickerOutcomeTests
{
    [Fact]
    public void Selected_preserves_typed_selection_and_shared_completion_state()
    {
        var selection = new Selection("sample.bin");

        var outcome = PickerOutcome<Selection>.Selected(selection);

        outcome.Status.Should().Be(OperationStatus.Completed);
        outcome.Selection.Should().BeSameAs(selection);
        outcome.Operation.Value.Should().BeSameAs(selection);
        outcome.IsSelected.Should().BeTrue();
        outcome.Message.Should().BeNull();
    }

    [Fact]
    public void Terminal_picker_states_use_the_shared_operation_state_machine()
    {
        var cancelled = PickerOutcome<Selection>.Cancelled;
        var unavailable = PickerOutcome<Selection>.Unavailable("Picker unavailable.");
        var invalid = PickerOutcome<Selection>.Invalid("A local selection is required.");

        cancelled.Status.Should().Be(OperationStatus.Cancelled);
        unavailable.Status.Should().Be(OperationStatus.Unavailable);
        unavailable.Message.Should().Be("Picker unavailable.");
        invalid.Status.Should().Be(OperationStatus.ValidationFailed);
        invalid.Operation.Validation!.Detail.Should().Be("A local selection is required.");
        invalid.Message.Should().Be("A local selection is required.");
    }

    [Fact]
    public void Message_states_reject_empty_diagnostics()
    {
        var unavailable = () => PickerOutcome<Selection>.Unavailable(" ");
        var invalid = () => PickerOutcome<Selection>.Invalid(string.Empty);

        unavailable.Should().Throw<ArgumentException>();
        invalid.Should().Throw<ArgumentException>();
    }

    private sealed record Selection(string Name);
}
