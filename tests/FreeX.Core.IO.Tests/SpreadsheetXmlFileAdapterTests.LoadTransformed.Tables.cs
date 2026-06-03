using System.Xml;
using System.Xml.Xsl;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class SpreadsheetXmlFileAdapterTests
{
    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromDynamicCellAttributes()
    {
        using var source = StreamFromString("""
            <rows>
              <row first="12.5" second="7.25" formula="=SUM(RC[-2]:RC[-1])" total="19.75" style="total"/>
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
                      <ss:NumberFormat ss:Format="0.00"/>
                    </ss:Style>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Dynamic Attributes">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@first"/></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@second"/></ss:Data></ss:Cell>
                        <xsl:element name="ss:Cell">
                          <xsl:attribute name="ss:Formula"><xsl:value-of select="row/@formula"/></xsl:attribute>
                          <xsl:attribute name="ss:StyleID"><xsl:value-of select="row/@style"/></xsl:attribute>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@total"/></ss:Data>
                        </xsl:element>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        var formulaCell = sheet.GetCell(1, 3);
        sheet.Name.Should().Be("Dynamic Attributes");
        formulaCell.Should().NotBeNull();
        formulaCell!.FormulaText.Should().Be("SUM(RC[-2]:RC[-1])");
        formulaCell.Value.Should().Be(new NumberValue(19.75));
        workbook.GetStyle(formulaCell.StyleId).NumberFormat.Should().Be("0.00");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlScalarValueTypesAndIndexes()
    {
        using var source = StreamFromString("""
            <rows>
              <row label="Ready" amount="42.25" active="1" timestamp="2026-05-31T08:15:30" error="#N/A"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Typed">
                    <ss:Table>
                      <ss:Row ss:Index="3">
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/@label"/></ss:Data></ss:Cell>
                        <ss:Cell ss:Index="3"><ss:Data ss:Type="Number"><xsl:value-of select="row/@amount"/></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Boolean"><xsl:value-of select="row/@active"/></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="DateTime"><xsl:value-of select="row/@timestamp"/></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Error"><xsl:value-of select="row/@error"/></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Typed");
        sheet.GetCell(3, 1)!.Value.Should().Be(new TextValue("Ready"));
        sheet.GetCell(3, 2).Should().BeNull();
        sheet.GetCell(3, 3)!.Value.Should().Be(new NumberValue(42.25));
        sheet.GetCell(3, 4)!.Value.Should().Be(new BoolValue(true));
        sheet.GetCell(3, 5)!.Value.Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 31, 8, 15, 30)));
        sheet.GetCell(3, 6)!.Value.Should().Be(new ErrorValue("#N/A"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromDataTypeAttributeValueTemplate()
    {
        using var source = StreamFromString("""
            <cells>
              <cell type="String" value="Ready"/>
              <cell type="Number" value="42.25"/>
              <cell type="Boolean" value="1"/>
              <cell type="DateTime" value="2026-05-31T08:15:30"/>
              <cell type="Error" value="#VALUE!"/>
            </cells>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/cells">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Dynamic Types">
                    <ss:Table>
                      <ss:Row>
                        <xsl:for-each select="cell">
                          <ss:Cell>
                            <ss:Data ss:Type="{@type}"><xsl:value-of select="@value"/></ss:Data>
                          </ss:Cell>
                        </xsl:for-each>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Dynamic Types");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Ready"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.25));
        sheet.GetCell(1, 3)!.Value.Should().Be(new BoolValue(true));
        sheet.GetCell(1, 4)!.Value.Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 31, 8, 15, 30)));
        sheet.GetCell(1, 5)!.Value.Should().Be(new ErrorValue("#VALUE!"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlFormulasAndMergedCells()
    {
        using var source = StreamFromString("""
            <rows>
              <row label="Projected total" first="12.5" second="7.25"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Formulas">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell ss:MergeAcross="2">
                          <ss:Data ss:Type="String"><xsl:value-of select="row/@label"/></ss:Data>
                        </ss:Cell>
                      </ss:Row>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@first"/></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@second"/></ss:Data></ss:Cell>
                        <ss:Cell ss:Formula="=SUM(RC[-2]:RC[-1])"><ss:Data ss:Type="Number">19.75</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Formulas");
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 3)));
        var formulaCell = sheet.GetCell(2, 3);
        formulaCell.Should().NotBeNull();
        formulaCell!.FormulaText.Should().Be("SUM(RC[-2]:RC[-1])");
        formulaCell.Value.Should().Be(new NumberValue(19.75));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromMergeAttributeValueTemplates()
    {
        using var source = StreamFromString("""
            <layout>
              <header title="Summary" across="1" down="2" next="Detail"/>
            </layout>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/layout">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Merged AVT">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell ss:MergeAcross="{header/@across}" ss:MergeDown="{header/@down}">
                          <ss:Data ss:Type="String"><xsl:value-of select="header/@title"/></ss:Data>
                        </ss:Cell>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="header/@next"/></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Merged AVT");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Summary"));
        sheet.GetCell(1, 3)!.Value.Should().Be(new TextValue("Detail"));
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2)));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromFormulaAttributeValueTemplate()
    {
        using var source = StreamFromString("""
            <rows>
              <row first="12.5" second="7.25" formula="=SUM(RC[-2]:RC[-1])" total="19.75" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Dynamic Formula">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@first" /></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@second" /></ss:Data></ss:Cell>
                        <ss:Cell ss:Formula="{row/@formula}">
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@total" /></ss:Data>
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
        sheet.Name.Should().Be("Dynamic Formula");
        sheet.GetCell(1, 1)!.Value.Should().Be(new NumberValue(12.5));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(7.25));
        var formulaCell = sheet.GetCell(1, 3);
        formulaCell.Should().NotBeNull();
        formulaCell!.FormulaText.Should().Be("SUM(RC[-2]:RC[-1])");
        formulaCell.Value.Should().Be(new NumberValue(19.75));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlColumnSpanLayout()
    {
        using var source = StreamFromString("""
            <layout width="21.25"/>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/layout">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Layout">
                    <ss:Table>
                      <ss:Column ss:Index="2" ss:Span="2" ss:Width="{@width}" ss:Hidden="1"/>
                      <ss:Row>
                        <ss:Cell ss:Index="4"><ss:Data ss:Type="String">After span</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.ColumnWidths.Should().Contain(new KeyValuePair<uint, double>(2, 21.25));
        sheet.ColumnWidths.Should().Contain(new KeyValuePair<uint, double>(3, 21.25));
        sheet.ColumnWidths.Should().Contain(new KeyValuePair<uint, double>(4, 21.25));
        sheet.HiddenCols.Should().Contain([2u, 3u, 4u]);
        sheet.GetCell(1, 4)!.Value.Should().Be(new TextValue("After span"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlRowSpanLayout()
    {
        using var source = StreamFromString("""
            <layout height="24.5"/>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/layout">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Layout">
                    <ss:Table>
                      <ss:Row ss:Index="2" ss:Span="2" ss:Height="{@height}" ss:Hidden="1">
                        <ss:Cell><ss:Data ss:Type="String">Spanned row</ss:Data></ss:Cell>
                      </ss:Row>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String">After span</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.RowHeights.Should().Contain(new KeyValuePair<uint, double>(2, 24.5));
        sheet.RowHeights.Should().Contain(new KeyValuePair<uint, double>(3, 24.5));
        sheet.RowHeights.Should().Contain(new KeyValuePair<uint, double>(4, 24.5));
        sheet.HiddenRows.Should().Contain([2u, 3u, 4u]);
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Spanned row"));
        sheet.GetCell(5, 1)!.Value.Should().Be(new TextValue("After span"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromRowColumnLayoutAttributeValueTemplates()
    {
        using var source = StreamFromString("""
            <layout columnIndex="2" columnSpan="1" width="22.75" columnHidden="TRUE"
                    rowIndex="3" rowSpan="1" height="28.5" rowHidden="TRUE">
              <cell value="Layout"/>
            </layout>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/layout">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Layout AVT">
                    <ss:Table>
                      <ss:Column ss:Index="{@columnIndex}" ss:Span="{@columnSpan}" ss:Width="{@width}" ss:Hidden="{@columnHidden}"/>
                      <ss:Row ss:Index="{@rowIndex}" ss:Span="{@rowSpan}" ss:Height="{@height}" ss:Hidden="{@rowHidden}">
                        <ss:Cell ss:Index="{@columnIndex}">
                          <ss:Data ss:Type="String"><xsl:value-of select="cell/@value"/></ss:Data>
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
        sheet.Name.Should().Be("Layout AVT");
        sheet.ColumnWidths.Should().Contain(new KeyValuePair<uint, double>(2, 22.75));
        sheet.ColumnWidths.Should().Contain(new KeyValuePair<uint, double>(3, 22.75));
        sheet.HiddenCols.Should().Contain([2u, 3u]);
        sheet.RowHeights.Should().Contain(new KeyValuePair<uint, double>(3, 28.5));
        sheet.RowHeights.Should().Contain(new KeyValuePair<uint, double>(4, 28.5));
        sheet.HiddenRows.Should().Contain([3u, 4u]);
        sheet.GetCell(3, 2)!.Value.Should().Be(new TextValue("Layout"));
    }

}
