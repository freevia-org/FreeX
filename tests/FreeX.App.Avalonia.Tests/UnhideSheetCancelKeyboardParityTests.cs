using System.Reflection;
using System.Threading;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class UnhideSheetCancelKeyboardParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task EscapeCancelsUnhideSheetDialogWhenCancelButtonHasFocus()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            Window? dialog = null;
            Task<WorkbookHiddenSheet?>? opener = null;
            try
            {
                owner.Show();
                var method = typeof(MainWindow).GetMethod(
                    "ShowUnhideSheetDialogAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Missing Unhide Sheet production dialog route.");
                opener = method.Invoke(owner, [new[]
                {
                    new WorkbookHiddenSheet(owner.Session.ActiveSheet.Id, "Archive"),
                }]) as Task<WorkbookHiddenSheet?>
                    ?? throw new InvalidOperationException("Unhide Sheet route returned an unexpected task.");

                for (var attempt = 0; attempt < 200 && dialog is null; attempt++)
                {
                    dialog = owner.OwnedWindows.FirstOrDefault(window =>
                        window.IsVisible && window.Title == UiText.Get("UnhideSheet_UnhideSheet"));
                    if (dialog is null)
                        await Task.Delay(10);
                }

                dialog.Should().NotBeNull("the production Unhide Sheet route should open its owned dialog");
                var cancel = dialog!.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "UnhideSheetCancelButton");
                cancel.IsCancel.Should().BeTrue();
                cancel.Focus();
                Dispatcher.UIThread.RunJobs();
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(cancel);

                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Dispatcher.UIThread.RunJobs();

                dialog.IsVisible.Should().BeFalse();
                (await opener).Should().BeNull("Escape cancels without unhiding the selected sheet");
            }
            finally
            {
                if (dialog?.IsVisible == true)
                    dialog.Close();
                if (opener is not null && !opener.IsCompleted)
                    await Task.WhenAny(opener, Task.Delay(1000));
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
}
