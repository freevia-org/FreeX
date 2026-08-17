using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R139-shared-clipboard-images (clipboard-drawing-object-no-os-clipboard-write): before this fix,
/// Ctrl+C on a selected chart/shape/picture/text box on the Avalonia shell only ever populated the
/// in-process _drawingObjectClipboard (see R91_AvaloniaObjectClipboardCopyPasteTests) -- it never
/// touched the real OS clipboard, so pasting into any OTHER application (or a second FreeX window)
/// was a total, silent no-op. These tests drive the REAL product entry point (the Ctrl+C key route)
/// with a fake <see cref="IPlatformClipboard"/> injected through MainWindow's existing internal
/// constructor seam, mirroring the WPF host's identical R139 test.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R139_DrawingObjectClipboardOsImageTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public async Task CopySelectedDrawingObject_PlacesRenderedImageOnOsClipboard(SelectionPaneObjectKind kind)
    {
        await Session.Dispatch(async () =>
        {
            var fakeClipboard = new FakePlatformClipboard();
            var window = CreateWindow(fakeClipboard);
            try
            {
                var sheet = window.Session.ActiveSheet;
                ClearSampleDrawingObjects(sheet);
                var anchor = new CellAddress(sheet.Id, 2, 2);
                var objectId = AddObject(sheet, kind, anchor);
                window.SelectDrawingObjectForTest(kind, objectId, anchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.C,
                    KeyModifiers = KeyModifiers.Control,
                });

                fakeClipboard.WriteCount.Should().BeGreaterThan(
                    0,
                    "copying a selected drawing object must place SOMETHING on the real OS clipboard");
                fakeClipboard.LastWritten.Should().NotBeNull();
                fakeClipboard.LastWritten!.Image.Should().NotBeNull(
                    "external apps (or another FreeX instance) need a picture flavor to paste");
                fakeClipboard.LastWritten!.Image!.PngBytes.Should().NotBeEmpty();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CopySelectedPicture_PreservesTransparencyOnOsClipboardImage()
    {
        await Session.Dispatch(async () =>
        {
            var fakeClipboard = new FakePlatformClipboard();
            var window = CreateWindow(fakeClipboard);
            try
            {
                var sheet = window.Session.ActiveSheet;
                ClearSampleDrawingObjects(sheet);
                var anchor = new CellAddress(sheet.Id, 2, 2);
                var picture = new PictureModel
                {
                    Kind = PictureKind.Image,
                    Anchor = anchor,
                    Width = 40,
                    Height = 40,
                    ImageBytes = BuildFullyTransparentPngBytes(4, 4),
                    ContentType = "image/png",
                };
                sheet.Pictures.Add(picture);
                window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Picture, picture.Id, anchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.C,
                    KeyModifiers = KeyModifiers.Control,
                });

                fakeClipboard.LastWritten.Should().NotBeNull();
                fakeClipboard.LastWritten!.Image.Should().NotBeNull();
                var alpha = ReadTopLeftAlpha(fakeClipboard.LastWritten!.Image!.PngBytes);
                alpha.Should().Be(0, "a fully transparent source picture must stay transparent on the clipboard");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sibling/no-regression proof: the OS-clipboard image write added above must not disturb the
    /// pre-existing internal FreeX-to-FreeX paste path that R91_AvaloniaObjectClipboardCopyPasteTests
    /// already covers -- this repeats that same duplicate-then-paste assertion with the fake OS
    /// clipboard installed, so a regression that made the new async OS-clipboard write throw or
    /// otherwise short-circuit TryCopySelectedDrawingObjectAsync before it reaches
    /// _drawingObjectClipboard.TryCaptureExisting would fail this test even though R91's own tests
    /// (which use the real production AvaloniaPlatformClipboard) might still pass.
    /// </summary>
    [Fact]
    public async Task CopyThenPaste_WithChartSelected_StillDuplicatesTheChart_WithFakeOsClipboardInstalled()
    {
        await Session.Dispatch(async () =>
        {
            var fakeClipboard = new FakePlatformClipboard();
            var window = CreateWindow(fakeClipboard);
            try
            {
                var sheet = window.Session.ActiveSheet;
                ClearSampleDrawingObjects(sheet);
                var anchor = new CellAddress(sheet.Id, 2, 2);
                var destination = new CellAddress(sheet.Id, 12, 12);
                sheet.SetCell(anchor, new NumberValue(99));
                var objectId = AddObject(sheet, SelectionPaneObjectKind.Chart, anchor);
                window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Chart, objectId, anchor);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.C,
                    KeyModifiers = KeyModifiers.Control,
                });

                window.SelectCellForTest(destination);
                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.V,
                    KeyModifiers = KeyModifiers.Control,
                });

                sheet.Charts.Should().HaveCount(2, "Ctrl+V on a copied chart must still duplicate it internally");
                sheet.Charts.Should().Contain(c => c.Id == objectId);
                sheet.GetCell(anchor)!.Value.Should().Be(new NumberValue(99));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static MainWindow CreateWindow(IPlatformClipboard platformClipboard) => new(
        [],
        WorkbookShareSheetServiceFactory.Create("macOS Share Sheet"),
        WorkbookFileAccessServiceFactory.Create(),
        PlatformPrintServiceSelector.Select(
            windowsFactory: null,
            cupsFactory: static () => new CupsPrintService(discoveryMode: CupsPrinterDiscoveryMode.DestinationNames)),
        platformClipboard);

    private static void ClearSampleDrawingObjects(Sheet sheet)
    {
        sheet.Pictures.Clear();
        sheet.TextBoxes.Clear();
        sheet.DrawingShapes.Clear();
        sheet.Charts.Clear();
    }

    private static Guid AddObject(Sheet sheet, SelectionPaneObjectKind kind, CellAddress anchor)
    {
        switch (kind)
        {
            case SelectionPaneObjectKind.Chart:
                var chart = new ChartModel
                {
                    Title = "Sales",
                    Type = ChartType.Column,
                    DataRange = new GridRange(anchor, new CellAddress(sheet.Id, 4, 3)),
                    Left = 10,
                    Top = 10,
                    Width = 240,
                    Height = 160,
                };
                sheet.Charts.Add(chart);
                return chart.Id;
            case SelectionPaneObjectKind.Shape:
                var shape = new DrawingShapeModel
                {
                    Name = "SalesShape",
                    Anchor = anchor,
                    Width = 100,
                    Height = 60,
                    HasFill = true,
                    FillColor = new CellColor(0x11, 0x22, 0x33),
                    ShapeText = "Hello",
                };
                sheet.DrawingShapes.Add(shape);
                return shape.Id;
            case SelectionPaneObjectKind.Picture:
                var picture = new PictureModel
                {
                    Name = "SalesPicture",
                    Anchor = anchor,
                    Kind = PictureKind.Image,
                    Width = 40,
                    Height = 40,
                    ImageBytes = BuildFullyTransparentPngBytes(4, 4),
                    ContentType = "image/png",
                };
                sheet.Pictures.Add(picture);
                return picture.Id;
            case SelectionPaneObjectKind.TextBox:
                var textBox = new TextBoxModel
                {
                    Name = "SalesTextBox",
                    Anchor = anchor,
                    Text = "Sales",
                    Width = 100,
                    Height = 40,
                };
                sheet.TextBoxes.Add(textBox);
                return textBox.Id;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static byte[] BuildFullyTransparentPngBytes(int width, int height)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        // A freshly allocated SKBitmap is zero-filled (fully transparent) -- exactly what this test needs.
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte ReadTopLeftAlpha(byte[] pngBytes)
    {
        using var bitmap = SkiaSharp.SKBitmap.Decode(pngBytes);
        return bitmap.GetPixel(0, 0).Alpha;
    }

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
