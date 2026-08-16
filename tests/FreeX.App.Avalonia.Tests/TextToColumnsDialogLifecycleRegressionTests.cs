using System.Threading;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class TextToColumnsDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    [Fact]
    public async Task TextToColumnsWizard_MatchesWpfFocusTargetsAcrossStepsAndBackNavigation()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            Task? opener = null;
            Window? dialog = null;
            try
            {
                owner.Show();
                opener = owner.ShowTextToColumnsParityDialogForTestAsync();
                dialog = await WaitForOwnedDialogAsync(owner, "TextToColumnsDialog");
                dialog.Should().NotBeNull();

                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                DescribeFocused(dialog!).Should().Be("RadioButton#TextToColumnsDelimitedButton");

                var next = Find<Button>(dialog, "TextToColumnsNextButton");
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                DescribeFocused(dialog).Should().Be("CheckBox#TextToColumnsTabBox");

                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                DescribeFocused(dialog).Should().Be("ComboBox#TextToColumnsFormatColumnBox");

                var back = Find<Button>(dialog, "TextToColumnsBackButton");
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                DescribeFocused(dialog).Should().Be("CheckBox#TextToColumnsTabBox");

                MainWindow.SendDialogKeyForTest(
                        dialog,
                        Key.Tab,
                        RawInputModifiers.Shift,
                        out var shiftTabError)
                    .Should().BeTrue(shiftTabError);
                dialog.FocusManager?.GetFocusedElement().Should().NotBeNull();

                MainWindow.SendDialogKeyForTest(
                        dialog,
                        Key.Escape,
                        RawInputModifiers.None,
                        out var escapeError)
                    .Should().BeTrue(escapeError);
                dialog.IsVisible.Should().BeFalse();
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
                        // The dialog is deliberately closed by the lifecycle probe.
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

    [Fact]
    public async Task TextToColumnsDialogContract_PassesInitialFocusTabCycleAndEscape()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-text-to-columns-lifecycle-"))
        {
            var outputDirectory = temporaryDirectory.Path;
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var selectedIds = new HashSet<string>(StringComparer.Ordinal)
                    {
                        "dialog.TextToColumnsDialog",
                    };

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var contract = window.DialogInteractionContracts["dialog.TextToColumnsDialog"];
                    contract.InitialFocus.Should().Be("passed:RadioButton#TextToColumnsDelimitedButton");
                    contract.TabForward.Should().StartWith("passed:");
                    contract.TabBackward.Should().StartWith("passed:");
                    contract.EscapeCancel.Should().Be("passed:closed-by-escape");
                    window.BuildDialogInteractionContractResults(selectedIds)
                        .Should().ContainSingle(result => result.Status == "passed");
                }
                finally
                {
                    foreach (var owned in window.OwnedWindows.ToArray())
                    {
                        if (owned.IsVisible)
                            owned.Close();
                    }

                    window.AllowCloseWithoutDirtyPromptForParityCapture();

                    if (window.IsVisible)
                        window.Close();
                }
                return true;
            }, CancellationToken.None);
        }
    }

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

    private static string DescribeFocused(Window dialog)
    {
        var focused = dialog.FocusManager?.GetFocusedElement();
        focused.Should().NotBeNull();
        var control = focused as Control;
        control.Should().NotBeNull();
        var automationId = AutomationProperties.GetAutomationId(control!);
        return control!.GetType().Name + "#" + automationId;
    }
}
