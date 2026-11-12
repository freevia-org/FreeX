using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Home &gt; Text Effects on ordinary body text: the w14 run-property effects Word writes
/// (glow, shadow, reflection, outline, fill, 3-D, contextual alternates) must survive a FreeW
/// open + save unchanged, on direct run formatting, on styles and on the document defaults.
/// </summary>
public sealed class RunTextEffectsRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    // Verbatim shapes of what Word 2016+ writes for a body run formatted from the Text Effects gallery.
    private const string EffectsXml = """
        <w14:fx xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml">
          <w14:glow w14:rad="63500"><w14:schemeClr w14:val="accent1"><w14:alpha w14:val="60000"/><w14:satMod w14:val="175000"/></w14:schemeClr></w14:glow>
          <w14:shadow w14:blurRad="38100" w14:dist="19050" w14:dir="2700000" w14:sx="100000" w14:sy="100000" w14:kx="0" w14:ky="0" w14:algn="tl"><w14:schemeClr w14:val="dk1"><w14:alpha w14:val="60000"/></w14:schemeClr></w14:shadow>
          <w14:reflection w14:blurRad="6350" w14:stA="55000" w14:stPos="0" w14:endA="300" w14:endPos="45500" w14:dist="0" w14:dir="5400000" w14:fadeDir="5400000" w14:sx="100000" w14:sy="-100000" w14:kx="0" w14:ky="0" w14:algn="bl"/>
          <w14:textOutline w14:w="9525" w14:cap="rnd" w14:cmpd="sng" w14:algn="ctr"><w14:solidFill><w14:srgbClr w14:val="FF0000"/></w14:solidFill><w14:prstDash w14:val="solid"/><w14:bevel/></w14:textOutline>
          <w14:textFill><w14:gradFill><w14:gsLst><w14:gs w14:pos="0"><w14:srgbClr w14:val="00B0F0"/></w14:gs><w14:gs w14:pos="100000"><w14:schemeClr w14:val="accent2"><w14:lumMod w14:val="75000"/></w14:schemeClr></w14:gs></w14:gsLst><w14:lin w14:ang="5400000" w14:scaled="0"/></w14:gradFill></w14:textFill>
          <w14:scene3d><w14:camera w14:prst="orthographicFront"/><w14:lightRig w14:rig="threePt" w14:dir="t"><w14:rot w14:lat="0" w14:lon="0" w14:rev="0"/></w14:lightRig></w14:scene3d>
          <w14:props3d w14:extrusionH="57150" w14:contourW="0" w14:prstMaterial="softEdge"><w14:bevelT w14:w="25400" w14:h="38100"/></w14:props3d>
          <w14:ligatures w14:val="standard"/>
          <w14:cntxtAlts/>
        </w14:fx>
        """;

    private static readonly string[] EffectNames =
        ["glow", "shadow", "reflection", "textOutline", "textFill", "scene3d", "props3d", "ligatures", "cntxtAlts"];

    private static IReadOnlyList<XElement> Effects() =>
        XElement.Parse(EffectsXml).Elements().Select(Detach).ToArray();

    [Fact]
    public void BodyRunEffects_SurviveOpenAndSave_ElementForElement()
    {
        var source = PackageWith("word/document.xml", document =>
            RunProperties(document.Descendants(W + "r").Single(run => run.Value == "Effects")).Add(Effects()));

        var saved = Resave(source);

        var rPr = Part(saved, "word/document.xml").Descendants(W + "r").Single(run => run.Value == "Effects").Element(W + "rPr")!;
        AssertEffectsPreserved(rPr);
    }

    [Fact]
    public void BodyRunEffects_AreNotSmearedOntoNeighbouringRuns()
    {
        var source = PackageWith("word/document.xml", document =>
            RunProperties(document.Descendants(W + "r").Single(run => run.Value == "Effects")).Add(Effects()));

        var saved = Part(Resave(source), "word/document.xml");

        saved.Descendants(W + "r").Where(run => run.Value != "Effects")
            .SelectMany(run => run.Descendants().Where(element => element.Name.Namespace == W14))
            .Should().BeEmpty();
        saved.Descendants(W + "r").Count(run => run.Value == "Effects").Should().Be(1);
    }

    [Fact]
    public void StyleEffects_SurviveOpenAndSave()
    {
        var source = PackageWith("word/styles.xml", styles =>
        {
            var style = styles.Descendants(W + "style").First(element => (string?)element.Attribute(W + "styleId") == "Heading1");
            RunProperties(style).Add(Effects());
        });

        var saved = Resave(source);

        var rPr = Part(saved, "word/styles.xml").Descendants(W + "style")
            .First(element => (string?)element.Attribute(W + "styleId") == "Heading1").Element(W + "rPr")!;
        AssertEffectsPreserved(rPr);
    }

    [Fact]
    public void DocumentDefaultEffects_SurviveOpenAndSave()
    {
        var source = PackageWith("word/styles.xml", styles =>
        {
            var rPrDefault = styles.Descendants(W + "rPrDefault").Single();
            RunProperties(rPrDefault).Add(Effects());
        });

        var saved = Resave(source);

        var rPr = Part(saved, "word/styles.xml").Descendants(W + "rPrDefault").Single().Element(W + "rPr")!;
        AssertEffectsPreserved(rPr);
    }

    [Fact]
    public void Effects_SurviveARepeatedSave_ByteStable()
    {
        var source = PackageWith("word/document.xml", document =>
            RunProperties(document.Descendants(W + "r").Single(run => run.Value == "Effects")).Add(Effects()));

        var first = Resave(source);
        var second = Resave(first);

        Part(second, "word/document.xml").ToString(SaveOptions.DisableFormatting)
            .Should().Be(Part(first, "word/document.xml").ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void RunsWithoutEffects_WriteNoEffectElements()
    {
        var saved = Part(Resave(PackageWith("word/document.xml", _ => { })), "word/document.xml");

        saved.Descendants().Where(element => element.Name.Namespace == W14).Should().BeEmpty();
        DocxReader.Read(new MemoryStream(PackageWith("word/document.xml", _ => { })))
            .Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => run.Formatting.TextEffects == null);
    }

    [Fact]
    public void Model_ExposesEachEffect_AndRunsWithDifferentEffectsStayDistinct()
    {
        var source = PackageWith("word/document.xml", document =>
            RunProperties(document.Descendants(W + "r").Single(run => run.Value == "Effects")).Add(Effects()));

        var runs = DocxReader.Read(new MemoryStream(source)).Paragraphs.SelectMany(paragraph => paragraph.Runs).ToList();
        var effects = runs.Single(run => run.Text == "Effects").Formatting.TextEffects!;

        effects.Glow.Should().Contain("w14:rad=\"63500\"");
        effects.Shadow.Should().Contain("w14:blurRad=\"38100\"");
        effects.Reflection.Should().Contain("w14:endPos=\"45500\"");
        effects.Outline.Should().Contain("FF0000");
        effects.Fill.Should().Contain("gradFill");
        effects.Scene3D.Should().Contain("orthographicFront");
        effects.Props3D.Should().Contain("softEdge");
        effects.ContextualAlternates.Should().Be("<w14:cntxtAlts xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" />");
        effects.IsEmpty.Should().BeFalse();
        runs.Where(run => run.Text != "Effects").Should().OnlyContain(run => run.Formatting.TextEffects == null);
        (effects with { Glow = null }).Should().NotBe(effects);
        (effects with { }).Should().Be(effects);
    }

    [Fact]
    public void ModelAuthoredEffects_AreWrittenInSchemaOrder_AndMalformedOnesAreSkipped()
    {
        var effects = Effects();
        string Xml(string name) => effects.Single(element => element.Name.LocalName == name).ToString(SaveOptions.DisableFormatting);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Authored", new RunFormatting
        {
            TextEffects = new RunTextEffects
            {
                // Deliberately assigned out of order, with one unusable value and one foreign element.
                Fill = Xml("textFill"),
                Glow = Xml("glow"),
                Outline = "<not-xml",
                Shadow = "<w:b xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>",
            },
        }));
        document.Blocks.Add(paragraph);
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);

        var rPr = Part(stream.ToArray(), "word/document.xml").Descendants(W + "r").Single().Element(W + "rPr")!;

        rPr.Elements().Where(element => element.Name.Namespace == W14).Select(element => element.Name.LocalName)
            .Should().Equal("glow", "textFill");
        rPr.Elements(W + "b").Should().BeEmpty();
    }

    private static void AssertEffectsPreserved(XElement rPr)
    {
        var written = rPr.Elements().Where(element => element.Name.Namespace == W14).ToList();
        written.Select(element => element.Name.LocalName).Should().Equal(EffectNames);
        var expected = Effects();
        for (var index = 0; index < expected.Count; index++)
        {
            XNode.DeepEquals(Detach(written[index]), expected[index])
                .Should().BeTrue("w14:{0} must be written back exactly as read", expected[index].Name.LocalName);
        }

        // The effects region follows every core w: run property.
        rPr.Elements().SkipWhile(element => element.Name.Namespace != W14)
            .Should().OnlyContain(element => element.Name.Namespace == W14);
    }

    /// <summary>A copy without namespace declarations, so DeepEquals compares content rather than prefixes.</summary>
    private static XElement Detach(XElement element)
    {
        var copy = new XElement(element);
        foreach (var node in copy.DescendantsAndSelf())
            node.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
        return copy;
    }

    private static XElement RunProperties(XElement owner)
    {
        var rPr = owner.Element(W + "rPr");
        if (rPr is null)
        {
            rPr = new XElement(W + "rPr");
            var paragraphProperties = owner.Element(W + "pPr");
            if (paragraphProperties is not null)
                paragraphProperties.AddAfterSelf(rPr);
            else if (owner.Name == W + "r" || owner.Name == W + "rPrDefault")
                owner.AddFirst(rPr);
            else
                owner.Add(rPr);
        }

        return rPr;
    }

    /// <summary>A three-run FreeW-written package ("Before ", "Effects", " after") with one part edited.</summary>
    private static byte[] PackageWith(string partName, Action<XElement> edit)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var heading = new Paragraph("Heading") { StyleId = "Heading1" };
        document.Blocks.Add(heading);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Before ", new RunFormatting()));
        paragraph.Runs.Add(new Run("Effects", new RunFormatting { Bold = true, FontSizePt = 20 }));
        paragraph.Runs.Add(new Run(" after", new RunFormatting { Italic = true }));
        document.Blocks.Add(paragraph);
        document.DefaultRun = document.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry(partName)!;
            XDocument part;
            using (var reader = entry.Open())
                part = XDocument.Load(reader);
            edit(part.Root!);
            entry.Delete();
            using var writer = archive.CreateEntry(partName).Open();
            part.Save(writer, SaveOptions.DisableFormatting);
        }

        return stream.ToArray();
    }

    private static byte[] Resave(byte[] package)
    {
        var document = DocxReader.Read(new MemoryStream(package));
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XElement Part(byte[] package, string partName)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var reader = archive.GetEntry(partName)!.Open();
        return XDocument.Load(reader).Root!;
    }
}
