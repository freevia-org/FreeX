using System.Reflection;
using System.Threading;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AllowEditRangePointModeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task RangeBoxF4_EntersPointModeAndAppliesPointedRange()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            Task? opener = null;
            Window? dialog = null;
            try
            {
                owner.Show();
                opener = InvokePrivateTask(owner, "ShowAllowEditRangeDialogAsync", "Sheet1!$A$1:$A$1");
                dialog = await WaitForOwnedDialogAsync(owner, "AllowEditRangeDialog");
                dialog.Should().NotBeNull();

                var rangeBox = Find<TextBox>(dialog!, "AllowEditRangeBox");
                var rangePicker = dialog.GetLogicalDescendants()
                    .OfType<Button>()
                    .Single(button =>
                        AutomationProperties.GetAutomationId(button) == "AllowEditRangePickerButton");
                rangePicker.IsVisible.Should().BeFalse();
                rangePicker.IsTabStop.Should().BeFalse();

                var f4 = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.F4,
                    KeyModifiers = KeyModifiers.None,
                    Source = rangeBox,
                };
                rangeBox.RaiseEvent(f4);

                f4.Handled.Should().BeTrue();
                dialog!.Opacity.Should().Be(0);
                dialog.IsHitTestVisible.Should().BeFalse();

                var pointedRange = new GridRange(
                    new CellAddress(owner.Session.ActiveSheet.Id, 2, 2),
                    new CellAddress(owner.Session.ActiveSheet.Id, 3, 3));
                owner.Session.SelectRange(pointedRange);
                InvokePrivate(owner, "RaiseDialogRangeValidationKey", Key.Enter);

                rangeBox.Text.Should().Be("$B$2:$C$3");
                dialog.IsVisible.Should().BeTrue();
                dialog.Opacity.Should().Be(1);
                dialog.IsHitTestVisible.Should().BeTrue();
            }
            finally
            {
                if (dialog?.IsVisible == true)
                    dialog.Close();

                if (opener is not null)
                {
                    try
                    {
                        await Task.WhenAny(opener, Task.Delay(1000));
                    }
                    catch
                    {
                        // The dialog is deliberately closed by the behavior probe.
                    }
                }

                foreach (var owned in owner.OwnedWindows.ToArray())
                {
                    if (owned.IsVisible)
                        owned.Close();
                }

                owner.AllowCloseWithoutDirtyPromptForParityCapture();

                if (owner.IsVisible)
                    owner.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static object? InvokePrivate(MainWindow owner, string methodName, params object[] args)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing production method {methodName}.");
        return method.Invoke(owner, args);
    }

    private static Task InvokePrivateTask(MainWindow owner, string methodName, params object[] args) =>
        InvokePrivate(owner, methodName, args) as Task
        ?? throw new InvalidOperationException($"Production method {methodName} did not return a Task.");

    private static async Task<Window?> WaitForOwnedDialogAsync(MainWindow owner, string automationId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var dialog = owner.OwnedWindows.FirstOrDefault(window =>
                string.Equals(
                    AutomationProperties.GetAutomationId(window),
                    automationId,
                    StringComparison.Ordinal));
            if (dialog is not null)
                return dialog;

            await Task.Delay(10);
        }

        return null;
    }

    private static T Find<T>(Window dialog, string automationId)
        where T : Control =>
        dialog.GetVisualDescendants()
            .OfType<T>()
            .Single(control =>
                string.Equals(
                    AutomationProperties.GetAutomationId(control),
                    automationId,
                    StringComparison.Ordinal));
}
