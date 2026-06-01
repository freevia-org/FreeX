using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Guards that FreeX's XLSX output is schema-valid OOXML so Microsoft Excel will open it. A
/// schema-invalid theme part (incomplete fmtScheme / fontScheme) previously made Excel reject every
/// FreeX-authored workbook; this validates the saved package with the Open XML SDK validator.
/// </summary>
public sealed class XlsxSchemaValidationTests
{
    [Fact]
    public void XlsxAdapter_Save_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("SchemaValid");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(42));

        var schemaErrors = SchemaErrors(workbook);
        schemaErrors.Should().BeEmpty();
    }

    [Fact]
    public void XlsxAdapter_Save_ProducesSchemaValidThemePart()
    {
        var workbook = new Workbook("ThemeValid");
        workbook.AddSheet("Data");

        // The theme part (xl/theme/theme1.xml) is the part that previously broke Excel.
        var themeErrors = SchemaErrors(workbook).Where(e => e.Contains("a:theme", System.StringComparison.Ordinal)).ToList();
        themeErrors.Should().BeEmpty();
    }

    private static System.Collections.Generic.List<string> SchemaErrors(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }
}
