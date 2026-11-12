using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// OMML fidelity for the equation editor: everything the linear format can produce must be written as
/// schema-valid OMML and read back as the same equation; Word-authored OMML for the same constructs must
/// read into the editable model; constructs FreeW does not model must survive verbatim.
/// </summary>
public class EquationLinearOmmlFidelityTests
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private const string DocumentHeader =
        """<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"><w:body><w:p>""";

    private const string DocumentFooter = "</w:p></w:body></w:document>";

    private const string WordControlProperties =
        """<m:ctrlPr><w:rPr><w:rFonts w:ascii="Cambria Math" w:hAnsi="Cambria Math"/><w:i/></w:rPr></m:ctrlPr>""";

    private static string WordRun(string text) =>
        $"""<m:r><w:rPr><w:rFonts w:ascii="Cambria Math" w:hAnsi="Cambria Math"/></w:rPr><m:t>{text}</m:t></m:r>""";

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument Read(byte[] package)
    {
        using var stream = new MemoryStream(package);
        return DocxReader.Read(stream);
    }

    private static XDocument DocumentXml(byte[] package)
    {
        using var stream = new MemoryStream(package);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
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

    private static Equation ReadSingleEquation(string mathXml) =>
        ReadDocumentXml(DocumentHeader + mathXml + DocumentFooter)
            .Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!;

    private static TextDocument DocumentWith(Equation equation)
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Before "));
        paragraph.Runs.Add(Run.FromEquation(equation));
        paragraph.Runs.Add(new Run(" after"));
        document.Blocks.Add(paragraph);
        return document;
    }

    private static void AssertValid(byte[] package, string because)
    {
        using var stream = new MemoryStream(package);
        using var document = WordprocessingDocument.Open(stream, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2013).Validate(document)
            .Select(error => $"{error.Id}: {error.Description}; node={error.Node?.OuterXml}")
            .ToList();
        errors.Should().BeEmpty(because);
    }

    public static TheoryData<string> EditorOutput() => new()
    {
        "x + 1",
        "a/b",
        "(a+b)/(c+d)",
        @"n\atop k",
        @"a\sdiv b",
        @"a\ldiv b",
        "x^2",
        "x_i",
        "x_i^2",
        "x^(n+1)",
        "(a+b)^2",
        @"\sqrt(x)",
        @"\sqrt(n&x)",
        @"\cbrt(x+1)",
        "(a+b)",
        "[a+b]",
        "{a+b}",
        "|x|",
        @"\langle x\rangle ",
        @"\lfloor x\rfloor ",
        @"\lceil x\rceil ",
        @"\norm x\norm ",
        "(a,b]",
        "{a〗",
        "〖a)",
        @"(n\vbar k)",
        @"\sum_(i=1)^n i",
        @"\int_a^b f",
        @"\prod_(k=1)^n\of a_k",
        @"\int\of x",
        @"\oint_C\of F",
        @"\bigcup_(i=1)^n\of A_i",
        "sin x",
        "cos(x)",
        "sin^2 x",
        "lim_(x->0) f",
        @"max_(i\in I) a_i",
        "f\u2061x",
        @"a\below b",
        @"a\above b",
        @"x\hat",
        @"x\bar",
        @"x\vec",
        @"(a+b)\tilde",
        @"x\dot",
        @"x\ddot",
        @"\overbar(x+y)",
        @"\underbar(x)",
        @"\overbrace(x+y)",
        @"\underbrace(x+y)",
        @"\matrix(a&b@c&d)",
        @"(\matrix(a&b@c&d))",
        @"[\matrix(1&0&0@0&1&0@0&0&1)]",
        @"\eqarray(x=1@y=2)",
        @"\alpha+\beta\leq\gamma\rightarrow\infty\pm\times\geq\neq\approx\partial\nabla\omega",
        "⬚/⬚",
        @"A=\pi r^2",
        @"(x+a)^n=\sum_(k=0)^n\of (n\atop k)x^k a^(n-k)",
        @"(1+x)^n=1+(nx)/1!+(n(n-1)x^2)/2!+\dots",
        @"f(x)=a_0+\sum_(n=1)^\infty\of (a_n cos\funcapply((n\pi x)/L)+b_n sin\funcapply((n\pi x)/L))",
        "a^2+b^2=c^2",
        @"x=(-b\pm\sqrt(b^2-4ac))/(2a)",
        @"e^x=1+x/1!+x^2/2!+x^3/3!+\dots, -\infty<x<\infty",
        @"sin \alpha\pm sin \beta=2 sin\funcapply(1/2(\alpha\pm\beta)) cos\funcapply(1/2(\alpha\mp\beta))",
        @"cos \alpha+cos \beta=2 cos\funcapply(1/2(\alpha+\beta)) cos\funcapply(1/2(\alpha-\beta))",
    };

    [Theory]
    [MemberData(nameof(EditorOutput))]
    public void EditorOutput_IsValidOmml_AndReadsBackAsTheSameEquation(string linear)
    {
        var equation = EquationLinearFormat.Parse(linear);
        equation.Runs.Should().NotBeEmpty();

        var package = Write(DocumentWith(equation));

        AssertValid(package, $"'{linear}' must be written as schema-valid OMML");
        var read = Read(package).Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!;
        EquationStructure.AreEquivalent(read, equation, compareLayout: true).Should().BeTrue(
            "'{0}' read back as '{1}'",
            EquationLinearFormat.ToLinear(equation).Text,
            EquationLinearFormat.ToLinear(read).Text);
        EquationLinearFormat.ToLinear(read).HasOpaqueParts.Should().BeFalse();
    }

    [Theory]
    [InlineData(MathJustification.CenterGroup, null)]
    [InlineData(MathJustification.Center, "center")]
    [InlineData(MathJustification.Left, "left")]
    [InlineData(MathJustification.Right, "right")]
    public void DisplayEquation_WritesJustification_AndReadsItBack(MathJustification justification, string? expectedValue)
    {
        var equation = EquationLinearFormat.Parse("a/b");
        equation.IsDisplayMath = true;
        equation.Justification = justification;
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        document.Blocks.Add(paragraph);

        var package = Write(document);

        AssertValid(package, "a display equation must be schema-valid");
        var mathPara = DocumentXml(package).Descendants(M + "oMathPara").Single();
        mathPara.Element(M + "oMathParaPr")?.Element(M + "jc")?.Attribute(M + "val")?.Value.Should().Be(expectedValue);
        if (expectedValue is null)
            mathPara.Element(M + "oMathParaPr").Should().BeNull();

        var read = Read(package).Paragraphs.Single().Runs.Single().Equation!;
        read.IsDisplayMath.Should().BeTrue();
        read.Justification.Should().Be(justification);
        EquationStructure.AreEquivalent(read, equation, compareLayout: true).Should().BeTrue();
    }

    [Fact]
    public void FractionType_IsWrittenAsFPr_OnlyWhenNotTheDefault()
    {
        var package = Write(DocumentWith(EquationLinearFormat.Parse(@"a/b+n\atop k")));

        var fractions = DocumentXml(package).Descendants(M + "f").ToList();
        fractions.Should().HaveCount(2);
        fractions[0].Element(M + "fPr").Should().BeNull();
        fractions[1].Element(M + "fPr")!.Element(M + "type")!.Attribute(M + "val")!.Value.Should().Be("noBar");
    }

    [Fact]
    public void Limit_IsWrittenAsLimLowInsideTheFunctionName()
    {
        var package = Write(DocumentWith(EquationLinearFormat.Parse("lim_(x->0) f")));

        var function = DocumentXml(package).Descendants(M + "func").Single();
        var limit = function.Element(M + "fName")!.Element(M + "limLow")!;
        string.Concat(limit.Element(M + "e")!.Descendants(M + "t").Select(t => t.Value)).Should().Be("lim");
        string.Concat(limit.Element(M + "lim")!.Descendants(M + "t").Select(t => t.Value)).Should().Be("x→0");
        string.Concat(function.Element(M + "e")!.Descendants(M + "t").Select(t => t.Value)).Should().Be("f");
    }

    [Fact]
    public void WordAuthoredQuadraticFormula_ReadsIntoTheEditableModel()
    {
        var xml =
            "<m:oMath>"
            + WordRun("x=")
            + "<m:f><m:fPr>" + WordControlProperties + "</m:fPr>"
            + "<m:num>" + WordRun("-b±")
            + "<m:rad><m:radPr><m:degHide m:val=\"1\"/>" + WordControlProperties + "</m:radPr><m:deg/>"
            + "<m:e><m:sSup><m:sSupPr>" + WordControlProperties + "</m:sSupPr><m:e>" + WordRun("b") + "</m:e><m:sup>" + WordRun("2") + "</m:sup></m:sSup>"
            + WordRun("-4ac") + "</m:e></m:rad></m:num>"
            + "<m:den>" + WordRun("2a") + "</m:den></m:f>"
            + "</m:oMath>";

        var equation = ReadSingleEquation(xml);

        var linear = EquationLinearFormat.ToLinear(equation);
        linear.HasOpaqueParts.Should().BeFalse();
        linear.Text.Should().Be("x=(-b±√(b^2-4ac))/(2a)");
    }

    public static TheoryData<string, string, string> WordAuthoredConstructs() => new()
    {
        {
            "binomial coefficient",
            "<m:d><m:dPr>" + WordControlProperties + "</m:dPr><m:e><m:f><m:fPr><m:type m:val=\"noBar\"/>" + WordControlProperties
            + "</m:fPr><m:num>" + WordRun("n") + "</m:num><m:den>" + WordRun("k") + "</m:den></m:f></m:e></m:d>",
            "(n¦k)"
        },
        {
            "summation with limits",
            "<m:nary><m:naryPr><m:chr m:val=\"∑\"/><m:limLoc m:val=\"undOvr\"/>" + WordControlProperties + "</m:naryPr>"
            + "<m:sub>" + WordRun("k=0") + "</m:sub><m:sup>" + WordRun("n") + "</m:sup><m:e>"
            + "<m:sSub><m:sSubPr>" + WordControlProperties + "</m:sSubPr><m:e>" + WordRun("a") + "</m:e><m:sub>" + WordRun("k") + "</m:sub></m:sSub>"
            + "</m:e></m:nary>",
            "∑_(k=0)^n▒a_k"
        },
        {
            "integral with hidden limits",
            "<m:nary><m:naryPr><m:limLoc m:val=\"subSup\"/><m:subHide m:val=\"1\"/><m:supHide m:val=\"1\"/>" + WordControlProperties
            + "</m:naryPr><m:sub/><m:sup/><m:e>" + WordRun("x") + "</m:e></m:nary>",
            "∫▒x"
        },
        {
            "limit function",
            "<m:func><m:funcPr>" + WordControlProperties + "</m:funcPr><m:fName><m:limLow><m:limLowPr>" + WordControlProperties
            + "</m:limLowPr><m:e><m:r><m:rPr><m:sty m:val=\"p\"/></m:rPr><m:t>lim</m:t></m:r></m:e><m:lim>" + WordRun("n→∞")
            + "</m:lim></m:limLow></m:fName><m:e>" + WordRun("x") + "</m:e></m:func>",
            "lim┬(n→∞)\u2061x"
        },
        {
            "trigonometric function",
            "<m:func><m:funcPr>" + WordControlProperties + "</m:funcPr><m:fName><m:r><m:rPr><m:sty m:val=\"p\"/></m:rPr><m:t>sin</m:t></m:r></m:fName><m:e>"
            + WordRun("α") + "</m:e></m:func>",
            "sin\u2061α"
        },
        {
            "function with exponent",
            "<m:func><m:fName><m:sSup><m:e><m:r><m:rPr><m:sty m:val=\"p\"/></m:rPr><m:t>cos</m:t></m:r></m:e><m:sup>" + WordRun("2")
            + "</m:sup></m:sSup></m:fName><m:e>" + WordRun("x") + "</m:e></m:func>",
            "cos^2\u2061x"
        },
        {
            "vector accent",
            "<m:acc><m:accPr><m:chr m:val=\"\u20D7\"/>" + WordControlProperties + "</m:accPr><m:e>" + WordRun("v") + "</m:e></m:acc>",
            "v\u20D7"
        },
        {
            "overbar",
            "<m:bar><m:barPr><m:pos m:val=\"top\"/>" + WordControlProperties + "</m:barPr><m:e>" + WordRun("x+y") + "</m:e></m:bar>",
            "¯(x+y)"
        },
        {
            "bracketed matrix",
            "<m:d><m:dPr><m:begChr m:val=\"[\"/><m:endChr m:val=\"]\"/>" + WordControlProperties + "</m:dPr><m:e><m:m><m:mPr><m:mcs><m:mc><m:mcPr><m:count m:val=\"2\"/><m:mcJc m:val=\"center\"/></m:mcPr></m:mc></m:mcs>"
            + WordControlProperties + "</m:mPr><m:mr><m:e>" + WordRun("a") + "</m:e><m:e>" + WordRun("b") + "</m:e></m:mr><m:mr><m:e>" + WordRun("c")
            + "</m:e><m:e>" + WordRun("d") + "</m:e></m:mr></m:m></m:e></m:d>",
            "[■(a&b@c&d)]"
        },
        {
            "nth root",
            "<m:rad><m:radPr>" + WordControlProperties + "</m:radPr><m:deg>" + WordRun("3") + "</m:deg><m:e>" + WordRun("x") + "</m:e></m:rad>",
            "√(3&x)"
        },
        {
            "sub-superscript",
            "<m:sSubSup><m:sSubSupPr>" + WordControlProperties + "</m:sSubSupPr><m:e>" + WordRun("x") + "</m:e><m:sub>" + WordRun("i")
            + "</m:sub><m:sup>" + WordRun("2") + "</m:sup></m:sSubSup>",
            "x_i^2"
        },
        {
            "absolute value",
            "<m:d><m:dPr><m:begChr m:val=\"|\"/><m:endChr m:val=\"|\"/>" + WordControlProperties + "</m:dPr><m:e>" + WordRun("x") + "</m:e></m:d>",
            "|x|"
        },
    };

    [Theory]
    [MemberData(nameof(WordAuthoredConstructs))]
    public void WordAuthoredConstruct_ReadsIntoTheEditableModel(string name, string mathXml, string expectedLinear)
    {
        var equation = ReadSingleEquation("<m:oMath>" + mathXml + "</m:oMath>");

        var linear = EquationLinearFormat.ToLinear(equation);
        linear.HasOpaqueParts.Should().BeFalse("{0} must be editable", name);
        linear.Text.Should().Be(expectedLinear);

        // Editing it (here: no change) and saving must again be valid OMML with the same structure.
        var edited = EquationLinearFormat.Parse(linear.Text, linear.OpaqueParts);
        var package = Write(DocumentWith(edited));
        AssertValid(package, $"{name} must be re-written as schema-valid OMML");
        var reread = Read(package).Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!;
        EquationStructure.AreEquivalent(reread, equation).Should().BeTrue();
    }

    public static TheoryData<string, string> UnsupportedConstructs() => new()
    {
        {
            "pre-script",
            "<m:sPre><m:sPrePr><m:ctrlPr><w:rPr><w:i/></w:rPr></m:ctrlPr></m:sPrePr><m:sub><m:r><m:t>1</m:t></m:r></m:sub><m:sup><m:r><m:t>2</m:t></m:r></m:sup><m:e><m:r><m:t>X</m:t></m:r></m:e></m:sPre>"
        },
        {
            "box",
            "<m:box><m:boxPr><m:opEmu m:val=\"1\"/></m:boxPr><m:e><m:r><m:t>==</m:t></m:r></m:e></m:box>"
        },
        {
            "border box",
            "<m:borderBox><m:borderBoxPr><m:hideTop m:val=\"1\"/></m:borderBoxPr><m:e><m:r><m:t>a+b</m:t></m:r></m:e></m:borderBox>"
        },
        {
            "phantom",
            "<m:phant><m:phantPr><m:show m:val=\"0\"/></m:phantPr><m:e><m:r><m:t>y</m:t></m:r></m:e></m:phant>"
        },
    };

    [Theory]
    [MemberData(nameof(UnsupportedConstructs))]
    public void UnsupportedConstruct_IsPreservedVerbatim_ThroughAnEditAroundIt(string name, string mathXml)
    {
        var original = XElement.Parse(
            "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<m:r><m:t>a+</m:t></m:r><m:f><m:num>" + mathXml + "</m:num><m:den><m:r><m:t>2</m:t></m:r></m:den></m:f></m:oMath>");
        var equation = ReadSingleEquation(original.ToString(SaveOptions.DisableFormatting));

        // The unsupported object is one opaque fragment, even nested inside a supported structure.
        var numerator = equation.Runs[1].NumeratorEquation!.Runs.Should().ContainSingle().Subject;
        numerator.Kind.Should().Be(MathRunKind.Preserved, name);

        // Edit the equation around it through the linear format, exactly as the editor does.
        var linear = EquationLinearFormat.ToLinear(equation);
        linear.HasOpaqueParts.Should().BeTrue();
        var edited = EquationLinearFormat.Parse(linear.Text + "+x^2", linear.OpaqueParts);

        var package = Write(DocumentWith(edited));
        AssertValid(package, $"{name} must stay schema-valid");
        var written = DocumentXml(package).Descendants(M + "oMath").Single();
        var expectedObject = original.Element(M + "f")!.Element(M + "num")!.Elements().Single();
        var writtenObject = written.Element(M + "f")!.Element(M + "num")!.Elements().Single();
        XNode.DeepEquals(Normalize(writtenObject), Normalize(expectedObject)).Should().BeTrue(
            "{0} must be written back exactly as it was read:\n{1}\n{2}",
            name,
            writtenObject,
            expectedObject);
        written.Elements(M + "sSup").Should().ContainSingle("the edit must have been applied");
    }

    private static XElement Normalize(XElement element) =>
        new(
            element.Name,
            element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).OrderBy(attribute => attribute.Name.ToString()),
            element.Nodes().Select(node => node is XElement child ? Normalize(child) : node));

    [Fact]
    public void PreservedFragment_WithUnparseableXml_DegradesToItsText()
    {
        var equation = new Equation([MathRun.PreservedOmml("<m:box", "fallback")]);

        var package = Write(DocumentWith(equation));

        AssertValid(package, "a broken preserved fragment must not break the package");
        string.Concat(DocumentXml(package).Descendants(M + "oMath").Single().Descendants(M + "t").Select(t => t.Value))
            .Should().Be("fallback");
    }

    private static string? RunStyle(XElement mathRun) =>
        mathRun.Element(M + "rPr")?.Element(M + "sty")?.Attribute(M + "val")?.Value;

    [Theory]
    [InlineData("sin x", "sin")]
    [InlineData(@"cos\funcapply(x)", "cos")]
    [InlineData("log x", "log")]
    public void FunctionNames_AreWrittenAsPlainStyleRuns_AsWordWritesThem(string linear, string name)
    {
        var package = Write(DocumentWith(EquationLinearFormat.Parse(linear)));

        AssertValid(package, "plain-style function names must be schema-valid");
        var function = DocumentXml(package).Descendants(M + "func").Single();
        var nameRun = function.Element(M + "fName")!.Elements(M + "r").Single();
        nameRun.Element(M + "t")!.Value.Should().Be(name);
        RunStyle(nameRun).Should().Be("p", "Word draws m:sty p text upright");
        function.Element(M + "e")!.Descendants(M + "r").Should().OnlyContain(run => RunStyle(run) == null,
            "the argument keeps the default math italic");
    }

    [Fact]
    public void LimitAndExponentFunctionNames_KeepOnlyTheNameUpright()
    {
        var package = Write(DocumentWith(EquationLinearFormat.Parse("lim_(x->0) f+cos^2 x")));

        var functions = DocumentXml(package).Descendants(M + "func").ToList();
        functions.Should().HaveCount(2);
        var limit = functions[0].Element(M + "fName")!.Element(M + "limLow")!;
        limit.Element(M + "e")!.Elements(M + "r").Should().OnlyContain(run => RunStyle(run) == "p");
        limit.Element(M + "lim")!.Descendants(M + "r").Should().OnlyContain(run => RunStyle(run) == null);
        var power = functions[1].Element(M + "fName")!.Element(M + "sSup")!;
        power.Element(M + "e")!.Elements(M + "r").Should().OnlyContain(run => RunStyle(run) == "p");
        power.Element(M + "sup")!.Descendants(M + "r").Should().OnlyContain(run => RunStyle(run) == null);
    }

    [Fact]
    public void PlainStyleOmml_RoundTripsStably()
    {
        var equation = EquationLinearFormat.Parse(@"lim_(x->0) (sin x)+cos^2 x+f\funcapply(x)");
        equation.Runs.Add(MathRun.PlainText("d") with { IsUpright = true });

        var first = Write(DocumentWith(equation));
        var reread = Read(first).Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!;
        var second = Write(DocumentWith(reread));

        reread.Runs.Last().IsUpright.Should().BeTrue();
        XNode.DeepEquals(
                Normalize(DocumentXml(first).Descendants(M + "oMath").Single()),
                Normalize(DocumentXml(second).Descendants(M + "oMath").Single()))
            .Should().BeTrue("writing what was read must reproduce the same OMML");
    }

    [Theory]
    [InlineData("undOvr", MathLimitLocation.UnderOver)]
    [InlineData("subSup", MathLimitLocation.SubSup)]
    public void ExplicitNAryLimitLocation_RoundTrips(string value, MathLimitLocation expected)
    {
        var equation = ReadSingleEquation(
            "<m:oMath><m:nary><m:naryPr><m:chr m:val=\"∫\"/><m:limLoc m:val=\"" + value + "\"/></m:naryPr>"
            + "<m:sub>" + WordRun("0") + "</m:sub><m:sup>" + WordRun("1") + "</m:sup><m:e>" + WordRun("x") + "</m:e></m:nary></m:oMath>");
        equation.Runs.Single().NAryLimitLocation.Should().Be(expected);

        var package = Write(DocumentWith(equation));

        AssertValid(package, "m:limLoc must be written in schema order");
        DocumentXml(package).Descendants(M + "limLoc").Single().Attribute(M + "val")!.Value.Should().Be(value);
        Read(package).Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!.Runs.Single()
            .NAryLimitLocation.Should().Be(expected);
    }

    [Fact]
    public void NAryWithoutLimitLocation_StillWritesNone()
    {
        var package = Write(DocumentWith(EquationLinearFormat.Parse(@"\sum_(i=1)^n i")));

        DocumentXml(package).Descendants(M + "limLoc").Should().BeEmpty("the document default (m:naryLim) applies");
    }

    [Fact]
    public void GroupCharacterVerticalJustification_RoundTrips_AndOverbraceIsWrittenAsWordDoes()
    {
        var read = ReadSingleEquation(
            "<m:oMath><m:groupChr><m:groupChrPr><m:chr m:val=\"⏞\"/><m:pos m:val=\"top\"/><m:vertJc m:val=\"bot\"/></m:groupChrPr>"
            + "<m:e>" + WordRun("x+y") + "</m:e></m:groupChr></m:oMath>");
        read.Runs.Single().GroupChrVerticalJustification.Should().Be("bot");

        var package = Write(DocumentWith(EquationLinearFormat.Parse(@"\overbrace(x+y)+\underbrace(a)")));

        AssertValid(package, "m:vertJc must be written in schema order");
        var groups = DocumentXml(package).Descendants(M + "groupChrPr").ToList();
        groups[0].Element(M + "vertJc")!.Attribute(M + "val")!.Value.Should().Be("bot",
            "Word's over-brace keeps its base on the baseline with m:vertJc bot");
        groups[1].Element(M + "vertJc").Should().BeNull();
        Read(package).Paragraphs.Single().Runs.Single(run => run.Equation is not null).Equation!.Runs[0]
            .GroupChrVerticalJustification.Should().Be("bot");
    }

    [Theory]
    [InlineData("<m:rPr><m:sty m:val=\"p\"/></m:rPr>", true)]
    [InlineData("<m:rPr><m:nor/></m:rPr>", true)]
    [InlineData("<m:rPr><m:sty m:val=\"i\"/></m:rPr>", false)]
    [InlineData("", false)]
    public void WordAuthoredRunStyle_ReadsAsUprightText(string properties, bool upright)
    {
        var equation = ReadSingleEquation("<m:oMath><m:r>" + properties + "<m:t>dx</m:t></m:r></m:oMath>");

        equation.Runs.Single().IsUpright.Should().Be(upright);
    }
}
