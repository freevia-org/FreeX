using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 108's finding: <c>XlsxDataValidationNativeMetadataMapper
/// .TryCreateValidationElement</c> is the sibling writer used instead of
/// <see cref="XlsxDataValidationClosedXmlMapper.Save"/> whenever ANY data validation on a sheet
/// carries preserved native metadata (<c>XlsxFileAdapter.Save.cs</c> branches the two writers to be
/// mutually exclusive per-sheet). Unlike <see cref="XlsxDataValidationClosedXmlMapper.Save"/> --
/// which deliberately gates <see cref="XlsxDataValidationClosedXmlMapper.NormalizeNumericFormulaForSave"/>
/// to only WholeNumber/Decimal/Date/Time (see its own doc comment: Custom formulas are arbitrary
/// boolean expressions, not numeric bounds) -- the native-metadata sibling used to call that same
/// normalizer unconditionally for every non-List, non-x14 rule, including Custom and TextLength.
/// <c>NormalizeNumericFormulaForSave</c>'s number-parse attempt falls back to
/// <see cref="CultureInfo.CurrentCulture"/>, so on any comma-decimal locale (de-DE, fr-FR, ru-RU, ...)
/// a Custom formula whose text happens to parse as a single decimal number under that culture was
/// silently reformatted to invariant dot notation -- corrupting the original formula text.
///
/// These tests go through the real <see cref="XlsxFileAdapter.Save"/> entry point and force the
/// native-metadata writer to run (by giving one rule on the sheet opaque native attributes, exactly
/// as <c>R78_dv_showmessages_Tests</c> does), then temporarily switch the executing thread's
/// <see cref="CultureInfo.CurrentCulture"/> to de-DE to reproduce the locale dependency, always
/// restoring it in a finally block.
/// </summary>
public sealed class R108_DvNativeMetadataCustomFormulaCultureTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── Fail-before proof: a Custom formula whose text parses as a comma-decimal number ──

    [Fact]
    public void NativeMetadataMapper_CustomValidation_CommaDecimalLookingFormula_UnderCommaDecimalCulture_IsNotReinterpreted()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var wb = new Workbook("R108NativeMapperCustomFormulaTest");
            var sheet = wb.AddSheet("Sheet1");
            var sheetId = sheet.Id;
            sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(1));
            sheet.SetCell(new CellAddress(sheetId, 3, 3), new NumberValue(1));

            // B2: carries opaque native metadata (imeMode) -- triggers HasNativeMetadata(sheet), so
            // XlsxFileAdapter.Save.cs routes the WHOLE sheet's data validations through
            // XlsxDataValidationNativeMetadataMapper instead of XlsxDataValidationClosedXmlMapper.
            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2)),
                Type = DvType.WholeNumber,
                Operator = DvOperator.GreaterThan,
                Formula1 = "0",
                NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["imeMode"] = "fullKatakana",
                },
            });

            // C3: a Custom rule whose Formula1 text is, in its entirety, a string that
            // decimal.TryParse accepts under a comma-decimal culture (the exact shape
            // NormalizeNumericFormulaForSave's TryParseInvariantOrCurrentCultureNumber looks for --
            // it requires the WHOLE trimmed string to parse, so wrapping it in a real formula like
            // "=SUM(1000,2000)>0" would never trigger the bug; this is the minimal reproducer the
            // finding describes: "a Custom.Formula1 text that happens to parse as a single decimal
            // number under CurrentCulture").
            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheetId, 3, 3), new CellAddress(sheetId, 3, 3)),
                Type = DvType.Custom,
                Formula1 = "1,5",
            });

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(wb, stream);

            var savedFormula1 = ReadFormula(stream, "C3");

            savedFormula1.Should().Be("1,5",
                "a Custom validation formula is an arbitrary boolean expression, not a numeric bound, " +
                "and the native-metadata writer must never run it through the numeric normalizer, " +
                "mirroring the gate XlsxDataValidationClosedXmlMapper.Save already applies -- it must " +
                "never be reinterpreted as the decimal number 1.5 under a comma-decimal culture");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── Sibling/no-regression: TextLength must also bypass numeric normalization ──

    [Fact]
    public void NativeMetadataMapper_TextLengthValidation_CommaDecimalCulture_LeavesBoundVerbatim()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var wb = new Workbook("R108NativeMapperTextLengthTest");
            var sheet = wb.AddSheet("Sheet1");
            var sheetId = sheet.Id;
            sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(1));
            sheet.SetCell(new CellAddress(sheetId, 3, 3), new NumberValue(1));

            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2)),
                Type = DvType.WholeNumber,
                Operator = DvOperator.GreaterThan,
                Formula1 = "0",
                NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["imeMode"] = "fullKatakana",
                },
            });

            // TextLength is excluded from XlsxDataValidationClosedXmlMapper.Save's own
            // appliesNumericNormalization gate too (its ApplyNumeric already handles the
            // string-to-number conversion for that type) -- so the native-metadata writer's type
            // gate must exclude it as well, not just Custom. Use a comma-decimal-looking bound so a
            // reintroduced bug would visibly reformat it.
            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheetId, 3, 3), new CellAddress(sheetId, 3, 3)),
                Type = DvType.TextLength,
                Operator = DvOperator.LessThanOrEqual,
                Formula1 = "1,5",
            });

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(wb, stream);

            var savedFormula1 = ReadFormula(stream, "C3");

            savedFormula1.Should().Be("1,5", "a TextLength bound must round-trip verbatim through the native-metadata writer, " +
                "matching XlsxDataValidationClosedXmlMapper.Save's own exclusion of TextLength from numeric normalization");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── Sibling/no-regression: WholeNumber/Decimal/Date/Time bounds must still be normalized ──

    [Fact]
    public void NativeMetadataMapper_DecimalValidation_CommaDecimalBounds_UnderCommaDecimalCulture_StillNormalizedToInvariant()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var wb = new Workbook("R108NativeMapperDecimalTest");
            var sheet = wb.AddSheet("Sheet1");
            var sheetId = sheet.Id;
            sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(1));
            sheet.SetCell(new CellAddress(sheetId, 3, 3), new NumberValue(1));

            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2)),
                Type = DvType.WholeNumber,
                Operator = DvOperator.GreaterThan,
                Formula1 = "0",
                NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["imeMode"] = "fullKatakana",
                },
            });

            // A genuine Decimal-type numeric bound authored with a comma decimal separator -- this
            // is the case NormalizeNumericFormulaForSave exists to canonicalize, and must keep
            // working after the Custom/TextLength gate is added to the native-metadata writer.
            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheetId, 3, 3), new CellAddress(sheetId, 3, 3)),
                Type = DvType.Decimal,
                Operator = DvOperator.Between,
                Formula1 = "1,5",
                Formula2 = "9,5",
            });

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(wb, stream);

            var savedFormula1 = ReadFormula(stream, "C3");
            var savedFormula2 = ReadFormula(stream, "C3", "formula2");

            savedFormula1.Should().Be("1.5", "a Decimal bound must still be normalized to invariant dot notation");
            savedFormula2.Should().Be("9.5", "a Decimal bound must still be normalized to invariant dot notation");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string? ReadFormula(MemoryStream package, string sqrefCell, string elementName = "formula1")
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        var element = root.Element(WorksheetNs + "dataValidations")?
            .Elements(WorksheetNs + "dataValidation")
            .FirstOrDefault(e => e.Attribute("sqref")?.Value == sqrefCell);
        var result = element?.Element(WorksheetNs + elementName)?.Value;
        package.Position = 0;
        return result;
    }
}
