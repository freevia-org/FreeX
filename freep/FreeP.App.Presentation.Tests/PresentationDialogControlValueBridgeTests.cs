namespace FreeP.App.Compositor.Tests;

public sealed class PresentationDialogControlValueBridgeTests
{
    [Fact]
    public void CaptureAndApplyMapAllSupportedControlKindsThroughOneSharedPolicy()
    {
        var bridge = CreateBridge();
        var text = new FakeTextControl { Text = "Before" };
        var choice = new FakeChoiceControl { SelectedIndex = 1 };
        var toggle = new FakeToggleControl { IsChecked = null };

        bridge.Capture(text).Should().Be(new PresentationDialogFieldValue(Text: "Before"));
        bridge.Capture(choice).Should().Be(new PresentationDialogFieldValue(SelectedIndex: 1));
        bridge.Capture(toggle).Should().Be(new PresentationDialogFieldValue(IsChecked: null));

        bridge.Apply(text, new PresentationDialogFieldValue(Text: "After"));
        bridge.Apply(choice, new PresentationDialogFieldValue(SelectedIndex: 2));
        bridge.Apply(toggle, new PresentationDialogFieldValue(IsChecked: true));

        text.Text.Should().Be("After");
        choice.SelectedIndex.Should().Be(2);
        toggle.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void TextMappingNormalizesNullAtTheSharedBoundary()
    {
        var bridge = CreateBridge();
        var text = new FakeTextControl { Text = null };

        bridge.Capture(text).Text.Should().BeEmpty();
        bridge.Apply(text, new PresentationDialogFieldValue(Text: null!));

        text.Text.Should().BeEmpty();
    }

    [Fact]
    public void UnsupportedControlsFailClosedWithTheNativeTypeName()
    {
        var bridge = CreateBridge();

        var capture = () => bridge.Capture(new FakeUnsupportedControl());
        var apply = () => bridge.Apply(
            new FakeUnsupportedControl(),
            new PresentationDialogFieldValue());

        capture.Should().Throw<InvalidOperationException>()
            .WithMessage("Unsupported presentation dialog control: FakeUnsupportedControl.");
        apply.Should().Throw<InvalidOperationException>()
            .WithMessage("Unsupported presentation dialog control: FakeUnsupportedControl.");
    }

    private static PresentationDialogControlValueBridge<
        FakeControl,
        FakeTextControl,
        FakeChoiceControl,
        FakeToggleControl> CreateBridge() => new(
            control => control.Text,
            (control, value) => control.Text = value,
            control => control.SelectedIndex,
            (control, value) => control.SelectedIndex = value,
            control => control.IsChecked,
            (control, value) => control.IsChecked = value);

    private abstract class FakeControl
    {
    }

    private sealed class FakeTextControl : FakeControl
    {
        public string? Text { get; set; }
    }

    private sealed class FakeChoiceControl : FakeControl
    {
        public int SelectedIndex { get; set; }
    }

    private sealed class FakeToggleControl : FakeControl
    {
        public bool? IsChecked { get; set; }
    }

    private sealed class FakeUnsupportedControl : FakeControl
    {
    }
}
