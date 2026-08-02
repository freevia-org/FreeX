using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Model↔view round-trip coverage for <see cref="DocumentView"/>: load a model into the WPF surface
/// (<see cref="DocumentView.LoadModel"/> → Render), then <see cref="DocumentView.CommitToModel"/> and
/// assert the recovered <see cref="TextDocument"/> preserves content + formatting. These run on an STA
/// thread (<c>[StaFact]</c>, via Xunit.StaFact) because the RichTextBox/FlowDocument need STA + a
/// Dispatcher.
/// </summary>
public sealed class DocumentViewRoundTripTests
{
    // Load the model into a fresh DocumentView, commit straight back, and return the recovered model.
    private static TextDocument RoundTrip(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadModel(document);
        view.CommitToModel();
        return view.Model;
    }

    private static Run FirstRun(TextDocument document, int blockIndex = 0) =>
        ((Paragraph)document.Blocks[blockIndex]).Runs[0];

    [StaFact]
    public void BookmarkBoundaries_MoveWithInlineEditsBeforeCommit()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var first = new Paragraph();
        first.Runs.Add(new Run("A"));
        first.Runs.Add(new Run("B"));
        first.BookmarkNames.Add("Across");
        first.BookmarkBoundaries.Add(new BookmarkBoundary("7", BookmarkBoundaryKind.Start, 1, "Across"));
        var second = new Paragraph();
        second.Runs.Add(new Run("C"));
        second.Runs.Add(new Run("D"));
        second.BookmarkBoundaries.Add(new BookmarkBoundary("7", BookmarkBoundaryKind.End, 1));
        document.Blocks.Add(first);
        document.Blocks.Add(second);

        var view = new DocumentView();
        view.LoadModel(document);
        var renderedFirst = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        renderedFirst.Inlines.InsertBefore(renderedFirst.Inlines.FirstInline, new System.Windows.Documents.Run("X"));
        view.CommitToModel();
        var recovered = view.Model;
        var paragraphs = recovered.Paragraphs.ToList();

