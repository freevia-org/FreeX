using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R148-clipboard-interop-F3: <c>CutSelectedRangeToClipboardAsync</c> placed only plain text on the
/// real OS clipboard (<c>new PlatformClipboardContent(Text: cutResult.Text)</c>) -- unlike
/// <c>CopySelectedRangeToClipboardAsync</c> in the very same file, which builds the CF_HTML fragment,
/// a CSV-typed payload, and a rendered PNG picture flavor for the identical shape of selection. So
/// cutting a formatted range on Linux/macOS and pasting into any HTML-aware destination (Word,
/// LibreOffice, a browser) dropped every non-text flavor, unlike Copy in the same app or Cut on the
/// WPF host (whose <c>ExecuteCopy(bool isCut)</c> shares one code path for both). These tests drive
/// the real product entry point (the Ctrl+X key route) with a fake <see cref="IPlatformClipboard"/>
/// injected through MainWindow's existing internal constructor seam, mirroring the R139 drawing-object
/// clipboard tests' pattern.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R148_ClipboardCutFormatParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Cut_PlacesHtmlCsvAndImageFlavors_OnTheOsClipboard_LikeCopyDoes()
    {
        await Session.Dispatch(async () =>
        {
            var fakeClipboard = new FakePlatformClipboard();
            var window = CreateWindow(fakeClipboard);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("CutFormatFixture");
                window.Session.SelectSheet(sheet.Id);
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Widget"));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
                window.Session.UpdateViewportSize(881, 1440);

                var range = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 2, 2));
                window.Session.SelectRange(range);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.X,
                    KeyModifiers = KeyModifiers.Control,
                });

                fakeClipboard.WriteCount.Should().BeGreaterThan(
                    0,
                    "Ctrl+X must place SOMETHING on the real OS clipboard");
                var written = fakeClipboard.LastWritten;
                written.Should().NotBeNull();

                written!.Text.Should().NotBeNullOrEmpty(
                    "the plain-text payload must still be present, unchanged by adding the other flavors");
                written.Text.Should().Contain("Widget").And.Contain("10");

                var htmlFormat = written.CustomData.Should()
                    .Contain(d => d.Format.Name == "text/html" && !string.IsNullOrEmpty(d.Text))
                    .Subject;
                written.CustomData.Should().Contain(
                    d => d.Format.Name == "HTML Format" && !string.IsNullOrEmpty(d.Text),
                    "Cut must include the Windows CF_HTML variant alongside the 'text/html' fragment, exactly like Copy");
                written.CustomData.Should().Contain(
                    d => d.Format.Name == "text/csv" && !string.IsNullOrEmpty(d.Text),
                    "Cut must include a CSV-typed payload, exactly like Copy");
                written.CustomData.Should().Contain(
                    d => d.Format.Name == "Csv" && !string.IsNullOrEmpty(d.Text),
                    "Cut must include the Windows 'Csv' CSV variant, exactly like Copy");
                written.Image.Should().NotBeNull(
                    "Cut must include a rendered picture flavor for image-only paste destinations, exactly like Copy");
                written.Image!.PngBytes.Should().NotBeEmpty();

                _ = htmlFormat;
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    // ── No-regression sibling: Copy must still place the identical set of flavors it always has --
    // the fix only touches Cut's own OS-clipboard write, not Copy's.

    [Fact]
    public async Task Copy_StillPlacesHtmlCsvAndImageFlavors_OnTheOsClipboard_Unchanged()
    {
        await Session.Dispatch(async () =>
        {
            var fakeClipboard = new FakePlatformClipboard();
            var window = CreateWindow(fakeClipboard);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("CopyFormatFixture");
                window.Session.SelectSheet(sheet.Id);
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Widget"));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
                window.Session.UpdateViewportSize(881, 1440);

                var range = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 2, 2));
                window.Session.SelectRange(range);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.C,
                    KeyModifiers = KeyModifiers.Control,
                });

                var written = fakeClipboard.LastWritten;
                written.Should().NotBeNull();
                written!.CustomData.Should().Contain(d => d.Format.Name == "text/html" && !string.IsNullOrEmpty(d.Text));
                written.CustomData.Should().Contain(d => d.Format.Name == "text/csv" && !string.IsNullOrEmpty(d.Text));
                written.Image.Should().NotBeNull();
                written.Image!.PngBytes.Should().NotBeEmpty();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static MainWindow CreateWindow(IPlatformClipboard platformClipboard) => new(
        [],
        WorkbookShareSheetServiceFactory.Create("macOS Share Sheet"),
        WorkbookFileAccessServiceFactory.Create(),
        PlatformPrintServiceSelector.Select(
            windowsFactory: null,
            cupsFactory: static () => new CupsPrintService(discoveryMode: CupsPrinterDiscoveryMode.DestinationNames)),
        platformClipboard);

    private sealed class FakePlatformClipboard : IPlatformClipboard
    {
        public PlatformClipboardContent? LastWritten { get; private set; }
        public int WriteCount { get; private set; }

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardReadResult<PlatformClipboardContent>.Empty());

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default)
        {
            LastWritten = content;
            WriteCount++;
            return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        }

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }
}
