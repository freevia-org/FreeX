using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R91-io-clipboard-image-formats-5-3 (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs,
/// ExecuteCopy/TryRenderClipboardRangeBitmap).
///
/// Before the fix: a plain Ctrl+C of a cell range placed only Text, CF_HTML, and CSV on the OS
/// clipboard -- never any picture flavor (CF_BITMAP/CF_ENHMETAFILE) -- unlike real Excel, which
/// always places a rendered picture alongside those on every range copy. Pasting that same copy
/// into an image-only destination (Paint, an image well, an image-only paste target) got nothing at
/// all from FreeX where it would get a picture from Excel. There was also no "Copy as Picture"
/// command at all.
///
/// After the fix, ExecuteCopy also renders a simple bordered-grid Bitmap of the copied cells'
/// display text and places it under DataFormats.Bitmap on every plain range copy -- the "at minimum
/// offer a picture flavour" bar from the round summary (the full ribbon "Copy as Picture" command
/// with Appearance/Format options remains a separate, larger follow-up).
/// </summary>
public sealed class R91_CopyPictureClipboardFlavorTests
{
    [Fact]
    public void ExecuteCopy_PlainRangeCopy_AlsoPlacesABitmapClipboardFlavor()
    {
        StaTestRunner.Run(() =>
        {
            var clipboard = new RecordingClipboard();
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow(clipboard);
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(a1, new TextValue("Hello"));

                window.SheetGrid.SelectedRange = new GridRange(a1, a1);
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                clipboard.LastWritten.Should().NotBeNull();
                var image = clipboard.LastWritten!.Image;
                image.Should().NotBeNull(
                    "a plain range copy must always place a picture flavor on the clipboard, " +
                    "matching real Excel, so an image-only destination still gets something");
                image!.PngBytes.Should().NotBeEmpty();
                image.PixelWidth.Should().BeGreaterThan(0);
                image.PixelHeight.Should().BeGreaterThan(0);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: adding the new Bitmap flavor must not disturb the pre-existing plain
    // text payload that ExecutePaste's internal round trip and every external-text destination
    // still depend on.
    [Fact]
    public void ExecuteCopy_PlainRangeCopy_StillPlacesPlainTextUnaffectedByTheNewBitmapFlavor()
    {
        StaTestRunner.Run(() =>
        {
            var clipboard = new RecordingClipboard();
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow(clipboard);
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(a1, new TextValue("Hello"));

                window.SheetGrid.SelectedRange = new GridRange(a1, a1);
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                clipboard.LastWritten.Should().NotBeNull();
                clipboard.LastWritten!.Text.Should().Be("Hello");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private sealed class RecordingClipboard : IPlatformClipboard
    {
        public PlatformClipboardContent? LastWritten { get; private set; }

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(PlatformClipboardReadResult<PlatformClipboardContent>.Empty());
        }

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastWritten = content;
            return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        }

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastWritten = null;
            return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        }
    }
}
