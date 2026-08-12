using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class DialogButtonContentParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    [Fact]
    public async Task Shared_action_buttons_render_WPF_mnemonics_and_keep_dialog_semantics()
    {
        await Session.Dispatch(() =>
        {
            var row = AvaloniaDialogButtonRowFactory.CreateOkCancel(
                () => { },
                () => { },
                buttonWidth: 72);
            var apply = AvaloniaCompactDialogChrome.CreateActionButton(
                "_Apply",
                () => { },
                minWidth: 72,
                isDefault: true);
            row.Children.Add(apply);
            var window = new Window
            {
                Width = 320,
                Height = 100,
                Content = row,
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var buttons = row.Children.OfType<Button>().ToArray();
                buttons.Should().HaveCount(3);
                foreach (var button in buttons)
                {
                    button.Content.Should().BeOfType<AccessText>();
                    button.Background.Should().Be(Brushes.White);
                    button.CornerRadius.Should().Be(new CornerRadius(3));
                }

                var okText = (AccessText)buttons[0].Content!;
                var cancelText = (AccessText)buttons[1].Content!;
                var applyText = (AccessText)buttons[2].Content!;
                okText.Text.Should().Be(Free.Shared.Shell.ShellStrings.Current.Ok);
                cancelText.Text.Should().Be(Free.Shared.Shell.ShellStrings.Current.Cancel);
                applyText.Text.Should().Be("_Apply");
                buttons[0].GetVisualDescendants().OfType<AccessText>().Should().Contain(okText);

                AutomationProperties.GetName(buttons[0]).Should().Be("OK");
                AutomationProperties.GetName(buttons[1]).Should().Be("Cancel");
                AutomationProperties.GetName(buttons[2]).Should().Be("Apply");
                AutomationProperties.GetAccessKey(buttons[0]).Should().Be("Alt+O");
                AutomationProperties.GetAccessKey(buttons[1]).Should().Be("Alt+C");
                AutomationProperties.GetAccessKey(buttons[2]).Should().Be("Alt+A");
                buttons[0].IsDefault.Should().BeTrue();
                buttons[1].IsCancel.Should().BeTrue();
                buttons[2].IsDefault.Should().BeTrue();

                var direct = new Button { Content = "_Direct" };
                AvaloniaCompactDialogChrome.ApplyButton(
                    direct,
                    AvaloniaCompactDialogChrome.WindowsStyle,
                    minWidth: 72);
                var directText = direct.Content.Should().BeOfType<AccessText>().Subject;
                directText.Should().NotBeNull();
                directText!.Text.Should().Be("_Direct");
                direct.CornerRadius.Should().Be(new CornerRadius(3));
                AutomationProperties.GetName(direct).Should().Be("Direct");
                AutomationProperties.GetAccessKey(direct).Should().Be("Alt+D");
            }
            finally
            {
                if (window.IsVisible)
                    window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("_Apply", "Apply", "Alt+A")]
    [InlineData("Save __As", "Save _As", null)]
    [InlineData("Save ___As", "Save _As", "Alt+A")]
    public async Task Action_button_uses_shared_mnemonic_contract(
        string content,
        string expectedDisplayText,
        string? expectedAccessKey)
    {
        await Session.Dispatch(() =>
        {
            var button = new Button { Content = content };
            AvaloniaCompactDialogChrome.ApplyButton(
                button,
                AvaloniaCompactDialogChrome.WindowsStyle,
                minWidth: 72);

            button.Content.Should().BeOfType<AccessText>();
            var label = AvaloniaActionLabelInspector.Inspect(button);
            label.MnemonicText.Should().Be(content);
            label.DisplayText.Should().Be(expectedDisplayText);
            label.AutomationName.Should().Be(expectedDisplayText);

            var accessKey = label.AccessKey;
            if (expectedAccessKey is null)
                accessKey.Should().BeNullOrEmpty();
            else
                accessKey.Should().Be(expectedAccessKey);
        }, CancellationToken.None);
    }
}
