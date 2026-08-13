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
public sealed class SecondRoundDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(
        DialogRoute.SelectionPane,
        "SelectionPaneDialog",
        "SelectionPaneSearchBox",
        "SelectionPaneCancelButton")]
    [InlineData(
        DialogRoute.SpellCheck,
        "SpellCheckDialog",
        "SpellCheckSuggestionsList",
        "SpellCheckCancelButton")]
    [InlineData(
        DialogRoute.TextToColumns,
        "TextToColumnsDialog",
        "TextToColumnsDelimitedButton",
        "TextToColumnsCancelButton")]
    public async Task ProductionRoute_FocusesExpectedControl_AndDefersRealCancel(
        DialogRoute route,
        string dialogAutomationId,
        string initialFocusAutomationId,
        string cancelAutomationId)
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            Window? dialog = null;
            Task? opener = null;
            try
            {
                owner.Show();
                opener = OpenDialogAsync(owner, route);
                dialog = await WaitForOwnedDialogAsync(owner, dialogAutomationId);
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
                await Task.Delay(250);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                var focused = dialog.FocusManager?.GetFocusedElement() as Control;
                focused.Should().NotBeNull();
                AutomationProperties.GetAutomationId(focused!)
                    .Should().Be(initialFocusAutomationId);

                var cancelButton = dialog.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button =>
                        AutomationProperties.GetAutomationId(button) == cancelAutomationId);
                var cancelClickCount = 0;
                var dispatchingRawKey = false;
                bool? closedDuringRawKeyDispatch = null;
                cancelButton.Click += (_, _) => cancelClickCount++;
                dialog.Closing += (_, _) => closedDuringRawKeyDispatch = dispatchingRawKey;

                dispatchingRawKey = true;
                MainWindow.SendDialogKeyForTest(
                        dialog,
                        Key.Escape,
                        RawInputModifiers.None,
                        out var error)
                    .Should().BeTrue(error);
                dispatchingRawKey = false;
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                cancelClickCount.Should().Be(1, "Escape must invoke the production Cancel/Close action");
                closedDuringRawKeyDispatch.Should().BeFalse(
                    "Linux window closure must run after Avalonia finishes routing the raw key event");
                dialog.IsVisible.Should().BeFalse();
                await AwaitClosedAsync(opener);
            }
            finally
            {
                if (dialog?.IsVisible == true)
                    dialog.Close();
                if (opener is not null)
                    await AwaitClosedAsync(opener);

                owner.AllowCloseWithoutDirtyPromptForParityCapture();

                if (owner.IsVisible)
                    owner.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TextToColumns_PreHandledRoutedEscape_ReachesTunneledCancelHandler()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            Window? dialog = null;
            Task? opener = null;
            try
            {
                owner.Show();
                opener = OpenDialogAsync(owner, DialogRoute.TextToColumns);
                dialog = await WaitForOwnedDialogAsync(owner, "TextToColumnsDialog");
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                var cancelButton = dialog.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button =>
                        AutomationProperties.GetAutomationId(button) == "TextToColumnsCancelButton");
                var focused = dialog.FocusManager?.GetFocusedElement() as InputElement;
                focused.Should().NotBeNull();

                var cancelClickCount = 0;
                var dispatchingRoutedKey = false;
                bool? closedDuringRoutedKeyDispatch = null;
                cancelButton.Click += (_, _) => cancelClickCount++;
                dialog.Closing += (_, _) => closedDuringRoutedKeyDispatch = dispatchingRoutedKey;

                var escape = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Escape,
                    PhysicalKey = PhysicalKey.Escape,
                    KeyDeviceType = KeyDeviceType.Keyboard,
                    KeyModifiers = KeyModifiers.None,
                    Source = focused,
                    Handled = true,
                };

                dispatchingRoutedKey = true;
                focused!.RaiseEvent(escape);
                dispatchingRoutedKey = false;
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                cancelClickCount.Should().Be(1,
                    "handledEventsToo must preserve Escape when a child marks the routed key handled");
                closedDuringRoutedKeyDispatch.Should().BeFalse();
                dialog.IsVisible.Should().BeFalse();
                await AwaitClosedAsync(opener);
            }
            finally
            {
                if (dialog?.IsVisible == true)
                    dialog.Close();
                if (opener is not null)
                    await AwaitClosedAsync(opener);

                owner.AllowCloseWithoutDirtyPromptForParityCapture();

                if (owner.IsVisible)
                    owner.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    private static Task OpenDialogAsync(MainWindow owner, DialogRoute route) =>
        route switch
        {
            DialogRoute.SelectionPane => owner.ShowSelectionPaneParityDialogForTestAsync(),
            DialogRoute.SpellCheck => owner.ShowSpellCheckParityDialogForTestAsync(),
            DialogRoute.TextToColumns => owner.ShowTextToColumnsParityDialogForTestAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

    public enum DialogRoute
    {
        SelectionPane,
        SpellCheck,
        TextToColumns,
    }

    private static async Task<Window> WaitForOwnedDialogAsync(
        MainWindow owner,
        string dialogAutomationId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var dialog = owner.OwnedWindows.FirstOrDefault(window =>
                window.IsVisible &&
                string.Equals(
                    AutomationProperties.GetAutomationId(window),
                    dialogAutomationId,
                    StringComparison.Ordinal));
            if (dialog is not null)
                return dialog;

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException(
            $"Dialog {dialogAutomationId} did not open within 5 seconds.");
    }

    private static async Task AwaitClosedAsync(Task opener)
    {
        var completed = await Task.WhenAny(opener, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().BeSameAs(opener, "the modal opener must complete after cancellation");
        await opener;
    }
}
