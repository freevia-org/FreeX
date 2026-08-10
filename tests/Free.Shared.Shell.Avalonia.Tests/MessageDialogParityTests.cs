using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class MessageDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    [Fact]
    public async Task Warning_and_error_messages_keep_severity_and_WPF_button_contract()
    {
        await Session.Dispatch(() =>
        {
            var error = AvaloniaUserMessageDialog.CreateForTests("Failure details");
            var warning = AvaloniaUserMessageDialog.CreateForTests(
                "Validation details",
                title: "Warning",
                icon: UserMessageIcon.Warning);

            try
            {
                error.Show();
                warning.Show();
                error.UpdateLayout();
                warning.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                AssertDialog(error, UserMessageIcon.Error, "X", "Error");
                AssertDialog(warning, UserMessageIcon.Warning, "!", "Warning");
            }
            finally
            {
                if (error.IsVisible)
                    error.Close();
                if (warning.IsVisible)
                    warning.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Confirmation_messages_realize_the_typed_button_contract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = AvaloniaUserMessageDialog.CreateForTests(
                "Keep changes?",
                title: "Confirm",
                icon: UserMessageIcon.Question,
                buttons: UserMessageButtons.YesNoCancel);

            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                dialog.MessageButtons.Should().Be(UserMessageButtons.YesNoCancel);
                var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
                buttons.Should().HaveCount(3);
                buttons.Select(ReadButtonText).Should().Equal(
                    ShellStrings.Current.Yes,
                    ShellStrings.Current.No,
                    ShellStrings.Current.Cancel);
                buttons.Single(button => button.IsDefault).Should().BeSameAs(buttons[0]);
                buttons.Single(button => button.IsCancel).Should().BeSameAs(buttons[2]);
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }
        }, CancellationToken.None);
    }

    private static void AssertDialog(
        AvaloniaUserMessageDialog dialog,
        UserMessageIcon icon,
        string expectedGlyph,
        string expectedTitle)
    {
        dialog.MessageIcon.Should().Be(icon);
        dialog.Title.Should().Be(expectedTitle);

        var severityGlyph = dialog.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(text => AutomationProperties.GetAutomationId(text) == "MessageSeverityIcon");
        severityGlyph.Text.Should().Be(expectedGlyph);
        AutomationProperties.GetName(severityGlyph).Should().Be(expectedTitle);

        var button = dialog.GetVisualDescendants().OfType<Button>().Single();
        button.Content.Should().BeOfType<AccessText>();
        ((AccessText)button.Content!).Text.Should().Be(Free.Shared.Shell.ShellStrings.Current.Ok);
        AutomationProperties.GetName(button).Should().Be("OK");
        AutomationProperties.GetAccessKey(button).Should().Be("Alt+O");
        button.IsDefault.Should().BeTrue();
        button.IsCancel.Should().BeTrue();
    }

    private static string ReadButtonText(Button button) =>
        button.Content is AccessText accessText
            ? accessText.Text ?? string.Empty
            : button.Content?.ToString() ?? string.Empty;
}
