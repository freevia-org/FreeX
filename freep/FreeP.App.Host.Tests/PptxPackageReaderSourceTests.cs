using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class PptxPackageReaderSourceTests
{
    [Fact]
    public void SmartArtAndDspXmlParsing_UsesSharedOpcXmlLoader()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageReader.cs"));

        ExtractMethod(source, "private static SmartArtData? ReadSmartArtData(")
            .Should()
            .Contain("OpcXml.LoadXml(")
            .And.NotContain("XDocument.Load(");

        ExtractMethod(source, "private static void ReadDspDrawing(")
            .Should()
            .Contain("OpcXml.LoadXml(")
            .And.NotContain("XDocument.Load(");
    }

    [Fact]
    public void PackageLoadXml_UsesSharedHardenedOpcXmlLoader()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageReader.cs"));

        source.Should()
            .Contain("OpcXml.TryLoadXml(archive,")
            .And.Contain("OpcRelationships.LoadTargets")
            .And.Contain("OpcRelationships.FirstTargetByType")
            .And.NotContain("private static XDocument? LoadXml(")
            .And.NotContain("private static List<(string id, string type, string target)> LoadRels(")
            .And.NotContain("XDocument.Load(");
    }

    [Fact]
    public void DocumentMathProperties_ReadWrapIndentAndWrapRightWithOpenXmlDefaults()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageReader.cs"));

        ExtractMethod(source, "private static OmmlMathProperties? ReadOmmlMathProperties(")
            .Should()
            .Contain("WrapIndent: ReadTwipsMeasureValue(mathProperties.Element(M + \"wrapIndent\"))")
            .And.Contain("WrapRight: ReadOnOffValue(mathProperties.Element(M + \"wrapRight\"))");
        ExtractMethod(source, "private static string? ReadTwipsMeasureValue(")
            .Should()
            .Contain("return string.IsNullOrWhiteSpace(value) ? \"1440\" : value.Trim();");
    }

    [Fact]
    public void CorePropertiesRead_UsesSharedOpcDocumentPropertiesHelper()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageReader.cs"));

        source.Should()
            .Contain("OpcDocumentProperties.ReadCoreProperties(")
            .And.Contain("presentation.Properties,")
            .And.NotContain("private static void ReadCoreProperties(")
            .And.NotContain("XDocument.Load(");
    }

    [Fact]
    public void SmartArtPictureLayouts_AreAdmittedOnlyThroughDeterministicNodeImages()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageReader.cs"));

        ExtractMethod(source, "private static SmartArtData? ReadSmartArtData(")
            .Should()
            .Contain("var isLiveLayoutSupported = IsLiveSmartArtLayoutSupported(layoutUniqueId, family);")
            .And.Contain("if (IsPictureNodeLayout(layoutUniqueId))")
            .And.Contain("isLiveLayoutSupported = false;")
            .And.Contain("IsLiveLayoutSupported = isLiveLayoutSupported");

        ExtractMethod(source, "private static bool IsLiveSmartArtLayoutSupported(")
            .Should()
            .Contain("\"circlearrowprocess\"")
            .And.Contain("\"hierarchy1\"");

        ExtractMethod(source, "private static void TryAttachPictureNodePictures(")
            .Should()
            .Contain("if (pictures.Count != nodes.Count)")
            .And.Contain("data.IsLiveLayoutSupported = false;")
            .And.Contain("nodes[i].Picture = pictures[i].Picture;")
            .And.Contain("data.IsLiveLayoutSupported = true;");

        ExtractMethod(source, "private static SmartArtData? ReadSmartArtData(")
            .Should()
            .Contain("DiagramML defaults an untyped connection to parOf")
            .And.Contain("sourcePoint.type")
            .And.Contain("doc");

        ExtractMethod(source, "private static bool IsLiveSmartArtLayoutSupported(")
            .Should()
            .Contain("picturecaptionlist")
            .And.Contain("pictureaccentprocess")
            .And.Contain("pictureaccentlist")
            .And.Contain("picturestack")
            .And.Contain("picturelineup")
            .And.Contain("picturegrid")
            .And.Contain("pyramidlist")
            .And.Contain("invertedpyramid")
            .And.Contain("relationship1")
            .And.Contain("opposingideas")
            .And.Contain("convergingradial")
            .And.Contain("interlockingrings")
            .And.Contain("cycle2")
            .And.Contain("verticalprocess")
            .And.Contain("horizontalhierarchy")
            .And.Contain("hierarchy3")
            .And.Contain("increasingcircleprocess");

        ExtractMethod(source, "private static bool CanUseCycle2NodeAndArrowCache(")
            .Should()
            .Contain("DrawingShapeKind.Ellipse")
            .And.Contain("DrawingShapeKind.RightArrow")
            .And.Contain("nodeShapes.Count + arrowShapes.Count != smart.FallbackShapes.Count")
            .And.Contain("HasUnsupportedSmartArtShapeEffects")
            .And.Contain("string.IsNullOrWhiteSpace(shape.PlainText)");

        ExtractMethod(source, "private static bool CanUseHierarchy3NodeAndConnectorCache(")
            .Should()
            .Contain("visibleNodes.Count == 4")
            .And.Contain("connectorShapes.Count == 4")
            .And.Contain("HasUnsupportedSmartArtShapeEffects")
            .And.Contain("HasUnsupportedSmartArtDrawingEffects");

        ExtractMethod(source, "private static bool CanUseGridMatrixCache(")
            .Should()
            .Contain("nodes.Count != 4")
            .And.Contain("DrawingShapeKind.Rectangle")
            .And.Contain("shape.ExtentCyEmu != cellSize")
            .And.Contain("gridSize * 0.025")
            .And.Contain("HasUnsupportedSmartArtDrawingEffects");

        ExtractMethod(source, "private static bool HasUnsupportedSmartArtShapeEffects(")
            .Should()
            .Contain("effects.HasOuterShadow")
            .And.Contain("effects.HasGlow")
            .And.Contain("effects.BevelTop is not null")
            .And.Contain("effects.Scene3d is not null");

        ExtractMethod(source, "private static bool HasUnsupportedSmartArtDrawingEffects(")
            .Should()
            .Contain("smart.DrawingPartPath")
            .And.Contain("effectList.Elements().Any()")
            .And.Contain("return true");
    }

    [Fact]
    public void DrawingMlSrgbParsing_UsesSharedRgbHelper()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var packageReaderSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.Core.IO",
            "PptxPackageReader.cs"));
        var colorReaderSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.Core.IO",
            "PptxColorReader.cs"));

        ExtractMethod(packageReaderSource, "private static bool TryParseHex6(")
            .Should()
            .Contain("DrawingMlRgbColor.TryParseHexRgb")
            .And.NotContain("byte.TryParse");

        ExtractMethod(colorReaderSource, "private static SrgbColor? ParseHexColor(")
            .Should()
            .Contain("DrawingMlRgbColor.TryParseHexRgb")
            .And.NotContain("byte.TryParse");
    }

    [Fact]
    public void Read_PresentationXmlWithDtd_DoesNotApplyParsedPayload()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml" />
                </Relationships>
                """);

            WriteEntry(archive, "ppt/presentation.xml", """
                <!DOCTYPE p:presentation [ <!ENTITY x "blocked"> ]>
                <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
                  <p:sldSz cx="9144000" cy="5143500" />
                  <p:sldIdLst>&x;</p:sldIdLst>
                </p:presentation>
                """);
        }

        stream.Position = 0;

        var presentation = PptxPackageReader.Read(stream);

        presentation.SlideSizeCxEmu.Should().Be(new Presentation().SlideSizeCxEmu);
        presentation.SlideSizeCyEmu.Should().Be(new Presentation().SlideSizeCyEmu);
        presentation.Slides.Should().BeEmpty();
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"method '{signature}' should exist");

        var nextMethod = Regex.Match(
            source[(start + signature.Length)..],
            @"\r?\n    (private|internal|public) static ");

        return nextMethod.Success
            ? source[start..(start + signature.Length + nextMethod.Index)]
            : source[start..];
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

}
