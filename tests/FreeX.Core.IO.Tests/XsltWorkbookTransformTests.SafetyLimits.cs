using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed partial class XsltWorkbookTransformTests
{
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
    public void TransformToSpreadsheetXml_SourceInputLimitFailure_LeavesInputStreamsOpen()
    {
        using var source = StreamFromString($"<rows><row name=\"{new string('A', 600)}\" /></rows>");
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(
            source,
            stylesheet,
            XsltWorkbookTransform.DefaultMaxOutputBytes,
            maxInputCharacters: 512);

        act.Should().Throw<InvalidDataException>();
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
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
    public void TransformToSpreadsheetXml_StylesheetInputLimitFailure_LeavesInputStreamsOpen()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(
            source,
            stylesheet,
            XsltWorkbookTransform.DefaultMaxOutputBytes,
            maxInputCharacters: 8);

        act.Should().Throw<InvalidDataException>();
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
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

}
