using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed partial class XsltWorkbookTransformTests
{
    [Fact]
    public void TransformToSpreadsheetXml_CommentAttributeValueTemplates_GenerateSpreadsheetMl()
    {
        using var source = StreamFromString("""
            <notes>
              <note label="Total" author="Finance" text="Check generated total"/>
            </notes>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/notes">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Comments">
                    <ss:Table>
                      <xsl:for-each select="note">
                        <ss:Row>
                          <ss:Cell>
                            <ss:Data ss:Type="String"><xsl:value-of select="@label"/></ss:Data>
                            <ss:Comment ss:Author="{@author}">
                              <ss:Data><xsl:value-of select="@text"/></ss:Data>
                            </ss:Comment>
                          </ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var cell = XDocument.Load(transformed).Descendants(ss + "Cell").Single();
        var comment = cell.Element(ss + "Comment")!;

        cell.Element(ss + "Data")!.Value.Should().Be("Total");
        comment.Attribute(ss + "Author")!.Value.Should().Be("Finance");
        comment.Element(ss + "Data")!.Value.Should().Be("Check generated total");
    }

    [Fact]
    public void TransformToSpreadsheetXml_FormulaAttributeValueTemplate_GeneratesSpreadsheetMl()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var formulaCell = XDocument.Load(transformed).Descendants(ss + "Cell").ElementAt(2);

        formulaCell.Attribute(ss + "Formula")!.Value.Should().Be("=SUM(RC[-2]:RC[-1])");
        formulaCell.Element(ss + "Data")!.Value.Should().Be("19.75");
    }

    [Fact]
    public void TransformToSpreadsheetXml_MergeAttributeValueTemplates_GenerateSpreadsheetMl()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var cells = XDocument.Load(transformed).Descendants(ss + "Cell").ToList();

        cells[0].Attribute(ss + "MergeAcross")!.Value.Should().Be("1");
        cells[0].Attribute(ss + "MergeDown")!.Value.Should().Be("2");
        cells[0].Element(ss + "Data")!.Value.Should().Be("Summary");
        cells[1].Element(ss + "Data")!.Value.Should().Be("Detail");
    }

    [Fact]
    public void TransformToSpreadsheetXml_WorksheetVisibleAttributeValueTemplates_GenerateSpreadsheetMl()
    {
        using var source = StreamFromString("""
            <sheets>
              <sheet name="Hidden report" visible="SheetHidden"/>
              <sheet name="Audit stash" visible="SheetVeryHidden"/>
            </sheets>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/sheets">
                <ss:Workbook>
                  <xsl:for-each select="sheet">
                    <ss:Worksheet ss:Name="{@name}" ss:Visible="{@visible}">
                      <ss:Table>
                        <ss:Row><ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data></ss:Cell></ss:Row>
                      </ss:Table>
                    </ss:Worksheet>
                  </xsl:for-each>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var worksheets = XDocument.Load(transformed).Descendants(ss + "Worksheet").ToList();

        worksheets.Select(sheet => sheet.Attribute(ss + "Name")!.Value)
            .Should().Equal("Hidden report", "Audit stash");
        worksheets.Select(sheet => sheet.Attribute(ss + "Visible")!.Value)
            .Should().Equal("SheetHidden", "SheetVeryHidden");
    }

    [Fact]
    public void TransformToSpreadsheetXml_RowColumnLayoutAttributeValueTemplates_GenerateSpreadsheetMl()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var document = XDocument.Load(transformed);
        var column = document.Descendants(ss + "Column").Single();
        var row = document.Descendants(ss + "Row").Single();
        var cell = document.Descendants(ss + "Cell").Single();

        column.Attribute(ss + "Index")!.Value.Should().Be("2");
        column.Attribute(ss + "Span")!.Value.Should().Be("1");
        column.Attribute(ss + "Width")!.Value.Should().Be("22.75");
        column.Attribute(ss + "Hidden")!.Value.Should().Be("TRUE");
        row.Attribute(ss + "Index")!.Value.Should().Be("3");
        row.Attribute(ss + "Span")!.Value.Should().Be("1");
        row.Attribute(ss + "Height")!.Value.Should().Be("28.5");
        row.Attribute(ss + "Hidden")!.Value.Should().Be("TRUE");
        cell.Attribute(ss + "Index")!.Value.Should().Be("2");
        cell.Element(ss + "Data")!.Value.Should().Be("Layout");
    }

    [Fact]
    public void TransformToSpreadsheetXml_RowColumnStyleAttributeValueTemplates_GenerateSpreadsheetMl()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var document = XDocument.Load(transformed);
        var column = document.Descendants(ss + "Column").Single();
        var rows = document.Descendants(ss + "Row").ToList();

        column.Attribute(ss + "StyleID")!.Value.Should().Be("money");
        rows[0].Attribute(ss + "StyleID")!.Value.Should().Be("percent");
        rows[0].Element(ss + "Cell")!.Element(ss + "Data")!.Value.Should().Be("0.875");
        rows[1].Element(ss + "Cell")!.Element(ss + "Data")!.Value.Should().Be("42.5");
    }

    [Fact]
    public void TransformToSpreadsheetXml_WorksheetOptionsDynamicValues_GenerateSpreadsheetMl()
    {
        using var source = StreamFromString("""
            <view sheet="Frozen report" rows="2" cols="3" showGridlines="false" printGridlines="true"/>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"
                xmlns:x="urn:schemas-microsoft-com:office:excel">
              <xsl:template match="/view">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="{@sheet}">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@sheet"/></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                    <x:WorksheetOptions>
                      <xsl:if test="@showGridlines = 'false'">
                        <x:DoNotDisplayGridlines/>
                      </xsl:if>
                      <xsl:if test="@printGridlines = 'true'">
                        <x:Print><x:Gridlines/></x:Print>
                      </xsl:if>
                      <x:FreezePanes/>
                      <x:FrozenNoSplit/>
                      <x:SplitHorizontal><xsl:value-of select="@rows"/></x:SplitHorizontal>
                      <x:TopRowBottomPane><xsl:value-of select="@rows"/></x:TopRowBottomPane>
                      <x:SplitVertical><xsl:value-of select="@cols"/></x:SplitVertical>
                      <x:LeftColumnRightPane><xsl:value-of select="@cols"/></x:LeftColumnRightPane>
                    </x:WorksheetOptions>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        XNamespace x = "urn:schemas-microsoft-com:office:excel";
        var worksheet = XDocument.Load(transformed).Descendants(ss + "Worksheet").Single();
        var options = worksheet.Element(x + "WorksheetOptions")!;

        worksheet.Attribute(ss + "Name")!.Value.Should().Be("Frozen report");
        options.Element(x + "DoNotDisplayGridlines").Should().NotBeNull();
        options.Element(x + "Print")?.Element(x + "Gridlines").Should().NotBeNull();
        options.Element(x + "FreezePanes").Should().NotBeNull();
        options.Element(x + "FrozenNoSplit").Should().NotBeNull();
        options.Element(x + "SplitHorizontal")!.Value.Should().Be("2");
        options.Element(x + "TopRowBottomPane")!.Value.Should().Be("2");
        options.Element(x + "SplitVertical")!.Value.Should().Be("3");
        options.Element(x + "LeftColumnRightPane")!.Value.Should().Be("3");
    }

    [Fact]
    public void TransformToSpreadsheetXml_NamedRangeAttributeValueTemplate_GeneratesSpreadsheetMl()
    {
        using var source = StreamFromString("""
            <report sheet="Q1 Bob's Team" range="='Q1 Bob''s Team'!$A$1:$B$2">
              <row name="Alpha" amount="12.5"/>
              <row name="Beta" amount="7.25"/>
            </report>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/report">
                <ss:Workbook>
                  <ss:Names>
                    <ss:NamedRange ss:Name="GeneratedRows" ss:RefersTo="{@range}"/>
                  </ss:Names>
                  <ss:Worksheet ss:Name="{@sheet}">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data></ss:Cell>
                          <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="@amount"/></ss:Data></ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var document = XDocument.Load(transformed);
        var namedRange = document.Descendants(ss + "NamedRange").Single();

        namedRange.Attribute(ss + "Name")!.Value.Should().Be("GeneratedRows");
        namedRange.Attribute(ss + "RefersTo")!.Value.Should().Be("='Q1 Bob''s Team'!$A$1:$B$2");
        document.Descendants(ss + "Worksheet").Single().Attribute(ss + "Name")!.Value.Should().Be("Q1 Bob's Team");
    }

    [Fact]
    public void TransformToSpreadsheetXml_DataTypeAttributeValueTemplate_GeneratesSpreadsheetMl()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var data = XDocument.Load(transformed).Descendants(ss + "Data").ToList();

        data.Select(element => element.Attribute(ss + "Type")!.Value)
            .Should().Equal("String", "Number", "Boolean", "DateTime", "Error");
        data.Select(element => element.Value)
            .Should().Equal("Ready", "42.25", "1", "2026-05-31T08:15:30", "#VALUE!");
    }

    [Fact]
    public void TransformToSpreadsheetXml_HyperlinkAttributeValueTemplates_GenerateSpreadsheetMl()
    {
        using var source = StreamFromString("""
            <links>
              <link label="Review" url="https://example.com/review" tip="Open review"/>
            </links>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/links">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Links">
                    <ss:Table>
                      <xsl:for-each select="link">
                        <ss:Row>
                          <ss:Cell ss:HRef="{@url}" ss:HRefScreenTip="{@tip}">
                            <ss:Data ss:Type="String"><xsl:value-of select="@label"/></ss:Data>
                          </ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var cell = XDocument.Load(transformed).Descendants(ss + "Cell").Single();

        cell.Attribute(ss + "HRef")!.Value.Should().Be("https://example.com/review");
        cell.Attribute(ss + "HRefScreenTip")!.Value.Should().Be("Open review");
        cell.Element(ss + "Data")!.Value.Should().Be("Review");
    }

    [Fact]
    public void TransformToSpreadsheetXml_InternalHyperlinkAttributeValueTemplates_GenerateSpreadsheetMl()
    {
        using var source = StreamFromString("""
            <links>
              <link label="Jump to summary" target="#'Q1 Summary'!A1" tip="Open summary"/>
            </links>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/links">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Links">
                    <ss:Table>
                      <xsl:for-each select="link">
                        <ss:Row>
                          <ss:Cell ss:HRef="{@target}" ss:HRefScreenTip="{@tip}">
                            <ss:Data ss:Type="String"><xsl:value-of select="@label"/></ss:Data>
                          </ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var cell = XDocument.Load(transformed).Descendants(ss + "Cell").Single();

        cell.Attribute(ss + "HRef")!.Value.Should().Be("#'Q1 Summary'!A1");
        cell.Attribute(ss + "HRefScreenTip")!.Value.Should().Be("Open summary");
        cell.Element(ss + "Data")!.Value.Should().Be("Jump to summary");
    }

    [Fact]
    public void TransformToSpreadsheetXml_EmailHyperlinkAttributeValueTemplates_GenerateSpreadsheetMl()
    {
        using var source = StreamFromString("""
            <links>
              <link label="Email finance" target="mailto:finance@example.com" tip="Send email"/>
            </links>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/links">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Links">
                    <ss:Table>
                      <xsl:for-each select="link">
                        <ss:Row>
                          <ss:Cell ss:HRef="{@target}" ss:HRefScreenTip="{@tip}">
                            <ss:Data ss:Type="String"><xsl:value-of select="@label"/></ss:Data>
                          </ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var cell = XDocument.Load(transformed).Descendants(ss + "Cell").Single();

        cell.Attribute(ss + "HRef")!.Value.Should().Be("mailto:finance@example.com");
        cell.Attribute(ss + "HRefScreenTip")!.Value.Should().Be("Send email");
        cell.Element(ss + "Data")!.Value.Should().Be("Email finance");
    }

    [Fact]
    public void TransformToSpreadsheetXml_NumberInstruction_GeneratesFormattedSequenceCells()
    {
        using var source = StreamFromString("""
            <rows>
              <row label="Alpha" />
              <row label="Beta" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Numbered">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:number value="position()" format="001" /></ss:Data></ss:Cell>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@label" /></ss:Data></ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .Contain("<ss:Worksheet ss:Name=\"Numbered\">")
            .And.Contain("<ss:Data ss:Type=\"String\">001</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"String\">Alpha</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"String\">002</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"String\">Beta</ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_AttributeSet_GeneratesStyledCells()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .Contain("<ss:Style ss:ID=\"money\">")
            .And.Contain("<ss:Cell ss:StyleID=\"money\">")
            .And.Contain("<ss:Data ss:Type=\"Number\">42.5</ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StyleAttributeValueTemplates_GenerateSpreadsheetMl()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var document = XDocument.Load(transformed);

        document.Descendants(ss + "Style")
            .Select(element => element.Attribute(ss + "ID")!.Value)
            .Should().Equal("money", "percent");
        document.Descendants(ss + "NumberFormat")
            .Select(element => element.Attribute(ss + "Format")!.Value)
            .Should().Equal("$#,##0.00", "0.00%");
        document.Descendants(ss + "Cell")
            .Select(element => element.Attribute(ss + "StyleID")?.Value)
            .Where(value => value is not null)
            .Should().Equal("money", "percent", "percent");
        document.Descendants(ss + "Cell").Last().Attribute(ss + "Index")!.Value.Should().Be("3");
    }

    [Fact]
    public void TransformToSpreadsheetXml_InheritedStyleAttributeValueTemplates_GenerateSpreadsheetMl()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var document = XDocument.Load(transformed);
        var styles = document.Descendants(ss + "Style").ToList();
        var cell = document.Descendants(ss + "Cell").Single();

        styles.Select(style => style.Attribute(ss + "ID")?.Value)
            .Should().Equal("moneyBase", "moneyChild");
        styles[0].Element(ss + "NumberFormat")!.Attribute(ss + "Format")!.Value.Should().Be("$#,##0.00");
        styles[1].Attribute(ss + "Parent")!.Value.Should().Be("moneyBase");
        cell.Attribute(ss + "StyleID")!.Value.Should().Be("moneyChild");
        cell.Element(ss + "Data")!.Value.Should().Be("42.5");
    }

    [Fact]
    public void TransformToSpreadsheetXml_DecimalFormat_GeneratesFormattedTextCells()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .Contain("<ss:Worksheet ss:Name=\"Formatted\">")
            .And.Contain("<ss:Data ss:Type=\"String\">1.234,50</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"String\">87,5%</ss:Data>");
    }

}
