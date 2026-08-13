using System.Threading;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Free.Shared.Shell.Avalonia;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class LegalNoticesKeyboardLifecycleTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task DedicatedKeyboardScope_CyclesAndClosesWithEnterAndEscape()
    {
        await Session.Dispatch(() =>
        {
            var owner = new MainWindow([]);
            Window? enterDialog = null;
            Window? escapeDialog = null;
            try
            {
                owner.Show();
                (enterDialog, var textBox, var closeButton) = CreateDialog(owner);
                var closeClickCount = 0;
                closeButton.Click += (_, _) => closeClickCount++;

                textBox.Focus().Should().BeTrue();
                Send(enterDialog, Key.Tab);
                enterDialog.FocusManager?.GetFocusedElement().Should().BeSameAs(closeButton);
                Send(enterDialog, Key.Tab, RawInputModifiers.Shift);
                enterDialog.FocusManager?.GetFocusedElement().Should().BeSameAs(textBox);
                Send(enterDialog, Key.Enter);
                closeClickCount.Should().Be(1, "Enter must invoke the actual default Close button");
                enterDialog.IsVisible.Should().BeFalse("Enter must invoke the WPF default Close behavior");

                (escapeDialog, _, _) = CreateDialog(owner);
                Send(escapeDialog, Key.Escape);
                escapeDialog.IsVisible.Should().BeFalse("Escape must cancel Legal Notices");
            }
            finally
            {
                if (enterDialog?.IsVisible == true)
                    enterDialog.Close();
                if (escapeDialog?.IsVisible == true)
                    escapeDialog.Close();

                owner.AllowCloseWithoutDirtyPromptForParityCapture();

                if (owner.IsVisible)
                    owner.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProductionDialogUsesSharedLocalizedSurfaceAndKeyboardLifecycle()
    {
        await Session.Dispatch(() =>
        {
            var owner = new MainWindow([]);
            LegalNoticesDialog? dialog = null;
            try
            {
                owner.Show();
                dialog = new LegalNoticesDialog();
                dialog.Should().BeAssignableTo<AvaloniaLegalNoticesDialog>();
                AutomationProperties.GetAutomationId(dialog).Should().Be("LegalNoticesDialog");

                var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
                tabs.Items.Count.Should().Be(5);
                var closeButton = dialog.GetLogicalDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == "LegalNoticesCloseButton");
                closeButton.Content.Should().NotBeNull();
            }
            finally
            {
                if (dialog?.IsVisible == true)
                    dialog.Close();

                owner.AllowCloseWithoutDirtyPromptForParityCapture();

                if (owner.IsVisible)
                    owner.Close();
            }
        }, CancellationToken.None);
    }

    private static (Window Dialog, TextBox TextBox, Button CloseButton) CreateDialog(MainWindow owner)
    {
        var textBox = new TextBox { Text = "License", IsReadOnly = true };
        var tabControl = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem
                {
                    Header = "Project License",
                    Content = new ScrollViewer { Content = textBox },
                },
            },
            SelectedIndex = 0,
        };
        var closeButton = new Button { Content = "Close", IsDefault = true, IsCancel = true };
        var dialog = new Window
        {
            Width = 420,
            Height = 280,
            Content = new DockPanel
            {
                Children = { closeButton, tabControl },
            },
        };

        MainWindow.ConfigureLegalNoticesDialogKeyboardForTest(dialog, tabControl, closeButton);

        dialog.Show(owner);
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
        return (dialog, textBox, closeButton);
    }

    private static void Send(
        Window dialog,
        Key key,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        MainWindow.SendDialogKeyForTest(dialog, key, modifiers, out var error).Should().BeTrue(error);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
    }
}
