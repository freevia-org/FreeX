using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed partial class XsltWorkbookTransformTests
{
    [Fact]
    public void TransformToSpreadsheetXml_StylesheetKeys_CanJoinLookupRows()
    {
        using var source = StreamFromString("""
            <catalog>
              <categories>
                <category id="A" name="Hardware" />
                <category id="B" name="Services" />
              </categories>
              <items>
                <item sku="100" category="B" />
              </items>
            </catalog>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:key name="categoryById" match="category" use="@id" />
              <xsl:template match="/catalog">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Lookup">
                    <ss:Table>
                      <xsl:for-each select="items/item">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@sku" /></ss:Data></ss:Cell>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="key('categoryById', @category)/@name" /></ss:Data></ss:Cell>
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
            .Contain("<ss:Data ss:Type=\"String\">100</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"String\">Services</ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetSort_OrdersGeneratedRows()
    {
        using var source = StreamFromString("""
            <rows>
              <row name="Gamma" amount="12.5" />
              <row name="Alpha" amount="42.5" />
              <row name="Beta" amount="42.5" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Sorted">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <xsl:sort select="@amount" data-type="number" order="descending" />
                        <xsl:sort select="@name" data-type="text" order="ascending" />
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name" /></ss:Data></ss:Cell>
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
        var xml = reader.ReadToEnd();
        xml.IndexOf(">Alpha<", StringComparison.Ordinal).Should().BeLessThan(xml.IndexOf(">Beta<", StringComparison.Ordinal));
        xml.IndexOf(">Beta<", StringComparison.Ordinal).Should().BeLessThan(xml.IndexOf(">Gamma<", StringComparison.Ordinal));
    }

    [Fact]
    public void TransformToSpreadsheetXml_DynamicElementsAndAttributes_GenerateSpreadsheetMl()
    {
        using var source = StreamFromString("<rows><row sheet=\"Dynamic\" label=\"Alpha\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <xsl:element name="ss:Workbook">
                  <xsl:element name="ss:Worksheet">
                    <xsl:attribute name="ss:Name"><xsl:value-of select="row/@sheet" /></xsl:attribute>
                    <xsl:element name="ss:Table">
                      <xsl:element name="ss:Row">
                        <xsl:element name="ss:Cell">
                          <xsl:element name="ss:Data">
                            <xsl:attribute name="ss:Type">String</xsl:attribute>
                            <xsl:value-of select="row/@label" />
                          </xsl:element>
                        </xsl:element>
                      </xsl:element>
                    </xsl:element>
                  </xsl:element>
                </xsl:element>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .Contain("<ss:Workbook")
            .And.Contain("ss:Name=\"Dynamic\"")
            .And.Contain("<ss:Data ss:Type=\"String\">Alpha</ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_DynamicCellAttributes_GenerateSpreadsheetMl()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var formulaCell = XDocument.Load(transformed).Descendants(ss + "Cell").ElementAt(2);

        formulaCell.Attribute(ss + "Formula")!.Value.Should().Be("=SUM(RC[-2]:RC[-1])");
        formulaCell.Attribute(ss + "StyleID")!.Value.Should().Be("total");
        formulaCell.Element(ss + "Data")!.Value.Should().Be("19.75");
    }

    [Fact]
    public void TransformToSpreadsheetXml_NamespaceUriDynamicElements_GenerateSpreadsheetMl()
    {
        using var source = StreamFromString("<rows><row sheet=\"UriDynamic\" label=\"Bravo\" amount=\"18.75\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:variable name="spreadsheetml" select="'urn:schemas-microsoft-com:office:spreadsheet'" />
              <xsl:template match="/rows">
                <xsl:element name="Workbook" namespace="{$spreadsheetml}">
                  <xsl:element name="Worksheet" namespace="{$spreadsheetml}">
                    <xsl:attribute name="Name" namespace="{$spreadsheetml}"><xsl:value-of select="row/@sheet" /></xsl:attribute>
                    <xsl:element name="Table" namespace="{$spreadsheetml}">
                      <xsl:element name="Row" namespace="{$spreadsheetml}">
                        <xsl:element name="Cell" namespace="{$spreadsheetml}">
                          <xsl:element name="Data" namespace="{$spreadsheetml}">
                            <xsl:attribute name="Type" namespace="{$spreadsheetml}">String</xsl:attribute>
                            <xsl:value-of select="row/@label" />
                          </xsl:element>
                        </xsl:element>
                        <xsl:element name="Cell" namespace="{$spreadsheetml}">
                          <xsl:element name="Data" namespace="{$spreadsheetml}">
                            <xsl:attribute name="Type" namespace="{$spreadsheetml}">Number</xsl:attribute>
                            <xsl:value-of select="row/@amount" />
                          </xsl:element>
                        </xsl:element>
                      </xsl:element>
                    </xsl:element>
                  </xsl:element>
                </xsl:element>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var document = XDocument.Load(transformed);
        var worksheet = document.Root!.Element(ss + "Worksheet");
        var dataCells = worksheet!.Descendants(ss + "Data").ToArray();

        document.Root.Name.Should().Be(ss + "Workbook");
        worksheet.Attribute(ss + "Name")!.Value.Should().Be("UriDynamic");
        dataCells.Select(cell => cell.Attribute(ss + "Type")!.Value).Should().Equal("String", "Number");
        dataCells.Select(cell => cell.Value).Should().Equal("Bravo", "18.75");
    }

    [Fact]
    public void TransformToSpreadsheetXml_CopyOf_CopiesSpreadsheetMlFragmentsFromSource()
    {
        using var source = StreamFromString("""
            <payload xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <template>
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Copied">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String">Alpha</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </template>
            </payload>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" omit-xml-declaration="yes" />
              <xsl:template match="/payload">
                <xsl:copy-of select="template/*" />
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .StartWith("<ss:Workbook")
            .And.Contain("ss:Name=\"Copied\"")
            .And.Contain("<ss:Data ss:Type=\"String\">Alpha</ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_CallTemplateWithParams_GeneratesReusableCells()
    {
        using var source = StreamFromString("<rows><row label=\"Alpha\" amount=\"42.5\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template name="cell">
                <xsl:param name="type" />
                <xsl:param name="value" />
                <ss:Cell><ss:Data ss:Type="{$type}"><xsl:value-of select="$value" /></ss:Data></ss:Cell>
              </xsl:template>
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Templates">
                    <ss:Table>
                      <ss:Row>
                        <xsl:call-template name="cell">
                          <xsl:with-param name="type" select="'String'" />
                          <xsl:with-param name="value" select="row/@label" />
                        </xsl:call-template>
                        <xsl:call-template name="cell">
                          <xsl:with-param name="type" select="'Number'" />
                          <xsl:with-param name="value" select="row/@amount" />
                        </xsl:call-template>
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
            .Contain("<ss:Data ss:Type=\"String\">Alpha</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"Number\">42.5</ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_ConditionalTemplates_GenerateOptionalCells()
    {
        using var source = StreamFromString("""
            <rows>
              <row name="Alpha" status="ok" note="Ready" />
              <row name="Beta" status="warn" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Conditional">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name" /></ss:Data></ss:Cell>
                          <ss:Cell>
                            <ss:Data ss:Type="String">
                              <xsl:choose>
                                <xsl:when test="@status = 'ok'">Pass</xsl:when>
                                <xsl:otherwise>Review</xsl:otherwise>
                              </xsl:choose>
                            </ss:Data>
                          </ss:Cell>
                          <xsl:if test="@note">
                            <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@note" /></ss:Data></ss:Cell>
                          </xsl:if>
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
            .Contain("<ss:Data ss:Type=\"String\">Pass</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"String\">Review</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"String\">Ready</ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_ApplyTemplatesWithModes_GeneratesRows()
    {
        using var source = StreamFromString("""
            <rows>
              <row label="Alpha" amount="42.5" />
              <row label="Beta" amount="7.25" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Applied">
                    <ss:Table>
                      <xsl:apply-templates select="row" mode="sheet-row" />
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
              <xsl:template match="row" mode="sheet-row">
                <ss:Row>
                  <xsl:apply-templates select="@label | @amount" mode="cell" />
                </ss:Row>
              </xsl:template>
              <xsl:template match="@label" mode="cell">
                <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="." /></ss:Data></ss:Cell>
              </xsl:template>
              <xsl:template match="@amount" mode="cell">
                <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="." /></ss:Data></ss:Cell>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .Contain("<ss:Worksheet ss:Name=\"Applied\">")
            .And.Contain("<ss:Data ss:Type=\"String\">Alpha</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"Number\">42.5</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"String\">Beta</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"Number\">7.25</ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_VariablesAndAggregates_GenerateSummaryCells()
    {
        using var source = StreamFromString("""
            <rows>
              <row label="Alpha" amount="42.5" />
              <row label="Beta" amount="7.25" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:variable name="sheetName" select="'Variable Summary'" />
              <xsl:template match="/rows">
                <xsl:variable name="rowCount" select="count(row)" />
                <xsl:variable name="total" select="sum(row/@amount)" />
                <ss:Workbook>
                  <ss:Worksheet ss:Name="{$sheetName}">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String">Rows</ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="$rowCount" /></ss:Data></ss:Cell>
                      </ss:Row>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String">Total</ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="$total" /></ss:Data></ss:Cell>
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
            .Contain("<ss:Worksheet ss:Name=\"Variable Summary\">")
            .And.Contain("<ss:Data ss:Type=\"String\">Rows</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"Number\">2</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"String\">Total</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"Number\">49.75</ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_ResultTreeVariable_CopiesReusableSpreadsheetMlRows()
    {
        using var source = StreamFromString("""
            <rows>
              <row label="Alpha" amount="42.5" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <xsl:variable name="headerRow">
                  <ss:Row>
                    <ss:Cell><ss:Data ss:Type="String">Name</ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="String">Amount</ss:Data></ss:Cell>
                  </ss:Row>
                </xsl:variable>
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Reusable">
                    <ss:Table>
                      <xsl:copy-of select="$headerRow" />
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/@label" /></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@amount" /></ss:Data></ss:Cell>
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
        var rows = document.Root!.Descendants(ss + "Row").ToArray();

        rows.Should().HaveCount(2);
        rows[0].Descendants(ss + "Data").Select(cell => cell.Value).Should().Equal("Name", "Amount");
        rows[1].Descendants(ss + "Data").Select(cell => cell.Value).Should().Equal("Alpha", "42.5");
    }

    [Fact]
    public void TransformToSpreadsheetXml_TextInstruction_GeneratesLiteralCellText()
    {
        using var source = StreamFromString("""
            <rows>
              <row first="Alpha" second="Beta" amount="42.5" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Text">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell>
                          <ss:Data ss:Type="String">
                            <xsl:value-of select="row/@first" />
                            <xsl:text> - </xsl:text>
                            <xsl:value-of select="row/@second" />
                          </ss:Data>
                        </ss:Cell>
                        <ss:Cell>
                          <ss:Data ss:Type="String">
                            <xsl:text>Total: </xsl:text>
                            <xsl:value-of select="row/@amount" />
                          </ss:Data>
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
        var values = XDocument.Load(transformed).Descendants(ss + "Data").Select(cell => cell.Value).ToArray();

        values.Should().Equal("Alpha - Beta", "Total: 42.5");
    }

    [Fact]
    public void TransformToSpreadsheetXml_CommentsAndProcessingInstructions_GenerateSpreadsheetMl()
    {
        using var source = StreamFromString("<rows><row label=\"Alpha\" amount=\"42.5\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <xsl:processing-instruction name="freex">source="xslt"</xsl:processing-instruction>
                  <ss:Worksheet ss:Name="Noise">
                    <xsl:comment>generated worksheet metadata</xsl:comment>
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/@label" /></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@amount" /></ss:Data></ss:Cell>
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
            .Contain("<?freex source=\"xslt\"?>")
            .And.Contain("<!--generated worksheet metadata-->")
            .And.Contain("<ss:Data ss:Type=\"String\">Alpha</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"Number\">42.5</ss:Data>");
    }

}
