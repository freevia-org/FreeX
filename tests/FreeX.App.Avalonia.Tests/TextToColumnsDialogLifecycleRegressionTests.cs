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
                // ConfigureDeferredDialogCancel posts the cancel at Input priority rather than closing
                // inline, so the dialog is still visible until the dispatcher runs -- pump it the same
                // way every other step in this test does.
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
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
    public async Task TextToColumnsWizard_HasWpfDefaultButtonsOtherBoxGatingRulerAndFormatRadios()
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
                dialog!.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                var next = Find<Button>(dialog, "TextToColumnsNextButton");
                var finish = Find<Button>(dialog, "TextToColumnsApplyButton");
                next.IsDefault.Should().BeTrue("WPF makes Next the default button on step 1");
                finish.IsDefault.Should().BeFalse();

                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                next.IsDefault.Should().BeTrue("WPF keeps Next the default button on step 2");

                var otherBox = Find<CheckBox>(dialog, "TextToColumnsOtherBox");
                var otherCharBox = Find<TextBox>(dialog, "TextToColumnsOtherCharBox");
                otherCharBox.IsEnabled.Should().BeFalse("the custom delimiter box is disabled until its check box is ticked");
                otherBox.IsChecked = true;
                otherCharBox.IsEnabled.Should().BeTrue();
                otherBox.IsChecked = false;
                otherCharBox.IsEnabled.Should().BeFalse();

                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                next.IsDefault.Should().BeFalse();
                finish.IsDefault.Should().BeTrue("WPF makes Finish the default button on step 3");

                var general = Find<RadioButton>(dialog, "TextToColumnsFormatGeneralButton");
                var text = Find<RadioButton>(dialog, "TextToColumnsFormatTextButton");
                var date = Find<RadioButton>(dialog, "TextToColumnsFormatDateButton");
                var skip = Find<RadioButton>(dialog, "TextToColumnsFormatSkipButton");
                var dateOrder = Find<ComboBox>(dialog, "TextToColumnsDateFormatBox");
                general.IsChecked.Should().BeTrue();
                dateOrder.ItemCount.Should().Be(6);

                // The stored format follows the output column: column 1 keeps the skip choice while
                // column 2 still shows the general default.
                var columnBox = Find<ComboBox>(dialog, "TextToColumnsFormatColumnBox");
                skip.IsChecked = true;
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                columnBox.ItemCount.Should().BeGreaterThan(1);
                columnBox.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                general.IsChecked.Should().BeTrue();
                skip.IsChecked.Should().BeFalse();
                date.IsChecked = true;
                dateOrder.SelectedItem = "DMY";
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                columnBox.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                skip.IsChecked.Should().BeTrue();
                columnBox.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                date.IsChecked.Should().BeTrue();
                dateOrder.SelectedItem.Should().Be("DMY");
                text.IsChecked.Should().BeFalse();

                // Fixed-width step: the ruler and its hint replace the delimiter group.
                var back = Find<Button>(dialog, "TextToColumnsBackButton");
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.UpdateLayout();
                Find<RadioButton>(dialog, "TextToColumnsFixedWidthButton").IsChecked = true;
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                var ruler = Find<Canvas>(dialog, "TextToColumnsFixedWidthRuler");
                ruler.IsEffectivelyVisible.Should().BeTrue();
                Find<TextBlock>(dialog, "TextToColumnsFixedWidthRulerHint").Text.Should().Be(
                    global::FreeX.App.Localization.LocalizedUiText.Get("TextToColumns_ClickTheRulerToCreateABreakLineDragToMoveItOrRightClickALineToRemoveIt"));

                var breaksBox = Find<TextBox>(dialog, "TextToColumnsBreaksBox");
                breaksBox.Text = "3,6";
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
                ruler.Children.Select(child => child.Tag).OfType<int>().Should().Equal(3, 6);
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
                        // The dialog is deliberately closed by the probe.
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
    public async Task FixedWidthRuler_DrawsTheSampleATickPerPositionAndALinePerBreak()
    {
        await Session.Dispatch(() =>
        {
            var ruler = new Canvas { Width = 440, Height = 58 };
            MainWindow.RenderTextToColumnsFixedWidthRuler(ruler, ["abcdefghij", "abc"], [4, 7]);

            ruler.Children.OfType<TextBlock>().Single().Text.Should().Be("abcdefghij");
            // Nine ticks for a ten-character sample plus one line per break position.
            ruler.Children.OfType<global::Avalonia.Controls.Shapes.Line>().Should().HaveCount(9 + 2);
            ruler.Children.Select(child => child.Tag).OfType<int>().Should().Equal(4, 7);
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

                    var contract = window.DialogInteractionContracts["dialog.TextToColumns"];
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
