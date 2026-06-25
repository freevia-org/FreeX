using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for G5: the x14 data-validation reader must default AllowBlank to FALSE
/// for new (x14-only) rules that have no allowBlank attribute. OOXML specifies FALSE as the
/// default; the old code incorrectly defaulted to TRUE, silently enabling "ignore blank".
/// </summary>
public sealed class XlsxX14DataValidationReaderAllowBlankTests
{
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XDocument BuildWorksheetWithX14DvNoAllowBlank()
    {
        // Minimal worksheet XML: one x14 data-validation rule with no allowBlank attribute.
        return XDocument.Parse("""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData/>
              <extLst>
                <ext uri="{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}"
                     xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                     xmlns:xm="http://schemas.microsoft.com/office/excel/2006/main">
                  <x14:dataValidations count="1">
                    <x14:dataValidation type="list">
                      <x14:formula1><xm:f>Sheet2!$A$1:$A$5</xm:f></x14:formula1>
                      <xm:sqref>B2</xm:sqref>
                    </x14:dataValidation>
                  </x14:dataValidations>
                </ext>
              </extLst>
            </worksheet>
            """);
    }

    [Fact]
    public void Apply_X14OnlyRuleWithNoAllowBlankAttr_DefaultsAllowBlankToFalse()
    {
        var worksheetXml = BuildWorksheetWithX14DvNoAllowBlank();
        var metadata = XlsxX14DataValidationReader.Read(worksheetXml);
        metadata.Should().HaveCount(1);

        var sheet = new Workbook("G5Test").AddSheet("Sheet1");
        // No legacy rule exists → Apply creates a new rule.
        XlsxX14DataValidationReader.Apply(sheet, metadata);

        sheet.DataValidations.Should().HaveCount(1);
        sheet.DataValidations[0].AllowBlank.Should().BeFalse(
            "OOXML default for allowBlank is FALSE; an absent allowBlank attribute must NOT enable ignore-blank");
    }

    [Fact]
    public void Apply_X14OnlyRuleWithAllowBlank1_AllowBlankIsTrue()
    {
        var doc = XDocument.Parse("""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData/>
              <extLst>
                <ext uri="{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}"
                     xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                     xmlns:xm="http://schemas.microsoft.com/office/excel/2006/main">
                  <x14:dataValidations count="1">
                    <x14:dataValidation type="list" allowBlank="1">
                      <x14:formula1><xm:f>Sheet2!$A$1:$A$5</xm:f></x14:formula1>
                      <xm:sqref>C3</xm:sqref>
                    </x14:dataValidation>
                  </x14:dataValidations>
                </ext>
              </extLst>
            </worksheet>
            """);

        var metadata = XlsxX14DataValidationReader.Read(doc);
        var sheet = new Workbook("G5TrueTest").AddSheet("Sheet1");
        XlsxX14DataValidationReader.Apply(sheet, metadata);

        sheet.DataValidations.Should().HaveCount(1);
        sheet.DataValidations[0].AllowBlank.Should().BeTrue(
            "an explicit allowBlank=\"1\" attribute must still produce AllowBlank=true");
    }
}
