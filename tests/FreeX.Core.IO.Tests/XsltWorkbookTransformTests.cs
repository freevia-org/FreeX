using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed partial class XsltWorkbookTransformTests
{
    [Fact]
    public void TransformToSpreadsheetXml_ValidStylesheet_ReturnsSpreadsheetMl()
    {
        using var source = StreamFromString("<rows><row name=\"Alpha\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Data">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/@name"/></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var xml = reader.ReadToEnd();
        xml.Should().Contain("Alpha");
        xml.Should().Contain("<ss:Workbook");
        xml.Should().Contain("xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"");
    }

    [Fact]
    public void TransformToSpreadsheetXml_SimplifiedStylesheetRoot_GeneratesSpreadsheetMl()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .Contain("<ss:Workbook")
            .And.Contain("ss:Name=\"Simplified\"")
            .And.Contain("<ss:Data ss:Type=\"String\">Alpha</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"Number\">42.5</ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_Success_ReturnsRewoundOutputStream()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = IdentityStylesheet();

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        transformed.Position.Should().Be(0);
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetOutputSettings_PreservesCDataSections()
    {
        using var source = StreamFromString("<rows><row note=\"A &lt; B &amp; C\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" cdata-section-elements="note" omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <worksheet>
                  <note><xsl:value-of select="row/@note" /></note>
                </worksheet>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should().Contain("<note><![CDATA[A < B & C]]></note>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetOutputSettings_PreservesNamespacedCDataSections()
    {
        using var source = StreamFromString("<rows><row note=\"A &lt; B &amp; C\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:output method="xml" cdata-section-elements="ss:Data" omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet>
                    <ss:Data><xsl:value-of select="row/@note" /></ss:Data>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should().Contain("<ss:Data><![CDATA[A < B & C]]></ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetOutputEncoding_PreservesUtf16Output()
    {
        using var source = StreamFromString("<rows><row name=\"Delta\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" encoding="utf-16" />
              <xsl:template match="/rows">
                <worksheet>
                  <cell><xsl:value-of select="row/@name" /></cell>
                </worksheet>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        var bytes = transformed.ToArray();
        bytes.Should().StartWith(Encoding.Unicode.GetPreamble());
        Encoding.Unicode.GetString(bytes).Should()
            .Contain("encoding=\"utf-16\"")
            .And.Contain("<cell>Delta</cell>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_SourceEncoding_ReadsUtf16Input()
    {
        using var source = Utf16StreamFromString("<?xml version=\"1.0\" encoding=\"utf-16\"?><rows><row name=\"Echo\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/rows">
                <worksheet>
                  <cell><xsl:value-of select="row/@name" /></cell>
                </worksheet>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should().Contain("<cell>Echo</cell>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetEncoding_ReadsUtf16Input()
    {
        using var source = StreamFromString("<rows><row name=\"Foxtrot\" /></rows>");
        using var stylesheet = Utf16StreamFromString("""
            <?xml version="1.0" encoding="utf-16"?>
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/rows">
                <worksheet>
                  <cell><xsl:value-of select="row/@name" /></cell>
                </worksheet>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should().Contain("<cell>Foxtrot</cell>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetOutputDeclaration_PreservesStandaloneFlag()
    {
        using var source = StreamFromString("<rows><row name=\"Echo\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" standalone="yes" />
              <xsl:template match="/rows">
                <worksheet>
                  <cell><xsl:value-of select="row/@name" /></cell>
                </worksheet>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .StartWith("<?xml")
            .And.Contain("standalone=\"yes\"")
            .And.Contain("<cell>Echo</cell>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetOutputDeclaration_CanBeOmitted()
    {
        using var source = StreamFromString("<rows><row name=\"Hotel\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <worksheet>
                  <cell><xsl:value-of select="row/@name" /></cell>
                </worksheet>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .StartWith("<worksheet>")
            .And.Contain("<cell>Hotel</cell>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetOutputDocType_PreservesSystemIdentifier()
    {
        using var source = StreamFromString("<rows><row name=\"Kilo\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" doctype-system="freex-workbook.dtd" omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <workbook>
                  <cell><xsl:value-of select="row/@name" /></cell>
                </workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .StartWith("""<!DOCTYPE workbook SYSTEM "freex-workbook.dtd">""")
            .And.Contain("<cell>Kilo</cell>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetOutputDocType_PreservesPublicIdentifier()
    {
        using var source = StreamFromString("<rows><row name=\"Lima\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml"
                  doctype-public="-//FreeX//DTD Workbook 1.0//EN"
                  doctype-system="freex-workbook.dtd"
                  omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <workbook>
                  <cell><xsl:value-of select="row/@name" /></cell>
                </workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .StartWith("""<!DOCTYPE workbook PUBLIC "-//FreeX//DTD Workbook 1.0//EN" "freex-workbook.dtd">""")
            .And.Contain("<cell>Lima</cell>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetOutputIndent_PreservesIndentedXml()
    {
        using var source = StreamFromString("<rows><row name=\"Mike\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" indent="yes" omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <workbook>
                  <row><cell><xsl:value-of select="row/@name" /></cell></row>
                </workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .Contain("<workbook>\r\n")
            .And.Contain("  <row>\r\n")
            .And.Contain("    <cell>Mike</cell>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetOutputIndentNo_PreservesCompactXml()
    {
        using var source = StreamFromString("<rows><row name=\"Nina\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" indent="no" omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <workbook>
                  <row><cell><xsl:value-of select="row/@name" /></cell></row>
                </workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should().Be("<workbook><row><cell>Nina</cell></row></workbook>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetTextOutput_PreservesRawText()
    {
        using var source = StreamFromString("<rows><row name=\"India &amp; Juliet\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="text" />
              <xsl:template match="/rows">
                <xsl:value-of select="row/@name" />
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should().Be("India & Juliet");
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetTextOutputEncoding_PreservesUtf16Output()
    {
        using var source = StreamFromString("<rows><row name=\"Juliet\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="text" encoding="utf-16" />
              <xsl:template match="/rows">
                <xsl:value-of select="row/@name" />
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        var bytes = transformed.ToArray();
        bytes.Should().StartWith(Encoding.Unicode.GetPreamble());
        Encoding.Unicode.GetString(bytes).Should().Contain("Juliet");
    }

    [Fact]
    public void TransformToSpreadsheetXml_DisableOutputEscaping_PreservesGeneratedMarkup()
    {
        using var source = StreamFromString("""
            <rows>
              <fragment>&lt;ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"&gt;&lt;ss:Worksheet ss:Name="Generated"&gt;&lt;ss:Table /&gt;&lt;/ss:Worksheet&gt;&lt;/ss:Workbook&gt;</fragment>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <xsl:value-of select="fragment" disable-output-escaping="yes" />
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .StartWith("<ss:Workbook")
            .And.Contain("<ss:Worksheet ss:Name=\"Generated\">")
            .And.NotContain("&lt;ss:Workbook");
    }

}
