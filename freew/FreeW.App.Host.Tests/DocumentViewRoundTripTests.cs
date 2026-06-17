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

    // A valid 1x1 PNG so the WPF image decoder in BuildImageRun succeeds under test.
    private static byte[] OnePixelPng() => System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
