using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia.Tests;

public sealed class PresentationDialogControlAdapterTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    [Fact]
    public async Task Avalonia_adapter_round_trips_values_and_applies_portable_semantics()
    {
        await Session.Dispatch(() =>
        {
            var text = new TextBox { Text = "Before" };
            var choice = new ComboBox { ItemsSource = new[] { "A", "B", "C" }, SelectedIndex = 1 };
            var toggle = new CheckBox { IsThreeState = true, IsChecked = null };

            PresentationDialogControlAdapter.CaptureValue(text).Text.Should().Be("Before");
            PresentationDialogControlAdapter.CaptureValue(choice).SelectedIndex.Should().Be(1);
            PresentationDialogControlAdapter.CaptureValue(toggle).IsChecked.Should().BeNull();

            PresentationDialogControlAdapter.ApplyValue(
                text,
                new PresentationDialogFieldValue(Text: "After"));
            PresentationDialogControlAdapter.ApplyValue(
                choice,
                new PresentationDialogFieldValue(SelectedIndex: 2));
            PresentationDialogControlAdapter.ApplyValue(
                toggle,
                new PresentationDialogFieldValue(IsChecked: true));

            text.Text.Should().Be("After");
            choice.SelectedIndex.Should().Be(2);
            toggle.IsChecked.Should().BeTrue();

            var field = new PresentationDialogFieldPlan<TestField>(
                TestField.Value,
                PresentationDialogControlKind.Text,
                "Value",
                "Dialog value",
                "FreeP.Dialog.Value",
                "Enter a value.");
            PresentationDialogControlAdapter.ApplySemantic(text, field, ".Primary");

            AutomationProperties.GetName(text).Should().Be("Dialog value");
            AutomationProperties.GetAutomationId(text).Should().Be("FreeP.Dialog.Value.Primary");
            AutomationProperties.GetHelpText(text).Should().Be("Enter a value.");
        }, CancellationToken.None);
    }

    private enum TestField
    {
        Value,
    }
}
