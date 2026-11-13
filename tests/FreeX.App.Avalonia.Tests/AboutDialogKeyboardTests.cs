using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AboutDialogKeyboardTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Enter_in_the_read_only_About_text_invokes_the_default_OK_button()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new AboutDialog();
            var text = dialog.GetLogicalDescendants().OfType<TextBox>()
                .Single(textBox => AutomationProperties.GetAutomationId(textBox) == "AboutFreeXText");
            var ok = dialog.GetLogicalDescendants().OfType<Button>().Single(button => button.IsDefault);
            var okInvoked = false;
            ok.Click += (_, _) => okInvoked = true;

            // The About text is the initial keyboard target (as in WPF). It is read-only but keeps
            // AcceptsReturn for its multi-line document, and Avalonia's TextBox marks Return handled
            // whenever AcceptsReturn is set -- so Enter never reached the IsDefault OK button and the
            // dialog could not be dismissed with Enter on macOS. WPF's read-only box lets it through.
            var enter = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                PhysicalKey = PhysicalKey.Enter,
                KeyModifiers = KeyModifiers.None,
                Source = text,
            };
            text.RaiseEvent(enter);

            okInvoked.Should().BeTrue();
            enter.Handled.Should().BeTrue();
            text.Text.Should().NotContain("\n\n\n", "Enter must not be typed into the read-only text");
            return true;
        }, CancellationToken.None);
    }
}
