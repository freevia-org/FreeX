using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Free.Shared.AppServices;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R139-shared-clipboard-images (clipboard-drawing-object-no-os-clipboard-write): before this fix,
/// Ctrl+C on a selected chart/shape/picture/text box only ever populated the in-process
/// _drawingObjectClipboard (see R91_ObjectClipboardCopyPasteTests) -- ExecuteCopy's drawing-object
/// branch returned before any code that calls SetClipboardDataWithRetry/_platformClipboard.WriteAsync,
/// so pasting into any OTHER application (or a second FreeX window) was a total, silent no-op: nothing
/// was ever placed on the real OS clipboard. These tests drive the REAL product entry point
/// (CopyBtn_Click on a live MainWindow) and inject a fake IPlatformClipboard (MainWindow's existing,
/// already-supported constructor seam) so the assertions read exactly what production code wrote,
/// without touching the developer's/CI machine's actual OS clipboard.
/// </summary>
public sealed class R139_DrawingObjectClipboardOsImageTests
{
    [Fact]
    public void CopySelectedChart_PlacesRenderedImageOnOsClipboard()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var fakeClipboard = new FakePlatformClipboard();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance,
                platformClipboard: fakeClipboard);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var anchorCell = new CellAddress(sheet.Id, 1, 1);
                var chart = new ChartModel
                {
                    Type = ChartType.Column,
                    DataRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
                    Title = "Sales",
                    Left = 10,
                    Top = 10,
                    Width = 240,
                    Height = 160,
                };
                sheet.Charts.Add(chart);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = chart.Id;
                grid.SelectedObjectKind = ObjectKind.Chart;

                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();

                fakeClipboard.WriteCount.Should().BeGreaterThan(
                    0,
                    "copying a selected chart must place SOMETHING on the real OS clipboard");
                fakeClipboard.LastWritten.Should().NotBeNull();
                fakeClipboard.LastWritten!.Image.Should().NotBeNull(
                    "external apps (Paint, Word, a browser, another FreeX instance) need a picture flavor to paste");
                var image = fakeClipboard.LastWritten!.Image!;
                image.PixelWidth.Should().BeGreaterThan(0);
                image.PixelHeight.Should().BeGreaterThan(0);
                DecodePng(image.PngBytes).PixelWidth.Should().BeGreaterThan(0);
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void CopySelectedShape_PlacesRenderedImageOnOsClipboard()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var fakeClipboard = new FakePlatformClipboard();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance,
                platformClipboard: fakeClipboard);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var anchorCell = new CellAddress(sheet.Id, 1, 1);
                var shape = new DrawingShapeModel
                {
                    Kind = DrawingShapeKind.Rectangle,
                    Anchor = anchorCell,
                    Width = 100,
                    Height = 60,
                    HasFill = true,
                    FillColor = new CellColor(0x11, 0x22, 0x33),
                    ShapeText = "Hello",
                };
                sheet.DrawingShapes.Add(shape);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = shape.Id;
                grid.SelectedObjectKind = ObjectKind.Shape;

                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();

                fakeClipboard.LastWritten.Should().NotBeNull();
                fakeClipboard.LastWritten!.Image.Should().NotBeNull(
                    "a copied shape must also carry a picture flavor for external paste");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void CopySelectedPicture_PreservesTransparencyOnOsClipboardImage()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var fakeClipboard = new FakePlatformClipboard();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance,
                platformClipboard: fakeClipboard);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var anchorCell = new CellAddress(sheet.Id, 1, 1);
                var picture = new PictureModel
                {
                    Kind = PictureKind.Image,
                    Anchor = anchorCell,
                    Width = 40,
                    Height = 40,
                    ImageBytes = BuildFullyTransparentPngBytes(4, 4),
                    ContentType = "image/png",
                };
                sheet.Pictures.Add(picture);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = picture.Id;
                grid.SelectedObjectKind = ObjectKind.Picture;

                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();

                fakeClipboard.LastWritten.Should().NotBeNull();
                fakeClipboard.LastWritten!.Image.Should().NotBeNull();
                var alpha = ReadTopLeftAlpha(fakeClipboard.LastWritten!.Image!.PngBytes);
                alpha.Should().Be(0, "a fully transparent source picture must stay transparent on the clipboard");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    /// <summary>
    /// Sibling/no-regression proof: the OS-clipboard image write added above must not disturb the
    /// pre-existing internal FreeX-to-FreeX paste path (<see cref="_drawingObjectClipboard"/> /
    /// DuplicateDrawingObjectCommand) that R91_ObjectClipboardCopyPasteTests already covers -- this
    /// repeats that same duplicate-then-paste assertion but with the fake OS clipboard installed, so
    /// a regression that made the new OS-clipboard write throw or otherwise short-circuit
    /// TryCopySelectedDrawingObject before it reaches _drawingObjectClipboard.TryCapture would fail
    /// this test even though R91's own tests (which use the real, more forgiving default clipboard)
    /// might still pass.
    /// </summary>
    [Fact]
    public void CopyThenPaste_WithChartSelected_StillDuplicatesTheChart_WithFakeOsClipboardInstalled()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var fakeClipboard = new FakePlatformClipboard();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance,
                platformClipboard: fakeClipboard);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var anchorCell = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(anchorCell, new NumberValue(99));
                var chart = new ChartModel
                {
                    Type = ChartType.Column,
                    DataRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
                    Title = "Sales",
                    Left = 10,
                    Top = 10,
                };
                sheet.Charts.Add(chart);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = chart.Id;
                grid.SelectedObjectKind = ObjectKind.Chart;

                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();
                InvokeClickHandler(window, "PasteBtn_Click");
                PumpDispatcher();

                sheet.Charts.Should().HaveCount(2, "Ctrl+V on a copied chart must still duplicate it internally");
                sheet.Charts.Should().Contain(c => c.Id == chart.Id);
                sheet.GetCell(anchorCell)!.Value.Should().Be(new NumberValue(99));
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    private static byte[] BuildFullyTransparentPngBytes(int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (visual.RenderOpen())
        {
            // Deliberately draw nothing -- a fresh Pbgra32 render target is fully transparent
            // (alpha 0) everywhere, which is exactly the case this test needs to round-trip.
        }
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource DecodePng(byte[] pngBytes)
    {
        var decoder = BitmapDecoder.Create(
            new MemoryStream(pngBytes),
            BitmapCreateOptions.None,
            BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    private static byte ReadTopLeftAlpha(byte[] pngBytes)
    {
        var frame = DecodePng(pngBytes);
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        // BGRA byte order -- alpha is the 4th byte of the first pixel.
        return pixels[3];
    }

    private static void InvokeClickHandler(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            [typeof(object), typeof(RoutedEventArgs)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [window, new RoutedEventArgs()]);
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
