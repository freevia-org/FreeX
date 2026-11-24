using Free.Shared.IO;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// Behavior and dedup guards for format-IO helpers that FreeX, FreeW and FreeP used to each carry a
/// private copy of. The shared implementations own the mechanical scan/serialize step; every app keeps
/// its own interpretation of the result.
/// </summary>
public sealed class FormatIoDedupGuardTests
{
    private static string ReadRepoFile(string relativePath)
    {
        var root = TestWorkspaceFileLocator.FindContainingDirectory("FreeX.slnx");
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    [Theory]
    [InlineData("font-weight:bold; color:#ff0000", "font-weight=bold|color=#ff0000")]
    [InlineData("FONT-STYLE : Italic ;;  ; text-align:center;", "font-style=Italic|text-align=center")]
    [InlineData("font-family:'Times New Roman';font-size:12pt", "font-family='Times New Roman'|font-size=12pt")]
    [InlineData("no-colon; :novalue; empty:; empty2:   ; ok:1", "ok=1")]
    [InlineData("color:red;color:blue", "color=red|color=blue")]
    [InlineData("background:url(a:b)", "background=url(a:b)")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void CssInlineDeclarationsSplitsLikeTheTwoAppCopiesDid(string? css, string expected)
    {
        var actual = string.Join("|", CssInlineDeclarations.Enumerate(css).Select(d => d.Property + "=" + d.Value));
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("freew/FreeW.Core.IO/DocxWriter.cs")]
    [InlineData("freep/FreeP.Core.IO/PptxChartWriter.cs")]
    public void ChartWorkbookScaffoldingLivesOnlyInTheSharedPackage(string relativePath)
    {
        var text = ReadRepoFile(relativePath);
        text.Should().Contain("EmbeddedChartWorkbookPackage.Build",
            "the embedded chart workbook is assembled by Free.Shared.Opc.EmbeddedChartWorkbookPackage");
        text.Should().NotContain("spreadsheetml.sheet.main+xml",
            "a private [Content_Types]/workbook scaffold re-creates the duplicate the shared package replaced");
        text.Should().NotContain("xl/_rels/workbook.xml.rels");
    }

    [Theory]
    [InlineData("src/FreeX.Core.IO/XlsxChartXmlWriter.Series.cs")]
    [InlineData("freew/FreeW.Core.IO/DocxWriter.cs")]
    [InlineData("freep/FreeP.Core.IO/PptxChartWriter.cs")]
    public void ChartCacheElementsAreBuiltByTheSharedHelperOnly(string relativePath)
    {
        var text = ReadRepoFile(relativePath);
        text.Should().Contain("DrawingMlChartCache.Create");
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(?:C|chartNs)\s*\+\s*""ptCount""").Should().BeFalse(
            "a private numCache/strCache point loop re-creates the three copies the shared helper replaced");
        text.Should().NotContain("\"G15\"",
            "chart cache numbers are Free.Shared.Drawing.DrawingMlChartCache.FormatNumber (round-trippable)");
    }

    [Theory]
    [InlineData("src/FreeX.Core.IO/OdsFileAdapter.cs")]
    [InlineData("freew/FreeW.Core.IO/OdtFileAdapter.cs")]
    public void OdfNamespaceUrisAreDeclaredOnlyInTheSharedClass(string relativePath)
    {
        var text = ReadRepoFile(relativePath);
        text.Should().Contain("OdfNamespaces.");
        text.Should().NotContain("urn:oasis:names:tc:opendocument:xmlns:",
            "ODF namespace URIs live in Free.Shared.IO.OdfNamespaces");
        text.Should().NotContain("www.w3.org/1999/xlink");
    }

    [Fact]
    public void OdfNamespaceUrisMatchTheOpenDocumentSpecification()
    {
        OdfNamespaces.Office.NamespaceName.Should().Be("urn:oasis:names:tc:opendocument:xmlns:office:1.0");
        OdfNamespaces.Text.NamespaceName.Should().Be("urn:oasis:names:tc:opendocument:xmlns:text:1.0");
        OdfNamespaces.Style.NamespaceName.Should().Be("urn:oasis:names:tc:opendocument:xmlns:style:1.0");
        OdfNamespaces.Fo.NamespaceName.Should().Be("urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0");
        OdfNamespaces.Table.NamespaceName.Should().Be("urn:oasis:names:tc:opendocument:xmlns:table:1.0");
        OdfNamespaces.Draw.NamespaceName.Should().Be("urn:oasis:names:tc:opendocument:xmlns:drawing:1.0");
        OdfNamespaces.Svg.NamespaceName.Should().Be("urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0");
        OdfNamespaces.Meta.NamespaceName.Should().Be("urn:oasis:names:tc:opendocument:xmlns:meta:1.0");
        OdfNamespaces.Number.NamespaceName.Should().Be("urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0");
        OdfNamespaces.OpenFormula.NamespaceName.Should().Be("urn:oasis:names:tc:opendocument:xmlns:of:1.2");
        OdfNamespaces.Xlink.NamespaceName.Should().Be("http://www.w3.org/1999/xlink");
        OdfNamespaces.DublinCore.NamespaceName.Should().Be("http://purl.org/dc/elements/1.1/");
    }

    [Theory]
    [InlineData("src/FreeX.Core.IO/XlsxWorkbookThemeWriter.cs")]
    [InlineData("freew/FreeW.Core.IO/DocxWriter.cs")]
    [InlineData("freep/FreeP.Core.IO/PptxPackageWriter.cs")]
    public void ThemePartScaffoldLivesOnlyInTheSharedBuilder(string relativePath)
    {
        var text = ReadRepoFile(relativePath);
        text.Should().Contain("DrawingMlThemeXml.CreateThemeDocument");
        System.Text.RegularExpressions.Regex.IsMatch(text, @"new\s+XElement\(\s*\w+\s*\+\s*""(?:themeElements|fillStyleLst|clrScheme|fontScheme)""")
            .Should().BeFalse("the a:theme scaffold is Free.Shared.Drawing.DrawingMlThemeXml.CreateThemeDocument");
    }

    [Theory]
    [InlineData("src/FreeX.Core.IO/HtmlCssParser.cs")]
    [InlineData("freew/FreeW.Core.IO/HtmlCssFormatting.cs")]
    public void HtmlInlineStyleScanningLivesOnlyInTheSharedHelper(string relativePath)
    {
        var text = ReadRepoFile(relativePath);
        text.Should().Contain("CssInlineDeclarations.Enumerate",
            "the declaration scan is Free.Shared.IO.CssInlineDeclarations");
        text.Should().NotContain("Split(';'",
            "a private ';' declaration splitter re-creates the duplicate the shared helper replaced");
    }
}
