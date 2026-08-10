using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class AvaloniaSynchronousUserMessageDialogTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    [Fact]
    public async Task YesNo_PreservesExistingOwnedWindowLayoutFocusAndDismissalPolicy()
    {
        await Session.Dispatch(() =>
        {
            var request = new UserMessageRequest(
                "The file changed on disk.",
                "File Changed",
                UserMessageButtons.YesNo,
                UserMessageIcon.Warning);
            var dialog = AvaloniaSynchronousUserMessageDialog.CreateForTests(request, UserMessageResult.No);

            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                dialog.Title.Should().Be("File Changed");
                dialog.Width.Should().Be(420);
                dialog.MinHeight.Should().Be(150);
                dialog.SizeToContent.Should().Be(SizeToContent.Height);
                dialog.CanResize.Should().BeFalse();
                dialog.ShowInTaskbar.Should().BeFalse();
                dialog.WindowStartupLocation.Should().Be(WindowStartupLocation.CenterOwner);

                var message = dialog.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == "The file changed on disk.");
                message.Text.Should().Be("The file changed on disk.");
                message.Margin.Should().Be(new Thickness(16, 16, 16, 20));

                var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
                AssertButtonContent(buttons[0], ShellStrings.Current.Yes, "Yes");
                AssertButtonContent(buttons[1], ShellStrings.Current.No, "No");
                buttons.Single(button => button.IsDefault).Should().BeSameAs(buttons[0]);
                buttons[0].IsFocused.Should().BeTrue();
                buttons.Single(button => button.IsCancel).Should().BeSameAs(buttons[1]);
                buttons.Should().OnlyContain(button => button.MinWidth == 82);
                buttons.Should().OnlyContain(button => button.Margin == new Thickness(8, 0, 0, 0));
                dialog.MessageIcon.Should().Be(UserMessageIcon.Warning);
                dialog.GetVisualDescendants()
                    .Should().NotContain(control =>
                        AutomationProperties.GetAutomationId(control) == "MessageSeverityIcon");

                buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.Result.Should().Be(UserMessageResult.No);
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(UserMessageButtons.OkCancel, UserMessageResult.Cancel, "OK", "Cancel")]
    [InlineData(UserMessageButtons.YesNoCancel, UserMessageResult.Cancel, "Yes", "No", "Cancel")]
    public async Task MultiChoicePrompts_PreserveButtonOrderAndEscapeTarget(
        UserMessageButtons buttonSet,
        UserMessageResult dismissedResult,
        params string[] labels)
    {
        await Session.Dispatch(() =>
        {
            var dialog = AvaloniaSynchronousUserMessageDialog.CreateForTests(
                new UserMessageRequest("Message", "Title", buttonSet, UserMessageIcon.Information),
                dismissedResult);

            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();

                buttons.Select(ReadVisibleButtonText).Should().Equal(labels);
                ReadVisibleButtonText(buttons.Single(button => button.IsCancel)).Should().Be(labels[^1]);

                dialog.Close();
                dialog.Result.Should().Be(dismissedResult);
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }
        }, CancellationToken.None);
    }

    private static void AssertButtonContent(Button button, string exactContent, string visibleText)
    {
        button.Content.Should().BeOfType<AccessText>();
        ((AccessText)button.Content!).Text.Should().Be(exactContent);
        ReadVisibleButtonText(button).Should().Be(visibleText);
        AutomationProperties.GetName(button).Should().Be(visibleText);
    }

    private static string ReadVisibleButtonText(Button button) =>
        button.Content is AccessText accessText
            ? ShellStringText.NormalizeAccessText(accessText.Text)
            : button.Content?.ToString() ?? string.Empty;
}
