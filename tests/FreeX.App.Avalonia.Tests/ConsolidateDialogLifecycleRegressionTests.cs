using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using FreeX.App.Presentation.Consolidate;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class ConsolidateDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    [Fact]
    public async Task ConsolidateDialog_UsesWpfFunctionComboAsInitialFocusAndCyclesKeyboardLifecycle()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-consolidate-focus-"))
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

                    window.AllowCloseWithoutDirtyPromptForParityCapture();

                    if (window.IsVisible)
                        window.Close();
                }
                return true;
            }, CancellationToken.None);
        }
    }

    [Fact]
    public async Task ConsolidateCapture_UsesFixtureStateAndProducesFixedNonBlankSurface()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-consolidate-capture-"))
        {
            var outputDirectory = temporaryDirectory.Path;
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var results = await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        targetSurfaceId: "dialog.Consolidate");

                    var result = results.Should().ContainSingle().Subject;
                    result.Id.Should().Be("dialog.Consolidate");
                    result.Captured.Should().BeTrue(result.Note);

                    var pngPath = Path.Combine(outputDirectory, result.PngFileName);
                    using var bitmap = new Bitmap(pngPath);
                    bitmap.PixelSize.Width.Should().Be((int)ConsolidateDialogPlanner.CaptureWidth);
                    bitmap.PixelSize.Height.Should().Be((int)ConsolidateDialogPlanner.CaptureHeight);
                    bitmap.Dpi.X.Should().Be(96);
                    bitmap.Dpi.Y.Should().Be(96);
                    ConsolidateParityFixture.SourceReference.Should().Be("A1:C4");
                    ConsolidateParityFixture.DestinationReference.Should().Be("H2");
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
}
