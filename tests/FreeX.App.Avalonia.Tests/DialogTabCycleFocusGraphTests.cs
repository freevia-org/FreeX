using System.Reflection;
using System.Threading;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class DialogTabCycleFocusGraphTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task DataValidation_AllTabsCycleForwardAndReverse_AndEscapeCloses() =>
        await AssertTabbedDialogAsync("ShowDataValidationInputDialogAsync", "DataValidationTabStrip", 3, "DataValidationTypeBox");

    [Fact]
    public async Task FindReplace_AllTabsCycleForwardAndReverse_AndEscapeCloses() =>
        await AssertTabbedDialogAsync("ShowFindReplaceTabbedDialogAsync", "FindReplaceTabs", 2, "FindReplaceFindBox");

    [Fact]
    public async Task FormatCells_AllTabsCycleForwardAndReverse_AndEscapeCloses() =>
        await AssertTabbedDialogAsync("ShowFormatCellsInputDialogAsync", "FormatCellsTabStrip", 6, "FormatCellsNumberCategoryList");

    [Fact]
    public async Task PageSetup_AllTabsCycleForwardAndReverse_AndEscapeCloses() =>
        await AssertPageSetupGraphAsync(
            "ShowPageSetupDialogAsync",
            initialTabIndex: 0,
            initialAutomationId: "PageSetupOrientationBox");

    [Fact]
    public async Task HeaderFooterRoute_UsesPageSetupScope_AndEscapeCloses() =>
        await AssertPageSetupGraphAsync(
            "ShowHeaderFooterDialogAsync",
            initialTabIndex: 2,
            initialAutomationId: "PageSetupHeaderPresetBox");

    [Fact]
    public async Task LegalNotices_CyclesInBothDirections_EscapeAndEnterClose()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            owner.Show();
            var opener = InvokeOpener(owner, "ShowLegalNoticesDialogAsync");
            var dialog = await WaitForOwnedDialogAsync(owner);
            try
            {
                var initial = FindByAutomationId<TextBox>(dialog, "LegalNoticesProjectLicenseText")!;
                AssertFullCycle(dialog, initial);
                Send(dialog, Key.Escape);
                dialog.IsVisible.Should().BeFalse("Escape must close Legal Notices");

                opener = InvokeOpener(owner, "ShowLegalNoticesDialogAsync");
                dialog = await WaitForOwnedDialogAsync(owner);
                initial = FindByAutomationId<TextBox>(dialog, "LegalNoticesProjectLicenseText")!;
                initial.Focus().Should().BeTrue();
                Send(dialog, Key.Enter);
                dialog.IsVisible.Should().BeFalse("Enter must invoke the WPF default Close button");
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
                await AwaitClosedAsync(opener);
                owner.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SymbolPicker_UsesListFocus_CyclesBothTabs_AndEscapeCloses()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            owner.Show();
            var opener = InvokeOpener(owner, "ShowSymbolPickerAsync");
            var dialog = await WaitForOwnedDialogAsync(owner);
            try
            {
                var tabs = FindByAutomationId<TabControl>(dialog, "SymbolPickerTabs")!;
                tabs.ItemCount.Should().Be(2);
                for (var index = 0; index < tabs.ItemCount; index++)
                {
                    tabs.SelectedIndex = index;
                    dialog.UpdateLayout();
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                    Control initial = index == 0
                        ? FindByAutomationId<WrapPanel>(dialog, "SymbolPickerSymbolsList")!
                        : tabs.GetVisualDescendants().OfType<Border>().Single(border => border.Focusable && border.IsVisible);
                    AssertFullCycle(dialog, initial);
                }

                Send(dialog, Key.Escape);
                dialog.IsVisible.Should().BeFalse("Escape must close Symbol Picker");
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
                await AwaitClosedAsync(opener);
                owner.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    private static async Task AssertTabbedDialogAsync(string openerName, string tabAutomationId, int tabCount, string initialAutomationId)
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            owner.Show();
            var opener = InvokeOpener(owner, openerName);
            var dialog = await WaitForOwnedDialogAsync(owner);
            try
            {
                var tabs = FindByAutomationId<TabControl>(dialog, tabAutomationId)!;
                tabs.ItemCount.Should().Be(tabCount);
                for (var index = 0; index < tabCount; index++)
                {
                    tabs.SelectedIndex = index;
                    dialog.UpdateLayout();
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                    var initial = FindByAutomationId<Control>(dialog, initialAutomationId, required: index == 0)
                        ?? FindFirstFocusableVisibleDescendant((tabs.SelectedItem as TabItem)?.Content as Control)
                        ?? throw new InvalidOperationException($"No focus target for {openerName} tab {index}.");
                    AssertFullCycle(dialog, initial);
                }

                Send(dialog, Key.Escape);
                dialog.IsVisible.Should().BeFalse($"Escape must close {openerName}");
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
                await AwaitClosedAsync(opener);
                owner.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    private static async Task AssertPageSetupGraphAsync(
        string openerName,
        int initialTabIndex,
        string initialAutomationId)
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            owner.Show();
            var opener = InvokeOpener(owner, openerName);
            var dialog = await WaitForOwnedDialogAsync(owner);
            try
            {
                var tabs = FindByAutomationId<TabControl>(dialog, "PageSetupTabs")!;
                tabs.ItemCount.Should().Be(4);
                for (var index = 0; index < tabs.ItemCount; index++)
                {
                    tabs.SelectedIndex = index;
                    dialog.UpdateLayout();
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                    var initial = index == initialTabIndex
                        ? FindByAutomationId<Control>(dialog, initialAutomationId)
                        : FindFirstFocusableVisibleDescendant((tabs.SelectedItem as TabItem)?.Content as Control);
                    initial.Should().NotBeNull($"No focus target for {openerName} tab {index}.");
                    AssertFullCycle(dialog, initial!);
                }

                Send(dialog, Key.Escape);
                dialog.IsVisible.Should().BeFalse($"Escape must close {openerName}");
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
                await AwaitClosedAsync(opener);
                owner.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    private static void AssertFullCycle(Window dialog, Control initial)
    {
        initial.Focus().Should().BeTrue($"Could not focus {Describe(initial)}");
        var forward = WalkUntilReturn(dialog, initial, reverse: false);
        forward.Should().BeGreaterThan(1, $"Forward Tab must visit the complete scope from {Describe(initial)}");
        initial.Focus().Should().BeTrue();
        var reverse = WalkUntilReturn(dialog, initial, reverse: true);
        reverse.Should().Be(forward, "forward and reverse cycles must have the same tab-stop count");
    }

    private static int WalkUntilReturn(Window dialog, Control initial, bool reverse)
    {
        for (var step = 1; step <= 128; step++)
        {
            Send(dialog, Key.Tab, reverse ? RawInputModifiers.Shift : RawInputModifiers.None);
            var focused = dialog.FocusManager?.GetFocusedElement();
            focused.Should().NotBeNull($"Tab lost focus at step {step}");
            if (ReferenceEquals(focused, initial))
                return step;
        }

        throw new Xunit.Sdk.XunitException($"Tab cycle did not return to {Describe(initial)} within 128 steps.");
    }

    private static void Send(Window dialog, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        MainWindow.SendDialogKeyForTest(dialog, key, modifiers, out var error).Should().BeTrue(error);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
    }

    private static Control? FindFirstFocusableVisibleDescendant(Control? root) =>
        root is null
            ? null
            : root.GetVisualDescendants().OfType<Control>().Prepend(root).FirstOrDefault(control =>
                control.Focusable && KeyboardNavigation.GetIsTabStop(control) && control.IsVisible && control.IsEffectivelyEnabled);

    private static T? FindByAutomationId<T>(Window dialog, string automationId, bool required = true)
        where T : Control
    {
        var match = dialog.GetVisualDescendants().OfType<T>().FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
        if (required && match is null)
            throw new InvalidOperationException($"Missing {typeof(T).Name}#{automationId}.");
        return match;
    }

    private static Task InvokeOpener(MainWindow owner, string methodName)
    {
        var parameterCount = methodName switch
        {
            "ShowFindReplaceTabbedDialogAsync" => 1,
            "ShowFormatCellsInputDialogAsync" => 2,
            "ShowPageSetupDialogAsync" => 2,
            _ => 0,
        };
        var method = typeof(MainWindow).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == parameterCount);
        var args = methodName switch
        {
            "ShowFindReplaceTabbedDialogAsync" => new object?[] { false },
            "ShowFormatCellsInputDialogAsync" => new object?[] { null, 0 },
            "ShowPageSetupDialogAsync" => new object?[] { Enum.GetValues(method.GetParameters()[0].ParameterType).GetValue(0), false },
            _ => [],
        };
        return method.Invoke(owner, args) as Task ?? throw new InvalidOperationException($"{methodName} did not return Task.");
    }

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
        await Task.WhenAny(opener, Task.Delay(TimeSpan.FromSeconds(2)));
        if (opener.IsFaulted)
            await opener;
    }

    private static string Describe(Control control)
    {
        var automationId = AutomationProperties.GetAutomationId(control);
        return string.IsNullOrWhiteSpace(automationId) ? control.GetType().Name : $"{control.GetType().Name}#{automationId}";
    }
}
