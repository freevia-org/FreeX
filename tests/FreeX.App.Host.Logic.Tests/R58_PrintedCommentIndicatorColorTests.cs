using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R58-render-comment-indicator-6-4: DrawDisplayedComments (PrintRenderer.Comments.cs) computed a
/// single "indicator" brush once, outside the per-overlay loop, hardcoded to note-red (192,0,0) (or
/// black in black-and-white mode) with no branch on WorksheetDisplayedComment.Kind -- so a
/// ThreadedComment/Mixed overlay printed with the identical red corner triangle as a plain legacy
/// Note, even though the on-screen indicator (GridView.Rendering.CommentIndicatorBrush) deliberately
/// draws Note in red vs ThreadedComment/Mixed in purple #7C379E (124,55,158) to match Excel 365. The
/// fix selects the indicator brush per-overlay from overlay.Kind so the printed page matches what the
/// user actually saw on the sheet.
/// </summary>
public sealed class R58_PrintedCommentIndicatorColorTests
{
    private const double ColumnWidth = 60.0;
    private const double RowHeight = 20.0;

    [Fact]
    public void DrawDisplayedComments_ThreadedCommentOnly_UsesPurpleIndicatorNotNoteRed()
    {
        StaTestRunner.Run(() =>
        {
            var pixels = RenderIndicator(
                comments: new Dictionary<CellAddress, string>(),
                threadedComments: new Dictionary<CellAddress, ThreadedComment>
                {
                    [Address()] = new ThreadedComment("Reviewer note"),
                });

            var (red, green, blue) = SampleIndicatorPixel(pixels);

            // Pre-fix, the indicator brush was hardcoded to (192,0,0) regardless of kind, so this
            // would fail: a pure ThreadedComment overlay (no legacy note at the same address) would
            // still sample as note-red. Post-fix it must sample as the threaded-comment purple
            // #7C379E (124,55,158) used on-screen by GridView.Rendering.CommentIndicatorBrush.
            red.Should().BeInRange(114, 134, "ThreadedComment overlays must print the purple #7C379E indicator");
            green.Should().BeInRange(45, 65);
            blue.Should().BeInRange(148, 168);
        });
    }

    [Fact]
    public void DrawDisplayedComments_LegacyNoteOnly_StillUsesNoteRedIndicator()
    {
        // Sibling/no-regression case: an ordinary legacy note (the overwhelming majority of real
        // comments/notes) must still print the original note-red indicator after adding the
        // per-kind branch.
        StaTestRunner.Run(() =>
        {
            var pixels = RenderIndicator(
                comments: new Dictionary<CellAddress, string> { [Address()] = "Plain note" },
                threadedComments: new Dictionary<CellAddress, ThreadedComment>());

            var (red, green, blue) = SampleIndicatorPixel(pixels);

            red.Should().BeInRange(182, 202, "a plain legacy Note must still print the note-red indicator");
            green.Should().BeLessThan(40);
            blue.Should().BeLessThan(40);
        });
    }

    private static readonly SheetId TestSheetId = new(Guid.NewGuid());

    private static CellAddress Address() => new(TestSheetId, 1, 1);

    private static (byte Red, byte Green, byte Blue) SampleIndicatorPixel(byte[] pixels)
    {
        var width = (int)ColumnWidth;
        // The triangle spans [cellLeft+colWidth-7, cellTop] .. [cellLeft+colWidth, cellTop+7]; sample
        // just inside its top-right corner, well clear of the comment box drawn below/right of it.
        const int x = (int)(ColumnWidth - 2);
        const int y = 2;
        var i = (y * width + x) * 4;
        return (pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    private static byte[] RenderIndicator(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments)
    {
        var textOverlays = new List<PdfTextOverlay>();
        var kind = threadedComments.Count > 0
            ? CellCommentDisplayKind.ThreadedComment
            : CellCommentDisplayKind.Note;
        var text = threadedComments.Count > 0
            ? threadedComments.Values.Single().Text
            : comments.Values.Single();
        var displayedComments = new[]
        {
            new PageDisplayedCommentBlock(
                kind,
                text,
                [
                    new LayoutPoint(ColumnWidth - 7, 0),
                    new LayoutPoint(ColumnWidth, 0),
                    new LayoutPoint(ColumnWidth, 7),
                ],
                new LayoutRect(8, 8, 80, 48)),
        };

        var method = typeof(PrintRenderer).GetMethod(
            "DrawDisplayedComments",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        const int width = (int)ColumnWidth;
        const int height = (int)RowHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            method!.Invoke(null,
            [
                dc,
                textOverlays,
                displayedComments,
                false,
            ]);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }
}
