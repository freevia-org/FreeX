using System.Xml;
using System.Xml.Xsl;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class SpreadsheetXmlFileAdapterTests
{
    [Fact]
    public void LoadTransformed_AppliesSafeXsltAndLoadsSpreadsheetMlOutput()
    {
        using var source = StreamFromString("""
            <rows>
              <row name="Alpha" amount="12.5"/>
              <row name="Beta" amount="7.25"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:output method="xml" indent="yes"/>
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Transformed">
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Transformed");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(12.5));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Beta"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(7.25));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromSimplifiedStylesheetRoot()
    {
        using var source = StreamFromString("<rows><row sheet=\"Simplified\" label=\"Alpha\" amount=\"42.5\" /></rows>");
        using var stylesheet = StreamFromString("""
            <ss:Workbook xsl:version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="{/rows/row/@sheet}">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="/rows/row/@label" /></ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="/rows/row/@amount" /></ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Simplified");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void LoadTransformed_LoadsDisableOutputEscapingSpreadsheetMlOutput()
    {
        using var source = StreamFromString("""
            <rows>
              <workbook>&lt;ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"&gt;&lt;ss:Worksheet ss:Name="Generated"&gt;&lt;ss:Table&gt;&lt;ss:Row&gt;&lt;ss:Cell&gt;&lt;ss:Data ss:Type="String"&gt;Alpha&lt;/ss:Data&gt;&lt;/ss:Cell&gt;&lt;/ss:Row&gt;&lt;/ss:Table&gt;&lt;/ss:Worksheet&gt;&lt;/ss:Workbook&gt;</workbook>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <xsl:value-of select="workbook" disable-output-escaping="yes" />
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Generated");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
    }

    [Fact]
    public void LoadTransformed_LoadsSpreadsheetMlCDataOutput()
    {
        using var source = StreamFromString("<rows><row note=\"A &lt; B &amp; C\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:output method="xml" cdata-section-elements="ss:Data" omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="CData">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/@note" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("CData");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("A < B & C"));
    }

    [Fact]
    public void LoadTransformed_LoadsUtf16SpreadsheetMlOutput()
    {
        using var source = StreamFromString("<rows><row name=\"Delta\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:output method="xml" encoding="utf-16" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Utf16">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/@name" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Utf16");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Delta"));
    }

    [Fact]
    public void LoadTransformed_ReadsUtf16SourceAndStylesheetInputs()
    {
        using var source = Utf16StreamFromString("""<?xml version="1.0" encoding="utf-16"?><rows><row name="Echo" /></rows>""");
        using var stylesheet = Utf16StreamFromString("""
            <?xml version="1.0" encoding="utf-16"?>
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Utf16Inputs">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/@name" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Utf16Inputs");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Echo"));
    }

    [Fact]
    public void LoadTransformed_LoadsStandaloneSpreadsheetMlOutput()
    {
        using var source = StreamFromString("<rows><row name=\"Echo\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:output method="xml" standalone="yes" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Standalone">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/@name" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Standalone");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Echo"));
    }

    [Fact]
    public void LoadTransformed_LoadsOmittedDeclarationSpreadsheetMlOutput()
    {
        using var source = StreamFromString("<rows><row name=\"Hotel\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:output method="xml" omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="NoDeclaration">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/@name" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("NoDeclaration");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Hotel"));
    }

    [Fact]
    public void LoadTransformed_LoadsCompactSpreadsheetMlOutput()
    {
        using var source = StreamFromString("<rows><row name=\"India\" amount=\"17.5\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:output method="xml" indent="no" />
              <xsl:template match="/rows">
                <ss:Workbook><ss:Worksheet ss:Name="Compact"><ss:Table><ss:Row><ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/@name" /></ss:Data></ss:Cell><ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@amount" /></ss:Data></ss:Cell></ss:Row></ss:Table></ss:Worksheet></ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Compact");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("India"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(17.5));
    }

    [Fact]
    public void LoadTransformed_LoadsDefaultNamespaceSpreadsheetMlOutput()
    {
        using var source = StreamFromString("""
            <Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <Worksheet ss:Name="DefaultNamespace">
                <Table>
                  <Row>
                    <Cell><Data ss:Type="String">Juliet</Data></Cell>
                    <Cell><Data ss:Type="Number">21.25</Data></Cell>
                  </Row>
                </Table>
              </Worksheet>
            </Workbook>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="@*|node()">
                <xsl:copy>
                  <xsl:apply-templates select="@*|node()" />
                </xsl:copy>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("DefaultNamespace");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Juliet"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(21.25));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromWhitespaceRules()
    {
        using var source = StreamFromString("""
            <rows>
              <row>
                <label>   India   </label>
                <note>  Juliet  </note>
              </row>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:output method="xml" omit-xml-declaration="yes" />
              <xsl:strip-space elements="*" />
              <xsl:preserve-space elements="note" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Whitespace">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="normalize-space(row/label)" /></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/note" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Whitespace");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("India"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("  Juliet  "));
    }

    [Fact]
    public void LoadTransformed_AppliesXsltParametersToGeneratedSpreadsheetMl()
    {
        using var source = StreamFromString("<rows><row amount=\"42.5\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:param name="sheetName" />
              <xsl:param name="label" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="{$sheetName}">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="$label" /></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@amount" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(
            source,
            stylesheet,
            new Dictionary<string, string?>
            {
                ["sheetName"] = "Parameterized",
                ["label"] = "Runtime label"
            });

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Parameterized");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Runtime label"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromDefaultXsltParameters()
    {
        using var source = StreamFromString("<rows><row amount=\"18.75\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:param name="sheetName" select="'Default Parameters'" />
              <xsl:param name="label" select="'Default label'" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="{$sheetName}">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="$label" /></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@amount" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Default Parameters");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Default label"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(18.75));
    }

    [Fact]
    public void LoadTransformed_AppliesNamespacedXsltParametersToGeneratedSpreadsheetMl()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:cfg="urn:freex:xslt:test"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:param name="cfg:sheetName" />
              <xsl:param name="cfg:label" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="{$cfg:sheetName}">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="$cfg:label" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(
            source,
            stylesheet,
            new Dictionary<string, string?>
            {
                ["{urn:freex:xslt:test}sheetName"] = "Namespaced",
                ["{urn:freex:xslt:test}label"] = "Namespaced label"
            });

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Namespaced");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Namespaced label"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromXsltKeyLookup()
    {
        using var source = StreamFromString("""
            <catalog>
              <categories>
                <category id="A" name="Hardware" />
                <category id="B" name="Services" />
              </categories>
              <items>
                <item sku="100" category="B" amount="42.5" />
                <item sku="101" category="A" amount="7.25" />
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
                          <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="@amount" /></ss:Data></ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Lookup");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("100"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Services"));
        sheet.GetCell(1, 3)!.Value.Should().Be(new NumberValue(42.5));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("101"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new TextValue("Hardware"));
        sheet.GetCell(2, 3)!.Value.Should().Be(new NumberValue(7.25));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromXsltSort()
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
                          <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="@amount" /></ss:Data></ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Sorted");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Beta"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(42.5));
        sheet.GetCell(3, 1)!.Value.Should().Be(new TextValue("Gamma"));
        sheet.GetCell(3, 2)!.Value.Should().Be(new NumberValue(12.5));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromDynamicXsltElementsAndAttributes()
    {
        using var source = StreamFromString("<rows><row sheet=\"Dynamic\" label=\"Alpha\" amount=\"42.5\" /></rows>");
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
                        <xsl:element name="ss:Cell">
                          <xsl:element name="ss:Data">
                            <xsl:attribute name="ss:Type">Number</xsl:attribute>
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Dynamic");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromNamespaceUriDynamicElements()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("UriDynamic");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Bravo"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(18.75));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlCopiedFromSourceTemplate()
    {
        using var source = StreamFromString("""
            <payload xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <template>
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Copied">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String">Alpha</ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number">42.5</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </template>
            </payload>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/payload">
                <xsl:copy-of select="template/*" />
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Copied");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromCalledTemplates()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Templates");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromConditionalTemplates()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Conditional");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Pass"));
        sheet.GetCell(1, 3)!.Value.Should().Be(new TextValue("Ready"));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Beta"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new TextValue("Review"));
        sheet.GetCell(2, 3).Should().BeNull();
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromApplyTemplates()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Applied");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Beta"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(7.25));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromVariablesAndAggregates()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Variable Summary");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Rows"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(2));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Total"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(49.75));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromResultTreeVariable()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Reusable");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Name"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Amount"));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromTextInstruction()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Text");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha - Beta"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Total: 42.5"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromNumberInstruction()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Numbered");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("001"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("002"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new TextValue("Beta"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromNamespaceAlias()
    {
        using var source = StreamFromString("<rows><row label=\"Aliased\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:proto="urn:placeholder-spreadsheetml"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:namespace-alias stylesheet-prefix="proto" result-prefix="ss" />
              <xsl:template match="/rows">
                <proto:Workbook>
                  <proto:Worksheet proto:Name="Aliased">
                    <proto:Table>
                      <proto:Row>
                        <proto:Cell><proto:Data proto:Type="String"><xsl:value-of select="row/@label" /></proto:Data></proto:Cell>
                      </proto:Row>
                    </proto:Table>
                  </proto:Worksheet>
                </proto:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Aliased");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Aliased"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromCurrentFunction()
    {
        using var source = StreamFromString("""
            <report>
              <customers>
                <customer id="c1" name="Northwind" />
                <customer id="c2" name="Contoso" />
              </customers>
              <orders>
                <order id="100" customer="c2" />
                <order id="101" customer="c1" />
              </orders>
            </report>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/report">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Current">
                    <ss:Table>
                      <xsl:for-each select="orders/order">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@id" /></ss:Data></ss:Cell>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="/report/customers/customer[@id = current()/@customer]/@name" /></ss:Data></ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Current");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("100"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Contoso"));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("101"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new TextValue("Northwind"));
    }

}
