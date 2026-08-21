using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Automation;
using Avalonia.VisualTree;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Tests;

public sealed class ManualHyphenationDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Cancel_button_is_the_cancel_action_and_Escape_returns_cancel_result()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new Window();
            owner.Show();
            try
            {
                var dialog = new ManualHyphenationDialog(Candidate());

                // Show first: a Window builds no visual tree until it is shown, so querying visual
                // descendants on the freshly constructed dialog finds no buttons at all.
                var resultTask = dialog.ShowDialog<ManualHyphenationDialogResult?>(owner);

                var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
                var cancel = buttons.Single(button => button.Content?.ToString() == "Cancel");

                cancel.IsCancel.Should().BeTrue();
                cancel.IsDefault.Should().BeFalse();
                var escape = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Escape,
                    Source = dialog,
                };
                dialog.RaiseEvent(escape);

                escape.Handled.Should().BeTrue();
                (await resultTask).Should().Be(new ManualHyphenationDialogResult(ManualHyphenationDialogAction.Cancel));
            }
            finally
            {
                owner.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Realized_action_semantics_match_the_Wpf_authority()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new Window();
            owner.Show();
            try
            {
                var dialog = new ManualHyphenationDialog(Candidate());
                var resultTask = dialog.ShowDialog<ManualHyphenationDialogResult?>(owner);

                var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
                buttons.Select(button => AutomationProperties.GetName(button))
                    .Should().Equal("Accept hyphenation", "Skip hyphenation", "Cancel");
                buttons.Single(button => AutomationProperties.GetName(button) == "Accept hyphenation")
                    .IsDefault.Should().BeTrue();
                buttons.Single(button => AutomationProperties.GetName(button) == "Cancel")
                    .IsCancel.Should().BeTrue();
                dialog.GetVisualDescendants().OfType<ComboBox>().Single().IsFocused.Should().BeTrue();

                dialog.Close(null);
                (await resultTask).Should().BeNull();
            }
            finally
            {
                owner.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static ManualHyphenationCandidate Candidate() =>
        new(1, "characterization", [new ManualHyphenationOption(5, "char-acterization")]);
}
