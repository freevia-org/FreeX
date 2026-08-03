using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.App.Compositor.MathLayout;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Wave 100 production-path coverage for OMML document and containing-part
/// defaults. The package fixture uses a related settings part deliberately:
/// ordinary PPTX packages do not contain that source.
/// </summary>
public sealed class OmmlMathDefaultsIntegrationTests
{
    private const string MathNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string Drawing2010Namespace = "http://schemas.microsoft.com/office/drawing/2010/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string SettingsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";

    [Fact]
    public void Reader_ExtractsMathDefaultsOnlyFromRelatedSettingsPart()
    {
        using var package = WriteMathPackage();
        AddRelatedSettingsPart(package,
            "<m:mathPr xmlns:m=\"" + MathNamespace + "\">" +
            "<m:mathFont m:val=\"Arial\"/>" +
            "<m:brkBin m:val=\"repeat\"/>" +
            "<m:brkBinSub m:val=\"-+\"/>" +
            "<m:smallFrac/>" +
            "</m:mathPr>");

        package.Position = 0;
        var presentation = PptxPackageReader.Read(package);

        presentation.DocumentMathProperties.Should().Be(
            new OmmlMathProperties("repeat", "-+", "Arial", true));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    public void Reader_PropagatesSmallFractionCtOnOffFromRelatedSettingsPart(
        string value,
        bool expected)
    {
        using var package = WriteMathPackage();
        AddRelatedSettingsPart(package,
            "<m:mathPr xmlns:m=\"" + MathNamespace + "\"><m:smallFrac m:val=\"" + value + "\"/></m:mathPr>");

        package.Position = 0;
        var presentation = PptxPackageReader.Read(package);

        presentation.DocumentMathProperties.Should().Be(
            new OmmlMathProperties(SmallFraction: expected));
    }

    [Fact]
    public void Reader_DoesNotInventDocumentDefaultsWhenSettingsSourceIsAbsent()
    {
        using var package = WriteMathPackage();
        AddUnrelatedXmlPart(package,
            "<m:mathPr xmlns:m=\"" + MathNamespace + "\"><m:mathFont m:val=\"Arial\"/></m:mathPr>");

        package.Position = 0;
        var presentation = PptxPackageReader.Read(package);

        presentation.DocumentMathProperties.Should().BeNull();
    }

    [Fact]
    public void Compose_UsesDocumentThenContainingThenRawWrapperThenParagraphPrecedence()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.DocumentMathProperties = new OmmlMathProperties(
            BinaryBreak: "repeat",
            BinarySubtraction: "-+",
            MathFontFamily: "Arial");

        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "x",
                    Math = new MathRunInfo
                    {
                        ContainingProperties = new OmmlMathProperties(
                            BinaryBreak: "after",
                            BinarySubtraction: "+-",
                            MathFontFamily: "Calibri"),
                        RawXml =
                            "<a14:m xmlns:a14=\"" + Drawing2010Namespace + "\" xmlns:m=\"" + MathNamespace + "\">" +
                            "<m:mathPr><m:mathFont m:val=\"Times New Roman\"/></m:mathPr>" +
                            "<m:oMathPara>" +
                            "<m:mathPr><m:brkBin m:val=\"before\"/><m:brkBinSub m:val=\"--\"/></m:mathPr>" +
                            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>" +
                            "</m:oMathPara></a14:m>"
                    }
                }
            }
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 100,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            ExtentCxEmu = 2_000_000,
            ExtentCyEmu = 1_000_000,
            TextBody = body
        });

        var run = SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .Single(shape => shape.ShapeId == 100)
            .Text!.Paragraphs.Single()
            .Runs.Single();

        var glyph = MathBoxRenderPlanner.Plan(
                run.MathLayout!,
                0,
                0,
                SrgbColor.Black,
                run.FontFamily)
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        glyph.FontFamily.Should().Be("Times New Roman",
            "the raw containing wrapper must override the containing-part and document defaults");
        var paragraph = Assert.IsType<MathNode.MathParagraph>(
            OmmlParser.Parse(
                "<m:oMathPara xmlns:m=\"" + MathNamespace + "\">" +
                "<m:mathPr><m:brkBin m:val=\"before\"/><m:brkBinSub m:val=\"--\"/></m:mathPr>" +
                "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara>",
                "x",
                new MathNode.MathProperties(
                    MathNode.MathParagraphBinaryBreak.Repeat,
                    MathNode.MathParagraphBinarySubtraction.MinusPlus,
                    "Arial")));

        paragraph.BinaryBreak.Should().Be(MathNode.MathParagraphBinaryBreak.Before);
        paragraph.BinarySubtraction.Should().Be(MathNode.MathParagraphBinarySubtraction.MinusMinus);
    }

    private static MemoryStream WriteMathPackage()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            Name = "Math",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            ExtentCxEmu = 2_000_000,
            ExtentCyEmu = 1_000_000,
            TextBody = new TextBody()
        };
        shape.TextBody.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "x",
                    Math = new MathRunInfo
                    {
                        RawXml =
                            "<a14:m xmlns:a14=\"" + Drawing2010Namespace + "\" xmlns:m=\"" + MathNamespace + "\">" +
                            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></a14:m>"
                    }
                }
            }
        });
        slide.Shapes.Add(shape);

        var package = new MemoryStream();
        PptxPackageWriter.Write(presentation, package);
        return package;
    }

    private static void AddRelatedSettingsPart(Stream package, string settingsXml)
    {
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        var relationshipEntry = archive.GetEntry("ppt/_rels/presentation.xml.rels")
            ?? throw new InvalidOperationException("presentation relationships part is missing");
        XDocument relationships;
        using (var reader = new StreamReader(relationshipEntry.Open(), Encoding.UTF8, leaveOpen: false))
            relationships = XDocument.Parse(reader.ReadToEnd());
        relationshipEntry.Delete();
        relationships.Root!.Add(new XElement(
            XNamespace.Get(RelationshipNamespace) + "Relationship",
            new XAttribute("Id", "rIdMathSettings"),
            new XAttribute("Type", SettingsRelationshipType),
            new XAttribute("Target", "mathSettings.xml")));
        var rewrittenRelationships = archive.CreateEntry("ppt/_rels/presentation.xml.rels");
        using (var writer = new StreamWriter(rewrittenRelationships.Open(), new UTF8Encoding(false), leaveOpen: false))
            writer.Write(relationships.ToString(SaveOptions.DisableFormatting));

        var settingsEntry = archive.CreateEntry("ppt/mathSettings.xml");
        using var settingsWriter = new StreamWriter(settingsEntry.Open(), new UTF8Encoding(false), leaveOpen: false);
        settingsWriter.Write(
            "<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            settingsXml +
            "</w:settings>");
    }

    private static void AddUnrelatedXmlPart(Stream package, string xml)
    {
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.CreateEntry("ppt/unrelated-math.xml");
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false), leaveOpen: false);
        writer.Write(xml);
    }
}
