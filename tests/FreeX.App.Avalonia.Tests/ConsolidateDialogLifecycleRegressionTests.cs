using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class ConsolidateDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ConsolidateDialog_UsesWpfFunctionComboAsInitialFocusAndCyclesKeyboardLifecycle()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-consolidate-focus-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var selectedIds = new HashSet<string>(StringComparer.Ordinal)
                    {
                        "dialog.Consolidate",
                    };

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var contract = window.DialogInteractionContracts["dialog.Consolidate"];
                    contract.InitialFocus.Should().Be("passed:ComboBox#ConsolidateFunctionBox");
                    contract.TabForward.Should().StartWith("passed:");
                    contract.TabBackward.Should().StartWith("passed:");
                    contract.EscapeCancel.Should().Be("passed:closed-by-escape");
                    window.BuildDialogInteractionContractResults(selectedIds)
                        .Should().ContainSingle(
                            result => result.Status == "passed",
                            "Consolidate contract should pass: {0}; {1}; {2}; {3}",
                            contract.InitialFocus,
                            contract.TabForward,
                            contract.TabBackward,
                            contract.EscapeCancel);
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
                // Temp cleanup must not hide the dialog focus regression.
            }
        }
    }
}
