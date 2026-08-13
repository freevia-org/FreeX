using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.Pdf;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using SkiaSharp;
using AvaloniaColor = global::Avalonia.Media.Color;
using AvaloniaDrawing = global::Avalonia.Media.Drawing;
using AvaloniaDrawingGroup = global::Avalonia.Media.DrawingGroup;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewEmbeddedObjectTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task Dispatch(Action action) => Session.Dispatch(action, CancellationToken.None);

    [Fact]
    public async Task Body_flow_paints_decoded_icon_and_labelled_fallback_from_shared_plan()
    {
        await Dispatch(() =>
        {
            var iconObject = EmbeddedObject.Create(
                [1, 2, 3],
                "Excel.Sheet.12",
                new InlineImage(SolidPng(SKColors.Red), 24, 24) { AltText = "Workbook icon" },
                widthPt: 48,
                heightPt: 30);
            var fallbackObject = EmbeddedObject.Create(
                [4, 5, 6],
                "Acme.Package",
                widthPt: 54,
                heightPt: 32);
            var document = DocumentWithBodyObjects(iconObject, fallbackObject);
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(816, 1100));
            view.Arrange(new Rect(0, 0, 816, 1100));
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

            var items = view.EmbeddedObjectRenderItems.OrderBy(item => item.BlockIndex).ToArray();
            items.Should().HaveCount(2);
            items[0].HasDecodedIcon.Should().BeTrue();
            items[0].Plan.AccessibleName.Should().Be("Workbook icon");
            items[0].Rect.Width.Should().BeApproximately(48 * 96.0 / 72.0, 0.01);
            items[0].Rect.Height.Should().BeApproximately(30 * 96.0 / 72.0, 0.01);
            items[1].HasDecodedIcon.Should().BeFalse();
            items[1].Plan.Label.Should().Be("Acme.Package");
            items[1].Plan.BackgroundColorHex.Should().Be(EmbeddedObjectVisualPlanner.BackgroundColorHex);
            items[1].Plan.BorderColorHex.Should().Be(EmbeddedObjectVisualPlanner.BorderColorHex);

            view.GetPlacedForBlock(0).Should().ContainSingle()
                .Which.Ch.Should().Be('\0', "an embedded object occupies one body-flow caret position");
            view.GetPlacedForBlock(1).Should().ContainSingle()
                .Which.Ch.Should().Be('\0');

            var fallbackView = new DocumentView();
            fallbackView.LoadDocument(DocumentWithBodyObjects(fallbackObject));
            fallbackView.Measure(new Size(816, 1100));
            fallbackView.Arrange(new Rect(0, 0, 816, 1100));
            var fallbackRect = fallbackView.EmbeddedObjectRenderItems.Single().Rect;
            var drawingGroup = new AvaloniaDrawingGroup();
            using (var context = drawingGroup.Open())
                fallbackView.Render(context);
            var drawings = FlattenDrawings(drawingGroup).ToArray();
            drawings.OfType<GeometryDrawing>().Any(geometry =>
                geometry.Brush is ISolidColorBrush { Color: var color }
                && color == AvaloniaColor.Parse(EmbeddedObjectVisualPlanner.BackgroundColorHex)
                && geometry.GetBounds() == fallbackRect).Should().BeTrue();
            drawings.OfType<GeometryDrawing>().Any(geometry =>
                geometry.Pen?.Brush is ISolidColorBrush { Color: var color }
                && color == AvaloniaColor.Parse(EmbeddedObjectVisualPlanner.BorderColorHex)
                && geometry.GetBounds().Intersects(fallbackRect)).Should().BeTrue();
            drawings.OfType<GlyphRunDrawing>().Should().NotBeEmpty(
                "the object-only document can emit text only through the native fallback label painter");

            var ops = view.BuildPdfContent().Pages.SelectMany(page => page.Ops).ToArray();
            ops.OfType<PdfImage>().Should().ContainSingle();
            ops.OfType<PdfText>().Should().Contain(text => text.Text == "Acme.Package");
            ops.OfType<PdfFillRect>().Should().Contain(fill =>
                fill.Color == new PdfColor(0xF3, 0xF6, 0xFB));
            ops.OfType<PdfStrokeRect>().Should().Contain(stroke =>
                stroke.Color == new PdfColor(0xC0, 0xC8, 0xD8));

        });
    }

    [Fact]
    public async Task Table_cell_and_header_story_realize_objects_in_layout_pdf_and_automation()
    {
        await Dispatch(() =>
        {
            var bodyObject = EmbeddedObject.Create([1], "Body.Package", widthPt: 40, heightPt: 24);
            var cellObject = EmbeddedObject.Create([2], "Cell.Package", widthPt: 36, heightPt: 22);
            var headerObject = EmbeddedObject.CreateLinked(
                "https://example.test/header",
                "Header.Package",
                widthPt: 32,
                heightPt: 20);

            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(ParagraphWith(bodyObject));
            var table = new Table();
            var row = new TableRow();
            var cell = new TableCell();
            cell.Paragraphs.Add(ParagraphWith(cellObject));
            row.Cells.Add(cell);
            table.Rows.Add(row);
            document.Blocks.Add(table);
            document.Header = new HeaderFooter();
            document.Header.Paragraphs.Add(ParagraphWith(headerObject));

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(900, 1200));

            var flowItems = view.EmbeddedObjectRenderItems.OrderBy(item => item.BlockIndex).ToArray();
            flowItems.Should().HaveCount(2);
            flowItems[0].BlockIndex.Should().Be(0);
            flowItems[0].CellRow.Should().Be(-1);
            flowItems[1].BlockIndex.Should().Be(1);
            flowItems[1].CellRow.Should().Be(0);
            flowItems[1].CellColumn.Should().Be(0);
            flowItems[1].CellParagraphIndex.Should().Be(0);
            view.GetCellPlaced(1, 0, 0, 0).Where(item => !item.Sentinel)
                .Should().ContainSingle()
                .Which.Ch.Should().Be('\0', "the table-cell object occupies one caret position");

            var headerItem = view.HeaderFooterEmbeddedObjectItems.Should().ContainSingle().Subject;
            headerItem.Slot.Should().Be(HeaderFooterSlotKind.Header);
            headerItem.Plan.Label.Should().Be("Header.Package");
            headerItem.Rect.Width.Should().BeGreaterThan(0);
            headerItem.Rect.Height.Should().BeGreaterThan(0);

            var root = ControlAutomationPeer.CreatePeerForElement(view);
            var embeddedPeers = Descendants(root)
                .Where(peer => peer.GetItemType() == nameof(DocumentAccessibilityNodeKind.EmbeddedObject))
                .ToArray();
            embeddedPeers.Should().HaveCount(3);
            embeddedPeers.Select(peer => peer.GetName()).Should().BeEquivalentTo(
                "Body.Package", "Cell.Package", "Header.Package");
            embeddedPeers.Should().OnlyContain(peer =>
                peer.GetAutomationControlType() == AutomationControlType.Image
                && peer.GetBoundingRectangle().Width > 0
                && peer.GetBoundingRectangle().Height > 0
                && !string.IsNullOrWhiteSpace(peer.GetHelpText()));

            var pdfText = view.BuildPdfContent().Pages
                .SelectMany(page => page.Ops)
                .OfType<PdfText>()
                .Select(text => text.Text)
                .ToArray();
            pdfText.Should().Contain(["Body.Package", "Cell.Package", "Header.Package"]);
        });
    }

    [Fact]
    public async Task Page_vertical_alignment_moves_embedded_object_geometry_with_body_flow()
    {
        await Dispatch(() =>
        {
            static (Rect Rect, double Offset) Measure(PageVerticalAlignment alignment)
            {
                var document = DocumentWithBodyObjects(
                    EmbeddedObject.Create([1], "Aligned.Package", widthPt: 64, heightPt: 48));
                document.Page.VerticalAlignment = alignment;
                var view = new DocumentView();
                view.LoadDocument(document);
                view.Measure(new Size(900, 1200));
                return (view.EmbeddedObjectRenderItems.Single().Rect,
                    alignment == PageVerticalAlignment.Top
                        ? 0
                        : view.BodyPageVerticalOffsetsForTest.Single());
            }

            var top = Measure(PageVerticalAlignment.Top);
            var centered = Measure(PageVerticalAlignment.Center);
            centered.Offset.Should().BeGreaterThan(0);
            (centered.Rect.Y - top.Rect.Y).Should().BeApproximately(centered.Offset, 0.01);
        });
    }

    [Fact]
    public async Task Inserted_embedded_object_is_retained_undoable_and_realized()
    {
        await Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            var view = new DocumentView();
            view.LoadDocument(document);
            var embeddedObject = EmbeddedObject.Create([7, 8], "Inserted.Package", widthPt: 44, heightPt: 28);

            view.InsertEmbeddedObject(embeddedObject);
            view.Measure(new Size(900, 1200));

            var paragraph = document.Blocks.OfType<Paragraph>().Single();
            paragraph.Runs.Should().ContainSingle(run => ReferenceEquals(run.EmbeddedObject, embeddedObject));
            view.EmbeddedObjectRenderItems.Should().ContainSingle()
                .Which.Plan.Label.Should().Be("Inserted.Package");
            view.CanUndo.Should().BeTrue();

            view.Undo();
            paragraph.Runs.Should().NotContain(run => run.EmbeddedObject != null);
            view.Redo();
            paragraph.Runs.Should().ContainSingle(run => run.EmbeddedObject != null);
        });
    }

    private static TextDocument DocumentWithBodyObjects(params EmbeddedObject[] objects)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var embeddedObject in objects)
            document.Blocks.Add(ParagraphWith(embeddedObject));
        return document;
    }

    private static Paragraph ParagraphWith(EmbeddedObject embeddedObject)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEmbeddedObject(embeddedObject));
        return paragraph;
    }

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static byte[] SolidPng(SKColor color)
    {
        using var bitmap = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static IEnumerable<AvaloniaDrawing> FlattenDrawings(AvaloniaDrawing drawing)
    {
        yield return drawing;
        if (drawing is not AvaloniaDrawingGroup group)
            yield break;
        foreach (var child in group.Children)
        foreach (var descendant in FlattenDrawings(child))
            yield return descendant;
    }
}
