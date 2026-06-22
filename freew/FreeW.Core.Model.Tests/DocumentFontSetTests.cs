namespace FreeW.Core.Model.Tests;

public class DocumentFontSetTests
{
    [Fact]
    public void Catalog_ContainsTheExpectedBuiltInFontSets_InOrder()
    {
        DocumentFontSet.Catalog.Select(s => s.Name)
            .Should().Equal("Office", "Cambria", "Georgia", "Trebuchet");
        DocumentFontSet.Default.Name.Should().Be("Office");
    }

    [Fact]
    public void FindByName_IsCaseInsensitive_AndReturnsNullForUnknown()
    {
        DocumentFontSet.FindByName("georgia").Should().BeSameAs(DocumentFontSet.Catalog[2]);
        DocumentFontSet.FindByName("Nope").Should().BeNull();
    }

    [Fact]
    public void Apply_RewritesOnlyFontPair_AndPreservesColorsAndSizes()
    {
        var doc = TextDocument.CreateEmpty();
        DocumentTheme.ApplyColors(doc, DocumentTheme.FindByName("Berlin")!);
        var beforeTitleSize = doc.Styles["Title"].Run.FontSizePt;
        var beforeTitleColor = doc.Styles["Title"].Run.ColorHex;
        var beforeHeadingColor = doc.Styles["Heading1"].Run.ColorHex;

        DocumentFontSet.Apply(doc, DocumentFontSet.FindByName("Georgia")!);

        doc.Theme.Name.Should().Be("Office");
        doc.Theme.HeadingFont.Should().Be("Georgia");
        doc.Theme.BodyFont.Should().Be("Georgia");
        doc.Theme.PrimaryColorHex.Should().Be("#C00000");
        doc.DefaultRun.FontFamily.Should().Be("Georgia");
        doc.Styles["Normal"].Run.FontFamily.Should().Be("Georgia");
        doc.Styles["Title"].Run.FontFamily.Should().Be("Georgia");
        doc.Styles["Heading1"].Run.FontFamily.Should().Be("Georgia");
        doc.Styles["Title"].Run.ColorHex.Should().Be(beforeTitleColor);
        doc.Styles["Heading1"].Run.ColorHex.Should().Be(beforeHeadingColor);
        doc.Styles["Title"].Run.FontSizePt.Should().Be(beforeTitleSize);
    }

    [Fact]
    public void Apply_PreservesCustomStyles()
    {
        var doc = TextDocument.CreateEmpty();
        var custom = StyleManager.CreateStyle(
            doc,
            "Callout",
            "Normal",
            new RunFormatting { FontFamily = "Arial", Bold = true },
            ParagraphFormatting.Default);

        DocumentFontSet.Apply(doc, DocumentFontSet.FindByName("Trebuchet")!);

        doc.Styles["Callout"].Should().BeSameAs(custom);
        doc.Styles["Callout"].Run.FontFamily.Should().Be("Arial");
    }
}
