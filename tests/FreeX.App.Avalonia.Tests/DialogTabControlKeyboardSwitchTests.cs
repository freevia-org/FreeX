using System.Threading;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for shared-keyboard-focus F1: Avalonia's TabControl has no built-in Ctrl+Tab /
/// Ctrl+Shift+Tab handling (unlike WPF's), so every dialog wired through ConfigureDialogTabCycle
/// (MainWindow.DialogTabCycleScopes.cs) left every tab but the initially-focused one unreachable by
/// keyboard. These tests drive the real Ctrl+Tab / Ctrl+Shift+Tab gesture -- not a programmatic
/// SelectedIndex assignment -- and assert focus actually lands inside the newly-selected tab's content.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class DialogTabControlKeyboardSwitchTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FormatCells_CtrlTab_ReachesEveryTabForwardAndBackward()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            owner.Show();
            var opener = owner.ShowFormatCellsInputDialogForTestAsync();
            var dialog = await WaitForOwnedDialogAsync(owner);
            try
            {
                var tabs = FindByAutomationId<TabControl>(dialog, "FormatCellsTabStrip")!;
                tabs.ItemCount.Should().Be(6);
                tabs.SelectedIndex.Should().Be(0, "Format Cells always opens on its first tab");

                // Ctrl+Tab must visit every one of the six tabs, in order, purely via the keyboard --
                // with no programmatic SelectedIndex assignment anywhere in this loop -- and land focus
                // inside each tab's own content rather than leaving it stranded on the tab strip.
                for (var step = 1; step <= 6; step++)
                {
                    Send(dialog, Key.Tab, RawInputModifiers.Control);
                    tabs.SelectedIndex.Should().Be(step % 6, $"Ctrl+Tab step {step} must advance the selected tab");
                    AssertFocusLandedInsideSelectedTabContent(dialog, tabs);
                }

                tabs.SelectedIndex.Should().Be(0, "six forward Ctrl+Tab presses from tab 0 must wrap back to tab 0");

                // Ctrl+Shift+Tab must walk the same six tabs in reverse.
                for (var step = 1; step <= 6; step++)
                {
                    Send(dialog, Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
                    var expected = ((0 - step) % 6 + 6) % 6;
                    tabs.SelectedIndex.Should().Be(expected, $"Ctrl+Shift+Tab step {step} must retreat the selected tab");
                    AssertFocusLandedInsideSelectedTabContent(dialog, tabs);
                }

                tabs.SelectedIndex.Should().Be(0);

                // The dialog must still close normally afterwards -- Ctrl+Tab handling must not swallow
                // or otherwise interfere with the pre-existing Escape contract.
                Send(dialog, Key.Escape, RawInputModifiers.None);
                dialog.IsVisible.Should().BeFalse("Escape must still close Format Cells after Ctrl+Tab was used");
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
                await AwaitClosedAsync(opener);

                owner.AllowCloseWithoutDirtyPromptForParityCapture();
                owner.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DataValidation_CtrlTab_ReachesEveryTab()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            owner.Show();
            var opener = owner.ShowDataValidationInputDialogForTestAsync();
            var dialog = await WaitForOwnedDialogAsync(owner);
            try
            {
                var tabs = FindByAutomationId<TabControl>(dialog, "DataValidationTabStrip")!;
                tabs.ItemCount.Should().Be(3);
                tabs.SelectedIndex.Should().Be(0);

                for (var step = 1; step <= 3; step++)
                {
                    Send(dialog, Key.Tab, RawInputModifiers.Control);
                    tabs.SelectedIndex.Should().Be(step % 3, $"Ctrl+Tab step {step} must advance the selected tab");
                    AssertFocusLandedInsideSelectedTabContent(dialog, tabs);
                }

                tabs.SelectedIndex.Should().Be(0, "three forward Ctrl+Tab presses from tab 0 must wrap back to tab 0");
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
                await AwaitClosedAsync(opener);

                owner.AllowCloseWithoutDirtyPromptForParityCapture();
                owner.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sibling no-regression check (rule 10): the pre-existing plain Tab / Shift+Tab cycle -- the one
    /// GetDialogTabStops has always built by deliberately excluding TabControl/TabItem -- must keep
    /// behaving exactly as before: it stays confined to whichever tab is currently selected and never
    /// changes the TabControl's SelectedIndex itself. Only the new Ctrl-modified gesture may do that.
    /// </summary>
    [Fact]
    public async Task FormatCells_PlainTabCycle_StaysWithinSelectedTab_AndDoesNotChangeSelection()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            owner.Show();
            var opener = owner.ShowFormatCellsInputDialogForTestAsync();
            var dialog = await WaitForOwnedDialogAsync(owner);
            try
            {
                var tabs = FindByAutomationId<TabControl>(dialog, "FormatCellsTabStrip")!;
                tabs.SelectedIndex = 2;
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                var content = (tabs.SelectedItem as TabItem)?.Content as Control;
                content.Should().NotBeNull();
                var initial = content!.GetVisualDescendants().OfType<Control>().Prepend(content).FirstOrDefault(
                    control => control.Focusable && KeyboardNavigation.GetIsTabStop(control) && control.IsVisible && control.IsEffectivelyEnabled);
                initial.Should().NotBeNull();
                initial!.Focus().Should().BeTrue();

                var returnedToInitial = false;
                for (var step = 1; step <= 40; step++)
                {
                    Send(dialog, Key.Tab, RawInputModifiers.None);
                    tabs.SelectedIndex.Should().Be(2, "plain Tab must never change which tab is selected");

                    var focused = dialog.FocusManager?.GetFocusedElement();
                    focused.Should().NotBeNull($"plain Tab must not lose focus at step {step}");
                    if (!ReferenceEquals(focused, initial))
                        continue;

                    returnedToInitial = true;
                    break;
                }

                returnedToInitial.Should().BeTrue(
                    "the plain Tab cycle within the Font tab must return to its start within 40 steps");
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
                await AwaitClosedAsync(opener);

                owner.AllowCloseWithoutDirtyPromptForParityCapture();
                owner.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static void AssertFocusLandedInsideSelectedTabContent(Window dialog, TabControl tabs)
    {
        var focused = dialog.FocusManager?.GetFocusedElement() as Control;
        focused.Should().NotBeNull("Ctrl+Tab must not lose keyboard focus");

        var content = (tabs.SelectedItem as TabItem)?.Content as Control;
        content.Should().NotBeNull();

        var withinContent = ReferenceEquals(focused, content) ||
            focused!.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, content));
        withinContent.Should().BeTrue(
            $"Ctrl+Tab must move focus into the newly-selected tab's own content, not leave it on {Describe(focused)}");
    }

    private static void Send(Window dialog, Key key, RawInputModifiers modifiers)
    {
        MainWindow.SendDialogKeyForTest(dialog, key, modifiers, out var error).Should().BeTrue(error);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

        // Switching the selected TabItem swaps its content into the TabControl's presenter, which is
        // realized only after a layout pass -- exactly like AssertTabbedDialogAsync's own
        // `tabs.SelectedIndex = index; dialog.UpdateLayout();` pairing elsewhere in this test project.
        // The production Dispatcher.Post(..., DispatcherPriority.Input) retry covers the equivalent gap
        // in a real, continuously-rendering window; the headless harness needs this pump made explicit.
        if (dialog.IsVisible)
        {
            dialog.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
        }
    }

    private static T? FindByAutomationId<T>(Window dialog, string automationId)
        where T : Control =>
        dialog.GetVisualDescendants().OfType<T>()
            .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static async Task<Window> WaitForOwnedDialogAsync(MainWindow owner)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var dialog = owner.OwnedWindows.FirstOrDefault(window => window.IsVisible);
            if (dialog is not null)
                return dialog;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("Dialog opener did not show an owned window within 5 seconds.");
    }

    private static async Task AwaitClosedAsync(Task opener)
    {
        var completed = await Task.WhenAny(opener, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().BeSameAs(opener, "the dialog opener must complete after the window closes");
        await opener;
    }

    private static string Describe(Control? control)
    {
        if (control is null)
            return "none";
        var automationId = AutomationProperties.GetAutomationId(control);
        return string.IsNullOrWhiteSpace(automationId) ? control.GetType().Name : $"{control.GetType().Name}#{automationId}";
    }
}
