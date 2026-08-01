using System.IO;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public class VisualEvidenceDocxSchemaTests
{
    public static IEnumerable<object[]> WordComparableDrawingFixtures()
    {
        yield return ["drawing-objects-complex", FreeWVisualEvidenceDocumentFactory.BuildDrawingObjectsCompositionDocument()];
        yield return ["f2-01-float-wrap", FreeWVisualEvidenceDocumentFactory.BuildFloatingWrapEvidenceDocument()];
        yield return ["object-format-position-size-style", FreeWVisualEvidenceDocumentFactory.BuildObjectFormatPositionSizeStyleDocument()];
        yield return ["wordart-watermark-stress", FreeWVisualEvidenceDocumentFactory.BuildWordArtWatermarkStressDocument()];
        yield return ["wordart-picture-watermark-layout", FreeWVisualEvidenceDocumentFactory.BuildWordArtPictureWatermarkLayoutDocument()];
    }

    [Theory]
    [MemberData(nameof(WordComparableDrawingFixtures))]
    public void WordComparableDrawingFixtureDocxPassesOpenXmlSchema(string scenarioId, TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;

        using var wordDocument = WordprocessingDocument.Open(stream, isEditable: false);
        var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365)
            .Validate(wordDocument)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath} in {error.Part?.Uri}")
            .ToList();

        errors.Should().BeEmpty($"{scenarioId} must open in Word without repair; found: {string.Join("; ", errors)}");
    }

    [Fact]
    public void WordArtWatermarkStressFixture_EmitsVisibleVmlTextPathInDefaultHeader()
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(FreeWVisualEvidenceDocumentFactory.BuildWordArtWatermarkStressDocument(), stream);
        stream.Position = 0;

        using var wordDocument = WordprocessingDocument.Open(stream, isEditable: false);
        var headerPart = wordDocument.MainDocumentPart!.HeaderParts.Single();
        using var headerStream = headerPart.GetStream(FileMode.Open, FileAccess.Read);
        var header = XDocument.Load(headerStream);
        var vml = XNamespace.Get("urn:schemas-microsoft-com:vml");
        var textPath = header.Descendants(vml + "textpath")
            .Single(element => element.Attribute("string") is not null);

        textPath.Attribute("string")!.Value.Should().Be("CONFIDENTIAL");
        textPath.Attribute("on")!.Value.Should().Be("t");
        textPath.Attribute("fitshape")!.Value.Should().Be("t");
    }
}
