using System.IO;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 36 regression tests:
/// <list type="bullet">
///   <item>
///     R36-io-data-validation-2-1 -- a List-validation whose &lt;formula1&gt; is a real-Excel-shaped
///     range/named-source reference (no leading '=', which is how genuine Excel-authored workbooks
///     always store it) must load with the leading '=' re-added, so
///     <c>DataValidationService.ListSources</c>'s "Formula1 starts with '='" gate actually resolves the
///     range/name instead of treating the raw reference text as a single literal list item. An inline
///     literal list (quoted, comma-separated) must still load as a literal item list with no '='.
///   </item>
///   <item>
///     R36-io-data-validation-2-2 -- legacy (non-x14) error/prompt title/message text that is entirely
///     absent from the source XML must load as <c>null</c>, not <c>""</c>, so FreeX's
///     <c>dv.ErrorMessage ?? "&lt;default text&gt;"</c> fallbacks actually trigger for the common case
///     where the author never customized the Error Alert / Input Message tabs.
///   </item>
/// </list>
/// </summary>
public sealed class R36_DataValidationListFormulaAndMessageNormalizationTests
{
    private const string WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── R36-io-data-validation-2-1 ──────────────────────────────────────────

    [Fact]
    public void Load_ListValidation_UnprefixedRangeReference_ReAddsLeadingEqualsSoSourceResolves()
    {
        // Real Excel stores a same-sheet range List source with no leading '=': <formula1>$D$1:$D$3</formula1>.
        var worksheetBody = """
            <dataValidations count="1">
              <dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="B2:B5">
                <formula1>$D$1:$D$3</formula1>
              </dataValidation>
            </dataValidations>
            """;

        using var stream = BuildMinimalXlsx(worksheetBody);
        var workbook = new XlsxFileAdapter().Load(stream);
        var dv = workbook.GetSheetAt(0).DataValidations.Should().ContainSingle().Subject;

        dv.Type.Should().Be(DvType.List);
        dv.Formula1.Should().Be("=$D$1:$D$3",
            "the raw un-prefixed range reference must be re-normalized to a resolvable formula, " +
            "not left as bare reference text that DataValidationService would treat as one literal list item");
    }

    [Fact]
    public void Load_ListValidation_UnprefixedDefinedNameReference_ReAddsLeadingEquals()
    {
        // Real Excel stores a defined-name List source the same way: <formula1>MyColors</formula1>.
        var worksheetBody = """
            <dataValidations count="1">
              <dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="B2:B5">
                <formula1>MyColors</formula1>
              </dataValidation>
            </dataValidations>
            """;

        using var stream = BuildMinimalXlsx(worksheetBody);
        var workbook = new XlsxFileAdapter().Load(stream);
        var dv = workbook.GetSheetAt(0).DataValidations.Should().ContainSingle().Subject;

        dv.Type.Should().Be(DvType.List);
        dv.Formula1.Should().Be("=MyColors",
            "a defined-name List source must also be re-normalized to a resolvable formula reference");
    }

    [Fact]
    public void Load_ListValidation_QuotedInlineLiteral_StaysLiteralItemsWithNoLeadingEquals()
    {
        // Sibling/no-regression case: an inline literal list is always quoted by Excel
        // (<formula1>"Red,Green,Blue"</formula1>) and must keep loading as plain literal items.
        var worksheetBody = """
            <dataValidations count="1">
              <dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="B2:B5">
                <formula1>"Red,Green,Blue"</formula1>
              </dataValidation>
            </dataValidations>
            """;

        using var stream = BuildMinimalXlsx(worksheetBody);
        var workbook = new XlsxFileAdapter().Load(stream);
        var dv = workbook.GetSheetAt(0).DataValidations.Should().ContainSingle().Subject;

        dv.Type.Should().Be(DvType.List);
        dv.Formula1.Should().Be("Red,Green,Blue",
            "an inline literal list must not gain a leading '=' -- it is not a formula reference");
    }

    // ── R36-io-data-validation-2-2 ──────────────────────────────────────────

    [Fact]
    public void Load_WholeNumberValidation_NoErrorOrPromptAttributes_LoadsAllMessagesAsNull()
    {
        // The overwhelming majority of real-world rules: author never touched the Error Alert /
        // Input Message tabs, so none of error/errorTitle/prompt/promptTitle are present at all.
        var worksheetBody = """
            <dataValidations count="1">
              <dataValidation type="whole" operator="between" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A2:A5">
                <formula1>1</formula1>
                <formula2>100</formula2>
              </dataValidation>
            </dataValidations>
            """;

        using var stream = BuildMinimalXlsx(worksheetBody);
        var workbook = new XlsxFileAdapter().Load(stream);
        var dv = workbook.GetSheetAt(0).DataValidations.Should().ContainSingle().Subject;

        dv.ErrorTitle.Should().BeNull("an absent errorTitle attribute must load as null, not \"\", so the default fallback message can trigger");
        dv.ErrorMessage.Should().BeNull("an absent error attribute must load as null, not \"\", so the default fallback message can trigger");
        dv.PromptTitle.Should().BeNull("an absent promptTitle attribute must load as null, not \"\"");
        dv.PromptMessage.Should().BeNull("an absent prompt attribute must load as null, not \"\"");
    }

    [Fact]
    public void Load_WholeNumberValidation_WithCustomMessages_StillLoadsTheActualText()
    {
        // Sibling/no-regression case: when the author DID customize these, the real text must still
        // come through unchanged (this must not regress into null-ing out real content).
        var worksheetBody = """
            <dataValidations count="1">
              <dataValidation type="whole" operator="between" allowBlank="1" showInputMessage="1" showErrorMessage="1"
                              errorTitle="Invalid" error="Out of range"
                              promptTitle="Enter a number" prompt="Between 1 and 100" sqref="A2:A5">
                <formula1>1</formula1>
                <formula2>100</formula2>
              </dataValidation>
            </dataValidations>
            """;

        using var stream = BuildMinimalXlsx(worksheetBody);
        var workbook = new XlsxFileAdapter().Load(stream);
        var dv = workbook.GetSheetAt(0).DataValidations.Should().ContainSingle().Subject;

        dv.ErrorTitle.Should().Be("Invalid");
        dv.ErrorMessage.Should().Be("Out of range");
        dv.PromptTitle.Should().Be("Enter a number");
        dv.PromptMessage.Should().Be("Between 1 and 100");
    }

    /// <summary>
    /// Builds the smallest possible valid XLSX stream that contains one worksheet with a few
    /// numeric cells and the given extra worksheet-root XML (dataValidations) appended after
    /// &lt;sheetData&gt; -- mirrors the real-Excel-shaped OOXML convention where List &lt;formula1&gt;
    /// source references never carry a leading '='.
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
