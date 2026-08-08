using System.Reflection;
using System.Threading;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class RemainingGeneralDialogKeyboardLifecycleTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task AdvancedFilterEscape_InvokesCancelAfterRawKeyDispatch()
    {
        await AssertProductionEscapeAsync(
            "ShowAdvancedFilterInputDialogAsync",
            [],
            "AdvancedFilterCancelButton");
    }

    [Theory]
    [InlineData("ShowPageSetupDialogAsync")]
    [InlineData("ShowHeaderFooterDialogAsync")]
    public async Task PageSetupRoutesEscape_InvokeCancelAfterRawKeyDispatch(string openerName)
    {
        await AssertProductionEscapeAsync(
            openerName,
            CreatePageSetupArguments(openerName),
            openerName == "ShowHeaderFooterDialogAsync"
                ? "HeaderFooterEditorCancelButton"
                : "PageSetupCancelButton");
    }

    [Fact]
    public async Task PreHandledRawEscape_ClosesOnceAfterKeyUp()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new Window();
            var cancelButton = new Button
            {
                Content = "Cancel",
                IsCancel = true,
            };
            dialog.Content = cancelButton;

            var cancelClickCount = 0;
            cancelButton.Click += (_, _) =>
            {
                cancelClickCount++;
                dialog.Close();
            };
            dialog.AddHandler(
                InputElement.KeyDownEvent,
                (_, args) => args.Handled = true,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            dialog.AddHandler(
                InputElement.KeyUpEvent,
                (_, args) => args.Handled = true,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);

            typeof(MainWindow)
                .GetMethod(
                    "ConfigureDialogCancelOnEscape",
                    BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [dialog, cancelButton]);

            dialog.Show();
            cancelButton.RaiseEvent(CreateEscapeEvent(InputElement.KeyDownEvent));
            cancelButton.RaiseEvent(CreateEscapeEvent(InputElement.KeyUpEvent));
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            cancelClickCount.Should().Be(1);
            dialog.IsVisible.Should().BeFalse();
            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AdvancedFilterRangePicker_RestoresDialogFocusBeforeEscape()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            Window? dialog = null;
            Task? opener = null;
            try
            {
                owner.Show();
                opener = InvokeOpener(owner, "ShowAdvancedFilterInputDialogAsync", []);
                dialog = await WaitForOwnedDialogAsync(owner);
                var controls = dialog.GetVisualDescendants().OfType<Control>().ToArray();
                var picker = controls.OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == "AdvancedFilterSelectListRangeButton");
                var target = controls.OfType<TextBox>().Single(textBox =>
                    AutomationProperties.GetAutomationId(textBox) == "AdvancedFilterListRangeBox");

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, picker));
                typeof(MainWindow)
                    .GetMethod(
                        "RaiseDialogRangeValidationKey",
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(owner, [Key.Enter]);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(target);
                MainWindow.SendDialogKeyForTest(
                        dialog,
                        Key.Escape,
                        RawInputModifiers.None,
                        out var error)
                    .Should().BeTrue(error);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

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
    public async Task RangeProbeBoundary_WaitsForPostedDialogFocusRestoration()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            Window? dialog = null;
            try
            {
                owner.Show();
                var target = new TextBox();
                dialog = new Window
                {
                    Content = target,
                    Width = 240,
                    Height = 120,
                };
                dialog.Show(owner);
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
                target.Focus().Should().BeTrue();
                IsFocusInside(dialog, dialog.FocusManager?.GetFocusedElement()).Should().BeTrue();

                var restorationPosted = false;
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        target.Focusable = true;
                        target.Focus();
                        restorationPosted = true;
                    },
                    DispatcherPriority.Background);

                var settleMethod = typeof(MainWindow).GetMethod(
                    "SettleDialogRangeInteractionBoundaryAsync",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                var settleTask = (Task)settleMethod.Invoke(null, [dialog])!;
                await settleTask;

                restorationPosted.Should().BeTrue();
                IsFocusInside(dialog, dialog.FocusManager?.GetFocusedElement()).Should().BeTrue();
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(target);
            }
            finally
            {
                if (dialog?.IsVisible == true)
                    dialog.Close();

                owner.AllowCloseWithoutDirtyPromptForParityCapture();

                if (owner.IsVisible)
                    owner.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    private static async Task AssertProductionEscapeAsync(
        string openerName,
        object?[] arguments,
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
                opener = InvokeOpener(owner, openerName, arguments);
                dialog = await WaitForOwnedDialogAsync(owner);
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

                cancelClickCount.Should().Be(1, "Escape must invoke the production Cancel action");
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

    private static KeyEventArgs CreateEscapeEvent(RoutedEvent<KeyEventArgs> routedEvent) =>
        new()
        {
            RoutedEvent = routedEvent,
            Key = Key.Escape,
            PhysicalKey = PhysicalKey.Escape,
            KeyDeviceType = KeyDeviceType.Keyboard,
        };

    private static bool IsFocusInside(Window dialog, IInputElement? element) =>
        element is Visual visual && ReferenceEquals(TopLevel.GetTopLevel(visual), dialog);

    private static object?[] CreatePageSetupArguments(string openerName)
    {
        if (openerName == "ShowHeaderFooterDialogAsync")
            return [];

        var method = FindOpener(openerName, parameterCount: 2);
        var source = Enum.GetValues(method.GetParameters()[0].ParameterType).GetValue(0);
        return [source, false];
    }

    private static Task InvokeOpener(MainWindow owner, string methodName, object?[] arguments)
    {
        var method = FindOpener(methodName, arguments.Length);
        return method.Invoke(owner, arguments) as Task
            ?? throw new InvalidOperationException($"{methodName} did not return Task.");
    }

    private static MethodInfo FindOpener(string methodName, int parameterCount) =>
        typeof(MainWindow)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method =>
                method.Name == methodName &&
                method.GetParameters().Length == parameterCount);

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
        completed.Should().BeSameAs(opener, "the modal opener must complete after cancellation");
        await opener;
    }
}
