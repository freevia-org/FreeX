using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R86-io-styles-dedup-index-5-2: <see cref="XlsxDifferentialStyleReader"/>
/// read a dxf's <c>&lt;b&gt;</c>/<c>&lt;i&gt;</c>/<c>&lt;strike&gt;</c> font toggles purely by
/// element presence, ignoring the <c>val</c> attribute. Per ECMA-376 CT_BooleanProperty semantics,
/// an element with no <c>val</c> defaults to <c>true</c>, but an explicit <c>val="0"</c>/<c>"false"</c>
/// means the toggle is OFF -- so a conditional-format rule whose dxf explicitly turns bold off
/// (<c>&lt;b val="0"/&gt;</c>) was read as bold ON, the opposite polarity. The sibling reader
/// <c>XlsxStructuredTableStyleMetadataReader.ReadDifferentialStyleDiff</c> already gets this right
/// for the same dxf font shape via <c>ReadBoolAttribute(defaultValue: true)</c>.
/// </summary>
public sealed class R86_DxfFontToggleValZeroTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Theory]
    [InlineData("b")]
    [InlineData("i")]
    [InlineData("strike")]
    public void DifferentialStyleReader_ExplicitValZero_ReadsToggleAsOff(string elementName)
    {
        var dxf = XElement.Parse(
            $"""<dxf xmlns="{WorkbookNs}"><font><{elementName} val="0"/></font></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs);

        switch (elementName)
        {
            case "b":
                style.Bold.Should().BeFalse("an explicit <b val=\"0\"/> means the dxf turns bold OFF");
                break;
            case "i":
                style.Italic.Should().BeFalse("an explicit <i val=\"0\"/> means the dxf turns italic OFF");
                break;
            case "strike":
                style.Strikethrough.Should().BeFalse("an explicit <strike val=\"0\"/> means the dxf turns strikethrough OFF");
                break;
        }
    }

    [Theory]
    [InlineData("b")]
    [InlineData("i")]
    [InlineData("strike")]
    public void DifferentialStyleReader_SiblingRegression_BareElement_StillReadsToggleAsOn(string elementName)
    {
        // No-regression: a bare element with no val attribute at all must still default to "on",
        // exactly as it did before this fix (and matching CT_BooleanProperty's documented default).
        var dxf = XElement.Parse($"""<dxf xmlns="{WorkbookNs}"><font><{elementName}/></font></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs);

        switch (elementName)
        {
            case "b":
                style.Bold.Should().BeTrue("a bare <b/> element (no val) must still default to bold ON");
                break;
            case "i":
                style.Italic.Should().BeTrue("a bare <i/> element (no val) must still default to italic ON");
                break;
            case "strike":
                style.Strikethrough.Should().BeTrue("a bare <strike/> element (no val) must still default to strikethrough ON");
                break;
        }
    }

    [Fact]
    public void DifferentialStyleReader_ExplicitValOne_ReadsToggleAsOn_NoRegression()
    {
        var dxf = XElement.Parse(
            $"""<dxf xmlns="{WorkbookNs}"><font><b val="1"/><i val="true"/></font></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs);

        style.Bold.Should().BeTrue("an explicit <b val=\"1\"/> means the dxf turns bold ON");
        style.Italic.Should().BeTrue("an explicit <i val=\"true\"/> means the dxf turns italic ON");
    }

    [Fact]
    public void DifferentialStyleReader_NoFontToggleElements_SiblingRegression_AllFlagsStayFalse()
    {
        var dxf = XElement.Parse($"""<dxf xmlns="{WorkbookNs}"><font><sz val="12"/></font></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs);

        style.Bold.Should().BeFalse();
        style.Italic.Should().BeFalse();
        style.Strikethrough.Should().BeFalse();
    }
}
