using System.Xml.Linq;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class SymbolMarkerFontPlannerTests
{
    [Fact]
    public void OrdinaryMarker_KeepsTheParagraphFont()
    {
        SymbolMarkerFontPlanner.ResolveMarkerFont(new TextDocument(), "1.").Should().BeNull();
        SymbolMarkerFontPlanner.ResolveMarkerFont(new TextDocument(), "•").Should().BeNull();
    }

    [Fact]
    public void PrivateUseMarker_TakesTheFontOfTheNumberingLevelThatDefinesIt()
    {
        var document = new TextDocument();
        document.Preserved.OriginalNumbering = XElement.Parse(
            """
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="0">
                <w:lvl w:ilvl="0"><w:numFmt w:val="bullet"/><w:lvlText w:val="&#xF0B7;"/>
                  <w:rPr><w:rFonts w:ascii="Symbol" w:hAnsi="Symbol"/></w:rPr></w:lvl>
                <w:lvl w:ilvl="2"><w:numFmt w:val="bullet"/><w:lvlText w:val="&#xF0A7;"/>
                  <w:rPr><w:rFonts w:ascii="Webdings" w:hAnsi="Webdings"/></w:rPr></w:lvl>
              </w:abstractNum>
            </w:numbering>
            """);

        SymbolMarkerFontPlanner.ResolveMarkerFont(document, "").Should().Be("Symbol");
        SymbolMarkerFontPlanner.ResolveMarkerFont(document, "").Should().Be("Webdings");
    }

    [Theory]
    [InlineData("", "Symbol")]
    [InlineData("", "Wingdings")]
    [InlineData("", "Wingdings")]
    public void PrivateUseMarker_WithoutDefinitions_UsesWordsDefaultBulletFonts(string marker, string expected) =>
        SymbolMarkerFontPlanner.ResolveMarkerFont(new TextDocument(), marker).Should().Be(expected);

    [Fact]
    public void PrivateUseMarker_TakesTheSizeOfItsNumberingLevel()
    {
        var document = new TextDocument();
        document.Preserved.OriginalNumbering = XElement.Parse(
            """
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="0">
                <w:lvl w:ilvl="0"><w:numFmt w:val="bullet"/><w:lvlText w:val="&#xF0B7;"/>
                  <w:rPr><w:rFonts w:ascii="Symbol" w:hAnsi="Symbol"/><w:sz w:val="20"/></w:rPr></w:lvl>
                <w:lvl w:ilvl="1"><w:numFmt w:val="bullet"/><w:lvlText w:val="&#xF0A7;"/>
                  <w:rPr><w:rFonts w:ascii="Wingdings" w:hAnsi="Wingdings"/></w:rPr></w:lvl>
              </w:abstractNum>
            </w:numbering>
            """);

        SymbolMarkerFontPlanner.ResolveMarkerFontSizePt(document, "").Should().Be(10);
        SymbolMarkerFontPlanner.ResolveMarkerFontSizePt(document, "").Should().BeNull("the level names no size");
        SymbolMarkerFontPlanner.ResolveMarkerFontSizePt(document, "1.").Should().BeNull("only symbol markers are resized");
    }
}
