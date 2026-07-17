using System.IO;
using System.Threading;
using Avalonia.Headless;
using FreeX.App.Presentation.Interactions;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class DialogInteractionValidationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task AboutCapture_ExercisesRawKeyboardAndEmitsOneContractRowPerCatalogDialog()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-dialog-contract-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var captures = await window.CaptureParitySurfacesAsync(
                    outputDirectory,
                    targetSurfaceId: "dialog.AboutDialog");

                captures.Should().ContainSingle(result => result.Id == "dialog.About" && result.Captured);
                window.DialogInteractionContracts.Should().ContainKey("dialog.About");
                var contract = window.DialogInteractionContracts["dialog.About"];
                contract.ActualModality.Should().Be("modal");
                contract.InitialFocus.Should().StartWith("passed:");
                contract.TabForward.Should().NotContain("raw-input-unavailable");
                contract.TabBackward.Should().NotContain("raw-input-unavailable");
                contract.EscapeCancel.Should().NotContain("raw-input-unavailable");
                contract.DefaultEnter.Should().NotContain("raw-input-unavailable");

                var results = window.BuildDialogInteractionContractResults();
                results.Should().HaveCount(120);
                results.Select(result => result.Id).Should().Equal(
                    InteractionSurfaceCatalog.Dialogs.Select(dialog => dialog.Id));
                results.Select(result => result.Id).Should().OnlyHaveUniqueItems();
                results.Should().ContainSingle(result =>
                    result.Id == "dialog.AboutDialog" &&
                    result.Category == "dialog-contract" &&
                    result.EvidenceLevel == "raw-keyboard-focus-contract" &&
                    result.Evidence.Contains("tab=", StringComparison.Ordinal) &&
                    result.Evidence.Contains("shift-tab=", StringComparison.Ordinal) &&
                    result.Evidence.Contains("escape=", StringComparison.Ordinal) &&
                    result.Evidence.Contains("owner-focus=", StringComparison.Ordinal) &&
                    result.Evidence.Contains("enter=", StringComparison.Ordinal));

                window.Close();
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
                // Temp cleanup is best-effort.
            }
        }
    }
}
