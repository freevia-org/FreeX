using Avalonia.Controls;
using Avalonia.Headless;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class ShapeEffectsDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    [Fact]
    public async Task ShapeEffectsDialog_UsesWpfFocusAndOwnedKeyboardLifecycle()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-shape-effects-lifecycle-"))
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
                        "dialog.ShapeEffects",
                    };

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var contract = window.DialogInteractionContracts["dialog.ShapeEffectsDialog"];
                    contract.ActualModality.Should().Be("modal");
                    contract.Ownership.Should().StartWith("passed:");
                    contract.OpenerLifecycle.Should().Be("passed:modal-opener-blocked-while-open");
                    contract.InitialFocus.Should().Be("passed:ComboBox#ShapeEffectsPresetBox");
                    contract.TabForward.Should().StartWith("passed:");
                    contract.TabBackward.Should().StartWith("passed:");
                    contract.EscapeCancel.Should().Be("passed:closed-by-escape");
                    contract.OwnerFocusRestore.Should().StartWith("passed:");

                    window.BuildDialogInteractionContractResults(selectedIds)
                        .Should().ContainSingle(
                            result => result.Status == "passed",
                            "Shape Effects lifecycle should pass: {0}; {1}; {2}; {3}",
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

                    window.AllowCloseWithoutDirtyPromptForParityCapture();

                    if (window.IsVisible)
                        window.Close();
                }
            }, CancellationToken.None);
        }
    }
}
