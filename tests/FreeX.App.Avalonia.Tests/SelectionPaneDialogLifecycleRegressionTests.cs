using System.Threading;

using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class SelectionPaneDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    [Fact]
    public async Task SelectionPaneDialog_MatchesWpfFocusTabAndEscapeLifecycle()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-selection-pane-lifecycle-"))
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
                        "dialog.SelectionPaneDialog",
                    };

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var contract = window.DialogInteractionContracts["dialog.SelectionPane"];
                    contract.ActualModality.Should().Be("modal");
                    contract.Ownership.Should().StartWith("passed:");
                    contract.OpenerLifecycle.Should().StartWith("passed:");
                    contract.InitialFocus.Should().Be("passed:TextBox#SelectionPaneSearchBox");
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
