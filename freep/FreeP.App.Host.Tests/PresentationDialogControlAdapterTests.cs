using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

public sealed class PresentationDialogControlAdapterTests
{
    [StaFact]
    public void Wpf_adapter_round_trips_values_and_applies_portable_semantics()
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
    }

    private enum TestField
    {
        Value,
    }
}
