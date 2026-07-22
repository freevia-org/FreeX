using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 74 io-dv findings 4-1/4-2/4-3 in the x14 data-validation
/// reader/writer:
/// <list type="bullet">
///   <item>R74-io-data-validation-xml-4-1: an x14 List rule whose formula1 is a cross-sheet
///     range/defined-name reference (e.g. "Sheet2!$A$1:$A$5") must get the same leading '='
///     marker the legacy ClosedXML loader adds, both when merging into an existing legacy rule
///     and when creating a brand-new x14-only rule -- otherwise DataValidationService.ListSources
///     treats the whole reference text as one literal dropdown item.</item>
///   <item>R74-io-data-validation-xml-4-2: an x14-only rule with no showInputMessage/
///     showErrorMessage attribute must default both to FALSE (the OOXML default), not TRUE.</item>
///   <item>R74-io-data-validation-xml-4-3: the x14 writer must normalize a Date/Time/Decimal/
///     WholeNumber bound to its OLE-serial/invariant on-disk form before emitting &lt;xm:f&gt;,
///     the same way the legacy ClosedXML path does, while leaving a List/Custom formula alone
///     (beyond stripping the in-memory '=' list-range marker back off).</item>
/// </list>
/// </summary>
public sealed class R74_X14DataValidationXml4Tests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";
    private const string X14DvUri = "{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}";
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";

    // ── R74-io-data-validation-xml-4-1 ──────────────────────────────────────────────────────

    [Fact]
    public void Apply_NewX14ListRuleWithCrossSheetRangeFormula_AddsLeadingEquals()
    {
        var worksheetXml = BuildWorksheetWithX14ListDv("Sheet2!$A$1:$A$5", "B2");
        var metadata = XlsxX14DataValidationReader.Read(worksheetXml);
        metadata.Should().HaveCount(1);

        var wb = new Workbook("Xml41NewRuleTest");
        var sheet = wb.AddSheet("Sheet1");

        // No legacy rule exists for B2 → Apply creates a brand-new rule from the x14 attributes.
        XlsxX14DataValidationReader.Apply(sheet, metadata);

        sheet.DataValidations.Should().HaveCount(1);
        sheet.DataValidations[0].Formula1.Should().Be(
            "=Sheet2!$A$1:$A$5",
            "a range/cross-sheet List source must get the leading '=' marker so " +
            "DataValidationService.ListSources resolves the range, not one literal item");
    }

    [Fact]
    public void Apply_ExistingLegacyListRuleMergedWithX14CrossSheetFormula_AddsLeadingEquals()
    {
        var worksheetXml = BuildWorksheetWithX14ListDv("Sheet2!$A$1:$A$5", "B2");
        var metadata = XlsxX14DataValidationReader.Read(worksheetXml);
        metadata.Should().HaveCount(1);

        var wb = new Workbook("Xml41MergeTest");
        var sheet = wb.AddSheet("Sheet1");
        var sheetId = sheet.Id;

        // Simulate the legacy <dataValidation> element already having been loaded by the
        // ClosedXML mapper: same range, List type, but an empty (inert) formula1.
        var existing = new DataValidation
        {
            AppliesTo = new GridRange(
                CellAddress.Parse("B2", sheetId),
                CellAddress.Parse("B2", sheetId)),
            Type = DvType.List,
            Formula1 = "",
        };
        sheet.DataValidations.Add(existing);

        XlsxX14DataValidationReader.Apply(sheet, metadata);

        sheet.DataValidations.Should().HaveCount(1, "the x14 metadata must merge into the existing legacy rule, not add a second one");
        sheet.DataValidations[0].Formula1.Should().Be(
            "=Sheet2!$A$1:$A$5",
            "the merge branch must add the same leading '=' marker as the new-rule branch");
    }

    [Fact]
    public void Apply_NewX14ListRuleWithQuotedInlineLiteral_KeepsLiteralWithoutEquals()
    {
        // An inline quoted literal list (rare in x14, but the shape can occur) must NOT get a
        // leading '=' -- it is already a literal comma-separated item list, not a reference.
        var worksheetXml = BuildWorksheetWithX14ListDv("\"a,b,c\"", "D4");
        var metadata = XlsxX14DataValidationReader.Read(worksheetXml);

        var wb = new Workbook("Xml41LiteralTest");
        var sheet = wb.AddSheet("Sheet1");
        XlsxX14DataValidationReader.Apply(sheet, metadata);

        sheet.DataValidations.Should().HaveCount(1);
        sheet.DataValidations[0].Formula1.Should().Be(
            "a,b,c",
            "a quoted inline literal must have its quotes stripped and stay a literal item list, not gain a '=' marker");
    }

    [Fact]
    public void Apply_NewX14DecimalRule_DoesNotAddLeadingEquals()
    {
        // A non-list (numeric) x14-only rule must be completely unaffected by the List-only fix.
        var worksheetXml = BuildWorksheetWithX14Dv(
            type: "decimal", operatorStr: "between", formula1: "10", formula2: "20", sqref: "C3");
        var metadata = XlsxX14DataValidationReader.Read(worksheetXml);

        var wb = new Workbook("Xml41NumericTest");
        var sheet = wb.AddSheet("Sheet1");
        XlsxX14DataValidationReader.Apply(sheet, metadata);

        sheet.DataValidations.Should().HaveCount(1);
        var dv = sheet.DataValidations[0];
        dv.Type.Should().Be(DvType.Decimal);
        dv.Formula1.Should().Be("10", "a numeric bound must never get the List-only '=' marker");
        dv.Formula2.Should().Be("20");
    }

    // ── R74-io-data-validation-xml-4-2 ──────────────────────────────────────────────────────

    [Fact]
    public void Apply_X14OnlyRuleWithNoShowMessageAttrs_DefaultsBothToFalse()
    {
        var worksheetXml = BuildWorksheetWithX14ListDv("Sheet2!$A$1:$A$5", "B2");
        var metadata = XlsxX14DataValidationReader.Read(worksheetXml);

        var wb = new Workbook("Xml42DefaultsTest");
        var sheet = wb.AddSheet("Sheet1");
        XlsxX14DataValidationReader.Apply(sheet, metadata);

        sheet.DataValidations.Should().HaveCount(1);
        var dv = sheet.DataValidations[0];
        dv.ShowInputMessage.Should().BeFalse(
            "OOXML default for showInputMessage is FALSE; an absent attribute must not enable it");
        dv.ShowErrorMessage.Should().BeFalse(
            "OOXML default for showErrorMessage is FALSE; an absent attribute must not enable it");
        dv.AllowBlank.Should().BeFalse("sibling AllowBlank default must remain unaffected (no regression)");
    }

    [Fact]
    public void Apply_X14OnlyRuleWithShowErrorMessageExplicitTrue_LoadsTrue()
    {
        var worksheetXml = XDocument.Parse("""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData/>
              <extLst>
                <ext uri="{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}"
                     xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                     xmlns:xm="http://schemas.microsoft.com/office/excel/2006/main">
                  <x14:dataValidations count="1">
                    <x14:dataValidation type="list" showErrorMessage="1">
                      <x14:formula1><xm:f>Sheet2!$A$1:$A$5</xm:f></x14:formula1>
                      <xm:sqref>E5</xm:sqref>
                    </x14:dataValidation>
                  </x14:dataValidations>
                </ext>
              </extLst>
            </worksheet>
            """);

        var metadata = XlsxX14DataValidationReader.Read(worksheetXml);
        var wb = new Workbook("Xml42ExplicitTrueTest");
        var sheet = wb.AddSheet("Sheet1");
        XlsxX14DataValidationReader.Apply(sheet, metadata);

        sheet.DataValidations.Should().HaveCount(1);
        var dv = sheet.DataValidations[0];
        dv.ShowErrorMessage.Should().BeTrue("an explicit showErrorMessage=\"1\" attribute must still load true");
        dv.ShowInputMessage.Should().BeFalse("showInputMessage was absent, so it must still default to false");
    }

    // ── R74-io-data-validation-xml-4-3 ──────────────────────────────────────────────────────

    [Fact]
    public void Save_X14DateRuleWithHumanDateFormula_NormalizesToOleSerial()
    {
        var wb = new Workbook("Xml43DateTest");
        var sheet = wb.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(1));

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 1, 2), new CellAddress(sheetId, 1, 2)),
            Type = DvType.Date,
            Operator = DvOperator.LessThanOrEqual,
            Formula1 = "1/1/2024",
            IsX14 = true,
        };
        sheet.DataValidations.Add(dv);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        var formula1Text = ReadX14Formula1(stream);
        formula1Text.Should().Be(
            "45292",
            "a Date bound must be normalized to the OLE Automation serial before being written into " +
            "<xm:f>, not saved literally as human date text that Excel would reparse as (1/1)/2024");
    }

    [Fact]
    public void Save_X14DecimalRuleWithLocaleDecimalFormula_NormalizesToInvariant()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR"); // comma decimal separator
        try
        {
            var wb = new Workbook("Xml43DecimalTest");
            var sheet = wb.AddSheet("Sheet1");
            var sheetId = sheet.Id;
            sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(1));

            var dv = new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheetId, 1, 2), new CellAddress(sheetId, 1, 2)),
                Type = DvType.Decimal,
                Operator = DvOperator.GreaterThanOrEqual,
                Formula1 = "10,5",
                IsX14 = true,
            };
            sheet.DataValidations.Add(dv);

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(wb, stream);
            stream.Position = 0;

            var formula1Text = ReadX14Formula1(stream);
            formula1Text.Should().Be(
                "10.5",
                "a Decimal bound authored under a comma-decimal culture must be normalized to invariant dot-decimal text");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Save_X14ListRuleWithCrossSheetRangeFormula_IsNotNumericallyNormalized()
    {
        var wb = new Workbook("Xml43ListTest");
        var sheet = wb.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(1));

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 1, 2), new CellAddress(sheetId, 1, 2)),
            Type = DvType.List,
            // In-memory form carries the '=' marker (as XlsxX14DataValidationReader now adds it) --
            // the writer must strip it back off (real Excel never writes '=' for x14 list sources)
            // and must NOT run it through numeric normalization.
            Formula1 = "=Sheet2!$A$1:$A$5",
            IsX14 = true,
        };
        sheet.DataValidations.Add(dv);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        var formula1Text = ReadX14Formula1(stream);
        formula1Text.Should().Be(
            "Sheet2!$A$1:$A$5",
            "a List range/cross-sheet reference must have the in-memory '=' marker stripped, and must not be numerically normalized");
    }

    private static XDocument BuildWorksheetWithX14ListDv(string formula1, string sqref) =>
        BuildWorksheetWithX14Dv(type: "list", operatorStr: null, formula1: formula1, formula2: null, sqref: sqref);

    private static XDocument BuildWorksheetWithX14Dv(string type, string? operatorStr, string formula1, string? formula2, string sqref)
    {
        var operatorAttr = operatorStr is null ? "" : $" operator=\"{operatorStr}\"";
        var formula2Xml = formula2 is null
            ? ""
            : $"<x14:formula2><xm:f>{formula2}</xm:f></x14:formula2>";

        return XDocument.Parse($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData/>
              <extLst>
                <ext uri="{X14DvUri}"
                     xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                     xmlns:xm="http://schemas.microsoft.com/office/excel/2006/main">
                  <x14:dataValidations count="1">
                    <x14:dataValidation type="{type}"{operatorAttr}>
                      <x14:formula1><xm:f>{formula1}</xm:f></x14:formula1>
                      {formula2Xml}
                      <xm:sqref>{sqref}</xm:sqref>
                    </x14:dataValidation>
                  </x14:dataValidations>
                </ext>
              </extLst>
            </worksheet>
            """);
    }

    private static string? ReadX14Formula1(MemoryStream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = worksheetEntry.Open())
            doc = XDocument.Load(xmlStream);

        return doc.Root!
            .Elements(WorksheetNs + "extLst")
            .SelectMany(extLst => extLst.Elements(WorksheetNs + "ext"))
            .Where(e => (string?)e.Attribute("uri") == X14DvUri)
            .SelectMany(e => e.Elements(X14Ns + "dataValidations"))
            .SelectMany(e => e.Elements(X14Ns + "dataValidation"))
            .SelectMany(e => e.Elements(X14Ns + "formula1"))
            .SelectMany(e => e.Elements(XmNs + "f"))
            .Select(e => e.Value)
            .FirstOrDefault();
    }
}
