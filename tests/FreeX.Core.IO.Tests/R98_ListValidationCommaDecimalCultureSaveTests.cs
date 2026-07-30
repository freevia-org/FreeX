using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 98's List-validation finding:
/// <see cref="XlsxDataValidationClosedXmlMapper.Save"/> used to run EVERY DataValidation's
/// Formula1/Formula2 through <see cref="XlsxDataValidationClosedXmlMapper.NormalizeNumericFormulaForSave"/>
/// unconditionally, before branching on <see cref="DataValidation.Type"/>. That helper exists only to
/// canonicalize Date/Time/Decimal/WholeNumber bounds, but its number-parse attempt
/// (<c>TryParseInvariantOrCurrentCultureNumber</c>) falls back to <see cref="CultureInfo.CurrentCulture"/>
/// when the invariant parse fails. FreeX never pins the thread culture, so on any machine whose locale
/// uses ',' as the decimal separator (de-DE, fr-FR, es-ES, it-IT, ru-RU, pt-BR, nl-NL, ...), a List
/// rule's literal Formula1 text that looks like "digits,digits" (e.g. the in-memory shape produced for
/// a two-item literal list "1000" and "2000") was silently reparsed as the single decimal number
/// 1000.2000 and reformatted to invariant dot notation BEFORE
/// <see cref="XlsxDataValidationClosedXmlMapper.NormalizeListFormulaForSave"/> -- the function actually
/// responsible for literal-vs-reference quoting -- ever saw the original text. The result: a two-item
/// dropdown silently collapsed into a single-item literal reading "1000.2000" on save.
///
/// The fix gates <c>NormalizeNumericFormulaForSave</c> to only run for
/// WholeNumber/Decimal/Date/Time -- mirroring the type gate already done correctly in
/// <c>XlsxDataValidationNativeMetadataMapper.TryCreateValidationElement</c> and
/// <c>XlsxX14DataValidationWriter.NormalizeFormulaForWrite</c> -- so List (and Custom) formulas reach
/// <c>NormalizeListFormulaForSave</c>/<c>xlDv.Custom</c> completely untouched.
///
/// These tests go through the real <see cref="XlsxFileAdapter.Save"/> entry point (the legacy, non-x14
/// &lt;dataValidation&gt;&lt;formula1&gt; path that <see cref="XlsxDataValidationClosedXmlMapper.Save"/>
/// drives) and temporarily switch the executing thread's <see cref="CultureInfo.CurrentCulture"/> to
/// de-DE to reproduce the locale dependency, always restoring it in a finally block.
/// </summary>
public sealed class R98_ListValidationCommaDecimalCultureSaveTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── Fail-before proof: a two-item literal list under a comma-decimal culture ──

    [Fact]
    public void Save_ListValidation_TwoItemNumericLiteral_UnderCommaDecimalCulture_KeepsBothItems()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            // This is exactly the in-memory shape XlsxDataValidationClosedXmlMapper.Load produces from
            // an on-disk <formula1>"1000,2000"</formula1> (a quoted, two-item literal list), or what a
            // user authors directly as a two-item numeric-looking dropdown.
            var workbook = new Workbook("Test");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
                Type = DvType.List,
                Formula1 = "1000,2000",
            });

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);

            var savedFormula1 = ReadLegacyFormula1(stream);

            savedFormula1.Should().Be("\"1000,2000\"",
                "the two literal list items '1000' and '2000' must survive save verbatim under a " +
                "comma-decimal-separator culture -- they must never be reparsed as the single decimal " +
                "number 1000.2000 and collapsed into a one-item list");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Save_ListValidation_SingleCommaDecimalLookingLiteral_UnderCommaDecimalCulture_StaysAsAuthored()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            // A one-item list whose sole allowed value's text itself looks like a comma-decimal
            // number, e.g. loaded from on-disk <formula1>"1,5"</formula1>.
            var workbook = new Workbook("Test");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
                Type = DvType.List,
                Formula1 = "1,5",
            });

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);

            var savedFormula1 = ReadLegacyFormula1(stream);

            savedFormula1.Should().Be("\"1,5\"",
                "a literal list item that happens to look like a comma-decimal number must not be " +
                "reinterpreted through the current thread's regional decimal-separator setting");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── Sibling/no-regression: numeric bound types must still get invariant normalization ──

    [Fact]
    public void Save_DecimalValidation_CommaDecimalBounds_UnderCommaDecimalCulture_StillNormalizedToInvariant()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            // A genuine Decimal-type numeric bound authored/loaded with a comma decimal separator --
            // this is the case NormalizeNumericFormulaForSave exists to canonicalize, and must keep
            // working after the List/Custom gate is added.
            var workbook = new Workbook("Test");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
                Type = DvType.Decimal,
                Operator = DvOperator.Between,
                Formula1 = "1,5",
                Formula2 = "9,5",
            });

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);

            var savedFormula1 = ReadLegacyFormula1(stream);
            var savedFormula2 = ReadLegacyFormula2(stream);

            savedFormula1.Should().Be("1.5", "a Decimal bound must still be normalized to invariant dot notation");
            savedFormula2.Should().Be("9.5", "a Decimal bound must still be normalized to invariant dot notation");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── Sibling/no-regression: Custom formulas must also bypass numeric normalization ──

    [Fact]
    public void Save_CustomValidation_FormulaWithComma_UnderCommaDecimalCulture_IsNotReinterpreted()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var workbook = new Workbook("Test");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
                Type = DvType.Custom,
                Formula1 = "=SUM(1000,2000)>0",
            });

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);

            var savedFormula1 = ReadLegacyFormula1(stream);

            savedFormula1.Should().Be("=SUM(1000,2000)>0",
                "a Custom validation formula is an arbitrary boolean expression, not a numeric bound, " +
                "and must never be run through the numeric normalizer");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string? ReadLegacyFormula1(MemoryStream package) => ReadLegacyFormula(package, "formula1");

    private static string? ReadLegacyFormula2(MemoryStream package) => ReadLegacyFormula(package, "formula2");

    private static string? ReadLegacyFormula(MemoryStream package, string elementName)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        var result = root.Element(WorksheetNs + "dataValidations")?
            .Element(WorksheetNs + "dataValidation")?
            .Element(WorksheetNs + elementName)?
            .Value;
        package.Position = 0;
        return result;
    }
}
