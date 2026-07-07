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
    public void TableRow_ExplicitHeight_SpacerInjected()
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

        // A BlockUIContainer containing a Border with MinHeight must be present.
        var spacerContainer = wpfCell.Blocks.OfType<BlockUIContainer>()
            .FirstOrDefault(b => b.Child is System.Windows.Controls.Border);
        spacerContainer.Should().NotBeNull("height-enforcer spacer must be injected into the cell");

        var border = (System.Windows.Controls.Border)spacerContainer!.Child;
        border.MinHeight.Should().BeApproximately(heightPt * pxPerPt, 0.01,
            "spacer MinHeight must equal HeightPt × PxPerPoint");
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
    public void TableRepeatHeader_RenderedRows_DoNotRoundTripIntoModel()
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var modelTable = doc.Blocks.OfType<Table>().Single();
        var pagination = DocumentViewLayoutPlanner.BuildTablePaginationPlan(modelTable, doc.Page);
        var repeatedPage = pagination.Pages.Single(page => page.IncludesRepeatedHeader);
        var firstPageRowIndex = repeatedPage.SourceRowIndexes[0];

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpfTables = view.Document.Blocks.OfType<System.Windows.Documents.Table>().ToList();
        wpfTables.Should().HaveCount(pagination.Pages.Count);
        wpfTables[0].BreakPageBefore.Should().BeFalse();
        wpfTables[1].BreakPageBefore.Should().BeTrue();
        wpfTables[0].RowGroups.SelectMany(group => group.Rows).Should().HaveCount(pagination.Pages[0].RenderRows.Count);
        wpfTables[1].RowGroups.SelectMany(group => group.Rows).Should().HaveCount(pagination.Pages[1].RenderRows.Count);

        var renderedRows = wpfTables.SelectMany(table => table.RowGroups.SelectMany(group => group.Rows)).ToList();
        renderedRows.Should().HaveCount(modelTable.Rows.Count + repeatedPage.RepeatedHeaderRowIndexes.Count);
        var secondPageRows = wpfTables[1].RowGroups.SelectMany(group => group.Rows).ToList();
        RenderedRowText(secondPageRows[0]).Should().Contain("Step");
        RenderedRowText(secondPageRows[0]).Should().Contain("Pagination evidence");
        RenderedRowText(secondPageRows[1]).Should().Contain($"Row {firstPageRowIndex}");

        view.CommitToModel();

        var committedTable = view.Model.Blocks.OfType<Table>().Single();
        committedTable.Rows.Should().HaveCount(modelTable.Rows.Count);
        committedTable.Rows[0].Cells.Select(cell => cell.PlainText)
            .Should().Equal(modelTable.Rows[0].Cells.Select(cell => cell.PlainText));
    }

    [StaFact]
    public void TablePagination_WithoutRepeatHeader_RendersPlannedPageBreakSegments()
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var modelTable = doc.Blocks.OfType<Table>().Single();
        modelTable.Formatting = modelTable.Formatting with { RepeatHeaderRow = false };
        var pagination = DocumentViewLayoutPlanner.BuildTablePaginationPlan(modelTable, doc.Page);
        var secondPageFirstRow = pagination.Pages[1].SourceRowIndexes[0];

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpfTables = view.Document.Blocks.OfType<System.Windows.Documents.Table>().ToList();
        wpfTables.Should().HaveCount(pagination.Pages.Count);
        wpfTables[1].BreakPageBefore.Should().BeTrue();
        var secondPageRows = wpfTables[1].RowGroups.SelectMany(group => group.Rows).ToList();
        RenderedRowText(secondPageRows[0]).Should().Contain($"Row {secondPageFirstRow}");
        RenderedRowText(secondPageRows[0]).Should().NotContain("Pagination evidence");

        view.CommitToModel();

        var committedTable = view.Model.Blocks.OfType<Table>().Single();
        committedTable.Rows.Should().HaveCount(modelTable.Rows.Count);
        committedTable.Formatting.RepeatHeaderRow.Should().BeFalse();
    }

    private static string RenderedRowText(System.Windows.Documents.TableRow row)
    {
        var text = row.Cells.SelectMany(RenderedCellParagraphs)
            .Select(paragraph => new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim());
        return string.Join(" ", text);
    }

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
