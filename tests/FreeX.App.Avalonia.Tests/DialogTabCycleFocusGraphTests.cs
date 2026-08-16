using System.Threading;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class DialogTabCycleFocusGraphTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task DataValidation_AllTabsCycleForwardAndReverse_AndEscapeCloses() =>
        await AssertTabbedDialogAsync(DialogRoute.DataValidation, "DataValidationTabStrip", 3, "DataValidationTypeBox");

    [Fact]
    public async Task FindReplace_AllTabsCycleForwardAndReverse_AndEscapeCloses() =>
        await AssertTabbedDialogAsync(DialogRoute.FindReplace, "FindReplaceTabs", 2, "FindReplaceFindBox");

    [Fact]
    public async Task FormatCells_AllTabsCycleForwardAndReverse_AndEscapeCloses() =>
        await AssertTabbedDialogAsync(DialogRoute.FormatCells, "FormatCellsTabStrip", 6, "FormatCellsNumberCategoryList");

    [Fact]
    public async Task PageSetup_AllTabsCycleForwardAndReverse_AndEscapeCloses() =>
        await AssertPageSetupGraphAsync(
            DialogRoute.PageSetup,
            initialTabIndex: 0,
            initialAutomationId: "PageSetupOrientationBox");

    [Fact]
    public async Task HeaderFooterRoute_UsesDedicatedEditorScope_AndEscapeCloses() =>
        await AssertTabbedDialogAsync(
            DialogRoute.HeaderFooter,
            "HeaderFooterTabs",
            2,
            "HeaderFooterHeaderCenterBox");

    [Fact]
    public async Task HeaderFooterRoute_AppliesAllEditorScopesThroughUndoableCommand()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            owner.Show();
            var opener = OpenDialogAsync(owner, DialogRoute.HeaderFooter);
            var dialog = await WaitForOwnedDialogAsync(owner);
            try
            {
                var values = new Dictionary<string, string>
                {
                    ["HeaderFooterHeaderLeftBox"] = "header-left",
                    ["HeaderFooterHeaderCenterBox"] = "header-center",
                    ["HeaderFooterHeaderRightBox"] = "header-right",
                    ["HeaderFooterFooterLeftBox"] = "footer-left",
                    ["HeaderFooterFooterCenterBox"] = "footer-center",
                    ["HeaderFooterFooterRightBox"] = "footer-right",
                    ["HeaderFooterFirstPageHeaderLeftBox"] = "first-header-left",
                    ["HeaderFooterFirstPageHeaderCenterBox"] = "first-header-center",
                    ["HeaderFooterFirstPageHeaderRightBox"] = "first-header-right",
                    ["HeaderFooterFirstPageFooterLeftBox"] = "first-footer-left",
                    ["HeaderFooterFirstPageFooterCenterBox"] = "first-footer-center",
                    ["HeaderFooterFirstPageFooterRightBox"] = "first-footer-right",
                    ["HeaderFooterEvenPageHeaderLeftBox"] = "even-header-left",
                    ["HeaderFooterEvenPageHeaderCenterBox"] = "even-header-center",
                    ["HeaderFooterEvenPageHeaderRightBox"] = "even-header-right",
                    ["HeaderFooterEvenPageFooterLeftBox"] = "even-footer-left",
                    ["HeaderFooterEvenPageFooterCenterBox"] = "even-footer-center",
                    ["HeaderFooterEvenPageFooterRightBox"] = "even-footer-right",
                };
                foreach (var (automationId, value) in values)
                    FindByAutomationId<TextBox>(dialog, automationId)!.Text = value;

                FindByAutomationId<CheckBox>(dialog, "HeaderFooterDifferentFirstPageCheck")!.IsChecked = true;
                FindByAutomationId<CheckBox>(dialog, "HeaderFooterDifferentOddEvenCheck")!.IsChecked = true;
                FindByAutomationId<Button>(dialog, "HeaderFooterEditorOkButton")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await AwaitClosedAsync(opener);

                var session = owner.Session;
                var sheet = session.ActiveSheet;
                sheet.PageHeader.Left.Should().Be("header-left");
                sheet.PageHeader.Center.Should().Be("header-center");
                sheet.PageFooter.Right.Should().Be("footer-right");
                sheet.FirstPageHeader.Center.Should().Be("first-header-center");
                sheet.FirstPageFooter.Right.Should().Be("first-footer-right");
                sheet.EvenPageHeader.Left.Should().Be("even-header-left");
                sheet.EvenPageFooter.Center.Should().Be("even-footer-center");
                sheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
                sheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
                session.CanUndo.Should().BeTrue("the dedicated ribbon route must use the undoable command pipeline");
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

    [Fact]
    public async Task SymbolPicker_UsesListFocus_CyclesBothTabs_AndEscapeCloses()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            owner.Show();
            var opener = OpenDialogAsync(owner, DialogRoute.SymbolPicker);
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

                owner.AllowCloseWithoutDirtyPromptForParityCapture();

                owner.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    private static async Task AssertTabbedDialogAsync(DialogRoute route, string tabAutomationId, int tabCount, string initialAutomationId)
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            owner.Show();
            var opener = OpenDialogAsync(owner, route);
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
                    var initialAutomationIdForTab = InitialAutomationIdForTab(route, index, initialAutomationId);
                    var initial = ResolveInitialTarget(
                            initialAutomationIdForTab is null
                                ? null
                                : FindByAutomationId<Control>(dialog, initialAutomationIdForTab))
                        ?? FindFirstFocusableVisibleDescendant((tabs.SelectedItem as TabItem)?.Content as Control)
                        ?? throw new InvalidOperationException($"No focus target for {route} tab {index}.");
                    AssertFullCycle(dialog, initial);
                }

                Send(dialog, Key.Escape);
                dialog.IsVisible.Should().BeFalse($"Escape must close {route}");
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

    private static async Task AssertPageSetupGraphAsync(
        DialogRoute route,
        int initialTabIndex,
        string initialAutomationId)
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            owner.Show();
            var opener = OpenDialogAsync(owner, route);
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
                    initial.Should().NotBeNull($"No focus target for {route} tab {index}.");
                    AssertFullCycle(dialog, initial!);
                }

                Send(dialog, Key.Escape);
                dialog.IsVisible.Should().BeFalse($"Escape must close {route}");
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

    private static Control? ResolveInitialTarget(Control? target)
    {
        if (target is not ListBox listBox)
            return target;

        return listBox.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .FirstOrDefault(item => Equals(item.Content, listBox.SelectedItem))
            ?? listBox.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault();
    }

    private static string? InitialAutomationIdForTab(DialogRoute route, int tabIndex, string firstTabAutomationId) =>
        route switch
        {
            DialogRoute.FormatCells => tabIndex switch
            {
                0 => firstTabAutomationId,
                1 => "FormatCellsHorizontalAlignmentBox",
                2 => "FormatCellsFontNameBox",
                3 => "FormatCellsBorderStyleBox",
                4 => "FormatCellsFillColorBox",
                5 => "FormatCellsLockedBox",
                _ => throw new ArgumentOutOfRangeException(nameof(tabIndex)),
            },
            DialogRoute.FindReplace => tabIndex switch
            {
                0 => firstTabAutomationId,
                1 => "FindReplaceReplaceFindBox",
                _ => throw new ArgumentOutOfRangeException(nameof(tabIndex)),
            },
            _ => null,
        };

    private static T? FindByAutomationId<T>(Window dialog, string automationId, bool required = true)
        where T : Control
    {
        var match = dialog.GetVisualDescendants().OfType<T>().FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
        if (required && match is null)
            throw new InvalidOperationException($"Missing {typeof(T).Name}#{automationId}.");
        return match;
    }

    private static Task OpenDialogAsync(MainWindow owner, DialogRoute route) =>
        route switch
        {
            DialogRoute.DataValidation => owner.ShowDataValidationInputDialogForTestAsync(),
            DialogRoute.FindReplace => owner.ShowFindReplaceTabbedDialogForTestAsync(),
            DialogRoute.FormatCells => owner.ShowFormatCellsInputDialogForTestAsync(),
            DialogRoute.PageSetup => owner.ShowPageSetupDialogForTestAsync(),
            DialogRoute.HeaderFooter => owner.ShowHeaderFooterDialogForTestAsync(),
            DialogRoute.SymbolPicker => owner.ShowSymbolPickerForTestAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

    private enum DialogRoute
    {
        DataValidation,
        FindReplace,
        FormatCells,
        PageSetup,
        HeaderFooter,
        SymbolPicker,
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
        var completed = await Task.WhenAny(opener, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().BeSameAs(opener, "the dialog opener must complete after the window closes");
        await opener;
    }

    private static string Describe(Control control)
    {
        var automationId = AutomationProperties.GetAutomationId(control);
        return string.IsNullOrWhiteSpace(automationId) ? control.GetType().Name : $"{control.GetType().Name}#{automationId}";
    }
}
