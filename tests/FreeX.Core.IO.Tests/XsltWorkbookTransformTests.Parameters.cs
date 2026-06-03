using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed partial class XsltWorkbookTransformTests
{
    [Fact]
    public void TransformToSpreadsheetXml_Parameters_AreAvailableToStylesheet()
    {
        using var source = StreamFromString("<rows />");
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
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(
            source,
            stylesheet,
            new Dictionary<string, string?>
            {
                ["sheetName"] = "Parameters",
                ["label"] = "Generated from parameter"
            });

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .Contain("ss:Name=\"Parameters\"")
            .And.Contain("Generated from parameter");
    }

    [Fact]
    public void TransformToSpreadsheetXml_DefaultParameters_GenerateSpreadsheetMl()
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

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(source, stylesheet);

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should()
            .Contain("ss:Name=\"Default Parameters\"")
            .And.Contain("<ss:Data ss:Type=\"String\">Default label</ss:Data>")
            .And.Contain("<ss:Data ss:Type=\"Number\">18.75</ss:Data>");
    }

    [Fact]
    public void TransformToSpreadsheetXml_NamespacedParameters_AreAvailableToStylesheet()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:cfg="urn:freex:xslt:test"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:param name="cfg:sheetName" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="{$cfg:sheetName}">
                    <ss:Table />
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(
            source,
            stylesheet,
            new Dictionary<string, string?> { ["{urn:freex:xslt:test}sheetName"] = "Namespaced" });

        using var reader = new StreamReader(transformed, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        reader.ReadToEnd().Should().Contain("ss:Name=\"Namespaced\"");
    }

    [Fact]
    public void TransformToSpreadsheetXml_EmptyParameterName_ThrowsArgumentException()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(
            source,
            stylesheet,
            new Dictionary<string, string?> { [" "] = "ignored" });

        act.Should().Throw<ArgumentException>()
            .Where(exception => exception.ParamName == "parameters");
    }

    [Fact]
    public void TransformToSpreadsheetXml_InvalidParameterName_ThrowsArgumentException()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(
            source,
            stylesheet,
            new Dictionary<string, string?> { ["bad:name"] = "ignored" });

        act.Should().Throw<ArgumentException>()
            .Where(exception => exception.ParamName == "parameters")
            .WithInnerException<XmlException>();
    }

    [Fact]
    public void TransformToSpreadsheetXml_InvalidNamespacedParameterName_ThrowsArgumentException()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = IdentityStylesheet();

        var act = () => XsltWorkbookTransform.TransformToSpreadsheetXml(
            source,
            stylesheet,
            new Dictionary<string, string?> { ["{urn:freex:xslt:test}"] = "ignored" });

        act.Should().Throw<ArgumentException>()
            .Where(exception => exception.ParamName == "parameters");
    }

}
