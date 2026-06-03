using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxFontNameSanitizerTests
{
    // Names within Excel's 31-character limit are accepted verbatim by Excel (rendering a fallback when the
    // family is not installed), so they must round-trip unchanged — including Google's quoted/CSS names.
    [Theory]
    [InlineData("\"Century Gothic\"")]
    [InlineData("\"docs-DM Sans\"")]
    [InlineData("Calibri")]
    [InlineData("Arial, sans-serif")]
    public void SanitizeValAttribute_PreservesNamesWithinExcelLengthLimit(string fontName)
    {
        var element = new XElement("name", new XAttribute("val", fontName));

        var changed = XlsxFontNameSanitizer.SanitizeValAttribute(element);

        changed.Should().BeFalse("a font name within Excel's 31-character limit is preserved verbatim");
        element.Attribute("val")!.Value.Should().Be(fontName);
    }

    // Names that exceed Excel's limit would force a repair on open, so they are still normalized to the first
    // CSS family with surrounding quotes stripped (and truncated as a last resort).
    [Fact]
    public void SanitizeValAttribute_NormalizesNamesExceedingExcelLengthLimit()
    {
        var element = new XElement("name", new XAttribute("val", "\"Google Sans\", Roboto, sans-serif"));

        var changed = XlsxFontNameSanitizer.SanitizeValAttribute(element);

        changed.Should().BeTrue();
        element.Attribute("val")!.Value.Should().Be("Google Sans");
    }

    [Fact]
    public void SanitizeValAttribute_TruncatesOverlongSingleFamilyName()
    {
        var overlong = new string('A', 40);
        var element = new XElement("name", new XAttribute("val", overlong));

        XlsxFontNameSanitizer.SanitizeValAttribute(element);

        element.Attribute("val")!.Value.Should().Be(new string('A', 31));
    }
}
