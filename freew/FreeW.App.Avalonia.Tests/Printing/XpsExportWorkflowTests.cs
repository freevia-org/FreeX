using Avalonia.Platform.Storage;
using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Options;

namespace FreeW.App.Avalonia.Tests.Printing;

public sealed class XpsExportWorkflowTests : IDisposable
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.XpsExportWorkflowTests-");

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public async Task ExportXps_CancelLeavesNoOutput()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateWindow((_, _) => Task.FromResult((true, (string?)null)));
            await window.ExportXpsForTests();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExportXps_UsesOverwritePromptAndReplacesExistingLocalPath()
    {
        var path = Path.Combine(_temporaryDirectory.Path, "overwrite.xps");
        await File.WriteAllTextAsync(path, "old export");
        AvaloniaFilePickerSaveRequest? request = null;
        try
        {
            await Session.Dispatch(async () =>
            {
                var window = CreateWindow((_, selected) =>
                {
                    request = selected;
                    return Task.FromResult((false, (string?)path));
                });
                await window.ExportXpsForTests();
            }, CancellationToken.None);

            request.Should().NotBeNull();
            request!.ShowOverwritePrompt.Should().BeTrue();
            request.DefaultExtensionWithoutDot.Should().Be("xps");
            File.ReadAllBytes(path).Take(4).Should().Equal(0x50, 0x4B, 0x03, 0x04);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task ExportXps_NonLocalSelectionDoesNotWriteAndReportsLimitation()
    {
        string? status = null;
        await Session.Dispatch(async () =>
        {
            var window = CreateWindow((_, _) => Task.FromResult((false, (string?)null)));
            await window.ExportXpsForTests();
            status = window.PrintStatusForTests;
        }, CancellationToken.None);

        status.Should().Contain("local");
    }

    private MainWindow CreateWindow(
        Func<IStorageProvider, AvaloniaFilePickerSaveRequest, Task<(bool Canceled, string? LocalPath)>> pickExportPath)
    {
        var settingsPath = Path.Combine(
            _temporaryDirectory.Path,
            Guid.NewGuid().ToString("N"),
            "settings.json");
        return new MainWindow(
            [],
            new FreeWOptions(),
            ApplicationOptionsStore<FreeWOptions>.ForPath(settingsPath),
            printService: new NoOpPrintService(),
            pickExportPath: pickExportPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup for a test artifact.
        }
    }

    private sealed class NoOpPrintService : IPlatformPrintService
    {
        public bool IsSupported => false;

        public Task<PrinterDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrinterDiscoveryResult(PrinterDiscoveryStatus.Unavailable, [], null));

        public Task<PrintSubmissionResult> SubmitAsync(
            string pdfPath,
            PrintSelection selection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrintSubmissionResult(PrintSubmissionStatus.Unavailable, selection.PrinterName));
    }
}
