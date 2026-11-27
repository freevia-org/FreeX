using Avalonia.Input;
using Free.Shared.AppServices;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Model;
using SkiaSharp;

namespace FreeX.App.Avalonia.Tests;

// Uses only Batch6's isolated production Skia renderer, never native windows or the OS clipboard.
[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class DrawingClipboardTextPixelTests
{
    [Theory]
    [InlineData(SelectionPaneObjectKind.Shape, "IIII\nIIII\nIIII")]
    [InlineData(SelectionPaneObjectKind.TextBox, "IIII\nIIII\nIIII")]
    [InlineData(SelectionPaneObjectKind.Shape, "IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII")]
    [InlineData(SelectionPaneObjectKind.TextBox, "IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII IIII")]
    public Task CopyDrawing_PaintsExplicitAndWrappedTextLinesOnTransparentCanvas(
        SelectionPaneObjectKind kind, string text) => ChartClipboardPixelTests.Run(async (window, clipboard) =>
    {
        SelectDrawing(window, kind, text);
        await ChartClipboardPixelTests.ShortcutAsync(window, Key.C);
        clipboard.Current!.Image.Should().NotBeNull();
        using var bitmap = SKBitmap.Decode(clipboard.Current.Image!.PngBytes);
        bitmap.Should().NotBeNull();
        bitmap!.Width.Should().Be(88);
        bitmap.Height.Should().Be(88);

        CountInkBands(bitmap).Should().BeGreaterThanOrEqualTo(3,
            "explicit newlines and width wrapping must paint multiple visible lines");
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (x < 4 || x >= 84 || y < 4 || y >= 84)
                    bitmap.GetPixel(x, y).Alpha.Should().Be(0, "No Fill/No Line must preserve transparent margins");
            }
        }
        clipboard.Current.GetText(WorkbookClipboardSession.MarkerFormatName,
            PlatformClipboardFormatScope.Application).Should().NotBeNullOrWhiteSpace();
    });

    [Theory]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public Task CopyEmptyDrawing_WithNoFillOrLine_RemainsFullyTransparent(SelectionPaneObjectKind kind) =>
        ChartClipboardPixelTests.Run(async (window, clipboard) =>
        {
            SelectDrawing(window, kind, "");
            await ChartClipboardPixelTests.ShortcutAsync(window, Key.C);
            clipboard.Current!.Image.Should().NotBeNull();
            using var bitmap = SKBitmap.Decode(clipboard.Current.Image!.PngBytes);
            bitmap.Should().NotBeNull();
            bitmap!.Pixels.Should().OnlyContain(pixel => pixel.Alpha == 0);
        });

    [Theory]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public Task CopyFilledDrawing_PreservesFillBehindMultilineText(SelectionPaneObjectKind kind) =>
        ChartClipboardPixelTests.Run(async (window, clipboard) =>
        {
            SelectDrawing(window, kind, "First\nSecond", transparent: false);
            await ChartClipboardPixelTests.ShortcutAsync(window, Key.C);
            clipboard.Current!.Image.Should().NotBeNull();
            using var bitmap = SKBitmap.Decode(clipboard.Current.Image!.PngBytes);
            bitmap.Should().NotBeNull();
            bitmap!.GetPixel(3, 3).Should().Be(new SKColor(17, 90, 150));
        });

    private static void SelectDrawing(MainWindow window, SelectionPaneObjectKind kind, string text, bool transparent = true)
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = new CellAddress(sheet.Id, 2, 2);
        Guid id;
        if (kind == SelectionPaneObjectKind.Shape)
        {
            var shape = new DrawingShapeModel
            {
                Anchor = anchor, Width = 88, Height = 88, ShapeText = text,
                HasFill = !transparent, OutlineHasNoFill = transparent, FillColor = new CellColor(17, 90, 150),
            };
            sheet.DrawingShapes.Add(shape);
            id = shape.Id;
        }
        else
        {
            var textBox = new TextBoxModel
            {
                Anchor = anchor, Width = 88, Height = 88, Text = text,
                HasFill = !transparent, OutlineHasNoFill = transparent, FillColor = new CellColor(17, 90, 150),
            };
            sheet.TextBoxes.Add(textBox);
            id = textBox.Id;
        }
        window.SelectDrawingObjectForTest(kind, id, anchor);
    }

    private static int CountInkBands(SKBitmap bitmap)
    {
        var count = 0;
        var previousRowHadInk = false;
        for (var y = 0; y < bitmap.Height; y++)
        {
            var rowHasInk = false;
            for (var x = 0; x < bitmap.Width; x++)
                rowHasInk |= bitmap.GetPixel(x, y).Alpha > 0;
            if (rowHasInk && !previousRowHadInk)
                count++;
            previousRowHadInk = rowHasInk;
        }
        return count;
    }
}
