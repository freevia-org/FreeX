using System.Xml;
using System.Xml.Xsl;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class SpreadsheetXmlFileAdapterTests
{
    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromAttributeSet()
    {
        using var source = StreamFromString("<rows><row amount=\"42.5\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:attribute-set name="moneyCell">
                <xsl:attribute name="ss:StyleID">money</xsl:attribute>
              </xsl:attribute-set>
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Styles>
                    <ss:Style ss:ID="money">
                      <ss:NumberFormat ss:Format="$#,##0.00" />
                    </ss:Style>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Styled">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell xsl:use-attribute-sets="moneyCell">
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@amount" /></ss:Data>
                        </ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Styled");
        sheet.GetCell(1, 1)!.Value.Should().Be(new NumberValue(42.5));
        workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromStyleAttributeValueTemplates()
    {
        using var source = StreamFromString("""
            <report>
              <style id="money" format="$#,##0.00"/>
              <style id="percent" format="0.00%"/>
              <row label="Revenue" amount="42.5" style="money"/>
              <row label="Margin" amount="0.875" style="percent"/>
              <marker row="4" column="3" style="percent"/>
            </report>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/report">
                <ss:Workbook>
                  <ss:Styles>
                    <xsl:for-each select="style">
                      <ss:Style ss:ID="{@id}">
                        <ss:NumberFormat ss:Format="{@format}" />
                      </ss:Style>
                    </xsl:for-each>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Styled AVT">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@label"/></ss:Data></ss:Cell>
                          <ss:Cell ss:StyleID="{@style}"><ss:Data ss:Type="Number"><xsl:value-of select="@amount"/></ss:Data></ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                      <ss:Row ss:Index="{marker/@row}">
                        <ss:Cell ss:Index="{marker/@column}" ss:StyleID="{marker/@style}"/>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Styled AVT");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Revenue"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Margin"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(0.875));
        workbook.GetStyle(sheet.GetCell(1, 2)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
        workbook.GetStyle(sheet.GetCell(2, 2)!.StyleId).NumberFormat.Should().Be("0.00%");
        sheet.GetCell(4, 3).Should().BeNull();
        workbook.GetStyle(sheet.GetStyleOnly(4, 3)!.Value).NumberFormat.Should().Be("0.00%");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromDecimalFormat()
    {
        using var source = StreamFromString("<rows><row amount=\"1234.5\" ratio=\"0.875\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:decimal-format name="report" decimal-separator="," grouping-separator="." percent="%" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Formatted">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="format-number(row/@amount, '#.##0,00', 'report')" /></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="format-number(row/@ratio, '0,0%', 'report')" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Formatted");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("1.234,50"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("87,5%"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlNumberFormatStyles()
    {
        using var source = StreamFromString("""
            <rows>
              <row amount="12.5"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Styles>
                    <ss:Style ss:ID="money">
                      <ss:NumberFormat ss:Format="$#,##0.00"/>
                    </ss:Style>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Generated">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell ss:StyleID="money">
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@amount"/></ss:Data>
                        </ss:Cell>
                        <ss:Cell ss:Index="3" ss:StyleID="money"/>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
        sheet.GetCell(1, 3).Should().BeNull();
        workbook.GetStyle(sheet.GetStyleOnly(1, 3)!.Value).NumberFormat.Should().Be("$#,##0.00");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlInheritedNumberFormatStyles()
    {
        using var source = StreamFromString("""
            <rows>
              <row amount="12.5"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Styles>
                    <ss:Style ss:ID="money">
                      <ss:NumberFormat ss:Format="$#,##0.00"/>
                    </ss:Style>
                    <ss:Style ss:ID="moneyGenerated" ss:Parent="money"/>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Generated">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell ss:StyleID="moneyGenerated">
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@amount"/></ss:Data>
                        </ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromInheritedStyleAttributeValueTemplates()
    {
        using var source = StreamFromString("""
            <report>
              <base id="moneyBase" format="$#,##0.00"/>
              <style id="moneyChild" parent="moneyBase"/>
              <row amount="42.5" style="moneyChild"/>
            </report>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/report">
                <ss:Workbook>
                  <ss:Styles>
                    <ss:Style ss:ID="{base/@id}">
                      <ss:NumberFormat ss:Format="{base/@format}" />
                    </ss:Style>
                    <ss:Style ss:ID="{style/@id}" ss:Parent="{style/@parent}" />
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Inherited Style AVT">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell ss:StyleID="{row/@style}">
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@amount"/></ss:Data>
                        </ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Inherited Style AVT");
        sheet.GetCell(1, 1)!.Value.Should().Be(new NumberValue(42.5));
        workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
    }

    [Fact]
    public void LoadTransformed_InheritsSpreadsheetMlRowAndColumnNumberFormatStyles()
    {
        using var source = StreamFromString("""
            <rows>
              <row first="12.5" second="7.25" override="3.5"/>
              <row first="42.5" second="9.75" override="6.5"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Styles>
                    <ss:Style ss:ID="money">
                      <ss:NumberFormat ss:Format="$#,##0.00"/>
                    </ss:Style>
                    <ss:Style ss:ID="percent">
                      <ss:NumberFormat ss:Format="0.00%"/>
                    </ss:Style>
                    <ss:Style ss:ID="integer">
                      <ss:NumberFormat ss:Format="0"/>
                    </ss:Style>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Generated">
                    <ss:Table>
                      <ss:Column ss:StyleID="money"/>
                      <ss:Column ss:Index="3" ss:StyleID="integer"/>
                      <ss:Row ss:StyleID="percent">
                        <ss:Cell>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@first"/></ss:Data>
                        </ss:Cell>
                        <ss:Cell>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@second"/></ss:Data>
                        </ss:Cell>
                        <ss:Cell>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@override"/></ss:Data>
                        </ss:Cell>
                      </ss:Row>
                      <ss:Row>
                        <ss:Cell>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row[2]/@first"/></ss:Data>
                        </ss:Cell>
                        <ss:Cell>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row[2]/@second"/></ss:Data>
                        </ss:Cell>
                        <ss:Cell>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row[2]/@override"/></ss:Data>
                        </ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("0.00%");
        workbook.GetStyle(sheet.GetCell(1, 2)!.StyleId).NumberFormat.Should().Be("0.00%");
        workbook.GetStyle(sheet.GetCell(1, 3)!.StyleId).NumberFormat.Should().Be("0.00%");
        workbook.GetStyle(sheet.GetCell(2, 1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
        sheet.GetCell(2, 2)!.StyleId.Should().Be(StyleId.Default);
        workbook.GetStyle(sheet.GetCell(2, 3)!.StyleId).NumberFormat.Should().Be("0");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlFormulaCellNumberFormatStyle()
    {
        using var source = StreamFromString("""
            <rows>
              <row first="12.5" second="7.25"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Styles>
                    <ss:Style ss:ID="total">
                      <ss:NumberFormat ss:Format="$#,##0.00"/>
                    </ss:Style>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Formulas">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@first"/></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@second"/></ss:Data></ss:Cell>
                        <ss:Cell ss:Formula="=SUM(RC[-2]:RC[-1])" ss:StyleID="total">
                          <ss:Data ss:Type="Number">19.75</ss:Data>
                        </ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var formulaCell = workbook.GetSheetAt(0).GetCell(1, 3);
        formulaCell.Should().NotBeNull();
        formulaCell!.FormulaText.Should().Be("SUM(RC[-2]:RC[-1])");
        formulaCell.Value.Should().Be(new NumberValue(19.75));
        workbook.GetStyle(formulaCell.StyleId).NumberFormat.Should().Be("$#,##0.00");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromRowColumnStyleAttributeValueTemplates()
    {
        using var source = StreamFromString("""
            <report columnStyle="money" rowStyle="percent" first="0.875" second="42.5"/>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/report">
                <ss:Workbook>
                  <ss:Styles>
                    <ss:Style ss:ID="money"><ss:NumberFormat ss:Format="$#,##0.00"/></ss:Style>
                    <ss:Style ss:ID="percent"><ss:NumberFormat ss:Format="0.00%"/></ss:Style>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Style Layout AVT">
                    <ss:Table>
                      <ss:Column ss:StyleID="{@columnStyle}"/>
                      <ss:Row ss:StyleID="{@rowStyle}">
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="@first"/></ss:Data></ss:Cell>
                      </ss:Row>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="@second"/></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Style Layout AVT");
        sheet.GetCell(1, 1)!.Value.Should().Be(new NumberValue(0.875));
        workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("0.00%");
        sheet.GetCell(2, 1)!.Value.Should().Be(new NumberValue(42.5));
        workbook.GetStyle(sheet.GetCell(2, 1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
    }

}
