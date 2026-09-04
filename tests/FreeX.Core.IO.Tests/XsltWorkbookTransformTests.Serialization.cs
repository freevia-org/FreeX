using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed partial class XsltWorkbookTransformTests
{
    [Fact]
    public void TransformToSpreadsheetXml_NamespaceAlias_GeneratesSpreadsheetMlElements()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var document = LoadTransformedXml(transformed);

        document.Root!.Name.Should().Be(ss + "Workbook");
        var worksheet = document.Root.Element(ss + "Worksheet");
        worksheet.Should().NotBeNull();
        worksheet!.Attribute(ss + "Name")!.Value.Should().Be("Aliased");
        worksheet.Descendants(ss + "Data").Single().Value.Should().Be("Aliased");
    }

    [Fact]
    public void TransformToSpreadsheetXml_CurrentFunction_GeneratesJoinedLookupCells()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .Contain("<ss:Worksheet ss:Name=\"Current\">")
            .And.Contain("<ss:Data ss:Type=\"String\">100</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"String\">Contoso</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"String\">101</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"String\">Northwind</ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetHtmlOutput_PreservesHtmlSerialization()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .Contain("<br>")
            .And.Contain("<span>April</span>")
            .And.NotContain("<?xml");
    }

    [Fact]
    public void TransformToSpreadsheetXml_IdentityTransform_PreservesXmlSpaceTextWhitespace()
    {
        using var source = StreamFromString("<rows><row xml:space=\"preserve\">  Foxtrot  </row></rows>");
        using var stylesheet = IdentityStylesheet();

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should().Contain("<row xml:space=\"preserve\">  Foxtrot  </row>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetWhitespaceRules_PreserveSelectedElementWhitespace()
    {
        using var source = StreamFromString("""
            <rows>
              <row>
                <name>Alpha</name>
                <note>  keep padded note  </note>
              </row>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" omit-xml-declaration="yes" />
              <xsl:strip-space elements="*" />
              <xsl:preserve-space elements="note" />
              <xsl:template match="/rows">
                <worksheet>
                  <cell><xsl:value-of select="row/name" /></cell>
                  <note><xsl:value-of select="row/note" /></note>
                </worksheet>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .Contain("<cell>Alpha</cell>")
            .And.Contain("<note>  keep padded note  </note>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_IdentityTransform_PreservesCommentsAndProcessingInstructions()
    {
        using var source = StreamFromString("<rows><?freex keep=\"true\"?><!--keep me--><row name=\"Golf\" /></rows>");
        using var stylesheet = IdentityStylesheet();

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var xml = reader.ReadToEnd();
        xml.Should().Contain("<?freex keep=\"true\"?>");
        xml.Should().Contain("<!--keep me-->");
    }

    [Fact]
    public void TransformToSpreadsheetXml_IdentityTransform_PreservesDocumentLevelCommentsAndProcessingInstructions()
    {
        using var source = StreamFromString(
            "<?freex before=\"true\"?><!--before root--><rows><row name=\"Golf\" /></rows><?freex after=\"true\"?><!--after root-->");
        using var stylesheet = IdentityStylesheet();

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var xml = reader.ReadToEnd();
        xml.Should().Contain("<?freex before=\"true\"?>");
        xml.Should().Contain("<!--before root-->");
        xml.Should().Contain("<rows><row name=\"Golf\" /></rows>");
        xml.Should().Contain("<?freex after=\"true\"?>");
        xml.Should().Contain("<!--after root-->");
    }

    [Fact]
    public void TransformToSpreadsheetXml_IdentityTransform_PreservesNamespacedElementsAndAttributes()
    {
        using var source = StreamFromString("<fx:rows xmlns:fx=\"urn:freex:test\"><fx:row fx:name=\"Golf\" /></fx:rows>");
        using var stylesheet = IdentityStylesheet();

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var xml = reader.ReadToEnd();
        var document = XDocument.Parse(xml);
        document.Root?.Name.Should().Be(XName.Get("rows", "urn:freex:test"));
        document.Root?.Elements().Single().Name.Should().Be(XName.Get("row", "urn:freex:test"));
        document.Root?.Elements().Single().Attribute(XName.Get("name", "urn:freex:test"))!.Value.Should().Be("Golf");
        xml.Should().Contain("xmlns:fx=\"urn:freex:test\"");
    }

    [Fact]
    public void TransformToSpreadsheetXml_IdentityTransform_PreservesDefaultNamespaceElements()
    {
        using var source = StreamFromString("<rows xmlns=\"urn:freex:test\"><row name=\"Hotel\" /></rows>");
        using var stylesheet = IdentityStylesheet();

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var xml = reader.ReadToEnd();
        var document = XDocument.Parse(xml);
        document.Root?.Name.Should().Be(XName.Get("rows", "urn:freex:test"));
        document.Root?.Elements().Single().Name.Should().Be(XName.Get("row", "urn:freex:test"));
        document.Root?.Elements().Single().Attribute("name")!.Value.Should().Be("Hotel");
        xml.Should().Contain("xmlns=\"urn:freex:test\"");
    }

    [Fact]
    public void TransformToSpreadsheetXml_IdentityTransform_PreservesCDataTextValue()
    {
        using var source = StreamFromString("<rows><formula><![CDATA[A1<B1 && C1>D1]]></formula></rows>");
        using var stylesheet = IdentityStylesheet();

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var xml = reader.ReadToEnd();
        XDocument.Parse(xml).Root?.Element("formula")?.Value.Should().Be("A1<B1 && C1>D1");
        xml.Should().Contain("<formula>A1&lt;B1 &amp;&amp; C1&gt;D1</formula>");
    }

}
