using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless coverage for the <c>--parity-capture</c> surface capture. Runs the real <see cref="MainWindow"/>
/// under the headless drawing platform, drives <see cref="MainWindow.CaptureParitySurfacesAsync"/> into a temp
/// directory, and asserts the grid surface, at least one dialog surface, and PNG files are produced. Pixel
/// fidelity is the comparison runner's concern; this proves the capture path produces real files headlessly.
/// </summary>
public sealed class ParityCaptureTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task CaptureParitySurfaces_ProducesGridAndDialogPngs()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-parity-capture-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var results = await window.CaptureParitySurfacesAsync(outputDirectory);

                // The grid surface always renders the live shell window — it must be captured.
                var grid = results.Single(r => r.Id == "grid.demo");
                grid.Captured.Should().BeTrue($"the demo grid should render headlessly (note: {grid.Note})");
                File.Exists(Path.Combine(outputDirectory, "grid.demo.png"))
                    .Should().BeTrue("grid.demo.png should be written");
                new FileInfo(Path.Combine(outputDirectory, "grid.demo.png")).Length
                    .Should().BeGreaterThan(0, "the PNG should not be empty");

                // Every ribbon tab surface should also capture (same window-render path as the grid).
                results.Where(r => r.Id.StartsWith("tab.", StringComparison.Ordinal))
                    .Should().OnlyContain(r => r.Captured, "ribbon tabs render the shell window");
                File.Exists(Path.Combine(outputDirectory, "tab.Home.png")).Should().BeTrue();

                // At least one dialog surface should be captured to a PNG via the modal-capture path.
                var capturedDialogs = results
                    .Where(r => r.Id.StartsWith("dialog.", StringComparison.Ordinal) && r.Captured)
                    .ToList();
                capturedDialogs.Should().NotBeEmpty("at least one dialog should open and render headlessly");
                foreach (var dialog in capturedDialogs)
                    File.Exists(Path.Combine(outputDirectory, dialog.PngFileName))
                        .Should().BeTrue($"{dialog.PngFileName} should be written for captured dialog {dialog.Id}");

                results.Where(r => r.Id.StartsWith("backstage.", StringComparison.Ordinal))
                    .Should()
                    .OnlyContain(
                        r => !r.Captured && r.Note.Contains("dialog-based", StringComparison.Ordinal),
                        "Avalonia must not compare modal File dialogs as if they were the Windows Backstage overlay");

                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }
}
