namespace FreeP.App.Compositor.Tests;

public sealed class PresentationDialogNativeBindingTests
{
    private readonly PresentationDialogNativeBinding<FakeControl, FakeText, FakeChoice, FakeToggle>
        _binding = new(
            static control => control.Text,
            static (control, value) => control.Text = value,
            static control => control.SelectedIndex,
            static (control, value) => control.SelectedIndex = value,
            static control => control.IsChecked,
            static (control, value) => control.IsChecked = value);

    [Fact]
    public void Binding_round_trips_all_supported_native_control_categories()
    {
        var text = new FakeText { Text = "Heading" };
        var choice = new FakeChoice { SelectedIndex = 3 };
        var toggle = new FakeToggle { IsChecked = null };

        _binding.CaptureValue(text).Text.Should().Be("Heading");
        _binding.CaptureValue(choice).SelectedIndex.Should().Be(3);
        _binding.CaptureValue(toggle).IsChecked.Should().BeNull();

        _binding.ApplyValue(text, new PresentationDialogFieldValue(Text: "Updated"));
        _binding.ApplyValue(choice, new PresentationDialogFieldValue(SelectedIndex: 1));
        _binding.ApplyValue(toggle, new PresentationDialogFieldValue(IsChecked: true));

        text.Text.Should().Be("Updated");
        choice.SelectedIndex.Should().Be(1);
        toggle.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void Binding_rejects_unsupported_controls_consistently()
    {
        var control = new FakeUnsupported();

        var capture = () => _binding.CaptureValue(control);
        var apply = () => _binding.ApplyValue(control, new PresentationDialogFieldValue());

        capture.Should().Throw<InvalidOperationException>()
            .WithMessage("Unsupported presentation dialog control: FakeUnsupported.");
        apply.Should().Throw<InvalidOperationException>()
            .WithMessage("Unsupported presentation dialog control: FakeUnsupported.");
    }

    [Fact]
    public void Zoom_binding_applies_state_and_text_focus_policy()
    {
        var binding = new ZoomObjectPropertiesDialogNativeBinding<
            FakeControl,
            FakeToggle,
            FakeText,
            FakeChoice>(
            static (control, value) => control.IsEnabled = value,
            static control => control.IsChecked,
            static (control, value) => control.IsChecked = value,
            static control => control.Text,
            static (control, value) => control.Text = value,
            static control => control.SelectedItem,
            static (control, value) => control.SelectedItem = value,
            static control => control.IsFocused = true,
            static control => control.IsSelectedAll = true);
        var text = new FakeText { Text = "old" };

        binding.ApplyFieldState(
            text,
            new ZoomObjectPropertiesDialogFieldState(
                ZoomObjectPropertiesDialogField.TransitionDuration,
                "1.5",
                IsEnabled: false));
        binding.Focus(text, selectAll: true);

        text.Text.Should().Be("1.5");
        text.IsEnabled.Should().BeFalse();
        text.IsFocused.Should().BeTrue();
        text.IsSelectedAll.Should().BeTrue();
    }

    private abstract class FakeControl
    {
        public bool IsEnabled { get; set; } = true;
        public bool IsFocused { get; set; }
    }
    private sealed class FakeText : FakeControl
    {
        public string? Text { get; set; }
        public bool IsSelectedAll { get; set; }
    }
    private sealed class FakeChoice : FakeControl
    {
        public int SelectedIndex { get; set; }
        public object? SelectedItem { get; set; }
    }
    private sealed class FakeToggle : FakeControl { public bool? IsChecked { get; set; } }
    private sealed class FakeUnsupported : FakeControl;
}
