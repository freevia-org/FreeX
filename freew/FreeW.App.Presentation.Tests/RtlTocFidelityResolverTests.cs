using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class RtlTocFidelityResolverTests
{
    [Fact]
    public void Resolve_IgnoresTheHyperlinkCharacterStyleInsideATocParagraph()
    {
        var document = TextDocument.CreateEmpty();
        document.Styles["Hyperlink"] = new DocumentStyle
        {
            Id = "Hyperlink",
            Name = "Hyperlink",
            Run = new RunFormatting { ColorHex = "#0000FF", Underline = true },
        };
        var toc = new Paragraph { StyleId = "TOC1" };
        var body = new Paragraph();

        var inToc = DocumentRunFormattingResolver.Resolve(document, toc, RunFormatting.Default, "Hyperlink");
        var inBody = DocumentRunFormattingResolver.Resolve(document, body, RunFormatting.Default, "Hyperlink");

        inToc.ColorHex.Should().BeNull("Word draws table-of-contents entries in the TOC style, not as blue links");
        inToc.Underline.Should().BeFalse();
        inBody.ColorHex.Should().Be("#0000FF");
        inBody.Underline.Should().BeTrue();
    }

    [Fact]
    public void ProjectForScript_BoldsComplexScriptTextFromBCs()
    {
        var resolved = new RunFormatting { ComplexScriptBold = true, Rtl = true, FontFamily = "Arial" };

        var projected = DocumentRunFormattingResolver.ProjectForScript(resolved, "مقدمه");

        projected.Bold.Should().BeTrue();
    }

    [Fact]
    public void ProjectForScript_LeavesLatinTextPlainWhenOnlyBCsIsSet()
    {
        var resolved = new RunFormatting { ComplexScriptBold = true, FontFamily = "Arial" };

        DocumentRunFormattingResolver.ProjectForScript(resolved, "Latin").Bold.Should().BeFalse();
    }

    [Fact]
    public void Resolve_CascadesComplexScriptBoldFromTheParagraphStyle()
    {
        var document = TextDocument.CreateEmpty();
        document.Styles["TOC1"] = new DocumentStyle
        {
            Id = "TOC1",
            Name = "toc 1",
            Run = new RunFormatting { ComplexScriptBold = true },
        };

        var resolved = DocumentRunFormattingResolver.Resolve(
            document, new Paragraph { StyleId = "TOC1" }, new RunFormatting { Rtl = true });

        resolved.ComplexScriptBold.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, "أ")]
    [InlineData(2, "ب")]
    [InlineData(3, "ج")]
    [InlineData(5, "هـ")]
    [InlineData(28, "غ")]
    [InlineData(29, "أأ")]
    public void FormatPageNumber_ArabicAbjad(int value, string expected)
    {
        PageNumberFormatDialogPlanner.FormatPageNumber(value, PageNumberFormat.ArabicAbjad).Should().Be(expected);
    }

    [Theory]
    [InlineData(3, "ت")]
    [InlineData(4, "ث")]
    public void FormatPageNumber_ArabicAlpha(int value, string expected)
    {
        PageNumberFormatDialogPlanner.FormatPageNumber(value, PageNumberFormat.ArabicAlpha).Should().Be(expected);
    }
}
