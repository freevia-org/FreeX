using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Editing;
using FreeX.App.Services;
using FreeX.Core.Model;
using SkiaSharp;

namespace FreeX.App.Avalonia.Tests;

// Routed exclusively to CaptureTests Batch6: its headless windowing uses the production Skia
// framebuffer. The ordinary Avalonia test host intentionally cannot encode rendered visuals.
[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class ChartClipboardPixelTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CopyOrCutChart_PublishesSeriesPixelsAndPreservesInternalObjectPaste(bool cut) =>
        Run(async (window, clipboard) =>
        {
            var sheet = window.Session.ActiveSheet;
            var chart = AddChart(sheet, sheet);
            await CopyAsync(window, chart, cut);

            var image = clipboard.Current!.Image;
            image.Should().NotBeNull();
            image!.PixelWidth.Should().Be(320);
            image.PixelHeight.Should().Be(240);
            CountSeriesPixels(image.PngBytes).Should().BeGreaterThan(1000,
                "the picture must contain plotted bars, not a default rectangle with the chart title");
            clipboard.Current.GetText(WorkbookClipboardSession.MarkerFormatName,
                PlatformClipboardFormatScope.Application).Should().NotBeNullOrWhiteSpace();

            window.SelectCellForTest(new CellAddress(sheet.Id, 12, 12));
            await ShortcutAsync(window, Key.V);
            sheet.Pictures.Should().BeEmpty("FreeX must paste its chart object rather than its external PNG flavor");
            sheet.Charts.Should().HaveCount(cut ? 1 : 2);
            sheet.Charts.Last().Type.Should().Be(ChartType.Column);
        });

    [Fact]
    public Task CopyChart_ChangesSeriesPixelsAfterRelativeDataChanges() =>
        Run(async (window, clipboard) =>
        {
            var sheet = window.Session.ActiveSheet;
            var chart = AddChart(sheet, sheet);
            await CopyAsync(window, chart);
            var before = clipboard.Current!.Image!.PngBytes;
            var originalCount = CountSeriesPixels(before);

            // The maximum stays 40, so the scale stays fixed while the first bar grows from 10 to 30.
            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(30));
            await CopyAsync(window, chart);
            var after = clipboard.Current!.Image!.PngBytes;
            after.Should().NotEqual(before);
            CountSeriesPixels(after).Should().BeGreaterThan(originalCount + 500);
        });

    [Fact]
    public Task CopyChart_ResolvesOffscreenDataFromReferencedSheet() =>
        Run(async (window, clipboard) =>
        {
            var destination = window.Session.ActiveSheet;
            var source = window.Session.Workbook.AddSheet("Source");
            var chart = AddChart(destination, source, startRow: 1000);
            await CopyAsync(window, chart);
            var original = clipboard.Current!.Image!.PngBytes;
            CountSeriesPixels(original).Should().BeGreaterThan(1000);

            // Identical coordinates on the chart's own sheet must never replace its source cells.
            destination.SetCell(new CellAddress(destination.Id, 1001, 2), new NumberValue(5000));
            await CopyAsync(window, chart);
            clipboard.Current!.Image!.PngBytes.Should().Equal(original);

            source.SetCell(new CellAddress(source.Id, 1001, 2), new NumberValue(30));
            await CopyAsync(window, chart);
            clipboard.Current!.Image!.PngBytes.Should().NotEqual(original);
        });

    private static Task Run(Func<MainWindow, Clipboard, Task> test) => Session.Dispatch(async () =>
    {
        var clipboard = new Clipboard();
        var window = new MainWindow([], WorkbookShareSheetServiceFactory.Create("macOS Share Sheet"),
            WorkbookFileAccessServiceFactory.Create(),
            PlatformPrintServiceSelector.Select(windowsFactory: null,
                cupsFactory: static () => new CupsPrintService(discoveryMode: CupsPrinterDiscoveryMode.DestinationNames)),
            clipboard);
        try
        {
            var sheet = window.Session.ActiveSheet;
            sheet.Charts.Clear();
            sheet.Pictures.Clear();
            sheet.DrawingShapes.Clear();
            sheet.TextBoxes.Clear();
            await test(window, clipboard);
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
        return true;
    }, CancellationToken.None);

    private static ChartModel AddChart(Sheet destination, Sheet source, uint startRow = 1)
    {
        source.SetCell(new CellAddress(source.Id, startRow, 1), new TextValue("Category"));
        source.SetCell(new CellAddress(source.Id, startRow, 2), new TextValue("Sales"));
        for (uint i = 0; i < 3; i++)
        {
            source.SetCell(new CellAddress(source.Id, startRow + i + 1, 1), new TextValue($"Item {i}"));
            source.SetCell(new CellAddress(source.Id, startRow + i + 1, 2), new NumberValue(10 * (1 << (int)i)));
        }
        var chart = new ChartModel
        {
            Title = "Sales", Type = ChartType.Column, Width = 320, Height = 240, ShowLegend = false,
            DataRange = new GridRange(new CellAddress(source.Id, startRow, 1), new CellAddress(source.Id, startRow + 3, 2)),
            SeriesFormats = [new ChartSeriesFormat(0, FillColor: new CellColor(17, 90, 150))],
        };
        destination.Charts.Add(chart);
        return chart;
    }

    private static Task CopyAsync(MainWindow window, ChartModel chart, bool cut = false)
    {
        var sheet = window.Session.ActiveSheet;
        window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Chart, chart.Id, new CellAddress(sheet.Id, 1, 1));
        return ShortcutAsync(window, cut ? Key.X : Key.C);
    }

    private static Task ShortcutAsync(MainWindow window, Key key) => window.RaiseKeyDownForTest(new KeyEventArgs
    {
        Key = key,
        KeyModifiers = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control,
    });

    private static int CountSeriesPixels(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        bitmap.Should().NotBeNull("the clipboard image must be a decodable PNG");
        return bitmap!.Pixels.Count(pixel => pixel == new SKColor(17, 90, 150));
    }

    private sealed class Clipboard : IPlatformClipboard
    {
        public PlatformClipboardContent? Current { get; private set; }

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current is { } content
                ? PlatformClipboardReadResult<PlatformClipboardContent>.Success(content)
                : PlatformClipboardReadResult<PlatformClipboardContent>.Empty());

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content, CancellationToken cancellationToken = default)
        {
            Current = content;
            return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        }

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(CancellationToken cancellationToken = default)
        {
            Current = null;
            return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        }
    }
}
