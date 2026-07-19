using System.Threading;

using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class ChartFormatDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static readonly IReadOnlyDictionary<string, string> ExpectedInitialFocus =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // These are the equivalent Avalonia controls for the WPF chart-area fill editor and axis
            // minimum editor, whose Loaded handlers explicitly focus and select the initial target.
            ["dialog.ChartAreaLegendDialog"] = "Button#ChartAreaFillButton",
            ["dialog.ChartAxisFormatDialog"] = "TextBox#ChartAxisMinimumBox",
        };

    [Fact]
    public async Task ChartFormatFamily_MatchesWpfInitialFocusTabCycleAndEscape()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-chart-format-lifecycle-" + Guid.NewGuid().ToString("N"));

        try
        {
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

                    if (window.IsVisible)
                        window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            try
            {
                if (Directory.Exists(outputDirectory))
                    Directory.Delete(outputDirectory, recursive: true);
            }
            catch
            {
                // Test cleanup must not hide a chart dialog lifecycle regression.
            }
        }
    }
}
