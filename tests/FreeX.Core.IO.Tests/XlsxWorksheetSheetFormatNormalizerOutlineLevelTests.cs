using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Covers the outline-level clamping in
/// <see cref="XlsxWorksheetSheetFormatNormalizer.NormalizeElement"/>: SpreadsheetML (and Excel,
/// ClosedXML, and FreeX's own <c>OutlineGroupingService</c>) allow outline levels up to 8, so
/// <c>outlineLevelRow</c>/<c>outlineLevelCol</c> values of 8 must be preserved, while values above
/// the legitimate maximum (9) are still stripped.
/// </summary>
public sealed class XlsxWorksheetSheetFormatNormalizerOutlineLevelTests
{
    private static XElement SheetFormatPr(params (string Name, string Value)[] attributes)
    {
        var element = new XElement("sheetFormatPr");
        foreach (var (name, value) in attributes)
            element.SetAttributeValue(name, value);
        return element;
    }

    [Fact]
    public void NormalizeElement_KeepsOutlineLevelEightRow()
    {
        var sheetFormat = SheetFormatPr(("outlineLevelRow", "8"));

        XlsxWorksheetSheetFormatNormalizer.NormalizeElement(sheetFormat);

        sheetFormat.Attribute("outlineLevelRow")!.Value.Should().Be("8");
    }

    [Fact]
    public void NormalizeElement_KeepsOutlineLevelEightCol()
    {
        var sheetFormat = SheetFormatPr(("outlineLevelCol", "8"));

        XlsxWorksheetSheetFormatNormalizer.NormalizeElement(sheetFormat);

        sheetFormat.Attribute("outlineLevelCol")!.Value.Should().Be("8");
    }

    [Fact]
    public void NormalizeElement_StripsOutlineLevelNine()
    {
        var sheetFormat = SheetFormatPr(("outlineLevelRow", "9"), ("outlineLevelCol", "9"));

        var changed = XlsxWorksheetSheetFormatNormalizer.NormalizeElement(sheetFormat);

        changed.Should().BeTrue();
        sheetFormat.Attribute("outlineLevelRow").Should().BeNull();
        sheetFormat.Attribute("outlineLevelCol").Should().BeNull();
    }
}
