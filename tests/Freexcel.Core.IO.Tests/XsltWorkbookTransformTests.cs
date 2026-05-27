using System.Text;
using System.Xml;
using System.Xml.Xsl;
using FluentAssertions;

namespace Freexcel.Core.IO.Tests;

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
        reader.ReadToEnd().Should().Contain("Alpha");
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
    public void TransformToSpreadsheetXml_StylesheetScript_ReportsDisabledFeatures()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:msxsl="urn:schemas-microsoft-com:xslt"
                xmlns:user="urn:freexcel-test-script">
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

    private static MemoryStream StreamFromString(string value) =>
        new(Encoding.UTF8.GetBytes(value));
}
