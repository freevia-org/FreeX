using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 96's List-validation finding:
/// <see cref="XlsxDataValidationClosedXmlMapper.NormalizeListFormulaForSave"/> gated its re-quoting
/// step on the trimmed text containing a comma, on top of the leading-'=' reference marker that R95
/// established as the sole literal-vs-reference authority. A literal List source that happens to be a
/// SINGLE item (no comma at all -- e.g. an ordinary one-choice dropdown whose only allowed value is
/// "Approved") therefore skipped the quoting step entirely and was written to disk completely
/// unquoted, which real Excel cannot parse as a literal: an unquoted, unresolvable token like
/// <c>Approved</c> is not valid A1/R1C1/defined-name syntax, so Excel repairs/strips the rule (and
/// FreeX itself misreads the surviving text back as a reference on the next load, since it no longer
/// looks like a quoted literal).
///
/// <see cref="XlsxDataValidationClosedXmlMapper.NormalizeListFormulaForSave"/> is reached from TWO real
/// Save paths:
/// <list type="bullet">
///   <item>
///     the legacy &lt;dataValidation&gt;&lt;formula1&gt; path (<see cref="XlsxDataValidationClosedXmlMapper.Save"/>),
///     which hands the normalized text to ClosedXML's own <c>IXLListValidation.List(...)</c> setter.
///     Unlike the comma-containing punctuation cases R95 fixed, ClosedXML does NOT independently
///     re-derive quoting for a bare single-item literal -- it writes exactly what it is given -- so
///     this path is the actual fail-before proof for THIS bug (verified empirically against the
///     ClosedXML 0.105.0 package this project references: <c>xlDv.List("Approved", true)</c> produces
///     <c>&lt;formula1&gt;Approved&lt;/formula1&gt;</c>, completely unquoted).
///   </item>
///   <item>
///     the x14 extLst path (<see cref="XlsxX14DataValidationWriter"/>), which writes
///     <c>NormalizeListFormulaForSave</c>'s return value directly into a raw &lt;xm:f&gt; element with
///     NO independent validation -- covered here as a sibling no-regression path.
///   </item>
/// </list>
/// </summary>
public sealed class R96_SingleItemListValidationLiteralQuotingTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";
    private const string X14DvUri = "{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}";

    // ── Fail-before proof: the real, ordinary one-choice dropdown (legacy, non-x14 path) ──

    [Theory]
    [InlineData("Approved")]
    [InlineData("A")]
    public void Save_LegacyListValidation_SingleItemLiteral_IsWrittenQuoted(string literalFormula1)
    {
        // This is exactly the in-memory shape XlsxDataValidationClosedXmlMapper.Load produces for an
        // inline literal List source with only one allowed value (no surrounding quotes, no leading
        // '=' marker) -- e.g. after loading a real Excel-authored single-choice dropdown whose on-disk
        // <formula1> was the correctly-quoted "Approved".
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
            Type = DvType.List,
            Formula1 = literalFormula1,
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        var formula1 = ReadLegacyFormula1(stream);
        formula1.Should().Be($"\"{literalFormula1}\"",
            "a single-item literal List source is still a literal and must be quoted on disk exactly " +
            "like a multi-item one -- an unquoted <formula1> token is not valid A1/R1C1/defined-name " +
            "syntax and Excel would repair or silently drop the rule on open");
    }

    // ── Sibling/no-regression: the x14 extLst path must quote a single-item literal too ──

    [Fact]
    public void Save_X14ListValidation_SingleItemLiteral_IsWrittenQuoted()
    {
        // The same single-item shape, but forced onto the x14 path -- e.g. a real Excel-authored x14
        // List rule whose source is one item longer than 255 characters (RequiresX14ForListSource
        // promotes any List formula1 over 255 chars to x14 regardless of comma count).
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
            Type = DvType.List,
            Formula1 = "Approved",
            IsX14 = true,
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        var x14Formula1 = ReadX14Formula1(stream);
        x14Formula1.Should().Be("\"Approved\"",
            "an inline single-item literal List source written into the x14 extension must be quoted " +
            "just like a multi-item one -- an unquoted <xm:f> token is not valid A1/R1C1/defined-name " +
            "syntax");
    }

    // ── Sibling/no-regression: a marked single-item reference (no comma) must stay unquoted ──

    [Fact]
    public void Save_LegacyListValidation_SingleCellReferenceMarked_StaysUnquoted()
    {
        // A genuine single-cell reference source, carrying the internal leading '=' marker that Load
        // always adds for a range/name/reference (see R46's regression tests). It has no comma either,
        // so this proves the fix didn't start (mis-)quoting references merely because they lack one.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
            Type = DvType.List,
            Formula1 = "=$A$1",
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        var formula1 = ReadLegacyFormula1(stream);
        formula1.Should().Be("$A$1",
            "a marked single-cell reference must stay unquoted even though it has no comma -- only the " +
            "leading '=' marker (not comma-count) decides literal-vs-reference");
    }

    // ── Sibling/no-regression: multi-item literals (R95's fix) still round-trip quoted ──

    [Fact]
    public void Save_LegacyListValidation_MultiItemLiteral_StillWrittenQuoted()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
            Type = DvType.List,
            Formula1 = "Approved,Pending,Rejected",
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        var formula1 = ReadLegacyFormula1(stream);
        formula1.Should().Be("\"Approved,Pending,Rejected\"",
            "the pre-existing multi-item literal quoting behavior must be unaffected by the single-item fix");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string? ReadLegacyFormula1(MemoryStream package)
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

    private static string? ReadX14Formula1(MemoryStream package)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            using var entryStream = entry.Open();
            var root = XDocument.Load(entryStream).Root!;

            var extLst = root.Elements().LastOrDefault(e => e.Name.LocalName == "extLst");
            var ext = extLst?.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "ext" && e.Attribute("uri")?.Value == X14DvUri);
            var result = ext?.Element(X14Ns + "dataValidations")?
                .Element(X14Ns + "dataValidation")?
                .Element(X14Ns + "formula1")?
                .Element(XmNs + "f")?
                .Value;

            package.Position = 0;
            return result;
        }
    }
}
