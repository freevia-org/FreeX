using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// Run theme-font tokens stay tokens in the model and resolve only when formatting is resolved for a
/// view, so changing the document theme re-fonts the run (as in Word).
/// </summary>
public sealed class RunThemeFontTokenRenderingTests
{
    [Fact]
    public void ThemeChange_ChangesTheRenderedFontOfATokenRun()
    {
        var document = new TextDocument
        {
            Theme = DocumentTheme.Default with { HeadingFont = "Cambria", BodyFont = "Calibri" },
            ThemeScriptFonts = new ThemeScriptFonts(MajorBidiScript: "Times New Roman", MinorBidiScript: "Arial"),
        };
        var paragraph = new Paragraph();
        var latin = new Run("heading-font text", RunFormatting.Default with { ThemeFonts = new RunThemeFontTokens("majorHAnsi", "majorHAnsi") });
        var bidi = new Run("bidi text", RunFormatting.Default with { ThemeFonts = new RunThemeFontTokens("majorBidi", "majorBidi", null, "majorBidi") });
        paragraph.Runs.Add(latin);
        paragraph.Runs.Add(bidi);
        document.Blocks.Add(paragraph);

        DocumentRunFormattingResolver.Resolve(document, paragraph, latin).FontFamily.Should().Be("Cambria");
        DocumentRunFormattingResolver.Resolve(document, paragraph, bidi).FontFamily.Should().Be("Times New Roman");

        document.Theme = document.Theme with { HeadingFont = "Georgia" };

        DocumentRunFormattingResolver.Resolve(document, paragraph, latin).FontFamily.Should().Be("Georgia");
        latin.Formatting.FontFamily.Should().BeNull("resolution never writes the font back into the model");
    }

    [Fact]
    public void LiteralFont_WinsOverATokenOnTheSameRun()
    {
        var document = new TextDocument { Theme = DocumentTheme.Default with { HeadingFont = "Cambria" } };
        var paragraph = new Paragraph();
        var run = new Run("x", RunFormatting.Default with
        {
            FontFamily = "Arial",
            ThemeFonts = new RunThemeFontTokens("majorHAnsi"),
        });
        paragraph.Runs.Add(run);
        document.Blocks.Add(paragraph);

        DocumentRunFormattingResolver.Resolve(document, paragraph, run).FontFamily.Should().Be("Arial");
    }

    [Fact]
    public void SymbolRun_RendersInItsSymbolFont()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        var run = new Run("", RunFormatting.Default with { SymbolFont = "Symbol" });
        paragraph.Runs.Add(run);
        document.Blocks.Add(paragraph);

        DocumentRunFormattingResolver.Resolve(document, paragraph, run).FontFamily.Should().Be("Symbol");
    }
}
