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
}
