using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using FreeX.App.Presentation.Editing;
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
    // Chart and shape/text images use the real Skia framebuffer in CaptureTests Batch6.
    [InlineData(SelectionPaneObjectKind.Picture)]
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
                fakeClipboard.LastWritten.GetText(
                    WorkbookClipboardSession.MarkerFormatName, PlatformClipboardFormatScope.Application)
                    .Should().NotBeNullOrWhiteSpace();
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

    [Fact]
    public Task CopyWithoutRenderablePreview_PublishesMarkerAndReplacesPendingCellClipboard() =>
        WithClipboardAsync(async (window, clipboard) =>
        {
            var sheet = window.Session.ActiveSheet;
            var anchor = new CellAddress(sheet.Id, 2, 2);
            sheet.SetCell(anchor, new TextValue("previous cell copy"));
            window.SelectCellForTest(anchor);
            await ShortcutAsync(window, Key.C);
            window.Session.HasPendingClipboardMarquee.Should().BeTrue();

            var picture = new PictureModel { Kind = PictureKind.Image, Anchor = anchor, Width = 40, Height = 40 };
            sheet.Pictures.Add(picture);
            window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Picture, picture.Id, anchor);
            await ShortcutAsync(window, Key.C);

            window.Session.HasPendingClipboardMarquee.Should().BeFalse();
            clipboard.LastWritten!.Image.Should().BeNull();
            clipboard.LastWritten.GetText(
                WorkbookClipboardSession.MarkerFormatName, PlatformClipboardFormatScope.Application)
                .Should().NotBeNullOrWhiteSpace();

            window.SelectCellForTest(new CellAddress(sheet.Id, 12, 12));
            await ShortcutAsync(window, Key.V);
            sheet.Pictures.Should().HaveCount(2);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PasteAfterExternalCopy_UsesLatestTextOrImage_AndDoesNotMoveOldShape(bool externalImage) =>
        WithClipboardAsync(async (window, clipboard) =>
        {
            var sheet = window.Session.ActiveSheet;
            var anchor = new CellAddress(sheet.Id, 2, 2);
            var destination = new CellAddress(sheet.Id, 12, 12);
            var shape = AddObject(sheet, SelectionPaneObjectKind.Shape, anchor);
            window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Shape, shape, anchor);
            await ShortcutAsync(window, Key.X);

            clipboard.Current = externalImage
                ? new PlatformClipboardContent(Image: new PlatformClipboardImage(BuildFullyTransparentPngBytes(4, 4), 4, 4))
                : new PlatformClipboardContent(Text: "new external clipboard");
            window.SelectCellForTest(destination);
            await ShortcutAsync(window, Key.V);

            sheet.DrawingShapes.Should().ContainSingle().Which.Id.Should().Be(shape);
            if (externalImage)
                sheet.Pictures.Should().ContainSingle().Which.Anchor.Should().Be(destination);
            else
                sheet.GetCell(destination)!.Value.Should().Be(new TextValue("new external clipboard"));
        });

    [Theory]
    [InlineData(PlatformClipboardReadStatus.Failed)]
    [InlineData(PlatformClipboardReadStatus.Unavailable)]
    public Task PasteAfterClipboardReadFailure_LeavesDrawingAvailableForRetry(PlatformClipboardReadStatus failure) =>
        WithClipboardAsync(async (window, clipboard) =>
        {
            var sheet = window.Session.ActiveSheet;
            var anchor = new CellAddress(sheet.Id, 2, 2);
            var shape = AddObject(sheet, SelectionPaneObjectKind.Shape, anchor);
            window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Shape, shape, anchor);
            await ShortcutAsync(window, Key.C);

            clipboard.ReadFailure = failure;
            window.SelectCellForTest(new CellAddress(sheet.Id, 12, 12));
            await ShortcutAsync(window, Key.V);
            sheet.DrawingShapes.Should().ContainSingle();

            clipboard.ReadFailure = null;
            await ShortcutAsync(window, Key.V);
            sheet.DrawingShapes.Should().HaveCount(2);
        });

    [Fact]
    public Task PasteWaitsForThePendingDrawingClipboardWrite() =>
        WithClipboardAsync(async (window, clipboard) =>
        {
            var sheet = window.Session.ActiveSheet;
            var anchor = new CellAddress(sheet.Id, 2, 2);
            var shape = AddObject(sheet, SelectionPaneObjectKind.Shape, anchor);
            window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Shape, shape, anchor);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            clipboard.WriteGate = release.Task;
            var copy = ShortcutAsync(window, Key.C);
            var paste = ShortcutAsync(window, Key.V);
            try
            {
                sheet.DrawingShapes.Should().ContainSingle();
            }
            finally
            {
                release.TrySetResult();
                await Task.WhenAll(copy, paste);
            }
            sheet.DrawingShapes.Should().HaveCount(2);
        });

    [Fact]
    public Task OlderPasteRead_DoesNotClearNewerDrawingCopy() =>
        WithClipboardAsync(async (window, clipboard) =>
        {
            var sheet = window.Session.ActiveSheet;
            var anchor = new CellAddress(sheet.Id, 2, 2);
            var first = AddObject(sheet, SelectionPaneObjectKind.Shape, anchor);
            window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Shape, first, anchor);
            await ShortcutAsync(window, Key.C);

            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            clipboard.ReadGate = release.Task;
            var olderPaste = ShortcutAsync(window, Key.V);
            try
            {
                olderPaste.IsCompleted.Should().BeFalse();
                clipboard.ReadGate = null;
                var newer = AddObject(sheet, SelectionPaneObjectKind.TextBox, anchor);
                window.SelectDrawingObjectForTest(SelectionPaneObjectKind.TextBox, newer, anchor);
                await ShortcutAsync(window, Key.C);
            }
            finally
            {
                release.TrySetResult();
                await olderPaste;
            }

            await ShortcutAsync(window, Key.V);
            sheet.DrawingShapes.Should().ContainSingle();
            sheet.Pictures.Should().BeEmpty();
            sheet.TextBoxes.Should().HaveCount(2);
        });

    private static Task WithClipboardAsync(Func<MainWindow, FakePlatformClipboard, Task> run) =>
        Session.Dispatch(async () =>
        {
            var clipboard = new FakePlatformClipboard();
            var window = CreateWindow(clipboard);
            try
            {
                ClearSampleDrawingObjects(window.Session.ActiveSheet);
                await run(window, clipboard);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);

    private static Task ShortcutAsync(MainWindow window, Key key) =>
        window.RaiseKeyDownForTest(new KeyEventArgs
        {
            Key = key,
            KeyModifiers = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control,
        });

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
        public PlatformClipboardContent? Current { get; set; }
        public PlatformClipboardReadStatus? ReadFailure { get; set; }
        public Task? ReadGate { get; set; }
        public Task? WriteGate { get; set; }
        public int WriteCount { get; private set; }

        public async ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default)
        {
            if (ReadFailure is { } status)
                return new PlatformClipboardReadResult<PlatformClipboardContent>(status);

            var payload = new PlatformClipboardContent(
                Text: request.IncludeText ? Current?.Text : null,
                Image: request.IncludeImage ? Current?.Image : null,
                CustomData: Current?.CustomData.Where(data => request.CustomFormats.Contains(data.Format)).ToArray());
            if (ReadGate is { } gate)
                await gate.WaitAsync(cancellationToken);

            return payload.IsEmpty
                ? PlatformClipboardReadResult<PlatformClipboardContent>.Empty()
                : PlatformClipboardReadResult<PlatformClipboardContent>.Success(payload);
        }

        public async ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default)
        {
            if (WriteGate is { } gate)
                await gate.WaitAsync(cancellationToken);

            Current = LastWritten = content;
            WriteCount++;
            return PlatformClipboardWriteResult.Success();
        }

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default)
        {
            Current = null;
            return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        }
    }
}
