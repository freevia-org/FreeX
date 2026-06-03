using System.Xml;
using System.Xml.Xsl;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class SpreadsheetXmlFileAdapterTests
{
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
