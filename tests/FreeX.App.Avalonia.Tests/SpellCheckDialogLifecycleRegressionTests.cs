using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class SpellCheckDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    [Fact]
    public async Task SpellCheckDialog_MatchesWpfFocusCycleEscapeAndDefaultAction()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-spell-check-dialog-lifecycle-" + Guid.NewGuid().ToString("N"));

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
                        "dialog.SpellCheckDialog",
                    };

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var contract = window.DialogInteractionContracts["dialog.SpellCheck"];
                    contract.ActualModality.Should().Be("modal");
                    contract.Ownership.Should().Be("passed:owned-by-main-window");
                    contract.OpenerLifecycle.Should().Be("passed:modal-opener-blocked-while-open");
                    contract.InitialFocus.Should().Be("passed:ListBox#SpellCheckSuggestionsList");
                    contract.TabForward.Should().StartWith("passed:");
                    contract.TabBackward.Should().StartWith("passed:");
                    contract.EscapeCancel.Should().Be("passed:closed-by-escape");
                    contract.DefaultEnter.Should().Be("classified:not-invoked-mutation-risk:Change");

                    window.BuildDialogInteractionContractResults(selectedIds)
                        .Should().ContainSingle(result => result.Status == "passed",
                            "Spell Check contract should pass: {0}; {1}; {2}; {3}; {4}",
                            contract.InitialFocus,
                            contract.TabForward,
                            contract.TabBackward,
                            contract.EscapeCancel,
                            contract.DefaultEnter);
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
                // Temp cleanup must not hide the dialog lifecycle regression.
            }
        }
    }
}
