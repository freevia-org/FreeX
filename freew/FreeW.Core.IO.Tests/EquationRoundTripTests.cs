using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for inline OMML equations (roadmap item W1): a <see cref="Run.Equation"/> must
/// survive write→read, emit a valid inline <c>m:oMath</c>, and declare the <c>m</c> namespace on the
/// document root.
/// </summary>
public class EquationRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument WriteDocumentXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    [Fact]
    public void PlainTextEquation_SurvivesRoundTrip()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("see "));
        paragraph.Runs.Add(Run.FromEquation(Equation.FromText("a + b = c")));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        var equationRun = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null);
        var equation = equationRun.Equation!;
        equation.Runs.Should().ContainSingle();
        equation.Runs[0].Kind.Should().Be(MathRunKind.Text);
        equation.Runs[0].Text.Should().Be("a + b = c");
        // The host run's fallback text mirrors the equation's linear form.
        equationRun.Text.Should().Be("a + b = c");
    }

    [Fact]
    public void SuperscriptEquation_SurvivesRoundTrip()
    {
        // E = mc^2 modelled as: text "E = mc" + superscript base "c"... but the classic form is the
        // exponent on a single base, so: text "E = m" + (base "c", sup "2").
        var equation = new Equation([
            MathRun.PlainText("E = m"),
            MathRun.Superscript("c", "2")
        ]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.Runs.Should().HaveCount(2);
        roundTripped.Runs[0].Kind.Should().Be(MathRunKind.Text);
        roundTripped.Runs[0].Text.Should().Be("E = m");
        roundTripped.Runs[1].Kind.Should().Be(MathRunKind.Superscript);
        roundTripped.Runs[1].Base.Should().Be("c");
        roundTripped.Runs[1].Sup.Should().Be("2");
        roundTripped.LinearText.Should().Be("E = mc^2");
    }

    [Fact]
    public void FractionEquation_SurvivesRoundTrip()
    {
        var equation = new Equation([MathRun.Fraction("1", "2")]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.Runs.Should().ContainSingle();
        roundTripped.Runs[0].Kind.Should().Be(MathRunKind.Fraction);
        roundTripped.Runs[0].Numerator.Should().Be("1");
        roundTripped.Runs[0].Denominator.Should().Be("2");
        roundTripped.LinearText.Should().Be("1/2");
    }

    [Fact]
    public void Equation_EmitsInlineOMathWithMathNamespaceDeclared()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(new Equation([
            MathRun.PlainText("E = m"),
            MathRun.Superscript("c", "2")
        ])));
        doc.Blocks.Add(paragraph);

        var xml = WriteDocumentXml(doc);

        // The m namespace must be declared on the document root.
        xml.Root!.Attribute(XNamespace.Xmlns + "m")!.Value.Should().Be(M.NamespaceName);

        // The equation serialises as an inline m:oMath that is a direct child of the paragraph.
        var oMath = xml.Descendants(M + "oMath").Single();
        oMath.Parent!.Name.Should().Be(W + "p");
        oMath.Elements(M + "r").Should().ContainSingle();
        oMath.Elements(M + "sSup").Should().ContainSingle();
    }

    [Fact]
    public void Equation_RoundTripsInsideTableCell()
    {
        // Equations are an inline run mark, so they must flow through table cells like any other run.
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(Run.FromEquation(Equation.FromText("x^n")));
        var doc = new TextDocument();
        doc.Blocks.Add(table);

        var read = RoundTrip(doc);

        var cellParagraph = ((Table)read.Blocks.Single()).Rows[0].Cells[0].Paragraphs.Single();
        cellParagraph.Runs.Single(r => r.Equation is not null).Equation!.LinearText.Should().Be("x^n");
    }
}
