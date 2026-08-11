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

    private static TextDocument ReadDocumentXml(string documentXml)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(documentXml);
        }

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
    public void NestedScriptSlots_SurviveRoundTripAndEmitDirectSlotChildren()
    {
        var baseEquation = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Subscript("x", "1")
        ]);
        var subEquation = new Equation([
            MathRun.PlainText("i+"),
            MathRun.Superscript("j", "2")
        ]);
        var supEquation = new Equation([
            MathRun.PlainText("n+"),
            MathRun.Subscript("k", "0")
        ]);
        var equation = new Equation([
            MathRun.Superscript(baseEquation, supEquation),
            MathRun.Subscript(baseEquation, subEquation),
            MathRun.SubSuperscript(baseEquation, subEquation, supEquation)
        ]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);
        var xml = WriteDocumentXml(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.Runs.Select(run => run.Kind).Should().Equal(
            MathRunKind.Superscript,
            MathRunKind.Subscript,
            MathRunKind.SubSuperscript);
        var superscript = roundTripped.Runs[0];
        superscript.Base.Should().Be("a+x1");
        superscript.Sup.Should().Be("n+k0");
        superscript.ScriptBaseEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        superscript.ScriptSupEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        var subscript = roundTripped.Runs[1];
        subscript.ScriptBaseEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        subscript.ScriptSubEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        var subSuperscript = roundTripped.Runs[2];
        subSuperscript.ScriptBaseEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        subSuperscript.ScriptSubEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        subSuperscript.ScriptSupEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        roundTripped.LinearText.Should().Be("a+x_1^n+k_0a+x_1_i+j^2a+x_1_i+j^2^n+k_0");

        var oMath = xml.Descendants(M + "oMath").Single();
        var writtenSuperscript = oMath.Elements(M + "sSup").Single();
        writtenSuperscript.Element(M + "e")!.Elements(M + "oMath").Should().BeEmpty();
        writtenSuperscript.Element(M + "e")!.Elements(M + "sSub").Should().ContainSingle();
        writtenSuperscript.Element(M + "sup")!.Elements(M + "sSub").Should().ContainSingle();

        var writtenSubscript = oMath.Elements(M + "sSub").Single();
        writtenSubscript.Element(M + "e")!.Elements(M + "sSub").Should().ContainSingle();
        writtenSubscript.Element(M + "sub")!.Elements(M + "sSup").Should().ContainSingle();

        var writtenSubSuperscript = oMath.Elements(M + "sSubSup").Single();
        writtenSubSuperscript.Element(M + "e")!.Elements(M + "sSub").Should().ContainSingle();
        writtenSubSuperscript.Element(M + "sub")!.Elements(M + "sSup").Should().ContainSingle();
        writtenSubSuperscript.Element(M + "sup")!.Elements(M + "sSub").Should().ContainSingle();
    }

    [Fact]
    public void RawNestedScriptSlots_ReadAsNestedEquations()
    {
        var documentXml = $$"""
            <w:document xmlns:w="{{W.NamespaceName}}" xmlns:m="{{M.NamespaceName}}">
              <w:body>
                <w:p>
                  <m:oMath>
                    <m:sSup>
                      <m:e>
                        <m:r><m:t>a+</m:t></m:r>
                        <m:sSub>
                          <m:e><m:r><m:t>x</m:t></m:r></m:e>
                          <m:sub><m:r><m:t>1</m:t></m:r></m:sub>
                        </m:sSub>
                      </m:e>
                      <m:sup>
                        <m:r><m:t>n+</m:t></m:r>
                        <m:sSub>
                          <m:e><m:r><m:t>k</m:t></m:r></m:e>
                          <m:sub><m:r><m:t>0</m:t></m:r></m:sub>
                        </m:sSub>
                      </m:sup>
                    </m:sSup>
                    <m:sSub>
                      <m:e>
                        <m:r><m:t>a+</m:t></m:r>
                        <m:sSub>
                          <m:e><m:r><m:t>x</m:t></m:r></m:e>
                          <m:sub><m:r><m:t>1</m:t></m:r></m:sub>
                        </m:sSub>
                      </m:e>
                      <m:sub>
                        <m:r><m:t>i+</m:t></m:r>
                        <m:sSup>
                          <m:e><m:r><m:t>j</m:t></m:r></m:e>
                          <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
                        </m:sSup>
                      </m:sub>
                    </m:sSub>
                    <m:sSubSup>
                      <m:e>
                        <m:r><m:t>a+</m:t></m:r>
                        <m:sSub>
                          <m:e><m:r><m:t>x</m:t></m:r></m:e>
                          <m:sub><m:r><m:t>1</m:t></m:r></m:sub>
                        </m:sSub>
                      </m:e>
                      <m:sub>
                        <m:r><m:t>i+</m:t></m:r>
                        <m:sSup>
                          <m:e><m:r><m:t>j</m:t></m:r></m:e>
                          <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
                        </m:sSup>
                      </m:sub>
                      <m:sup>
                        <m:r><m:t>n+</m:t></m:r>
                        <m:sSub>
                          <m:e><m:r><m:t>k</m:t></m:r></m:e>
                          <m:sub><m:r><m:t>0</m:t></m:r></m:sub>
                        </m:sSub>
                      </m:sup>
                    </m:sSubSup>
                  </m:oMath>
                </w:p>
              </w:body>
            </w:document>
            """;

        var read = ReadDocumentXml(documentXml);

        var equation = read.Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!;
        equation.Runs.Select(run => run.Kind).Should().Equal(
            MathRunKind.Superscript,
            MathRunKind.Subscript,
            MathRunKind.SubSuperscript);
        equation.Runs[0].ScriptBaseEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        equation.Runs[0].ScriptSupEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        equation.Runs[1].ScriptBaseEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        equation.Runs[1].ScriptSubEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        equation.Runs[2].ScriptBaseEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        equation.Runs[2].ScriptSubEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        equation.Runs[2].ScriptSupEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        equation.LinearText.Should().Be("a+x_1^n+k_0a+x_1_i+j^2a+x_1_i+j^2^n+k_0");
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
    public void NestedFractionSlots_SurviveRoundTripAndEmitDirectSlotChildren()
    {
        var equation = new Equation([
            MathRun.Fraction(
                new Equation([
                    MathRun.PlainText("a+"),
                    MathRun.Superscript("x", "2")
                ]),
                new Equation([
                    MathRun.PlainText("b+"),
                    MathRun.Subscript("y", "1")
                ]))
        ]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);
        var xml = WriteDocumentXml(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.Runs.Should().ContainSingle();
        var fraction = roundTripped.Runs[0];
        fraction.Kind.Should().Be(MathRunKind.Fraction);
        fraction.Numerator.Should().Be("a+x2");
        fraction.Denominator.Should().Be("b+y1");
        fraction.NumeratorEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        fraction.NumeratorEquation.Runs[1].Base.Should().Be("x");
        fraction.NumeratorEquation.Runs[1].Sup.Should().Be("2");
        fraction.DenominatorEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        fraction.DenominatorEquation.Runs[1].Base.Should().Be("y");
        fraction.DenominatorEquation.Runs[1].Sub.Should().Be("1");
        roundTripped.LinearText.Should().Be("a+x^2/b+y_1");

        var writtenFraction = xml.Descendants(M + "f").Single();
        var num = writtenFraction.Element(M + "num")!;
        var den = writtenFraction.Element(M + "den")!;
        num.Elements(M + "oMath").Should().BeEmpty();
        den.Elements(M + "oMath").Should().BeEmpty();
        num.Elements(M + "r").Should().ContainSingle();
        num.Elements(M + "sSup").Should().ContainSingle();
        den.Elements(M + "r").Should().ContainSingle();
        den.Elements(M + "sSub").Should().ContainSingle();
    }

    [Fact]
    public void RawNestedFractionSlots_ReadAsNestedEquations()
    {
        var documentXml = $$"""
            <w:document xmlns:w="{{W.NamespaceName}}" xmlns:m="{{M.NamespaceName}}">
              <w:body>
                <w:p>
                  <m:oMath>
                    <m:f>
                      <m:num>
                        <m:r><m:t>a+</m:t></m:r>
                        <m:sSup>
                          <m:e><m:r><m:t>x</m:t></m:r></m:e>
                          <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
                        </m:sSup>
                      </m:num>
                      <m:den>
                        <m:r><m:t>b+</m:t></m:r>
                        <m:sSub>
                          <m:e><m:r><m:t>y</m:t></m:r></m:e>
                          <m:sub><m:r><m:t>1</m:t></m:r></m:sub>
                        </m:sSub>
                      </m:den>
                    </m:f>
                  </m:oMath>
                </w:p>
              </w:body>
            </w:document>
            """;

        var read = ReadDocumentXml(documentXml);

        var equation = read.Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!;
        equation.Runs.Should().ContainSingle();
        var fraction = equation.Runs[0];
        fraction.Kind.Should().Be(MathRunKind.Fraction);
        fraction.NumeratorEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        fraction.DenominatorEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        equation.LinearText.Should().Be("a+x^2/b+y_1");
    }

    [Fact]
    public void NestedRadicalRadicand_SurvivesRoundTripAndEmitsDirectSlotChildren()
    {
        var equation = new Equation([
            MathRun.Radical(
                new Equation([
                    MathRun.PlainText("a+"),
                    MathRun.Superscript("x", "2")
                ]),
                "3")
        ]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);
        var xml = WriteDocumentXml(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.Runs.Should().ContainSingle();
        var radical = roundTripped.Runs[0];
        radical.Kind.Should().Be(MathRunKind.Radical);
        radical.Base.Should().Be("a+x2");
        radical.Degree.Should().Be("3");
        radical.RadicandEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        radical.RadicandEquation.Runs[1].Base.Should().Be("x");
        radical.RadicandEquation.Runs[1].Sup.Should().Be("2");
        roundTripped.LinearText.Should().Be("3\u221a(a+x^2)");

        var writtenRadical = xml.Descendants(M + "rad").Single();
        writtenRadical.Elements().Select(element => element.Name.LocalName)
            .Should().Equal("radPr", "deg", "e");
        var radicand = writtenRadical.Element(M + "e")!;
        radicand.Elements(M + "oMath").Should().BeEmpty();
        radicand.Elements(M + "r").Should().ContainSingle();
        radicand.Elements(M + "sSup").Should().ContainSingle();
    }

    [Fact]
    public void NestedRadicalDegree_SurvivesRoundTripAndEmitsDirectSlotChildren()
    {
        var equation = new Equation([
            MathRun.Radical(
                new Equation([
                    MathRun.PlainText("a+"),
                    MathRun.Superscript("x", "2")
                ]),
                new Equation([
                    MathRun.PlainText("n+"),
                    MathRun.Subscript("k", "1")
                ]))
        ]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);
        var xml = WriteDocumentXml(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.Runs.Should().ContainSingle();
        var radical = roundTripped.Runs[0];
        radical.Kind.Should().Be(MathRunKind.Radical);
        radical.Base.Should().Be("a+x2");
        radical.Degree.Should().Be("n+k1");
        radical.RadicandEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        radical.DegreeEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        radical.DegreeEquation.Runs[1].Base.Should().Be("k");
        radical.DegreeEquation.Runs[1].Sub.Should().Be("1");
        roundTripped.LinearText.Should().Be("n+k_1\u221a(a+x^2)");

        var writtenRadical = xml.Descendants(M + "rad").Single();
        writtenRadical.Elements().Select(element => element.Name.LocalName)
            .Should().Equal("radPr", "deg", "e");
        var degree = writtenRadical.Element(M + "deg")!;
        degree.Elements(M + "oMath").Should().BeEmpty();
        degree.Elements(M + "r").Should().ContainSingle();
        degree.Elements(M + "sSub").Should().ContainSingle();
        var radicand = writtenRadical.Element(M + "e")!;
        radicand.Elements(M + "oMath").Should().BeEmpty();
        radicand.Elements(M + "sSup").Should().ContainSingle();
    }

    [Fact]
    public void RawNestedRadicalRadicand_ReadsAsNestedEquation()
    {
        var documentXml = $$"""
            <w:document xmlns:w="{{W.NamespaceName}}" xmlns:m="{{M.NamespaceName}}">
              <w:body>
                <w:p>
                  <m:oMath>
                    <m:rad>
                      <m:radPr><m:degHide m:val="0" /></m:radPr>
                      <m:deg><m:r><m:t>3</m:t></m:r></m:deg>
                      <m:e>
                        <m:r><m:t>a+</m:t></m:r>
                        <m:sSup>
                          <m:e><m:r><m:t>x</m:t></m:r></m:e>
                          <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
                        </m:sSup>
                      </m:e>
                    </m:rad>
                  </m:oMath>
                </w:p>
              </w:body>
            </w:document>
            """;

        var read = ReadDocumentXml(documentXml);

        var equation = read.Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!;
        equation.Runs.Should().ContainSingle();
        var radical = equation.Runs[0];
        radical.Kind.Should().Be(MathRunKind.Radical);
        radical.Degree.Should().Be("3");
        radical.RadicandEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        equation.LinearText.Should().Be("3\u221a(a+x^2)");
    }

    [Fact]
    public void RawNestedRadicalDegree_ReadsAsNestedEquation()
    {
        var documentXml = $$"""
            <w:document xmlns:w="{{W.NamespaceName}}" xmlns:m="{{M.NamespaceName}}">
              <w:body>
                <w:p>
                  <m:oMath>
                    <m:rad>
                      <m:radPr><m:degHide m:val="0" /></m:radPr>
                      <m:deg>
                        <m:r><m:t>n+</m:t></m:r>
                        <m:sSub>
                          <m:e><m:r><m:t>k</m:t></m:r></m:e>
                          <m:sub><m:r><m:t>1</m:t></m:r></m:sub>
                        </m:sSub>
                      </m:deg>
                      <m:e><m:r><m:t>x</m:t></m:r></m:e>
                    </m:rad>
                  </m:oMath>
                </w:p>
              </w:body>
            </w:document>
            """;

        var read = ReadDocumentXml(documentXml);

        var equation = read.Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!;
        equation.Runs.Should().ContainSingle();
        var radical = equation.Runs[0];
        radical.Kind.Should().Be(MathRunKind.Radical);
        radical.Degree.Should().Be("n+k1");
        radical.DegreeEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        radical.RadicandEquation.Should().BeNull();
        equation.LinearText.Should().Be("n+k_1\u221a(x)");
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

    private static Equation RoundTripEquation(Equation equation)
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);
        return read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
    }

    [Fact]
    public void SubscriptEquation_SurvivesRoundTrip()
    {
        var read = RoundTripEquation(new Equation([MathRun.Subscript("x", "i")]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].Kind.Should().Be(MathRunKind.Subscript);
        read.Runs[0].Base.Should().Be("x");
        read.Runs[0].Sub.Should().Be("i");
        read.LinearText.Should().Be("x_i");
    }

    [Fact]
    public void SubSuperscriptEquation_SurvivesRoundTrip()
    {
        var read = RoundTripEquation(new Equation([MathRun.SubSuperscript("x", "i", "2")]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].Kind.Should().Be(MathRunKind.SubSuperscript);
        read.Runs[0].Base.Should().Be("x");
        read.Runs[0].Sub.Should().Be("i");
        read.Runs[0].Sup.Should().Be("2");
    }

    [Fact]
    public void SquareRootEquation_SurvivesRoundTrip()
    {
        var read = RoundTripEquation(new Equation([MathRun.Radical("x + 1")]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].Kind.Should().Be(MathRunKind.Radical);
        read.Runs[0].Base.Should().Be("x + 1");
        read.Runs[0].Degree.Should().BeEmpty();
        read.LinearText.Should().Be("√(x + 1)");
    }

    [Fact]
    public void NthRootEquation_SurvivesRoundTrip()
    {
        var read = RoundTripEquation(new Equation([MathRun.Radical("x", "3")]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].Kind.Should().Be(MathRunKind.Radical);
        read.Runs[0].Base.Should().Be("x");
        read.Runs[0].Degree.Should().Be("3");
        read.LinearText.Should().Be("3√(x)");
    }

    [Fact]
    public void NAryEquation_SurvivesRoundTrip()
    {
        var read = RoundTripEquation(new Equation([MathRun.NAry("∑", "i=1", "n", "i")]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].Kind.Should().Be(MathRunKind.NAry);
        read.Runs[0].Operator.Should().Be("∑");
        read.Runs[0].Sub.Should().Be("i=1");
        read.Runs[0].Sup.Should().Be("n");
        read.Runs[0].Base.Should().Be("i");
    }

    [Fact]
    public void NestedNArySlots_SurviveRoundTripAndEmitDirectSlotChildren()
    {
        var equation = new Equation([
            MathRun.NAry(
                "\u2211",
                new Equation([
                    MathRun.PlainText("i="),
                    MathRun.Subscript("j", "1")
                ]),
                new Equation([MathRun.Superscript("n", "2")]),
                new Equation([MathRun.Fraction("1", "i")]))
        ]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);
        var xml = WriteDocumentXml(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.Runs.Should().ContainSingle();
        var nary = roundTripped.Runs[0];
        nary.Kind.Should().Be(MathRunKind.NAry);
        nary.Operator.Should().Be("\u2211");
        nary.NAryLowerLimitEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        nary.NAryLowerLimitEquation.Runs[1].Base.Should().Be("j");
        nary.NAryLowerLimitEquation.Runs[1].Sub.Should().Be("1");
        nary.NAryUpperLimitEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Superscript);
        nary.NAryUpperLimitEquation.Runs[0].Base.Should().Be("n");
        nary.NAryUpperLimitEquation.Runs[0].Sup.Should().Be("2");
        nary.NAryOperandEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Fraction);
        nary.NAryOperandEquation.Runs[0].Numerator.Should().Be("1");
        nary.NAryOperandEquation.Runs[0].Denominator.Should().Be("i");
        roundTripped.LinearText.Should().Be("\u2211(i=j_1..n^2) 1/i");

        var writtenNAry = xml.Descendants(M + "nary").Single();
        var pr = writtenNAry.Element(M + "naryPr")!;
        pr.Element(M + "subHide")!.Attribute(M + "val")!.Value.Should().Be("0");
        pr.Element(M + "supHide")!.Attribute(M + "val")!.Value.Should().Be("0");
        var sub = writtenNAry.Element(M + "sub")!;
        var sup = writtenNAry.Element(M + "sup")!;
        var operand = writtenNAry.Element(M + "e")!;
        sub.Elements(M + "oMath").Should().BeEmpty();
        sup.Elements(M + "oMath").Should().BeEmpty();
        operand.Elements(M + "oMath").Should().BeEmpty();
        sub.Elements(M + "r").Should().ContainSingle();
        sub.Elements(M + "sSub").Should().ContainSingle();
        sup.Elements(M + "sSup").Should().ContainSingle();
        operand.Elements(M + "f").Should().ContainSingle();
    }

    [Fact]
    public void StructuredNAryEmptyLimits_EmitHiddenLimitProperties()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(new Equation([
            MathRun.NAry(
                "\u2211",
                new Equation(),
                new Equation(),
                Equation.FromText("x"))
        ])));
        doc.Blocks.Add(paragraph);

        var xml = WriteDocumentXml(doc);

        var writtenNAry = xml.Descendants(M + "nary").Single();
        var pr = writtenNAry.Element(M + "naryPr")!;
        pr.Element(M + "subHide")!.Attribute(M + "val")!.Value.Should().Be("1");
        pr.Element(M + "supHide")!.Attribute(M + "val")!.Value.Should().Be("1");
        writtenNAry.Element(M + "sub")!.Elements().Should().BeEmpty();
        writtenNAry.Element(M + "sup")!.Elements().Should().BeEmpty();
        writtenNAry.Element(M + "e")!.Elements(M + "r").Should().ContainSingle();
    }

    [Fact]
    public void RawNestedNArySlots_ReadAsNestedEquations()
    {
        var documentXml = $$"""
            <w:document xmlns:w="{{W.NamespaceName}}" xmlns:m="{{M.NamespaceName}}">
              <w:body>
                <w:p>
                  <m:oMath>
                    <m:nary>
                      <m:naryPr>
                        <m:chr m:val="&#x2211;" />
                        <m:subHide m:val="0" />
                        <m:supHide m:val="0" />
                      </m:naryPr>
                      <m:sub>
                        <m:r><m:t>i=</m:t></m:r>
                        <m:sSub>
                          <m:e><m:r><m:t>j</m:t></m:r></m:e>
                          <m:sub><m:r><m:t>1</m:t></m:r></m:sub>
                        </m:sSub>
                      </m:sub>
                      <m:sup>
                        <m:sSup>
                          <m:e><m:r><m:t>n</m:t></m:r></m:e>
                          <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
                        </m:sSup>
                      </m:sup>
                      <m:e>
                        <m:f>
                          <m:num><m:r><m:t>1</m:t></m:r></m:num>
                          <m:den><m:r><m:t>i</m:t></m:r></m:den>
                        </m:f>
                      </m:e>
                    </m:nary>
                  </m:oMath>
                </w:p>
              </w:body>
            </w:document>
            """;

        var read = ReadDocumentXml(documentXml);

        var equation = read.Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!;
        equation.Runs.Should().ContainSingle();
        var nary = equation.Runs[0];
        nary.Kind.Should().Be(MathRunKind.NAry);
        nary.Operator.Should().Be("\u2211");
        nary.NAryLowerLimitEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        nary.NAryUpperLimitEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Superscript);
        nary.NAryOperandEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Fraction);
        equation.LinearText.Should().Be("\u2211(i=j_1..n^2) 1/i");
    }

    [Fact]
    public void AccentEquation_SurvivesRoundTrip()
    {
        var read = RoundTripEquation(new Equation([MathRun.AccentOf("x", "→")]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].Kind.Should().Be(MathRunKind.Accent);
        read.Runs[0].Base.Should().Be("x");
        read.Runs[0].Accent.Should().Be("→");
        read.LinearText.Should().Be("x→");
    }

    [Fact]
    public void OverbarEquation_SurvivesRoundTrip()
    {
        var read = RoundTripEquation(new Equation([MathRun.BarOf("AB")]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].Kind.Should().Be(MathRunKind.Bar);
        read.Runs[0].Base.Should().Be("AB");
        read.Runs[0].BarTop.Should().BeTrue();
    }

    [Fact]
    public void UnderbarEquation_SurvivesRoundTrip()
    {
        var read = RoundTripEquation(new Equation([MathRun.BarOf("AB", top: false)]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].Kind.Should().Be(MathRunKind.Bar);
        read.Runs[0].Base.Should().Be("AB");
        read.Runs[0].BarTop.Should().BeFalse();
    }

    [Fact]
    public void AccentAndBar_EmitTheirOmmlElements()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(new Equation([
            MathRun.AccentOf("x"),
            MathRun.BarOf("y"),
            MathRun.BarOf("z", top: false)
        ])));
        doc.Blocks.Add(paragraph);

        var xml = WriteDocumentXml(doc);
        var oMath = xml.Descendants(M + "oMath").Single();

        var acc = oMath.Elements(M + "acc").Single();
        acc.Element(M + "accPr")!.Element(M + "chr")!.Attribute(M + "val")!.Value.Should().Be("̂");

        var bars = oMath.Elements(M + "bar").ToList();
        bars.Should().HaveCount(2);
        bars[0].Element(M + "barPr")!.Element(M + "pos")!.Attribute(M + "val")!.Value.Should().Be("top");
        bars[1].Element(M + "barPr")!.Element(M + "pos")!.Attribute(M + "val")!.Value.Should().Be("bot");
    }

    [Fact]
    public void NestedDecoratorBaseSlots_SurviveRoundTripAndEmitDirectSlotChildren()
    {
        var nestedBase = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Superscript("x", "2")
        ]);
        var equation = new Equation([
            MathRun.AccentOf(nestedBase, "hat"),
            MathRun.BarOf(nestedBase, top: false),
            MathRun.GroupCharOf(nestedBase, "\u23DF", "bot")
        ]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);
        var xml = WriteDocumentXml(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Accent, MathRunKind.Bar, MathRunKind.GroupChar);
        roundTripped.Runs.Should().OnlyContain(run => run.DecoratorBaseEquation != null);
        roundTripped.Runs[0].DecoratorBaseEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        roundTripped.Runs[1].DecoratorBaseEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        roundTripped.Runs[2].DecoratorBaseEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        roundTripped.LinearText.Should().Be("a+x^2hat_a+x^2_a+x^2\u23DF");

        var accBase = xml.Descendants(M + "acc").Single().Element(M + "e")!;
        var barBase = xml.Descendants(M + "bar").Single().Element(M + "e")!;
        var groupCharBase = xml.Descendants(M + "groupChr").Single().Element(M + "e")!;
        foreach (var slot in new[] { accBase, barBase, groupCharBase })
        {
            slot.Elements(M + "oMath").Should().BeEmpty();
            slot.Elements(M + "r").Should().ContainSingle();
            slot.Elements(M + "sSup").Should().ContainSingle();
        }
    }

    [Fact]
    public void RawNestedDecoratorBaseSlots_ReadAsNestedEquations()
    {
        var documentXml = $$"""
            <w:document xmlns:w="{{W.NamespaceName}}" xmlns:m="{{M.NamespaceName}}">
              <w:body>
                <w:p>
                  <m:oMath>
                    <m:acc>
                      <m:accPr><m:chr m:val="hat" /></m:accPr>
                      <m:e>
                        <m:r><m:t>a+</m:t></m:r>
                        <m:sSup>
                          <m:e><m:r><m:t>x</m:t></m:r></m:e>
                          <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
                        </m:sSup>
                      </m:e>
                    </m:acc>
                    <m:bar>
                      <m:barPr><m:pos m:val="bot" /></m:barPr>
                      <m:e>
                        <m:r><m:t>b+</m:t></m:r>
                        <m:sSub>
                          <m:e><m:r><m:t>y</m:t></m:r></m:e>
                          <m:sub><m:r><m:t>1</m:t></m:r></m:sub>
                        </m:sSub>
                      </m:e>
                    </m:bar>
                    <m:groupChr>
                      <m:groupChrPr>
                        <m:chr m:val="&#x23DF;" />
                        <m:pos m:val="bot" />
                      </m:groupChrPr>
                      <m:e>
                        <m:r><m:t>c+</m:t></m:r>
                        <m:f>
                          <m:num><m:r><m:t>1</m:t></m:r></m:num>
                          <m:den><m:r><m:t>z</m:t></m:r></m:den>
                        </m:f>
                      </m:e>
                    </m:groupChr>
                  </m:oMath>
                </w:p>
              </w:body>
            </w:document>
            """;

        var read = ReadDocumentXml(documentXml);

        var equation = read.Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!;
        equation.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Accent, MathRunKind.Bar, MathRunKind.GroupChar);
        equation.Runs[0].DecoratorBaseEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        equation.Runs[1].DecoratorBaseEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Subscript);
        equation.Runs[2].DecoratorBaseEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Fraction);
        equation.LinearText.Should().Be("a+x^2hat_b+y_1_c+1/z\u23DF");
    }

    [Fact]
    public void DelimiterEquation_SurvivesRoundTrip()
    {
        var read = RoundTripEquation(new Equation([MathRun.Delimiter("a, b", "[", "]")]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].Kind.Should().Be(MathRunKind.Delimiter);
        read.Runs[0].Base.Should().Be("a, b");
        read.Runs[0].OpenChar.Should().Be("[");
        read.Runs[0].CloseChar.Should().Be("]");
    }

    [Fact]
    public void NestedDelimiterContent_SurvivesRoundTripAndEmitsDirectSlotChildren()
    {
        var equation = new Equation([
            MathRun.Delimiter(
                new Equation([
                    MathRun.PlainText("a+"),
                    MathRun.Superscript("x", "2")
                ]),
                "[",
                "]")
        ]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);
        var xml = WriteDocumentXml(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.Runs.Should().ContainSingle();
        var delimiter = roundTripped.Runs[0];
        delimiter.Kind.Should().Be(MathRunKind.Delimiter);
        delimiter.Base.Should().Be("a+x2");
        delimiter.OpenChar.Should().Be("[");
        delimiter.CloseChar.Should().Be("]");
        delimiter.DelimiterContentEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        delimiter.DelimiterContentEquation.Runs[1].Base.Should().Be("x");
        delimiter.DelimiterContentEquation.Runs[1].Sup.Should().Be("2");
        roundTripped.LinearText.Should().Be("[a+x^2]");

        var writtenDelimiter = xml.Descendants(M + "d").Single();
        var content = writtenDelimiter.Element(M + "e")!;
        content.Elements(M + "oMath").Should().BeEmpty();
        content.Elements(M + "r").Should().ContainSingle();
        content.Elements(M + "sSup").Should().ContainSingle();
    }

    [Fact]
    public void RawNestedDelimiterContent_ReadsAsNestedEquation()
    {
        var documentXml = $$"""
            <w:document xmlns:w="{{W.NamespaceName}}" xmlns:m="{{M.NamespaceName}}">
              <w:body>
                <w:p>
                  <m:oMath>
                    <m:d>
                      <m:dPr>
                        <m:begChr m:val="[" />
                        <m:endChr m:val="]" />
                      </m:dPr>
                      <m:e>
                        <m:r><m:t>a+</m:t></m:r>
                        <m:sSup>
                          <m:e><m:r><m:t>x</m:t></m:r></m:e>
                          <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
                        </m:sSup>
                      </m:e>
                    </m:d>
                  </m:oMath>
                </w:p>
              </w:body>
            </w:document>
            """;

        var read = ReadDocumentXml(documentXml);

        var equation = read.Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!;
        equation.Runs.Should().ContainSingle();
        var delimiter = equation.Runs[0];
        delimiter.Kind.Should().Be(MathRunKind.Delimiter);
        delimiter.OpenChar.Should().Be("[");
        delimiter.CloseChar.Should().Be("]");
        delimiter.DelimiterContentEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        equation.LinearText.Should().Be("[a+x^2]");
    }

    [Fact]
    public void MatrixEquation_SurvivesRoundTrip()
    {
        var read = RoundTripEquation(new Equation([MathRun.MatrixOf(new MathMatrix([["1", "2"], ["3", "4"]]))]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].Kind.Should().Be(MathRunKind.Matrix);
        var matrix = read.Runs[0].Matrix!;
        matrix.RowCount.Should().Be(2);
        matrix.ColumnCount.Should().Be(2);
        matrix.Rows[0].Should().Equal("1", "2");
        matrix.Rows[1].Should().Equal("3", "4");
        read.LinearText.Should().Be("[1, 2; 3, 4]");
    }

    [Fact]
    public void NestedMatrixCellEquations_SurviveRoundTripAndEmitDirectCellChildren()
    {
        var scriptCell = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Superscript("x", "2")
        ]);
        var fractionCell = new Equation([MathRun.Fraction("p", "q")]);
        var matrix = new MathMatrix([["a+x2", "plain"], ["p/q", ""]]);
        matrix.CellEquations.Add([scriptCell, null]);
        matrix.CellEquations.Add([fractionCell, null]);
        var equation = new Equation([MathRun.MatrixOf(matrix)]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);
        var xml = WriteDocumentXml(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.Runs.Should().ContainSingle();
        var roundTrippedMatrix = roundTripped.Runs[0].Matrix!;
        roundTrippedMatrix.RowCount.Should().Be(2);
        roundTrippedMatrix.ColumnCount.Should().Be(2);
        roundTrippedMatrix.CellEquationAt(0, 0)!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        roundTrippedMatrix.CellEquationAt(1, 0)!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Fraction);
        roundTrippedMatrix.CellEquationAt(0, 1).Should().BeNull();
        roundTripped.LinearText.Should().Be("[a+x^2, plain; p/q, ]");

        var writtenMatrix = xml.Descendants(M + "m").Single();
        var writtenRows = writtenMatrix.Elements(M + "mr").ToArray();
        writtenRows[0].Elements(M + "e").First().Elements(M + "oMath").Should().BeEmpty();
        writtenRows[0].Elements(M + "e").First().Elements(M + "sSup").Should().ContainSingle();
        writtenRows[1].Elements(M + "e").First().Elements(M + "f").Should().ContainSingle();
    }

    [Fact]
    public void NestedEquationArrayCells_SurviveRoundTripAndEmitDirectCellChildren()
    {
        var scriptCell = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Superscript("x", "2")
        ]);
        var fractionCell = new Equation([MathRun.Fraction("p", "q")]);
        var array = MathMatrix.FromCellEquations([[scriptCell], [fractionCell]]);
        var equation = new Equation([MathRun.EquationArrayOf(array)]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);
        var xml = WriteDocumentXml(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.Runs.Should().ContainSingle();
        var roundTrippedArray = roundTripped.Runs[0];
        roundTrippedArray.Kind.Should().Be(MathRunKind.EquationArray);
        roundTrippedArray.Matrix!.RowCount.Should().Be(2);
        roundTrippedArray.Matrix.CellEquationAt(0, 0)!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        roundTrippedArray.Matrix.CellEquationAt(1, 0)!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Fraction);
        roundTripped.LinearText.Should().Be("a+x^2; p/q");

        var writtenArray = xml.Descendants(M + "eqArr").Single();
        writtenArray.Elements(M + "e").Should().HaveCount(2);
        writtenArray.Elements(M + "e").First().Elements(M + "oMath").Should().BeEmpty();
        writtenArray.Elements(M + "e").First().Elements(M + "sSup").Should().ContainSingle();
        writtenArray.Elements(M + "e").Last().Elements(M + "f").Should().ContainSingle();
    }

    [Fact]
    public void FunctionApplyEquation_SurvivesRoundTrip()
    {
        var read = RoundTripEquation(new Equation([MathRun.FunctionApply("sin", "x")]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].Kind.Should().Be(MathRunKind.FunctionApply);
        read.Runs[0].FuncName.Should().Be("sin");
        read.Runs[0].Base.Should().Be("x");
        read.LinearText.Should().Be("sin(x)");
    }

    [Fact]
    public void NestedFunctionArgument_SurvivesRoundTripAndEmitsDirectSlotChildren()
    {
        var equation = new Equation([
            MathRun.FunctionApply(
                "sin",
                new Equation([
                    MathRun.PlainText("a+"),
                    MathRun.Superscript("x", "2")
                ]))
        ]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);
        var xml = WriteDocumentXml(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.Runs.Should().ContainSingle();
        var function = roundTripped.Runs[0];
        function.Kind.Should().Be(MathRunKind.FunctionApply);
        function.FuncName.Should().Be("sin");
        function.Base.Should().Be("a+x2");
        function.FunctionArgumentEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        function.FunctionArgumentEquation.Runs[1].Base.Should().Be("x");
        function.FunctionArgumentEquation.Runs[1].Sup.Should().Be("2");
        roundTripped.LinearText.Should().Be("sin(a+x^2)");

        var writtenFunction = xml.Descendants(M + "func").Single();
        var argument = writtenFunction.Element(M + "e")!;
        argument.Elements(M + "oMath").Should().BeEmpty();
        argument.Elements(M + "r").Should().ContainSingle();
        argument.Elements(M + "sSup").Should().ContainSingle();
    }

    [Fact]
    public void RawNestedFunctionArgument_ReadsAsNestedEquation()
    {
        var documentXml = $$"""
            <w:document xmlns:w="{{W.NamespaceName}}" xmlns:m="{{M.NamespaceName}}">
              <w:body>
                <w:p>
                  <m:oMath>
                    <m:func>
                      <m:fName><m:r><m:t>sin</m:t></m:r></m:fName>
                      <m:e>
                        <m:r><m:t>a+</m:t></m:r>
                        <m:sSup>
                          <m:e><m:r><m:t>x</m:t></m:r></m:e>
                          <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
                        </m:sSup>
                      </m:e>
                    </m:func>
                  </m:oMath>
                </w:p>
              </w:body>
            </w:document>
            """;

        var read = ReadDocumentXml(documentXml);

        var equation = read.Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!;
        equation.Runs.Should().ContainSingle();
        var function = equation.Runs[0];
        function.Kind.Should().Be(MathRunKind.FunctionApply);
        function.FuncName.Should().Be("sin");
        function.FunctionArgumentEquation!.Runs.Select(run => run.Kind)
            .Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
        equation.LinearText.Should().Be("sin(a+x^2)");
    }

    [Fact]
    public void GroupCharEquation_SurvivesRoundTrip()
    {
        var read = RoundTripEquation(new Equation([MathRun.GroupCharOf("x+y", "\u23DF", "bot")]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].Kind.Should().Be(MathRunKind.GroupChar);
        read.Runs[0].Base.Should().Be("x+y");
        read.Runs[0].GroupChr.Should().Be("\u23DF");
        read.Runs[0].GroupChrPos.Should().Be("bot");
        read.LinearText.Should().Be("x+y\u23DF");
    }

    [Fact]
    public void Equation_EmitsNewStructureElements()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(new Equation([
            MathRun.Subscript("x", "i"),
            MathRun.Radical("y"),
            MathRun.NAry("∫", "a", "b", "f"),
            MathRun.Delimiter("z"),
            MathRun.MatrixOf(MathMatrix.Identity2x2()),
            MathRun.FunctionApply("sin", "x"),
            MathRun.GroupCharOf("x+y")
        ])));
        doc.Blocks.Add(paragraph);

        var xml = WriteDocumentXml(doc);
        var oMath = xml.Descendants(M + "oMath").Single();

        oMath.Elements(M + "sSub").Should().ContainSingle();
        oMath.Elements(M + "rad").Should().ContainSingle();
        oMath.Elements(M + "nary").Should().ContainSingle();
        oMath.Elements(M + "d").Should().ContainSingle();
        oMath.Elements(M + "m").Should().ContainSingle();
        oMath.Elements(M + "func").Should().ContainSingle();
        oMath.Elements(M + "groupChr").Should().ContainSingle();
        // The 2x2 matrix emits two rows of two cells each.
        var matrix = oMath.Elements(M + "m").Single();
        matrix.Elements(M + "mr").Should().HaveCount(2);
        matrix.Elements(M + "mr").First().Elements(M + "e").Should().HaveCount(2);
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

    [Fact]
    public void RawOMathPara_ReadsAsDisplayEquation_InsteadOfBeingDropped()
    {
        // Word's "Display"/"Professional" equation layout wraps the equation in m:oMathPara (a
        // paragraph-level sibling of w:r), distinct from an inline bare m:oMath. Before the fix this whole
        // element (and the equation inside it) was silently dropped on open — no branch in
        // AddParagraphContentElement recognised m:oMathPara, so it fell through unmatched.
        var documentXml = $$"""
            <w:document xmlns:w="{{W.NamespaceName}}" xmlns:m="{{M.NamespaceName}}">
              <w:body>
                <w:p>
                  <m:oMathPara>
                    <m:oMath>
                      <m:r><m:t>E=mc</m:t></m:r>
                      <m:sSup>
                        <m:e><m:r><m:t>2</m:t></m:r></m:e>
                        <m:sup><m:r><m:t></m:t></m:r></m:sup>
                      </m:sSup>
                    </m:oMath>
                  </m:oMathPara>
                </w:p>
              </w:body>
            </w:document>
            """;

        var read = ReadDocumentXml(documentXml);

        var paragraph = read.Paragraphs.Single();
        var equationRun = paragraph.Runs.SingleOrDefault(r => r.Equation is not null);
        equationRun.Should().NotBeNull("the display equation must not be dropped on open");
        var equation = equationRun!.Equation!;
        equation.IsDisplayMath.Should().BeTrue();
        equation.Runs.Should().ContainSingle(r => r.Kind == MathRunKind.Text && r.Text == "E=mc");
        equation.LinearText.Should().StartWith("E=mc");
    }

    [Fact]
    public void DisplayEquation_SurvivesWriteRoundTripAndEmitsOMathParaWrapper()
    {
        var equation = Equation.FromText("x = y + 1");
        equation.IsDisplayMath = true;
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var xml = WriteDocumentXml(doc);
        var read = RoundTrip(doc);

        // Renders in the correct (display/centred) mode: the OMML is wrapped in m:oMathPara, the container
        // Word itself keys off to lay the equation out on its own centred line rather than inline.
        var oMathPara = xml.Descendants(M + "oMathPara").Single();
        oMathPara.Elements(M + "oMath").Should().ContainSingle();

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        roundTripped.IsDisplayMath.Should().BeTrue();
        roundTripped.LinearText.Should().Be("x = y + 1");
    }

    [Fact]
    public void InlineEquation_DoesNotEmitOMathParaWrapper()
    {
        // Sibling no-regression: the default (IsDisplayMath = false, every equation authored before this
        // flag existed) must keep emitting a bare inline m:oMath — never wrapped in m:oMathPara.
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(Equation.FromText("a + b")));
        doc.Blocks.Add(paragraph);

        var xml = WriteDocumentXml(doc);

        xml.Descendants(M + "oMathPara").Should().BeEmpty();
        xml.Descendants(M + "oMath").Should().ContainSingle();

        var read = RoundTrip(doc);
        read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!.IsDisplayMath.Should().BeFalse();
    }

    [Fact]
    public void RawMultiArgumentDelimiter_ReadsEveryArgument_InsteadOfTruncating()
    {
        // A binomial/case/matrix-style delimiter carries more than one m:e child under a single m:d,
        // separated by m:dPr/m:sepChr. Before the fix, d.Element(M + "e") only ever read the FIRST m:e,
        // silently dropping every argument after it.
        var documentXml = $$"""
            <w:document xmlns:w="{{W.NamespaceName}}" xmlns:m="{{M.NamespaceName}}">
              <w:body>
                <w:p>
                  <m:oMath>
                    <m:d>
                      <m:dPr>
                        <m:begChr m:val="(" />
                        <m:endChr m:val=")" />
                        <m:sepChr m:val="," />
                      </m:dPr>
                      <m:e><m:r><m:t>n</m:t></m:r></m:e>
                      <m:e><m:r><m:t>k</m:t></m:r></m:e>
                      <m:e><m:r><m:t>m</m:t></m:r></m:e>
                    </m:d>
                  </m:oMath>
                </w:p>
              </w:body>
            </w:document>
            """;

        var read = ReadDocumentXml(documentXml);

        var equation = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        var delimiter = equation.Runs.Single();
        delimiter.Kind.Should().Be(MathRunKind.Delimiter);
        delimiter.Base.Should().Be("n");
        delimiter.AdditionalDelimiterArguments.Should().Equal("k", "m");
        delimiter.DelimiterSeparator.Should().Be(",");
        equation.LinearText.Should().Be("(n,k,m)");
    }

    [Fact]
    public void MultiArgumentDelimiterEquation_SurvivesWriteRoundTrip()
    {
        var equation = new Equation([MathRun.Delimiter(["n", "k", "m"], "(", ")", ",")]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        doc.Blocks.Add(paragraph);

        var xml = WriteDocumentXml(doc);
        var read = RoundTrip(doc);

        var writtenDelimiter = xml.Descendants(M + "d").Single();
        writtenDelimiter.Elements(M + "e").Should().HaveCount(3);
        writtenDelimiter.Element(M + "dPr")!.Element(M + "sepChr")!.Attribute(M + "val")!.Value.Should().Be(",");

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Equation is not null).Equation!;
        var delimiter = roundTripped.Runs.Single();
        delimiter.Base.Should().Be("n");
        delimiter.AdditionalDelimiterArguments.Should().Equal("k", "m");
        roundTripped.LinearText.Should().Be("(n,k,m)");
    }

    [Fact]
    public void SingleArgumentDelimiter_StillEmitsExactlyOneE_NoSepChr()
    {
        // Sibling no-regression: the ordinary single-argument delimiter (every delimiter authored before
        // this fix) must not gain a spurious m:sepChr or extra m:e now that multi-argument support exists.
        var read = RoundTripEquation(new Equation([MathRun.Delimiter("a, b", "[", "]")]));

        read.Runs.Should().ContainSingle();
        read.Runs[0].AdditionalDelimiterArguments.Should().BeEmpty();

        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(new Equation([MathRun.Delimiter("a, b", "[", "]")])));
        doc.Blocks.Add(paragraph);
        var xml = WriteDocumentXml(doc);
        var writtenDelimiter = xml.Descendants(M + "d").Single();
        writtenDelimiter.Elements(M + "e").Should().ContainSingle();
        writtenDelimiter.Element(M + "dPr")!.Element(M + "sepChr").Should().BeNull();
    }
}
