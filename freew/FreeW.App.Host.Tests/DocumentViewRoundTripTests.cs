using System.IO;
using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
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

    // A valid 1x1 PNG so the WPF image decoder in BuildImageRun succeeds under test.
    private static byte[] OnePixelPng() => System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