        paragraphs[0].BookmarkBoundaries.Should().Equal(
            new BookmarkBoundary("7", BookmarkBoundaryKind.Start, 2, "Across"));
        paragraphs[1].BookmarkBoundaries.Should().Equal(
            new BookmarkBoundary("7", BookmarkBoundaryKind.End, 1));
    }

    [StaFact]
    public void ContextualSpacing_SuppressesSharedMarginForAdjacentSameStyleParagraphs()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["Body"] = new DocumentStyle
        {
            Id = "Body",
            Name = "Body",
            Paragraph = ParagraphFormatting.Default with
            {
                SpaceBeforePt = 6,
                SpaceAfterPt = 10,
                SpaceBeforeIsSet = true,
                SpaceAfterIsSet = true,
                ContextualSpacing = true
            }
        };
        document.Blocks.Add(new Paragraph("first") { StyleId = "Body" });
        document.Blocks.Add(new Paragraph("second") { StyleId = "Body" });

        var view = new DocumentView();
        view.LoadModel(document);
        var paragraphs = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();

        paragraphs.Should().HaveCount(2);
        paragraphs[0].Margin.Bottom.Should().Be(0);
        paragraphs[1].Margin.Top.Should().Be(0);
    }

    [StaFact]
    public void ContextualSpacing_ExplicitOffKeepsAdjacentSameStyleMargins()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["Body"] = new DocumentStyle
        {
            Id = "Body",
            Name = "Body",
            Paragraph = ParagraphFormatting.Default with
            {
                SpaceBeforePt = 6,
                SpaceAfterPt = 10,
                SpaceBeforeIsSet = true,
                SpaceAfterIsSet = true,
                ContextualSpacing = false
            }
        };
        document.Blocks.Add(new Paragraph("first") { StyleId = "Body" });
        document.Blocks.Add(new Paragraph("second") { StyleId = "Body" });

        var view = new DocumentView();
        view.LoadModel(document);
        var paragraphs = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();

        paragraphs[0].Margin.Bottom.Should().BeApproximately(10 * 96.0 / 72.0, 0.001);
        paragraphs[1].Margin.Top.Should().BeApproximately(6 * 96.0 / 72.0, 0.001);
    }

    [StaFact]
    public void ContextualSpacing_SuppressesSharedMarginForAdjacentSameStyleListItems()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["ListBody"] = new DocumentStyle
        {
            Id = "ListBody",
            Name = "List Body",
            Paragraph = ParagraphFormatting.Default with
            {
                SpaceBeforePt = 6,
                SpaceAfterPt = 10,
                SpaceBeforeIsSet = true,
                SpaceAfterIsSet = true,
                ContextualSpacing = true
            }
        };
        document.Blocks.Add(new Paragraph("first")
        {
            StyleId = "ListBody",
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet }
        });
        document.Blocks.Add(new Paragraph("second")
        {
            StyleId = "ListBody",
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet }
        });

        var view = new DocumentView();
        view.LoadModel(document);
        var list = view.Document.Blocks.OfType<System.Windows.Documents.List>().Single();
        var paragraphs = list.ListItems
            .SelectMany(item => item.Blocks.OfType<System.Windows.Documents.Paragraph>())
            .ToList();

        paragraphs.Should().HaveCount(2);
        paragraphs[0].Margin.Bottom.Should().Be(0);
        paragraphs[1].Margin.Top.Should().Be(0);
    }

    [StaFact]
    public void ContextualSpacing_SuppressesSharedMarginForAdjacentSameStyleTableCellParagraphs()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["CellBody"] = new DocumentStyle
        {
            Id = "CellBody",
            Name = "Cell Body",
            Paragraph = ParagraphFormatting.Default with
            {
                SpaceBeforePt = 6,
                SpaceAfterPt = 10,
                SpaceBeforeIsSet = true,
                SpaceAfterIsSet = true,
                ContextualSpacing = true
            }
        };
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs.Clear();
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("first") { StyleId = "CellBody" });
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("second") { StyleId = "CellBody" });
        document.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(document);
        var cell = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single()
            .RowGroups.Single().Rows.Single().Cells.Single();
        var paragraphs = RenderedCellParagraphs(cell).ToList();

        paragraphs.Should().HaveCount(2);
        paragraphs[0].Margin.Bottom.Should().Be(0);
        paragraphs[1].Margin.Top.Should().Be(0);
    }

    [StaFact]
    public void ContextualSpacing_TableCellExplicitOffKeepsAdjacentSameStyleMargins()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["CellBody"] = new DocumentStyle
        {
            Id = "CellBody",
            Name = "Cell Body",
            Paragraph = ParagraphFormatting.Default with
            {
                SpaceBeforePt = 6,
                SpaceAfterPt = 10,
                SpaceBeforeIsSet = true,
                SpaceAfterIsSet = true,
                ContextualSpacing = false
            }
        };
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs.Clear();
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("first") { StyleId = "CellBody" });
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("second") { StyleId = "CellBody" });
        document.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(document);
        var cell = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single()
            .RowGroups.Single().Rows.Single().Cells.Single();
        var paragraphs = RenderedCellParagraphs(cell).ToList();

        paragraphs[0].Margin.Bottom.Should().BeApproximately(10 * 96.0 / 72.0, 0.001);
        paragraphs[1].Margin.Top.Should().BeApproximately(6 * 96.0 / 72.0, 0.001);
    }

    [StaFact]
    public void ContextualSpacing_SuppressesSharedMarginInsideTableCellHeightHost()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["CellBody"] = new DocumentStyle
        {
            Id = "CellBody",
            Name = "Cell Body",
            Paragraph = ParagraphFormatting.Default with
            {
                SpaceBeforePt = 6,
                SpaceAfterPt = 10,
                SpaceBeforeIsSet = true,
                SpaceAfterIsSet = true,
                ContextualSpacing = true
            }
        };
        var table = Table.Create(1, 1);
        table.Rows[0].HeightPt = 60;
        table.Rows[0].Cells[0].Paragraphs.Clear();
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("first") { StyleId = "CellBody" });
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("second") { StyleId = "CellBody" });
        document.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(document);
        var cell = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single()
            .RowGroups.Single().Rows.Single().Cells.Single();
        var paragraphs = RenderedCellParagraphs(cell).ToList();

        paragraphs.Should().HaveCount(2);
        paragraphs[0].Margin.Bottom.Should().Be(0);
        paragraphs[1].Margin.Top.Should().Be(0);
    }

    [StaFact]
    public void ContextualSpacing_SuppressesSharedMarginInsideOrdinaryRotatedTableCell()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["CellBody"] = new DocumentStyle
        {
            Id = "CellBody",
            Name = "Cell Body",
            Paragraph = ParagraphFormatting.Default with
            {
                SpaceBeforePt = 6,
                SpaceAfterPt = 10,
                SpaceBeforeIsSet = true,
                SpaceAfterIsSet = true,
                ContextualSpacing = true
            }
        };
        var table = Table.Create(1, 1);
        var sourceCell = table.Rows[0].Cells[0];
        sourceCell.TextDirection = CellTextDirection.Rotate90;
        sourceCell.Paragraphs.Clear();
        sourceCell.Paragraphs.Add(new Paragraph("first") { StyleId = "CellBody" });
        sourceCell.Paragraphs.Add(new Paragraph("second") { StyleId = "CellBody" });
        document.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(document);
        var cell = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single()
            .RowGroups.Single().Rows.Single().Cells.Single();
        var paragraphs = RenderedCellParagraphs(cell).ToList();

        paragraphs.Should().HaveCount(2);
        paragraphs[0].Margin.Bottom.Should().Be(0);
        paragraphs[1].Margin.Top.Should().Be(0);
    }

    [StaFact]
    public void ContextualSpacing_SuppressesSharedMarginInsideConstrainedRotatedTableCell()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["CellBody"] = new DocumentStyle
        {
            Id = "CellBody",
            Name = "Cell Body",
            Paragraph = ParagraphFormatting.Default with
            {
                SpaceBeforePt = 6,
                SpaceAfterPt = 10,
                SpaceBeforeIsSet = true,
                SpaceAfterIsSet = true,
                ContextualSpacing = true
            }
        };
        var table = Table.Create(1, 1);
        table.Rows[0].HeightPt = 60;
        var sourceCell = table.Rows[0].Cells[0];
        sourceCell.TextDirection = CellTextDirection.Rotate90;
        sourceCell.Paragraphs.Clear();
        sourceCell.Paragraphs.Add(new Paragraph("first") { StyleId = "CellBody" });
        sourceCell.Paragraphs.Add(new Paragraph("second") { StyleId = "CellBody" });
        document.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(document);
        var cell = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single()
            .RowGroups.Single().Rows.Single().Cells.Single();
        var contentHost = cell.Blocks.OfType<BlockUIContainer>().Single().Child;
        contentHost.Should().NotBeNull();
        var paragraphs = LogicalDescendants<TextBlock>(contentHost!).ToList();

        paragraphs.Should().HaveCount(2);
        paragraphs[0].Margin.Bottom.Should().Be(0);
        paragraphs[1].Margin.Top.Should().Be(0);
    }

    [StaFact]
    public void ContextualSpacing_ExplicitOffKeepsMarginsInsideConstrainedRotatedTableCell()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["CellBody"] = new DocumentStyle
        {
            Id = "CellBody",
            Name = "Cell Body",
            Paragraph = ParagraphFormatting.Default with
            {
                SpaceBeforePt = 6,
                SpaceAfterPt = 10,
                SpaceBeforeIsSet = true,
                SpaceAfterIsSet = true,
                ContextualSpacing = false
            }
        };
        var table = Table.Create(1, 1);
        table.Rows[0].HeightPt = 60;
        var sourceCell = table.Rows[0].Cells[0];
        sourceCell.TextDirection = CellTextDirection.Rotate90;
        sourceCell.Paragraphs.Clear();
        sourceCell.Paragraphs.Add(new Paragraph("first") { StyleId = "CellBody" });
        sourceCell.Paragraphs.Add(new Paragraph("second") { StyleId = "CellBody" });
        document.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(document);
        var cell = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single()
            .RowGroups.Single().Rows.Single().Cells.Single();
        var contentHost = cell.Blocks.OfType<BlockUIContainer>().Single().Child;
        contentHost.Should().NotBeNull();
        var paragraphs = LogicalDescendants<TextBlock>(contentHost!).ToList();

        paragraphs.Should().HaveCount(2);
        paragraphs[0].Margin.Bottom.Should().BeApproximately(10 * 96.0 / 72.0, 0.001);
        paragraphs[1].Margin.Top.Should().BeApproximately(6 * 96.0 / 72.0, 0.001);
    }

    [StaFact]
    public void Table_ExplicitBorderPayload_SurvivesViewRoundTrip()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = Table.Create(1, 1);
        table.Borders = new TableBorders
        {
            Top = new TableBorderEdge(BorderLineStyle.Double, "1F4E79", 1.5),
            InsideVertical = new TableBorderEdge(BorderLineStyle.Dotted, "auto", 0.5)
        };
        document.Blocks.Add(table);

        var recovered = RoundTrip(document).Blocks.OfType<Table>().Single();

        recovered.Borders.Should().Be(table.Borders);
    }

    private static List<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var result = new List<T>();
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
                continue;
            if (dependencyObject is T typed)
                result.Add(typed);
            result.AddRange(LogicalDescendants<T>(dependencyObject));
        }
        return result;
    }

    private static string TextBlockText(TextBlock textBlock) =>
        textBlock.Text + string.Concat(textBlock.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text));

    [StaFact]
    public void PlainText_RoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Hello world"));

        var result = RoundTrip(doc);

        result.PlainText.Should().Be("Hello world");
    }

    [StaFact]
    public void MultipleParagraphs_RoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("First"));
        doc.Blocks.Add(new Paragraph("Second"));
        doc.Blocks.Add(new Paragraph("Third"));

        var result = RoundTrip(doc);

        result.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("First", "Second", "Third");
    }

    [StaFact]
    public void RunFormatting_BoldItalicUnderlineColor_RoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("styled", new RunFormatting
        {
            Bold = true,
            Italic = true,
            Underline = true,
            ColorHex = "#FF0000"
        }));
        doc.Blocks.Add(para);

        var run = FirstRun(RoundTrip(doc));

        run.Text.Should().Be("styled");
        run.Formatting.Bold.Should().BeTrue();
        run.Formatting.Italic.Should().BeTrue();
        run.Formatting.Underline.Should().BeTrue();
        run.Formatting.ColorHex.Should().Be("#FF0000");
    }

    [StaFact]
    public void ExternalHyperlink_RoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("click me") { HyperlinkUrl = "https://example.com/" });
        doc.Blocks.Add(para);

        var run = FirstRun(RoundTrip(doc));

        run.Text.Should().Be("click me");
        run.HyperlinkUrl.Should().Be("https://example.com/");
    }

    [StaFact]
    public void RichInlineHyperlinks_RoundTripThroughView()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Linked(Run.FromImage(new InlineImage(OnePixelPng(), 24, 24) { AltText = "linked image" })));
        para.Runs.Add(Linked(Run.FromShape(new Shape(ShapeKind.Rectangle, 40, 20))));
        para.Runs.Add(Linked(Run.FromChart(Chart.Create(ChartKind.Column, ["Q1"], [1.0]))));
        para.Runs.Add(Linked(Run.FromWordArt(WordArt.Create("Banner", WordArtStyle.GradientFill))));
        para.Runs.Add(Linked(Run.FromEquation(Equation.FromText("x + y"))));
        para.Runs.Add(Linked(Run.FromSmartArt(SmartArt.Create(SmartArtKind.Process, ["One", "Two"]))));
        para.Runs.Add(Linked(Run.FromEmbeddedObject(EmbeddedObject.Create([1, 2, 3], "Package"))));
        doc.Blocks.Add(para);

        var result = RoundTrip(doc);

        var runs = ((Paragraph)result.Blocks[0]).Runs;
        runs.Should().HaveCount(7);
        runs.Should().OnlyContain(r => r.HyperlinkUrl == "https://example.com/rich" && r.HyperlinkTooltip == "Open rich object");
        runs.Count(r => r.Image is not null).Should().Be(1);
        runs.Count(r => r.Shape is not null).Should().Be(1);
        runs.Count(r => r.Chart is not null).Should().Be(1);
        runs.Count(r => r.WordArt is not null).Should().Be(1);
        runs.Count(r => r.Equation is not null).Should().Be(1);
        runs.Count(r => r.SmartArt is not null).Should().Be(1);
        runs.Count(r => r.EmbeddedObject is not null).Should().Be(1);

        static Run Linked(Run run)
        {
            run.HyperlinkUrl = "https://example.com/rich";
            run.HyperlinkTooltip = "Open rich object";
            return run;
        }
    }

    [StaFact]
    public void BulletList_RoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var text in new[] { "Alpha", "Beta", "Gamma" })
        {
            var para = new Paragraph(text)
            {
                Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet }
            };
            doc.Blocks.Add(para);
        }

        var result = RoundTrip(doc);
        var listParas = result.Blocks.OfType<Paragraph>().ToList();

        listParas.Select(p => p.PlainText).Should().Equal("Alpha", "Beta", "Gamma");
        listParas.Should().OnlyContain(p => p.Formatting.ListKind == ListKind.Bullet);
    }

    [StaFact]
    public void SingleHeadingMultiLevelItem_RendersWithoutAListContainerAndRoundTrips()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Outline heading")
        {
            StyleId = "Heading1",
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel }
        });
        document.Blocks.Add(new Paragraph("Following body paragraph."));

        var view = new DocumentView();
        view.LoadModel(document);

        view.Document.Blocks.OfType<System.Windows.Documents.List>().Should().BeEmpty();
        RoundTrip(document).Blocks.OfType<Paragraph>().First().Formatting.ListKind.Should().Be(ListKind.MultiLevel);
    }

    [StaFact]
    public void OmittedWidowControl_UsesWordDefaultWhileExplicitOffDoesNot()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Default widow behavior."));
        document.Blocks.Add(new Paragraph("Explicit widow off.")
        {
            Formatting = ParagraphFormatting.Default with { WidowControl = false, WidowControlIsSet = true }
        });

        var view = new DocumentView();
        view.LoadModel(document);

        var paragraphs = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        paragraphs[0].KeepTogether.Should().BeTrue();
        paragraphs[1].KeepTogether.Should().BeFalse();

        var roundTripped = RoundTrip(document).Blocks.OfType<Paragraph>().ToList();
        roundTripped[0].Formatting.KeepLinesTogether.Should().BeFalse();
        roundTripped[0].Formatting.WidowControlIsSet.Should().BeFalse();
    }

    [StaFact]
    public void Table_RoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(2, 3);
        table.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("R0C0");
        table.Rows[1].Cells[2].Paragraphs[0] = new Paragraph("R1C2");
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);
        var resultTable = result.Blocks.OfType<Table>().Single();

        resultTable.Rows.Should().HaveCount(2);
        resultTable.Rows[0].Cells.Should().HaveCount(3);
        resultTable.Rows[0].Cells[0].PlainText.Should().Be("R0C0");
        resultTable.Rows[1].Cells[2].PlainText.Should().Be("R1C2");
    }

    [StaFact]
    public void InlineImage_RoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        var image = new InlineImage(OnePixelPng(), 96, 48) { AltText = "diagram" };
        para.Runs.Add(Run.FromImage(image));
        doc.Blocks.Add(para);

        var run = FirstRun(RoundTrip(doc));

        run.Image.Should().NotBeNull();
        run.Image!.WidthPt.Should().Be(96);
        run.Image.HeightPt.Should().Be(48);
        run.Image.AltText.Should().Be("diagram");
    }

    // Regression: an image in a format WPF's WIC pipeline cannot decode (e.g. a WMF metafile, or just
    // corrupt bytes) must NOT fail the whole document render. The undecodable image renders as a sized
    // placeholder, the rest of the document still renders, and the image run still round-trips.
    [StaFact]
    public void UndecodableImage_DoesNotFailRender_AndRoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Before "));
        para.Runs.Add(Run.FromImage(new InlineImage(new byte[] { 1, 2, 3, 4 }, 50, 30, ImageFormat.Wmf)));
        para.Runs.Add(new Run(" After"));
        doc.Blocks.Add(para);
        doc.Blocks.Add(new Paragraph("Following paragraph"));

        var view = new DocumentView();

        // (a) Loading the model (which builds the FlowDocument, including the undecodable image) must not throw.
        var load = () => view.LoadModel(doc);
        load.Should().NotThrow();

        // (b) The rest of the document's text still renders into the surface.
        view.Document.Should().NotBeNull();
        var rendered = new System.Windows.Documents.TextRange(
            view.Document.ContentStart, view.Document.ContentEnd).Text;
        rendered.Should().Contain("Before");
        rendered.Should().Contain("After");
        rendered.Should().Contain("Following paragraph");

        // (c) The image run survives CommitToModel (the model Run.Image is preserved, never dropped).
        view.CommitToModel();
        var imageRun = view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .SingleOrDefault(r => r.Image is not null);
        imageRun.Should().NotBeNull();
        imageRun!.Image!.Format.Should().Be(ImageFormat.Wmf);
        imageRun.Image.WidthPt.Should().Be(50);
        imageRun.Image.HeightPt.Should().Be(30);
    }

    // Best-effort metafile rendering: a genuine (GDI+-produced) EMF decodes and round-trips without
    // falling back to the placeholder path or throwing.
    [StaFact]
    public void ValidEmfMetafile_RendersAndRoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.FromImage(new InlineImage(CreateEmf(), 60, 40, ImageFormat.Emf)));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        var load = () => view.LoadModel(doc);
        load.Should().NotThrow();

        view.CommitToModel();
        var run = FirstRun(view.Model);
        run.Image.Should().NotBeNull();
        run.Image!.Format.Should().Be(ImageFormat.Emf);
    }

    // Build a minimal valid EMF (enhanced metafile) via GDI+ that draws a single line, returning its bytes.
    // The metafile is recorded straight into a MemoryStream (the robust idiom) so disposing it flushes the
    // EMF bytes — no HENHMETAFILE handle juggling, which avoids GDI+ "generic error" flakiness.
    private static byte[] CreateEmf()
    {
        var stream = new MemoryStream();
        using (var reference = new System.Drawing.Bitmap(1, 1))
        using (var refGraphics = System.Drawing.Graphics.FromImage(reference))
        {
            var hdc = refGraphics.GetHdc();
            try
            {
                using var metafile = new System.Drawing.Imaging.Metafile(
                    stream,
                    hdc,
                    new System.Drawing.RectangleF(0, 0, 10, 10),
                    System.Drawing.Imaging.MetafileFrameUnit.Pixel,
                    System.Drawing.Imaging.EmfType.EmfOnly);
                using var g = System.Drawing.Graphics.FromImage(metafile);
                g.DrawLine(System.Drawing.Pens.Black, 0, 0, 10, 10);
            }
            finally
            {
                refGraphics.ReleaseHdc(hdc);
            }
        }
        return stream.ToArray();
    }

    [StaFact]
    public void ParagraphStyleId_RoundTrips()
    {
        // Style id has no FlowDocument slot; it is carried on the paragraph Tag so it survives commit
        // (the fix that also makes outline collapse work after a commit).
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("A heading") { StyleId = "Heading1" });

        var result = RoundTrip(doc);

        ((Paragraph)result.Blocks[0]).StyleId.Should().Be("Heading1");
    }

    [StaFact]
    public void Equation_RoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.FromEquation(Equation.FromText("a + b = c")));
        doc.Blocks.Add(para);

        var run = FirstRun(RoundTrip(doc));

        run.Equation.Should().NotBeNull();
        run.Equation!.LinearText.Should().Be("a + b = c");
    }

    [StaFact]
    public void StructuredEquation_RoundTripsThroughView()
    {
        // A radical + n-ary + 2x2 matrix must survive the view's render → CommitToModel path (the
        // structure is carried on the inline container's Tag, mirroring shapes).
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.FromEquation(new Equation([
            MathRun.Radical("x", "3"),
            MathRun.NAry("∑", "i=1", "n", "i"),
            MathRun.MatrixOf(MathMatrix.Identity2x2())
        ])));
        doc.Blocks.Add(para);

        var run = FirstRun(RoundTrip(doc));

        run.Equation.Should().NotBeNull();
        var runs = run.Equation!.Runs;
        runs.Should().HaveCount(3);
        runs[0].Kind.Should().Be(MathRunKind.Radical);
        runs[0].Degree.Should().Be("3");
        runs[1].Kind.Should().Be(MathRunKind.NAry);
        runs[2].Kind.Should().Be(MathRunKind.Matrix);
        runs[2].Matrix!.RowCount.Should().Be(2);
    }

    [StaFact]
    public void EquationVisualPlanner_SuperscriptRendersAsStyledInlineSegmentsAndRoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var equation = new Equation([MathRun.PlainText("E = m"), MathRun.Superscript("c", "2")]);
        var para = new Paragraph();
        para.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        var mathText = LogicalDescendants<TextBlock>(view.Document)
            .FirstOrDefault(textBlock => textBlock.FontFamily.Source.Contains("Cambria Math", StringComparison.Ordinal));

        mathText.Should().NotBeNull("the WPF equation visual should use the shared math display plan");
        var visualRuns = mathText!.Inlines.OfType<System.Windows.Documents.Run>().ToList();
        visualRuns.Select(run => run.Text).Should().Equal("E = m", "c", "2");
        visualRuns.Should().NotContain(run => run.Text.Contains('^') || run.Text.Contains('_'),
            "script markers should be represented by WPF baseline styling instead of literal characters");
        visualRuns[2].BaselineAlignment.Should().Be(BaselineAlignment.Superscript);
        visualRuns[2].FontSize.Should().BeLessThan(visualRuns[1].FontSize);

        view.CommitToModel();
        var recovered = FirstRun(view.Model);
        recovered.Equation.Should().NotBeNull();
        recovered.Equation!.Runs.Select(run => run.Kind).Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
    }

    [StaFact]
    public void EquationVisualPlanner_FractionAndRadicalRenderStructuredElementsAndRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var equation = new Equation([
            MathRun.Fraction("a + b", "c"),
            MathRun.Radical("x + 1", "3")
        ]);
        var para = new Paragraph();
        para.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        var structuredKinds = LogicalDescendants<StackPanel>(view.Document)
            .Where(panel => panel.Tag is EquationVisualElementKind)
            .Select(panel => (EquationVisualElementKind)panel.Tag)
            .ToList();
        structuredKinds.Should().Contain(EquationVisualElementKind.Fraction);
        structuredKinds.Should().Contain(EquationVisualElementKind.Radical);

        var visualText = LogicalDescendants<TextBlock>(view.Document)
            .Select(TextBlockText)
            .Where(text => text.Length > 0)
            .ToList();
        visualText.Should().Contain("a + b");
        visualText.Should().Contain("c");
        visualText.Should().Contain(EquationVisualPlanner.RadicalSignText);
        visualText.Should().Contain("3");
        visualText.Should().Contain("x + 1");
        visualText.Should().NotContain("a + b/c",
            "the WPF equation visual should not render fractions as the raw linear fallback");
        visualText.Should().NotContain($"3{EquationVisualPlanner.RadicalSignText}(x + 1)",
            "the WPF equation visual should not render radicals as the raw linear fallback");

        var fractionPanel = LogicalDescendants<StackPanel>(view.Document)
            .Single(panel => Equals(panel.Tag, EquationVisualElementKind.Fraction));
        LogicalDescendants<Border>(fractionPanel).Should().Contain(border => Math.Abs(border.Height - 1) < 0.01);
        var radicalPanel = LogicalDescendants<StackPanel>(view.Document)
            .Single(panel => Equals(panel.Tag, EquationVisualElementKind.Radical));
        LogicalDescendants<Border>(radicalPanel).Should()
            .Contain(border => border.BorderThickness.Top > 0 && border.BorderThickness.Bottom == 0);

        view.CommitToModel();
        var recovered = FirstRun(view.Model);
        recovered.Equation.Should().NotBeNull();
        var runs = recovered.Equation!.Runs;
        runs.Select(run => run.Kind).Should().Equal(MathRunKind.Fraction, MathRunKind.Radical);
        runs[0].Numerator.Should().Be("a + b");
        runs[0].Denominator.Should().Be("c");
        runs[1].Base.Should().Be("x + 1");
        runs[1].Degree.Should().Be("3");
    }

    [StaFact]
    public void EquationVisualPlanner_NAryRendersLargeOperatorWithLimitsAndRoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var equation = new Equation([MathRun.NAry("\u2211", "i=1", "n", "i")]);
        var para = new Paragraph();
        para.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        var naryPanel = LogicalDescendants<StackPanel>(view.Document)
            .Single(panel => Equals(panel.Tag, EquationVisualElementKind.NAry));
        var visualText = LogicalDescendants<TextBlock>(naryPanel)
            .Select(TextBlockText)
            .Where(text => text.Length > 0)
            .ToList();
        visualText.Should().Contain("\u2211");
        visualText.Should().Contain("i=1");
        visualText.Should().Contain("n");
        visualText.Should().Contain("i");
        visualText.Should().NotContain("\u2211(i=1..n) i",
            "the WPF equation visual should not render n-ary operators as raw linear fallback");

        var operatorText = LogicalDescendants<TextBlock>(naryPanel)
            .Single(text => TextBlockText(text) == "\u2211");
        var operandText = LogicalDescendants<TextBlock>(naryPanel)
            .Single(text => TextBlockText(text) == "i");
        var operatorRun = operatorText.Inlines.OfType<System.Windows.Documents.Run>().Single();
        var operandRun = operandText.Inlines.OfType<System.Windows.Documents.Run>().Single();
        operatorRun.FontSize.Should().BeGreaterThan(operandRun.FontSize);

        view.CommitToModel();
        var recovered = FirstRun(view.Model);
        recovered.Equation.Should().NotBeNull();
        var run = recovered.Equation!.Runs.Should().ContainSingle().Subject;
        run.Kind.Should().Be(MathRunKind.NAry);
        run.Operator.Should().Be("\u2211");
        run.Sub.Should().Be("i=1");
        run.Sup.Should().Be("n");
        run.Base.Should().Be("i");
    }

    [StaFact]
    public void EquationVisualPlanner_MatrixRendersBracketedGridAndRoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var equation = new Equation([MathRun.MatrixOf(MathMatrix.Identity2x2())]);
        var para = new Paragraph();
        para.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        var matrixPanel = LogicalDescendants<StackPanel>(view.Document)
            .Single(panel => Equals(panel.Tag, EquationVisualElementKind.Matrix));
        var grid = LogicalDescendants<Grid>(matrixPanel).Single();
        grid.RowDefinitions.Should().HaveCount(2);
        grid.ColumnDefinitions.Should().HaveCount(2);
        grid.Children.OfType<TextBlock>()
            .Select(TextBlockText)
            .Should().Equal("1", "0", "0", "1");
        grid.Children.OfType<TextBlock>()
            .Select(text => text.Margin)
            .Should().OnlyContain(margin => margin.Left == 2 && margin.Right == 2);

        var visualText = LogicalDescendants<TextBlock>(matrixPanel)
            .Select(TextBlockText)
            .Where(text => text.Length > 0)
            .ToList();
        visualText.Should().Contain(EquationVisualPlanner.MatrixOpenDelimiterText);
        visualText.Should().Contain(EquationVisualPlanner.MatrixCloseDelimiterText);
        visualText.Should().NotContain("[1, 0; 0, 1]",
            "the WPF equation visual should build a matrix grid instead of the raw linear fallback");

        view.CommitToModel();
        var recovered = FirstRun(view.Model);
        recovered.Equation.Should().NotBeNull();
        var run = recovered.Equation!.Runs.Should().ContainSingle().Subject;
        run.Kind.Should().Be(MathRunKind.Matrix);
        run.Matrix.Should().NotBeNull();
        run.Matrix!.RowCount.Should().Be(2);
        run.Matrix.ColumnCount.Should().Be(2);
        run.Matrix.Rows[0].Should().Equal("1", "0");
        run.Matrix.Rows[1].Should().Equal("0", "1");
    }

    [StaFact]
    public void EquationVisualPlanner_DecoratorsRenderStructuredElementsAndRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var equation = new Equation([
            MathRun.AccentOf("x", "hat"),
            MathRun.BarOf("y"),
            MathRun.BarOf("z", top: false),
            MathRun.Delimiter("a + b", "[", "]"),
            MathRun.GroupCharOf("n", "\u23DE", "top"),
            MathRun.GroupCharOf("m", "\u23DF", "bot")
        ]);
        var para = new Paragraph();
        para.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        var structuredKinds = LogicalDescendants<StackPanel>(view.Document)
            .Where(panel => panel.Tag is EquationVisualElementKind)
            .Select(panel => (EquationVisualElementKind)panel.Tag)
            .ToList();
        structuredKinds.Should().Contain(EquationVisualElementKind.Accent);
        structuredKinds.Should().Contain(EquationVisualElementKind.Bar);
        structuredKinds.Should().Contain(EquationVisualElementKind.Delimiter);
        structuredKinds.Should().Contain(EquationVisualElementKind.GroupChar);

        var accentPanel = LogicalDescendants<StackPanel>(view.Document)
            .Single(panel => Equals(panel.Tag, EquationVisualElementKind.Accent));
        LogicalDescendants<TextBlock>(accentPanel)
            .Select(TextBlockText)
            .Where(text => text.Length > 0)
            .Should().Equal("hat", "x");

        var barPanels = LogicalDescendants<StackPanel>(view.Document)
            .Where(panel => Equals(panel.Tag, EquationVisualElementKind.Bar))
            .ToList();
        barPanels.Should().HaveCount(2);
        barPanels.Should().OnlyContain(panel => LogicalDescendants<Border>(panel)
            .Count(border => Math.Abs(border.Height - 1) < 0.01) == 1);
        LogicalDescendants<TextBlock>(barPanels[0]).Select(TextBlockText).Should().Contain("y");
        LogicalDescendants<TextBlock>(barPanels[1]).Select(TextBlockText).Should().Contain("z");

        var delimiterPanel = LogicalDescendants<StackPanel>(view.Document)
            .Single(panel => Equals(panel.Tag, EquationVisualElementKind.Delimiter));
        LogicalDescendants<TextBlock>(delimiterPanel)
            .Select(TextBlockText)
            .Where(text => text.Length > 0)
            .Should().Equal("[", "a + b", "]");

        var groupPanels = LogicalDescendants<StackPanel>(view.Document)
            .Where(panel => Equals(panel.Tag, EquationVisualElementKind.GroupChar))
            .ToList();
        groupPanels.Should().HaveCount(2);
        LogicalDescendants<TextBlock>(groupPanels[0])
            .Select(TextBlockText)
            .Where(text => text.Length > 0)
            .Should().Equal("\u23DE", "n");
        LogicalDescendants<TextBlock>(groupPanels[1])
            .Select(TextBlockText)
            .Where(text => text.Length > 0)
            .Should().Equal("m", "\u23DF");

        var allVisualText = LogicalDescendants<TextBlock>(view.Document)
            .Select(TextBlockText)
            .Where(text => text.Length > 0)
            .ToList();
        allVisualText.Should().NotContain(equation.Runs[0].LinearText,
            "accent should render as a stacked mark/base pair instead of raw linear fallback");
        allVisualText.Should().NotContain(equation.Runs[3].LinearText,
            "delimiters should render as wrapped segments instead of one raw fallback string");

        view.CommitToModel();
        var recovered = FirstRun(view.Model);
        recovered.Equation.Should().NotBeNull();
        var runs = recovered.Equation!.Runs;
        runs.Select(run => run.Kind).Should().Equal(
            MathRunKind.Accent,
            MathRunKind.Bar,
            MathRunKind.Bar,
            MathRunKind.Delimiter,
            MathRunKind.GroupChar,
            MathRunKind.GroupChar);
        runs[0].Base.Should().Be("x");
        runs[0].Accent.Should().Be("hat");
        runs[1].Base.Should().Be("y");
        runs[1].BarTop.Should().BeTrue();
        runs[2].Base.Should().Be("z");
        runs[2].BarTop.Should().BeFalse();
        runs[3].Base.Should().Be("a + b");
        runs[3].OpenChar.Should().Be("[");
        runs[3].CloseChar.Should().Be("]");
        runs[4].GroupChr.Should().Be("\u23DE");
        runs[4].GroupChrPos.Should().Be("top");
        runs[5].GroupChr.Should().Be("\u23DF");
        runs[5].GroupChrPos.Should().Be("bot");
    }

    [StaFact]
    public void EquationVisualPlanner_FunctionApplyRendersStructuredNameArgumentAndRoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var equation = new Equation([MathRun.FunctionApply("sin", "x + y")]);
        var para = new Paragraph();
        para.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        var functionPanel = LogicalDescendants<StackPanel>(view.Document)
            .Single(panel => Equals(panel.Tag, EquationVisualElementKind.FunctionApply));
        var visualText = LogicalDescendants<TextBlock>(functionPanel)
            .Select(TextBlockText)
            .Where(text => text.Length > 0)
            .ToList();
        visualText.Should().Equal(
            "sin",
            "x + y");
        visualText.Should().NotContain("(",
            "Word OfficeMath functions contain a function name and argument, not display parentheses");

        var visualRuns = LogicalDescendants<TextBlock>(functionPanel)
            .SelectMany(textBlock => textBlock.Inlines.OfType<System.Windows.Documents.Run>())
            .ToList();
        visualRuns.Single(run => run.Text == "sin").FontStyle.Should().Be(FontStyles.Normal);
        visualRuns.Single(run => run.Text == "x + y").FontStyle.Should().Be(FontStyles.Italic);

        view.CommitToModel();
        var recovered = FirstRun(view.Model);
        recovered.Equation.Should().NotBeNull();
        var run = recovered.Equation!.Runs.Should().ContainSingle().Subject;
        run.Kind.Should().Be(MathRunKind.FunctionApply);
        run.FuncName.Should().Be("sin");
        run.Base.Should().Be("x + y");
    }

    [StaFact]
    public void InsertEquation_PlacesStructuredEquationAtCaret()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());

        view.InsertEquation(new Equation([MathRun.MatrixOf(MathMatrix.Identity2x2())]));
        view.CommitToModel();

        var equationRun = view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs).Single(r => r.Equation is not null);
        equationRun.Equation!.Runs[0].Kind.Should().Be(MathRunKind.Matrix);
        equationRun.Equation!.LinearText.Should().Be("[1, 0; 0, 1]");
    }

    [StaFact]
    public void Chart_RoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.FromChart(Chart.Create(
            ChartKind.Column, ["Q1", "Q2"], [3.0, 5.0], seriesName: "Sales", title: "Quarterly")));
        doc.Blocks.Add(para);

        var run = FirstRun(RoundTrip(doc));

        run.Chart.Should().NotBeNull();
        run.Chart!.Kind.Should().Be(ChartKind.Column);
        run.Chart.Title.Should().Be("Quarterly");
        run.Chart.Categories.Should().Equal("Q1", "Q2");
        run.Chart.Series.Should().ContainSingle();
        run.Chart.Series[0].Values.Should().Equal(3.0, 5.0);
    }

    [StaFact]
    public void WordArt_RoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.FromWordArt(WordArt.Create("Banner", WordArtStyle.GradientFill)));
        doc.Blocks.Add(para);

        var run = FirstRun(RoundTrip(doc));

        run.WordArt.Should().NotBeNull();
        run.WordArt!.Text.Should().Be("Banner");
        run.WordArt.Style.Should().Be(WordArtStyle.GradientFill);
    }

    // Insert > Media > SmartArt: inserting a SmartArt via the view and committing recovers a run carrying
    // the diagram (its kind + node texts survive the InsertSmartArt -> BuildSmartArtRun -> ReadInline path).
    [StaFact]
    public void InsertSmartArt_RoundTripsThroughView()
    {
        var view = new DocumentView();
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph());
        view.LoadModel(doc);

        view.InsertSmartArt(SmartArt.Create(SmartArtKind.Process, ["First", "Second", "Third"]));
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Single(r => r.SmartArt is not null);
        run.SmartArt!.Kind.Should().Be(SmartArtKind.Process);
        run.SmartArt.Nodes.Select(n => n.Text).Should().Equal("First", "Second", "Third");
    }

    // Insert > Media > Object: inserting an embedded OLE object via the view and committing recovers a run
    // carrying the object (its payload + ProgID survive the InsertEmbeddedObject -> ReadInline path).
    [StaFact]
    public void InsertEmbeddedObject_RoundTripsThroughView()
    {
        var view = new DocumentView();
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph());
        view.LoadModel(doc);

        var payload = System.Text.Encoding.UTF8.GetBytes("payload");
        view.InsertEmbeddedObject(EmbeddedObject.Create(payload, progId: "Package"));
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Single(r => r.EmbeddedObject is not null);
        run.EmbeddedObject!.ProgId.Should().Be("Package");
        run.EmbeddedObject.Payload.Should().Equal(payload);
    }

    // ── SectionBreak round-trip ───────────────────────────────────────────────────────────────────

    [StaFact]
    public void SectionBreak_NextPage_RoundTrips()
    {
        // A SectionBreak on a paragraph has no FlowDocument slot; it must be preserved via the
        // ParagraphTag so CommitToModel restores it losslessly.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var sec1Para = new Paragraph("Section 1 end");
        sec1Para.SectionBreak = new FreeW.Core.Model.Section(new PageSettings(), SectionBreakKind.NextPage);
        doc.Blocks.Add(sec1Para);
        doc.Blocks.Add(new Paragraph("Section 2 content"));

        var result = RoundTrip(doc);

        result.Blocks.Should().HaveCount(2);
        var recovered = (Paragraph)result.Blocks[0];
        recovered.SectionBreak.Should().NotBeNull("SectionBreak must survive render→CommitToModel");
        recovered.SectionBreak!.BreakKind.Should().Be(SectionBreakKind.NextPage);
    }

    [StaFact]
    public void SectionBreak_AllKinds_RoundTrip()
    {
        // Every SectionBreakKind must survive the render→CommitToModel cycle intact.
        var kinds = new[]
        {
            SectionBreakKind.NextPage,
            SectionBreakKind.Continuous,
            SectionBreakKind.EvenPage,
            SectionBreakKind.OddPage
        };

        foreach (var kind in kinds)
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var sectionPara = new Paragraph("Break para");
            sectionPara.SectionBreak = new FreeW.Core.Model.Section(new PageSettings(), kind);
            doc.Blocks.Add(sectionPara);
            doc.Blocks.Add(new Paragraph("Body"));

            var result = RoundTrip(doc);

            var recovered = (Paragraph)result.Blocks[0];
            recovered.SectionBreak.Should().NotBeNull($"SectionBreak ({kind}) must survive commit");
            recovered.SectionBreak!.BreakKind.Should().Be(kind,
                $"BreakKind {kind} must round-trip unchanged");
        }
    }

    [StaFact]
    public void SectionBreak_SectionCount_PreservedAcrossCommit()
    {
        // A three-paragraph doc with two section breaks must still have three sections
        // (section count = sectionBreak paragraphs + 1) after render→CommitToModel.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p1 = new Paragraph("End of section 1");
        p1.SectionBreak = new FreeW.Core.Model.Section(new PageSettings(), SectionBreakKind.NextPage);
        var p2 = new Paragraph("End of section 2");
        p2.SectionBreak = new FreeW.Core.Model.Section(new PageSettings(), SectionBreakKind.Continuous);
        doc.Blocks.Add(p1);
        doc.Blocks.Add(p2);
        doc.Blocks.Add(new Paragraph("Section 3 body"));

        var result = RoundTrip(doc);

        result.Sections.Should().HaveCount(3,
            "section count must be preserved after render→CommitToModel");
        result.Sections[0].BreakKind.Should().Be(SectionBreakKind.NextPage);
        result.Sections[1].BreakKind.Should().Be(SectionBreakKind.Continuous);
    }

    // ── Table render fixes (FreeW fidelity pass, 2026-06-25) ─────────────────────────────────────

    /// <summary>
    /// Banded-rows off-by-one fix: Word's Band 1 = first data row (bodyIndex 0). After the fix,
    /// <c>IsBandedBodyRow</c> returns true for bodyIndex 0 (even) so the first body row gets the
    /// grey BandedRowFill, and the second body row (bodyIndex 1, odd) is white.
    /// </summary>
    [StaFact]
    public void BandedRows_FirstBodyRow_IsBanded()
    {
        // 3-row table: header + 2 body rows. BandedRows=true, HeaderRow=true.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(2, 2);
        table.Formatting = table.Formatting with { HeaderRow = true, BandedRows = true };
        table.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("Header");
        table.Rows[1].Cells[0].Paragraphs[0] = new Paragraph("Body1");
        doc.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(doc);

        // Inspect the rendered WPF table cells: body row 0 (rowIndex=1) must have a non-null
        // Background (the grey banded fill); body row 1 (rowIndex=2) must be null/transparent.
        var wpfTable = (System.Windows.Documents.Table)view.Document.Blocks.First();
        var bodyRow0 = wpfTable.RowGroups[0].Rows[1]; // first body row (after header)
        var bodyRow1 = wpfTable.RowGroups[0].Rows.Count > 2 ? wpfTable.RowGroups[0].Rows[2] : null;

        bodyRow0.Cells[0].Background.Should().NotBeNull(
            "first data row (bodyIndex 0) must receive the banded fill");
        bodyRow0.Cells[0].Background.Should().BeOfType<System.Windows.Media.SolidColorBrush>(
            "banded fill is always a SolidColorBrush");

        if (bodyRow1 is not null)
        {
            var brush = bodyRow1.Cells[0].Background as System.Windows.Media.SolidColorBrush;
            var hasNoFill = brush is null || brush.Color.A == 0;
            hasNoFill.Should().BeTrue("second data row (bodyIndex 1) must be white / no fill");
        }
    }

    /// <summary>
    /// Row height fix: a row with <c>HeightPt=60, HeightRule=AtLeast</c> must produce a
    /// <see cref="BlockUIContainer"/> spacer (a <see cref="System.Windows.Controls.Border"/>
    /// with <c>MinHeight = 60 × PxPerPoint</c>) in every non-Continue cell so the WPF
    /// FlowDocument row is at least that tall.
    /// </summary>
    [StaFact]
    public void TableRow_ExplicitHeight_UsesSingleMinHeightContentHost()
    {
        const double heightPt = 60.0;
        const double pxPerPt = 96.0 / 72.0;

        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(1, 2);
        table.Rows[0].HeightPt = heightPt;
        table.Rows[0].HeightRule = TableRowHeightRule.AtLeast;
        table.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("Content");
        doc.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpfTable = (System.Windows.Documents.Table)view.Document.Blocks.First();
        var wpfCell = wpfTable.RowGroups[0].Rows[0].Cells[0];

        // The one content host holds the authored minimum height.
        var host = wpfCell.Blocks.OfType<BlockUIContainer>()
            .Should().ContainSingle().Subject;
        host.Child.Should().BeOfType<System.Windows.Controls.Grid>();

        var grid = (System.Windows.Controls.Grid)host.Child;
        grid.MinHeight.Should().BeApproximately(heightPt * pxPerPt, 0.01,
            "spacer MinHeight must equal HeightPt × PxPerPoint");
    }

    [StaFact]
    public void TableRow_ExactHeight_ReservesCellChromeOutsideTheContentHost()
    {
        const double heightPt = 60.0;
        const double pxPerPt = 96.0 / 72.0;

        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(1, 1);
        table.Rows[0].HeightPt = heightPt;
        table.Rows[0].HeightRule = TableRowHeightRule.Exact;
        table.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("Content");
        doc.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpfCell = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single()
            .RowGroups.Single().Rows.Single().Cells.Single();
        var host = wpfCell.Blocks.OfType<BlockUIContainer>().Single();
        var grid = host.Child.Should().BeOfType<System.Windows.Controls.Grid>().Subject;
        grid.MinHeight.Should().BeApproximately(heightPt * pxPerPt - 2, 0.01,
            "exact row heights include the surrounding FlowDocument cell chrome");
    }

    /// <summary>
    /// Cell vertical alignment fix: <see cref="TableCellVerticalAlignment"/> survives the
    /// Build→Commit round-trip (stashed in <c>TableCellTag</c> and recovered by <c>ReadTable</c>).
    /// </summary>
    [StaFact]
    public void TableCell_VerticalAlignment_RoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0].VerticalAlignment = TableCellVerticalAlignment.Top;
        table.Rows[0].Cells[1].VerticalAlignment = TableCellVerticalAlignment.Center;
        table.Rows[0].Cells[2].VerticalAlignment = TableCellVerticalAlignment.Bottom;
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);

        var resultTable = result.Blocks.OfType<Table>().Single();
        resultTable.Rows[0].Cells[0].VerticalAlignment.Should().Be(TableCellVerticalAlignment.Top);
        resultTable.Rows[0].Cells[1].VerticalAlignment.Should().Be(TableCellVerticalAlignment.Center);
        resultTable.Rows[0].Cells[2].VerticalAlignment.Should().Be(TableCellVerticalAlignment.Bottom);
    }

    [StaFact]
    public void TableVerticalMerge_RendersFiniteMergedRegion_AndRoundTrips()
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument();
        var view = new DocumentView();
        view.LoadModel(doc);

        var renderedTable = RenderedTables(view.Document).Should().ContainSingle().Subject;
        var rows = renderedTable.RowGroups.SelectMany(group => group.Rows).ToList();
        var restartCell = rows[1].Cells[0];
        var continuationCell = rows[2].Cells[0];

        rows.Should().HaveCount(5);
        rows[1].Cells.Should().HaveCount(4);
        rows[2].Cells.Should().HaveCount(4);
        restartCell.Background.Should().BeOfType<System.Windows.Media.SolidColorBrush>();
        continuationCell.Background.Should().BeOfType<System.Windows.Media.SolidColorBrush>();
        ((System.Windows.Media.SolidColorBrush)continuationCell.Background!).Color
            .Should().Be(((System.Windows.Media.SolidColorBrush)restartCell.Background!).Color);
        restartCell.BorderThickness.Bottom.Should().Be(0);
        continuationCell.BorderThickness.Top.Should().Be(0);

        view.CommitToModel();
        var committedTable = view.Model.Blocks.OfType<Table>().Should().ContainSingle().Subject;
        committedTable.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        committedTable.Rows[2].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);
    }

    [StaFact]
    public void TableRepeatHeader_RenderedRows_DoNotRoundTripIntoModel()
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var modelTable = doc.Blocks.OfType<Table>().Single();
        var pagination = DocumentViewLayoutPlanner.BuildTablePaginationPlan(modelTable, doc.Page);
        var repeatedPage = pagination.Pages.Single(page => page.IncludesRepeatedHeader);

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpfTables = RenderedTables(view.Document);
        var tableSections = RenderedTableSections(view.Document);
        wpfTables.Should().HaveCount(pagination.Pages.Count);
        tableSections.Should().HaveCount(pagination.Pages.Count);
        tableSections[0].BreakPageBefore.Should().BeFalse();
        tableSections[1].BreakPageBefore.Should().BeTrue();
        wpfTables[0].RowGroups.SelectMany(group => group.Rows).Should().HaveCount(5);
        wpfTables[1].RowGroups.SelectMany(group => group.Rows).Should().HaveCount(5);
        wpfTables[1].RowGroups[0].Rows[1].Cells[0].Padding.Top.Should().Be(2,
            "the no-cell-spacing pagination control must retain its original vertical padding");
        CellContentStack(wpfTables[1].RowGroups[0].Rows[1].Cells[0]).RenderTransform
            .Should().BeSameAs(System.Windows.Media.Transform.Identity,
                "the no-cell-spacing pagination control keeps its existing content baseline");

        var renderedRows = wpfTables.SelectMany(table => table.RowGroups.SelectMany(group => group.Rows)).ToList();
        renderedRows.Should().HaveCount(modelTable.Rows.Count + repeatedPage.RepeatedHeaderRowIndexes.Count);
        var secondPageRows = wpfTables[1].RowGroups.SelectMany(group => group.Rows).ToList();
        RenderedRowText(secondPageRows[0]).Should().Contain("Step");
        RenderedRowText(secondPageRows[0]).Should().Contain("Pagination evidence");
        RenderedRowText(secondPageRows[1]).Should().Contain("Row 5");

        view.CommitToModel();

        var committedTable = view.Model.Blocks.OfType<Table>().Single();
        committedTable.Rows.Should().HaveCount(modelTable.Rows.Count);
        committedTable.Rows[0].Cells.Select(cell => cell.PlainText)
            .Should().Equal(modelTable.Rows[0].Cells.Select(cell => cell.PlainText));
    }

    [StaFact]
    public void CenteredFixedWidthPaginatedTable_RendersWithWordLikeBlockMargin()
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var table = doc.Blocks.OfType<Table>().Single();
        var view = new DocumentView();
        view.LoadModel(doc);

        var rendered = RenderedTables(view.Document).First();
        var contentWidth = DocumentViewLayoutPlanner.BuildPageMetrics(doc.Page).ContentWidthDip;
        var expected = (contentWidth - table.PreferredWidthPt!.Value * (96.0 / 72.0)) / 2;

        Assert.InRange(rendered.Margin.Left, expected - 0.01, expected + 0.01);
        Assert.InRange(rendered.Margin.Right, expected - 0.01, expected + 0.01);
    }

    [StaFact]
    public void CenteredFixedWidthFlowTable_UsesTheAuthoredWidthConstraint()
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument();
        var sourceTable = doc.Blocks.OfType<Table>().Single();
        var view = new DocumentView();
        view.LoadModel(doc);

        var rendered = RenderedTables(view.Document).First();
        rendered.Margin.Left.Should().Be(0);
        rendered.Margin.Right.Should().Be(0);
        rendered.RowGroups[0].Rows[0].Cells[0].Padding.Top.Should()
            .BeApproximately(2 + sourceTable.CellSpacingPt!.Value * (96.0 / 72.0), 0.01);
        rendered.RowGroups[0].Rows[0].Cells[0].Background.Should().NotBeNull(
            "ordinary flow tables retain their existing WPF cell-surface ownership");
    }

    [StaFact]
    public void LeftAlignedPreferredWidthFlowTable_ReservesTrailingWidth()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(1, 2);
        table.PreferredWidthPt = 460;
        table.Alignment = TableAlignment.Left;
        table.ColumnWidthsPt.AddRange([230.0, 230.0]);
        doc.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(doc);

        var rendered = RenderedTables(view.Document).Single();
        var contentWidth = DocumentViewLayoutPlanner.BuildPageMetrics(doc.Page).ContentWidthDip;
        var expectedTrailing = contentWidth - 460 * (96.0 / 72.0);
        rendered.Margin.Left.Should().Be(0);
        rendered.Margin.Right.Should().BeApproximately(expectedTrailing, 0.01);
    }

    [StaFact]
    public void TablePagination_WithoutRepeatHeader_RendersPlannedPageBreakSegments()
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var modelTable = doc.Blocks.OfType<Table>().Single();
        modelTable.Formatting = modelTable.Formatting with { RepeatHeaderRow = false };
        var pagination = DocumentViewLayoutPlanner.BuildTablePaginationPlan(modelTable, doc.Page);

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpfTables = RenderedTables(view.Document);
        var tableSections = RenderedTableSections(view.Document);
        wpfTables.Should().HaveCount(pagination.Pages.Count);
        tableSections.Should().HaveCount(pagination.Pages.Count);
        tableSections[1].BreakPageBefore.Should().BeTrue();
        var secondPageRows = wpfTables[1].RowGroups.SelectMany(group => group.Rows).ToList();
        secondPageRows.Should().HaveCount(4);
        RenderedRowText(secondPageRows[0]).Should().Contain("Row 5");
        RenderedRowText(secondPageRows[0]).Should().NotContain("Pagination evidence");

        view.CommitToModel();

        var committedTable = view.Model.Blocks.OfType<Table>().Single();
        committedTable.Rows.Should().HaveCount(modelTable.Rows.Count);
        committedTable.Formatting.RepeatHeaderRow.Should().BeFalse();
    }

    [StaFact]
    public void TablePageCompositionStress_RendersWordLikePhysicalSegments()
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildTablePageCompositionStressDocument();
        var sourceTable = doc.Blocks.OfType<Table>().Single();
        var pagination = DocumentViewLayoutPlanner.BuildTableLayoutPlans(doc).Single().Pagination;

        var view = new DocumentView();
        view.LoadModel(doc);

        var tables = RenderedTables(view.Document);
        var sections = RenderedTableSections(view.Document);
        tables.Should().HaveCount(3);
        tables.Count.Should().Be(pagination.Pages.Count);
        pagination.Pages[0].SourceRowIndexes.Should().Equal(0, 1, 2);
        pagination.Pages[1].SourceRowIndexes.Should().Equal(3, 4, 5, 6);
        pagination.Pages[2].SourceRowIndexes.Should().Equal(7, 8);
        sections.Should().HaveCount(3);
        sections.Select(section => section.BreakPageBefore).Should().Equal(false, true, true);

        var pageRows = tables
            .Select(table => table.RowGroups.SelectMany(group => group.Rows).ToList())
            .ToList();
        pageRows.Select(rows => rows.Count).Should().Equal(3, 5, 3);
        var expectedVerticalPadding = 2 + sourceTable.CellSpacingPt!.Value * (96.0 / 72.0);
        tables[1].RowGroups[0].Rows[1].Cells[0].Padding.Top.Should()
            .BeApproximately(expectedVerticalPadding, 0.01);
        tables[0].RowGroups[0].Rows[0].Cells[0].Padding.Top.Should()
            .BeApproximately(expectedVerticalPadding, 0.01);
        var spacedCell = tables[1].RowGroups[0].Rows[1].Cells[0];
        spacedCell.Background.Should().BeNull(
            "paginated tables reserve the authored cell-spacing gutter outside the inner surface");
        var spacingDip = sourceTable.CellSpacingPt.Value * (96.0 / 72.0);
        var spacedRow = tables[1].RowGroups[0].Rows[1];
        var firstSurface = SpacedCellSurface(spacedRow.Cells[0]);
        var internalSurface = SpacedCellSurface(spacedRow.Cells[1]);
        var lastSurface = SpacedCellSurface(spacedRow.Cells[^1]);
        firstSurface.Background.Should().NotBeNull();
        firstSurface.Margin.Left.Should().BeApproximately(spacingDip / 2, 0.01);
        internalSurface.Margin.Left.Should().BeApproximately(-spacingDip, 0.01);
        lastSurface.Margin.Right.Should().BeApproximately(spacingDip, 0.01);
        var resolvedMargins = sourceTable.Rows[3].Cells[0].Margins ?? sourceTable.DefaultCellMargins!;
        var contentTransform = CellContentStack(spacedCell).RenderTransform
            .Should().BeOfType<System.Windows.Media.TranslateTransform>().Subject;
        contentTransform.X.Should().BeApproximately(
            Math.Max(0, resolvedMargins.LeftPt * (96.0 / 72.0) - 6.0),
            0.01,
            "the resolved left margin contributes only the inset not already owned by WPF's cell hosts");
        contentTransform.Y.Should().BeApproximately(
            resolvedMargins.TopPt * (96.0 / 72.0),
            0.01,
            "the resolved per-cell top margin registers content without changing exact row measurement");
        RenderedRowText(pageRows[0][0]).Should().Contain("Page area");
        RenderedRowText(pageRows[0][1]).Should().Contain("Segment 1");
        RenderedRowText(pageRows[0][2]).Should().Contain("Segment 2");
        RenderedRowText(pageRows[1][0]).Should().Contain("Page area");
        RenderedRowText(pageRows[1][1]).Should().Contain("Segment 3");
        RenderedRowText(pageRows[1][4]).Should().Contain("Segment 6");
        RenderedRowText(pageRows[2][0]).Should().Contain("Page area");
        RenderedRowText(pageRows[2][1]).Should().Contain("Segment 7");
        RenderedRowText(pageRows[2][2]).Should().Contain("Segment 8");
        pageRows[2].Select(RenderedRowText).Should().OnlyContain(text => !string.IsNullOrWhiteSpace(text));

        view.CommitToModel();
        view.Model.Blocks.OfType<Table>().Should().ContainSingle()
            .Which.Rows.Should().HaveCount(sourceTable.Rows.Count);
    }

    private static string RenderedRowText(System.Windows.Documents.TableRow row)
    {
        var text = row.Cells.SelectMany(RenderedCellParagraphs)
            .Select(paragraph => new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim());
        return string.Join(" ", text);
    }

    [StaFact]
    public void HiddenText_CollapsesInTheEditorAndSurvivesRoundTrip()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Visible A"));
        paragraph.Runs.Add(new Run("SECRET", RunFormatting.Default with
        {
            Hidden = true,
            FontSizePt = 17,
            ColorHex = "#123456",
        }));
        paragraph.Runs.Add(new Run("Visible B"));
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);

        var renderedParagraph = view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .Single();
        var renderedRuns = renderedParagraph.Inlines
            .OfType<System.Windows.Documents.Run>()
            .ToArray();
        var hidden = renderedRuns.Single(run => run.Text == "SECRET");
        hidden.FontSize.Should().Be(0.015);
        hidden.Foreground.Should().BeSameAs(System.Windows.Media.Brushes.Transparent);
        hidden.Background.Should().BeNull();
        hidden.TextDecorations.Should().BeNull();

        view.CommitToModel();

        var recovered = view.Model.Paragraphs.Single().Runs;
        recovered.Select(run => run.Text).Should().Equal("Visible A", "SECRET", "Visible B");
        recovered[1].Formatting.Hidden.Should().BeTrue();
        recovered[1].Formatting.FontSizePt.Should().Be(17);
        recovered[1].Formatting.ColorHex.Should().Be("#123456");
        recovered[0].Formatting.Hidden.Should().BeFalse();
        recovered[2].Formatting.Hidden.Should().BeFalse();
    }

    [StaFact]
    public void WebHiddenText_CollapsesOnlyInWebLayoutAndSurvivesRoundTrip()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Visible A"));
        paragraph.Runs.Add(new Run("WEB_ONLY", RunFormatting.Default with
        {
            WebHidden = true,
            FontSizePt = 16,
            ColorHex = "#345678",
        }));
        paragraph.Runs.Add(new Run("Visible B"));
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);

        System.Windows.Documents.Run RenderedWebRun() => view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .Single()
            .Inlines
            .OfType<System.Windows.Documents.Run>()
            .Single(run => run.Text == "WEB_ONLY");

        RenderedWebRun().FontSize.Should().BeGreaterThan(1);

        view.SetViewMode(DocumentViewMode.WebLayout);
        RenderedWebRun().FontSize.Should().Be(0.015);
        RenderedWebRun().Foreground.Should().BeSameAs(System.Windows.Media.Brushes.Transparent);

        view.SetViewMode(DocumentViewMode.Draft);
        RenderedWebRun().FontSize.Should().BeGreaterThan(1);
        RenderedWebRun().Foreground.Should().NotBeSameAs(System.Windows.Media.Brushes.Transparent);

        view.CommitToModel();
        var recovered = view.Model.Paragraphs.Single().Runs.Single(run => run.Text == "WEB_ONLY");
        recovered.Formatting.WebHidden.Should().BeTrue();
        recovered.Formatting.Hidden.Should().BeFalse();
        recovered.Formatting.FontSizePt.Should().Be(16);
        recovered.Formatting.ColorHex.Should().Be("#345678");
    }

    private static System.Windows.Controls.Border SpacedCellSurface(System.Windows.Documents.TableCell cell) =>
        cell.Blocks.OfType<BlockUIContainer>().Single().Child
            .Should().BeOfType<System.Windows.Controls.Grid>().Subject.Children
            .OfType<System.Windows.Controls.Border>().Single();

    private static System.Windows.Controls.StackPanel CellContentStack(System.Windows.Documents.TableCell cell) =>
        cell.Blocks.OfType<BlockUIContainer>().Single().Child
            .Should().BeOfType<System.Windows.Controls.Grid>().Subject.Children
            .OfType<System.Windows.Controls.StackPanel>().Single();

    private static List<System.Windows.Documents.Table> RenderedTables(FlowDocument document)
    {
        static IEnumerable<System.Windows.Documents.Block> FlattenSections(BlockCollection blocks)
        {
            foreach (var block in blocks)
            {
                if (block is System.Windows.Documents.Section section)
                {
                    foreach (var nested in FlattenSections(section.Blocks))
                        yield return nested;
                }
                else
                {
                    yield return block;
                }
            }
        }

        return FlattenSections(document.Blocks)
            .OfType<System.Windows.Documents.Table>()
            .ToList();
    }

    private static List<System.Windows.Documents.Section> RenderedTableSections(FlowDocument document) =>
        document.Blocks
            .OfType<System.Windows.Documents.Section>()
            .Where(section => section.Blocks.OfType<System.Windows.Documents.Table>().Any())
            .ToList();

    private static IEnumerable<System.Windows.Documents.Paragraph> RenderedCellParagraphs(System.Windows.Documents.TableCell cell)
    {
        foreach (var paragraph in cell.Blocks.OfType<System.Windows.Documents.Paragraph>())
            yield return paragraph;

        foreach (var blockUi in cell.Blocks.OfType<BlockUIContainer>())
        {
            if (blockUi.Child is null)
                continue;

            foreach (var richTextBox in RichTextBoxes(blockUi.Child))
            {
                foreach (var paragraph in richTextBox.Document.Blocks.OfType<System.Windows.Documents.Paragraph>())
                    yield return paragraph;
            }
        }
    }

    private static IEnumerable<System.Windows.Controls.RichTextBox> RichTextBoxes(System.Windows.DependencyObject root)
    {
        if (root is System.Windows.Controls.RichTextBox richTextBox)
            yield return richTextBox;

        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        {
            foreach (var nested in RichTextBoxes(child))
                yield return nested;
        }
    }

    // A valid 1x1 PNG so the WPF image decoder in BuildImageRun succeeds under test.
    private static byte[] OnePixelPng() => System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
