using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class ChartFormatDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    private static readonly IReadOnlyDictionary<string, string> ExpectedInitialFocus =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // These are the equivalent Avalonia controls for the WPF chart-area fill editor and axis
            // minimum editor, whose Loaded handlers explicitly focus and select the initial target.
            ["dialog.ChartAreaLegendDialog"] = "Button#ChartAreaFillButton",
            ["dialog.ChartAxisFormatDialog"] = "TextBox#ChartAxisMinimumBox",
        };

    [Fact]
    public async Task ChartDialogLifecycle_EstablishesNativeFocusAndRoutesRawKeyboardInput()
    {
        await Session.Dispatch(() =>
        {
            var owner = new Window { Width = 400, Height = 240 };
            Window? dialog = null;
            try
            {
                owner.Show();
                var initial = new TextBox { Text = "Initial" };
                var second = new Button { Content = "Second" };
                dialog = new Window
                {
                    Width = 300,
                    Height = 180,
                    Content = new StackPanel { Children = { initial, second } },
                };

                MainWindow.ConfigureChartDialogKeyboardLifecycleForTest(dialog, initial);

                dialog.Show(owner);
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(initial);

                Send(dialog, Key.Tab, RawInputModifiers.None);
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(second);
                Send(dialog, Key.Tab, RawInputModifiers.Shift);
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(initial);
                Send(dialog, Key.Escape, RawInputModifiers.None);
                dialog.IsVisible.Should().BeFalse();
            }
            finally
            {
                if (dialog?.IsVisible == true)
                    dialog.Close();
                if (owner.IsVisible)
                    owner.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ChartFormatFamily_MatchesWpfInitialFocusTabCycleAndEscape()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-chart-format-lifecycle-"))
        {
            var outputDirectory = temporaryDirectory.Path;
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var selectedIds = ExpectedInitialFocus.Keys.ToHashSet(StringComparer.Ordinal);

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var results = window.BuildDialogInteractionContractResults(selectedIds);
                    results.Should().HaveCount(ExpectedInitialFocus.Count);
                    results.Should().OnlyContain(
                        result => result.Status == "passed",
                        string.Join(Environment.NewLine, results.Select(result =>
                            $"{result.Id}: {result.Evidence}")));

                    foreach (var (surfaceId, expectedFocus) in ExpectedInitialFocus)
                    {
                        var contract = window.DialogInteractionContracts[surfaceId];
                        contract.InitialFocus.Should().Be("passed:" + expectedFocus, surfaceId);
                        contract.TabForward.Should().StartWith("passed:", surfaceId);
                        contract.TabBackward.Should().StartWith("passed:", surfaceId);
                        contract.EscapeCancel.Should().Be("passed:closed-by-escape", surfaceId);
                    }
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

    private static void Send(Window dialog, Key key, RawInputModifiers modifiers)
    {
        MainWindow.SendDialogKeyForTest(dialog, key, modifiers, out var error).Should().BeTrue(error);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
    }
}
