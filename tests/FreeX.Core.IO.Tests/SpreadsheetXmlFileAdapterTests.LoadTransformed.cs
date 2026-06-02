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

    [Theory]
    [InlineData(" ")]
    [InlineData("bad:name")]
    [InlineData("{urn:freex:xslt:test}")]
    public void LoadTransformed_InvalidXsltParameterName_ThrowsArgumentException(string parameterName)
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:param name="label" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Parameters">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="$label" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(
            source,
            stylesheet,
            new Dictionary<string, string?> { [parameterName] = "ignored" });

        act.Should().Throw<ArgumentException>()
            .Where(exception => exception.ParamName == "parameters");
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
    public void LoadTransformed_IgnoresGeneratedCommentsAndProcessingInstructions()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Noise");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromCommentAttributeValueTemplates()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Comments");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("Total"));
        sheet.Comments[address].Should().Be("Check generated total");
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
    public void LoadTransformed_PreservesSpreadsheetMlHyperlinksAndComments()
    {
        using var source = StreamFromString("""
            <rows>
              <row name="Review" url="https://example.com/review" note="Check generated output"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Generated">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell ss:HRef="{@url}" ss:HRefScreenTip="Open source">
                            <ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data>
                            <ss:Comment ss:Author="XSLT">
                              <ss:Data><xsl:value-of select="@note"/></ss:Data>
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("Review"));
        sheet.Hyperlinks[address].Should().Be("https://example.com/review");
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open source",
            ""));
        sheet.Comments[address].Should().Be("Check generated output");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromHyperlinkAttributeValueTemplates()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Links");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("Review"));
        sheet.Hyperlinks[address].Should().Be("https://example.com/review");
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open review",
            ""));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromInternalHyperlinkAttributeValueTemplates()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Links");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("Jump to summary"));
        sheet.Hyperlinks[address].Should().Be("#'Q1 Summary'!A1");
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Open summary",
            "'Q1 Summary'!A1"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromEmailHyperlinkAttributeValueTemplates()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Links");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("Email finance"));
        sheet.Hyperlinks[address].Should().Be("mailto:finance@example.com");
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Send email",
            ""));
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
    public void LoadTransformed_PreservesSpreadsheetMlWorkbookAndSheetMetadata()
    {
        using var source = StreamFromString("""
            <report sheet="Generated">
              <row name="Alpha" amount="12.5"/>
              <row name="Beta" amount="7.25"/>
            </report>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"
                xmlns:x="urn:schemas-microsoft-com:office:excel">
              <xsl:template match="/report">
                <ss:Workbook>
                  <ss:Names>
                    <ss:NamedRange ss:Name="GeneratedData" ss:RefersTo="=Generated!A1:B3"/>
                  </ss:Names>
                  <ss:Worksheet ss:Name="{@sheet}" ss:Visible="SheetHidden">
                    <ss:Table>
                      <ss:Column ss:Width="18.5"/>
                      <ss:Column ss:Index="3" ss:Hidden="1"/>
                      <ss:Row ss:Height="27.5">
                        <ss:Cell><ss:Data ss:Type="String">Name</ss:Data></ss:Cell>
                        <ss:Cell ss:Index="3"><ss:Data ss:Type="String">Amount</ss:Data></ss:Cell>
                      </ss:Row>
                      <xsl:for-each select="row">
                        <ss:Row ss:Index="{position() + 1}">
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data></ss:Cell>
                          <ss:Cell ss:Index="3"><ss:Data ss:Type="Number"><xsl:value-of select="@amount"/></ss:Data></ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                      <ss:Row ss:Index="4" ss:Hidden="1">
                        <ss:Cell><ss:Data ss:Type="String">Hidden footer</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                    <x:WorksheetOptions>
                      <x:DoNotDisplayGridlines/>
                      <x:Print>
                        <x:Gridlines/>
                      </x:Print>
                      <x:FreezePanes/>
                      <x:FrozenNoSplit/>
                      <x:SplitHorizontal>1</x:SplitHorizontal>
                      <x:TopRowBottomPane>1</x:TopRowBottomPane>
                      <x:SplitVertical>2</x:SplitVertical>
                      <x:LeftColumnRightPane>2</x:LeftColumnRightPane>
                    </x:WorksheetOptions>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Generated");
        sheet.IsHidden.Should().BeTrue();
        sheet.IsVeryHidden.Should().BeFalse();
        sheet.ShowGridlines.Should().BeFalse();
        sheet.PrintGridlines.Should().BeTrue();
        sheet.FrozenRows.Should().Be(1);
        sheet.FrozenCols.Should().Be(2);
        sheet.RowHeights[1].Should().Be(27.5);
        sheet.HiddenRows.Should().Contain(4u);
        sheet.ColumnWidths[1].Should().Be(18.5);
        sheet.HiddenCols.Should().Contain(3u);
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(3, 3)!.Value.Should().Be(new NumberValue(7.25));
        workbook.NamedRanges["GeneratedData"].Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2)));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromWorksheetVisibleAttributeValueTemplates()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var hidden = workbook.GetSheetAt(0);
        var veryHidden = workbook.GetSheetAt(1);
        hidden.Name.Should().Be("Hidden report");
        hidden.IsHidden.Should().BeTrue();
        hidden.IsVeryHidden.Should().BeFalse();
        hidden.GetCell(1, 1)!.Value.Should().Be(new TextValue("Hidden report"));
        veryHidden.Name.Should().Be("Audit stash");
        veryHidden.IsHidden.Should().BeTrue();
        veryHidden.IsVeryHidden.Should().BeTrue();
        veryHidden.GetCell(1, 1)!.Value.Should().Be(new TextValue("Audit stash"));
    }

    [Fact]
    public void LoadTransformed_PreservesQuotedSpreadsheetMlNamedRanges()
    {
        using var source = StreamFromString("""
            <report sheet="Q1 Bob's Team">
              <row name="Alpha"/>
              <row name="Beta"/>
            </report>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/report">
                <ss:Workbook>
                  <ss:Names>
                    <ss:NamedRange ss:Name="TeamRows" ss:RefersTo="='Q1 Bob''s Team'!$A$1:$A$2"/>
                  </ss:Names>
                  <ss:Worksheet ss:Name="{@sheet}">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data></ss:Cell>
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
        sheet.Name.Should().Be("Q1 Bob's Team");
        workbook.NamedRanges["TeamRows"].Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1)));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromNamedRangeAttributeValueTemplate()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Q1 Bob's Team");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(7.25));
        workbook.NamedRanges["GeneratedRows"].Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2)));
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

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromWorksheetOptionsDynamicValues()
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

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Frozen report");
        sheet.ShowGridlines.Should().BeFalse();
        sheet.PrintGridlines.Should().BeTrue();
        sheet.FrozenRows.Should().Be(2);
        sheet.FrozenCols.Should().Be(3);
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Frozen report"));
    }

    [Fact]
    public void LoadTransformed_UsesCurrentStreamPositionsAndLeavesInputStreamsOpen()
    {
        using var source = PositionedStreamFromString("ignored", "<rows><row name=\"Gamma\"/></rows>");
        using var stylesheet = PositionedStreamFromString("ignored", """
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Offset">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell>
                          <ss:Data ss:Type="String"><xsl:value-of select="/rows/row/@name"/></ss:Data>
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
        sheet.Name.Should().Be("Offset");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Gamma"));
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void LoadTransformed_AcceptsNonSeekableInputStreams()
    {
        using var source = NonSeekableStreamFromString("<rows><row name=\"Delta\"/></rows>");
        using var stylesheet = NonSeekableStreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="NonSeekable">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell>
                          <ss:Data ss:Type="String"><xsl:value-of select="row/@name"/></ss:Data>
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
        sheet.Name.Should().Be("NonSeekable");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Delta"));
    }

    [Fact]
    public void LoadTransformed_NullSource_ThrowsArgumentNullException()
    {
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/"><rows/></xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(null!, stylesheet);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "sourceXml");
    }

    [Fact]
    public void LoadTransformed_NullStylesheet_ThrowsArgumentNullException()
    {
        using var source = StreamFromString("<rows/>");

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "stylesheet");
    }

    [Fact]
    public void LoadTransformed_StylesheetFailure_DoesNotReadSourceStream()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("<xsl:stylesheet");

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*");
        source.Position.Should().Be(0);
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void LoadTransformed_EmptyStylesheet_ReportsTransformStylesheetDiagnostic()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString(string.Empty);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
        source.Position.Should().Be(0);
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 1, "maxOutputBytes")]
    [InlineData(1, 0, "maxInputCharacters")]
    public void LoadTransformed_InvalidSafetyLimit_ThrowsArgumentOutOfRangeException(
        long maxOutputBytes,
        long maxInputCharacters,
        string parameterName)
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Limited"><ss:Table/></ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(
            source,
            stylesheet,
            maxOutputBytes,
            maxInputCharacters);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Where(exception => exception.ParamName == parameterName);
    }

    [Fact]
    public void LoadTransformed_OutputAboveLimit_ReportsTransformSafetyDiagnostic()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Large">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String">This output is intentionally over the tiny adapter limit.</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet, maxOutputBytes: 32);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform output exceeded the 32 byte safety limit*");
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void LoadTransformed_SourceAboveInputLimit_ReportsTransformSourceDiagnostic()
    {
        using var source = StreamFromString($"<rows><row value=\"{new string('A', 1024)}\"/></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Limited"><ss:Table/></ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(
            source,
            stylesheet,
            XsltWorkbookTransform.DefaultMaxOutputBytes,
            maxInputCharacters: 512);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*source XML*");
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Theory]
    [InlineData("<rows>")]
    [InlineData("")]
    public void LoadTransformed_InvalidSourceXml_ReportsTransformSourceDiagnostic(string sourceXml)
    {
        using var source = StreamFromString(sourceXml);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Invalid Source"><ss:Table/></ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*source XML*")
            .WithInnerException<XmlException>();
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void LoadTransformed_SourceDtd_ReportsTransformSourceDiagnostic()
    {
        using var source = StreamFromString("""
            <!DOCTYPE rows [ <!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini"> ]>
            <rows>&xxe;</rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Source DTD"><ss:Table/></ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*source XML*")
            .WithInnerException<XmlException>();
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void LoadTransformed_StylesheetAboveInputLimit_ReportsTransformStylesheetDiagnostic()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Limited"><ss:Table/></ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(
            source,
            stylesheet,
            XsltWorkbookTransform.DefaultMaxOutputBytes,
            maxInputCharacters: 64);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*");
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void LoadTransformed_StylesheetDtd_ReportsTransformStylesheetDiagnostic()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <!DOCTYPE xsl:stylesheet [ <!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini"> ]>
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:value-of select="'blocked'"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void LoadTransformed_InvalidStylesheetExpression_ReportsTransformStylesheetDiagnostic()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:value-of select="count("/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
        source.Position.Should().Be(0);
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void LoadTransformed_RejectsExternalDocumentFunction()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:value-of select="document('file:///C:/Windows/win.ini')"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*External document access*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void LoadTransformed_RejectsRemoteDocumentFunction()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:value-of select="document('https://example.invalid/freex.xml')"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*External document access*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void LoadTransformed_RejectsStylesheetScript()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:msxsl="urn:schemas-microsoft-com:xslt"
                xmlns:user="urn:freex-test-script">
              <msxsl:script language="C#" implements-prefix="user">
                public string Value() { return "blocked"; }
              </msxsl:script>
              <xsl:template match="/">
                <xsl:value-of select="user:Value()"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*External document access and script are disabled*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void LoadTransformed_TerminatingMessage_ReportsTransformDiagnostic()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:message terminate="yes">adapter stop</xsl:message>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform failed*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void LoadTransformed_RejectsStylesheetInclude()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:include href="file:///C:/Windows/win.ini"/>
              <xsl:template match="/">
                <xsl:value-of select="'blocked'"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void LoadTransformed_RejectsRemoteStylesheetInclude()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:include href="https://example.invalid/freex.xsl"/>
              <xsl:template match="/">
                <xsl:value-of select="'blocked'"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void LoadTransformed_RejectsStylesheetImport()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:import href="file:///C:/Windows/win.ini"/>
              <xsl:template match="/">
                <xsl:value-of select="'blocked'"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void LoadTransformed_RejectsRemoteStylesheetImport()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:import href="https://example.invalid/freex.xsl"/>
              <xsl:template match="/">
                <xsl:value-of select="'blocked'"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void LoadTransformed_WrapsMalformedTransformOutputWithXsltContext()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="text"/>
              <xsl:template match="/">
                <xsl:text>&lt;ss:Workbook</xsl:text>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform output*")
            .WithInnerException<XmlException>();
    }

    [Fact]
    public void LoadTransformed_WrapsTextTransformOutputWithXsltContext()
    {
        using var source = StreamFromString("<rows><row name=\"India\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="text" />
              <xsl:template match="/rows">
                <xsl:value-of select="row/@name" />
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform output*")
            .WithInnerException<XmlException>();
    }

    [Fact]
    public void LoadTransformed_WrapsHtmlTransformOutputWithXsltContext()
    {
        using var source = StreamFromString("<rows><row name=\"April\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="html" omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <html>
                  <body>
                    <br />
                    <span><xsl:value-of select="row/@name" /></span>
                  </body>
                </html>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform output*")
            .WithInnerException<XmlException>();
    }

    [Fact]
    public void LoadTransformed_WrapsNonSpreadsheetMlOutputWithXsltContext()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <rows/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform output*")
            .WithInnerException<InvalidDataException>();
    }

    [Fact]
    public void LoadTransformed_RejectsStylesheetEmittedDtdOutput()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" doctype-system="freex-workbook.dtd" omit-xml-declaration="yes" />
              <xsl:template match="/">
                <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
                  <ss:Worksheet ss:Name="Bad"><ss:Table /></ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform output*")
            .WithInnerException<XmlException>();
    }

    [Fact]
    public void LoadTransformed_RejectsStylesheetEmittedPublicDtdOutput()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml"
                  doctype-public="-//FreeX//DTD Workbook 1.0//EN"
                  doctype-system="freex-workbook.dtd"
                  omit-xml-declaration="yes" />
              <xsl:template match="/">
                <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
                  <ss:Worksheet ss:Name="Bad"><ss:Table /></ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform output*")
            .WithInnerException<XmlException>();
    }

}
