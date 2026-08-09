using System.Threading;

using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class WatchWindowDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    [Fact]
    public async Task WatchWindowDialog_MatchesWpfFocusTabAndEscapeLifecycle()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-watch-window-lifecycle-"))
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
                        "dialog.WatchWindowDialog",
                    };

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var contract = window.DialogInteractionContracts["dialog.WatchWindow"];
                    contract.ActualModality.Should().Be("modeless");
                    contract.Ownership.Should().StartWith("passed:");
                    contract.OpenerLifecycle.Should().Be("passed:modeless-opener-completed-while-open");
                    contract.OwnerInteractivity.Should().Be("passed:modeless-owner-enabled");
                    contract.InitialFocus.Should().Be("passed:ListBox#WatchWindowList");
                    contract.TabForward.Should().StartWith("passed:");
                    contract.TabBackward.Should().StartWith("passed:");
                    contract.EscapeCancel.Should().Be("passed:closed-by-escape");
                    contract.OwnerFocusRestore.Should().StartWith("passed:");

                    window.BuildDialogInteractionContractResults(selectedIds)
                        .Should().ContainSingle(result => result.Status == "passed", contract.ToString());
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
            }, CancellationToken.None);
        }
    }
}
