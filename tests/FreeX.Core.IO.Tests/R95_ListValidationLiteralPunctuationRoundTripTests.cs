using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 95's List-validation literal-vs-reference finding:
/// <see cref="XlsxDataValidationClosedXmlMapper.NormalizeListFormulaForSave"/> used to decide whether
/// a List rule's Formula1 was an inline literal (needs re-quoting before writing to
/// &lt;formula1&gt;/&lt;xm:f&gt;) or an existing range/name/cross-sheet reference (must stay unquoted)
/// by sniffing the trimmed text for ':', '$', or '!'. That heuristic is wrong whenever the literal
/// list ITEMS themselves legitimately contain one of those characters -- e.g. time-of-day labels
/// ("9:00,10:00,11:00"), currency amounts ("$100,$200,$300"), or emphasis text ("Yes!,No!").
///
/// The fix makes the leading-'=' in-memory marker (the same marker
/// <see cref="R46_ListValidationLeadingEqualsRoundTripTests"/> documents, and that
/// DataValidationCopySupport.RewriteValidationFormula calls "the actual runtime authority" on
/// literal-vs-reference) the ONLY signal NormalizeListFormulaForSave trusts, instead of re-deriving
/// the shape from the text's characters.
///
/// <see cref="XlsxDataValidationClosedXmlMapper.NormalizeListFormulaForSave"/> is reached from TWO
/// real Save paths that behave differently here:
/// <list type="bullet">
///   <item>
///     the legacy &lt;dataValidation&gt;&lt;formula1&gt; path (<see cref="XlsxDataValidationClosedXmlMapper.Save"/>),
///     which hands the normalized text to ClosedXML's own <c>IXLListValidation.List(...)</c> setter --
///     ClosedXML independently re-derives literal-vs-reference from the text it is given and happens to
///     mask this particular bug for that path (verified empirically: the pre-fix normalizer's unquoted
///     output for e.g. "9:00,10:00,11:00" is still written out quoted, because ClosedXML itself decides
///     the un-parseable text must be a literal). That path is covered here only as no-regression
///     documentation, NOT as the fail-before proof.
///   </item>
///   <item>
///     the x14 extLst path (<see cref="XlsxX14DataValidationWriter"/>, used for every
///     <see cref="DataValidation.IsX14"/> rule -- cross-sheet references and any List source over 255
///     characters), which writes <c>NormalizeListFormulaForSave</c>'s return value directly into a raw
///     &lt;xm:f&gt; element with NO independent validation. This is the path that actually corrupts the
///     rule on disk, and is what the fail-before test below exercises through the real
///     <see cref="XlsxFileAdapter.Save"/> entry point.
///   </item>
/// </list>
/// </summary>
public sealed class R95_ListValidationLiteralPunctuationRoundTripTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";
    private const string X14DvUri = "{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}";

    // ── Fail-before proof: the unguarded x14 extLst path ───────────────────────

    [Theory]
    [InlineData("9:00,10:00,11:00")]
    [InlineData("$100,$200,$300")]
    [InlineData("Yes!,No!,Maybe!")]
    [InlineData("1:1,2:1,3:1")]
    public void Save_X14ListValidation_LiteralWithColonDollarOrBang_IsWrittenQuoted(string literalFormula1)
    {
        // This is exactly the in-memory shape XlsxDataValidationClosedXmlMapper.Load /
        // XlsxX14DataValidationReader.NormalizeX14ListFormula1 produce for an inline literal List
        // source (no surrounding quotes, no leading '=' marker) -- e.g. after loading a real
        // Excel-authored x14 List rule (promoted to x14 because the item list is over 255 chars) whose
        // source items happen to contain ':', '$', or '!'.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
            Type = DvType.List,
            Formula1 = literalFormula1,
            IsX14 = true,
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        var x14Formula1 = ReadX14Formula1(stream);
        x14Formula1.Should().Be($"\"{literalFormula1}\"",
            "an inline literal List source written into the x14 extension must be quoted regardless of " +
            "':', '$', or '!' inside the item text -- an unquoted <xm:f> token is not valid " +
            "A1/R1C1/defined-name syntax and Excel would repair or silently drop the rule on open");
    }

    // ── Sibling/no-regression: a genuine x14 cross-sheet reference must stay unquoted ──

    [Fact]
    public void Save_X14ListValidation_CrossSheetReferenceMarked_StaysUnquoted()
    {
        // A real cross-sheet range reference, carrying the internal leading '=' marker that
        // Load/XlsxX14DataValidationReader always add (see R46's regression tests for the legacy
        // element; the x14 reader documents the identical convention at
        // XlsxX14DataValidationReader.NormalizeX14ListFormula1). It legitimately contains '!' and '$'
        // and must never be quoted.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)),
            Type = DvType.List,
            Formula1 = "=Sheet2!$A$1:$A$5",
            IsX14 = true,
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        var x14Formula1 = ReadX14Formula1(stream);
        x14Formula1.Should().Be("Sheet2!$A$1:$A$5",
            "a marked cross-sheet reference must stay unquoted even though it contains '!' and '$' -- " +
            "only the leading '=' marker (not the characters) decides literal-vs-reference");
    }

    // ── Sibling/no-regression: the legacy (non-x14) ClosedXML-backed path is unaffected ──

    [Theory]
    [InlineData("\"9:00,10:00,11:00\"", "9:00,10:00,11:00")]
    [InlineData("\"$100,$200,$300\"", "$100,$200,$300")]
    public void LoadThenSave_LegacyListValidation_LiteralWithColonOrDollar_StillRoundTripsQuoted(
        string onDiskFormula1, string expectedLoadedFormula1)
    {
        var worksheetBody = $"""
            <dataValidations count="1">
              <dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="B2:B5">
                <formula1>{onDiskFormula1}</formula1>
              </dataValidation>
            </dataValidations>
            """;

        using var loadStream = BuildMinimalXlsx(worksheetBody);
        var workbook = new XlsxFileAdapter().Load(loadStream);
        var dv = workbook.GetSheetAt(0).DataValidations.Should().ContainSingle().Subject;

        dv.Type.Should().Be(DvType.List);
        dv.Formula1.Should().Be(expectedLoadedFormula1);

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var savedFormula1 = ReadLegacyFormula1(saved);

        savedFormula1.Should().Be(onDiskFormula1,
            "the legacy non-x14 path is backed by ClosedXML's own List() setter, which must keep " +
            "round-tripping this correctly (no-regression coverage alongside the x14 fail-before proof)");
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

    /// <summary>
    /// Builds the smallest possible valid XLSX stream that contains one worksheet with a few numeric
    /// cells and the given extra worksheet-root XML (dataValidations) appended after &lt;sheetData&gt;
    /// -- mirrors <see cref="R36_DataValidationListFormulaAndMessageNormalizationTests.BuildMinimalXlsx"/>.
    /// </summary>
    private static MemoryStream BuildMinimalXlsx(string worksheetBodyXml)
    {
        var worksheetXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="{WorksheetNs}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>3</v></c></row>
                <row r="4"><c r="A4"><v>4</v></c></row>
                <row r="5"><c r="A5"><v>5</v></c></row>
              </sheetData>
              {worksheetBodyXml}
            </worksheet>
            """;
        var workbookXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;
        var workbookRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet1.xml"/>
            </Relationships>
            """;
        var packageRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                Target="xl/workbook.xml"/>
            </Relationships>
            """;
        var contentTypes = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml"  ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """;

        var ms = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", contentTypes),
            ("_rels/.rels", packageRels),
            ("xl/workbook.xml", workbookXml),
            ("xl/_rels/workbook.xml.rels", workbookRels),
            ("xl/worksheets/sheet1.xml", worksheetXml));
        return ms;
    }
}
