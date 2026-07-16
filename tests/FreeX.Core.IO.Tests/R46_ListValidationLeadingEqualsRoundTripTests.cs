using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for R46-io-data-validation-list-custom-2-2: a List validation's Formula1 for a
/// range or defined-name source carries a leading '=' purely as FreeX's in-memory marker (added by
/// <see cref="XlsxDataValidationClosedXmlMapper.Load"/> so <c>DataValidationService.ListSources</c>'
/// "starts with '='" gate resolves the reference instead of treating the raw text as one literal
/// list item -- see <see cref="R36_DataValidationListFormulaAndMessageNormalizationTests"/>). Before
/// the fix, <see cref="XlsxDataValidationClosedXmlMapper.NormalizeListFormulaForSave"/> never
/// stripped that marker back off, so every save re-emitted a spurious leading '=' into
/// &lt;formula1&gt; that real Excel never writes for a range/name List source -- a permanent,
/// one-way format drift away from Excel's own authoring convention.
/// </summary>
public sealed class R46_ListValidationLeadingEqualsRoundTripTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Save_ListValidation_RangeReferenceFormula_StripsInternalLeadingEqualsMarker()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Red"));

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
            Type = DvType.List,
            // Exactly the shape XlsxDataValidationClosedXmlMapper.Load produces for a same-sheet
            // range source (see R36_DataValidationListFormulaAndMessageNormalizationTests).
            Formula1 = "=$D$1:$D$3",
        });

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var formula1 = ReadFormula1(saved);
        formula1.Should().Be("$D$1:$D$3",
            "real Excel never stores a leading '=' for a range List source; the '=' is only FreeX's " +
            "internal in-memory marker and must not leak into the saved OOXML");
    }

    [Fact]
    public void Save_ListValidation_DefinedNameReferenceFormula_StripsInternalLeadingEqualsMarker()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedRange("MyColors", new GridRange(
            new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 3, 4)));

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
            Type = DvType.List,
            Formula1 = "=MyColors",
        });

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var formula1 = ReadFormula1(saved);
        formula1.Should().Be("MyColors",
            "a defined-name List source must also round-trip without the internal leading '=' marker");
    }

    [Fact]
    public void Save_ListValidation_QuotedInlineLiteral_IsUnaffectedByMarkerStripping()
    {
        // Sibling/no-regression case: a quoted inline literal never carries the leading-'=' marker
        // in the first place, and must keep saving exactly as authored.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
            Type = DvType.List,
            Formula1 = "\"Red,Green,Blue\"",
        });

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var formula1 = ReadFormula1(saved);
        formula1.Should().Be("\"Red,Green,Blue\"",
            "a quoted inline literal list must be completely unaffected by the leading-'=' marker fix");
    }

    [Fact]
    public void Save_ListValidation_UnquotedLiteralWithComma_StillGetsQuoted()
    {
        // Sibling/no-regression case: an unquoted literal list (no marker, no quotes) must still be
        // auto-quoted for Excel-openability, exactly as before the fix.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
            Type = DvType.List,
            Formula1 = "Red,Green,Blue",
        });

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var formula1 = ReadFormula1(saved);
        formula1.Should().Be("\"Red,Green,Blue\"",
            "an unquoted comma-separated literal must still be auto-quoted for Excel-openability");
    }

    private static string? ReadFormula1(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        var result = root.Element(WorksheetNs + "dataValidations")?
            .Element(WorksheetNs + "dataValidation")?
            .Element(WorksheetNs + "formula1")?
            .Value;
        package.Position = 0;
        return result;
    }
}
