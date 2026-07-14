using System.IO;
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
}
