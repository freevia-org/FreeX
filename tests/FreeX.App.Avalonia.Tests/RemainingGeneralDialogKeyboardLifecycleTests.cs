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
            "PageSetupCancelButton");
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
                if (owner.IsVisible)
                    owner.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

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
