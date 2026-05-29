using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XsltWorkbookTransformTests
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
        document.Root?.Elements().Single().Attribute(XName.Get("name", "urn:freex:test"))?.Value.Should().Be("Golf");
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
        document.Root?.Elements().Single().Attribute("name")?.Value.Should().Be("Hotel");
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

    [Fact]
    public void TransformToSpreadsheetXml_OutputAboveLimit_ReportsSafetyDiagnostic()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <output>abcdefghijklmnopqrstuvwxyz</output>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet, maxOutputBytes: 16);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*output exceeded*16 byte safety limit*")
            .WithInnerException<IOException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_OutputLimitFailure_LeavesInputStreamsOpen()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <output>abcdefghijklmnopqrstuvwxyz</output>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet, maxOutputBytes: 16);

        act.Should().Throw<InvalidDataException>();
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void TransformToSpreadsheetXml_OutputAtLimit_Succeeds()
    {
        const string stylesheetXml = """
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <output>abcdefghijklmnopqrstuvwxyz</output>
              </xsl:template>
            </xsl:stylesheet>
            """;
        using var expected = XsltWorkbookTransform.TransformToSpreadsheetXml(
            StreamFromString("<rows />"),
            StreamFromString(stylesheetXml));
        var exactOutputLength = expected.Length;

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(
            StreamFromString("<rows />"),
            StreamFromString(stylesheetXml),
            exactOutputLength);

        transformed.ToArray().Should().BeEquivalentTo(expected.ToArray(), options => options.WithStrictOrdering());
    }

    [Fact]
    public void TransformToSpreadsheetXml_OutputOneByteOverLimit_ReportsSafetyDiagnostic()
    {
        const string stylesheetXml = """
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <output>abcdefghijklmnopqrstuvwxyz</output>
              </xsl:template>
            </xsl:stylesheet>
            """;
        using var expected = XsltWorkbookTransform.TransformToSpreadsheetXml(
            StreamFromString("<rows />"),
            StreamFromString(stylesheetXml));
        var tooSmallLimit = expected.Length - 1;

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(
            StreamFromString("<rows />"),
            StreamFromString(stylesheetXml),
            tooSmallLimit);

        act.Should().Throw<InvalidDataException>()
            .WithMessage($"*output exceeded*{tooSmallLimit} byte safety limit*")
            .WithInnerException<IOException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_SourceAboveInputLimit_ReportsSourceDiagnostic()
    {
        using var source = StreamFromString($"<rows><row name=\"{new string('A', 600)}\" /></rows>");
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(
            source,
            stylesheet,
            XsltWorkbookTransform.DefaultMaxOutputBytes,
            maxInputCharacters: 512);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*source XML*")
            .WithInnerException<XmlException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetAboveInputLimit_ReportsStylesheetDiagnostic()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(
            source,
            stylesheet,
            XsltWorkbookTransform.DefaultMaxOutputBytes,
            maxInputCharacters: 8);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XmlException>();
        source.Position.Should().Be(0);
    }

    [Fact]
    public void TransformToSpreadsheetXml_InvalidInputLimit_ThrowsArgumentOutOfRangeException()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(
            source,
            stylesheet,
            XsltWorkbookTransform.DefaultMaxOutputBytes,
            maxInputCharacters: 0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Where(exception => exception.ParamName == "maxInputCharacters");
    }

    [Fact]
    public void TransformToSpreadsheetXml_InvalidOutputLimit_ThrowsArgumentOutOfRangeException()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet, maxOutputBytes: 0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Where(exception => exception.ParamName == "maxOutputBytes");
    }

    [Fact]
    public void TransformToSpreadsheetXml_MalformedStylesheet_ReportsStylesheetDiagnostic()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("<xsl:stylesheet");

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_EmptyStylesheet_ReportsStylesheetDiagnostic()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString(string.Empty);

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_InvalidStylesheetExpression_ReportsStylesheetDiagnostic()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:value-of select="count("/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetDtd_ReportsStylesheetXmlDiagnostic()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <!DOCTYPE xsl:stylesheet [ <!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini"> ]>
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:value-of select="'blocked'"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_NullSource_ThrowsArgumentNullException()
    {
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(null!, stylesheet);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "sourceXml");
    }

    [Fact]
    public void TransformToSpreadsheetXml_NullStylesheet_ThrowsArgumentNullException()
    {
        using var source = StreamFromString("<rows />");

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "stylesheet");
    }

    [Fact]
    public void TransformToSpreadsheetXml_Success_LeavesInputStreamsOpen()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = IdentityStylesheet();

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        transformed.Length.Should().BeGreaterThan(0);
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void TransformToSpreadsheetXml_UsesCurrentInputStreamPositions()
    {
        using var source = PositionedStreamFromString("ignored", "<rows><row name=\"Bravo\" /></rows>");
        using var stylesheet = PositionedStreamFromString("ignored", """
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
        reader.ReadToEnd().Should().Contain("Bravo");
    }

    [Fact]
    public void TransformToSpreadsheetXml_AcceptsNonSeekableInputStreams()
    {
        using var source = NonSeekableStreamFromString("<rows><row name=\"Charlie\" /></rows>");
        using var stylesheet = NonSeekableStreamFromString("""
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
        reader.ReadToEnd().Should().Contain("Charlie");
    }

    [Fact]
    public void TransformToSpreadsheetXml_Failure_LeavesInputStreamsOpen()
    {
        using var source = StreamFromString("<rows>");
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>();
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetFailure_LeavesInputStreamsOpen()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("<xsl:stylesheet");

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>();
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetFailure_DoesNotReadSourceStream()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("<xsl:stylesheet");

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>();
        source.Position.Should().Be(0);
    }

    [Fact]
    public void TransformToSpreadsheetXml_SourceDtd_ReportsSourceDiagnostic()
    {
        using var source = StreamFromString("""
            <!DOCTYPE rows [ <!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini"> ]>
            <rows>&xxe;</rows>
            """);
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*source XML*")
            .WithInnerException<XmlException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_MalformedSource_ReportsSourceDiagnostic()
    {
        using var source = StreamFromString("<rows>");
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*source XML*")
            .WithInnerException<XmlException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_EmptySource_ReportsSourceDiagnostic()
    {
        using var source = StreamFromString(string.Empty);
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*source XML*")
            .WithInnerException<XmlException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_DocumentFunction_ReportsDisabledExternalAccess()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:value-of select="document('file:///C:/Windows/win.ini')"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*External document access*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_RemoteDocumentFunction_ReportsDisabledExternalAccess()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:value-of select="document('https://example.invalid/freex.xml')"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*External document access*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_TerminatingMessage_ReportsTransformDiagnostic()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:message terminate="yes">stop</xsl:message>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform failed*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_TransformFailure_LeavesInputStreamsOpen()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = TerminatingMessageStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>();
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetInclude_ReportsDisabledExternalAccess()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:include href="file:///C:/Windows/win.ini"/>
              <xsl:template match="/">
                <xsl:value-of select="'blocked'"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_RemoteStylesheetInclude_ReportsDisabledExternalAccess()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:include href="https://example.invalid/freex.xsl"/>
              <xsl:template match="/">
                <xsl:value-of select="'blocked'"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetImport_ReportsDisabledExternalAccess()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:import href="file:///C:/Windows/win.ini"/>
              <xsl:template match="/">
                <xsl:value-of select="'blocked'"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_RemoteStylesheetImport_ReportsDisabledExternalAccess()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:import href="https://example.invalid/freex.xsl"/>
              <xsl:template match="/">
                <xsl:value-of select="'blocked'"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_StylesheetScript_ReportsDisabledFeatures()
    {
        using var source = StreamFromString("<rows />");
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

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*External document access and script are disabled*")
            .WithInnerException<XsltException>();
    }

    private static MemoryStream IdentityStylesheet() =>
        StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:copy-of select="."/>
              </xsl:template>
            </xsl:stylesheet>
            """);

    private static MemoryStream TerminatingMessageStylesheet() =>
        StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:message terminate="yes">stop</xsl:message>
              </xsl:template>
            </xsl:stylesheet>
            """);

    private static MemoryStream StreamFromString(string value) =>
        new(Encoding.UTF8.GetBytes(value));

    private static MemoryStream Utf16StreamFromString(string value) =>
        new(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(value)).ToArray());

    private static Stream NonSeekableStreamFromString(string value) =>
        new NonSeekableReadStream(StreamFromString(value));

    private static MemoryStream PositionedStreamFromString(string prefix, string value)
    {
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var stream = new MemoryStream(prefixBytes.Concat(valueBytes).ToArray());
        stream.Position = prefixBytes.Length;
        return stream;
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
