namespace FreeW.Core.Model.Tests;

public class DocumentEffectSetTests
{
    [Fact]
    public void Catalog_ContainsWordStyleEffectSets_InOrder()
    {
        DocumentEffectSet.Catalog.Select(s => s.Name)
            .Should().Equal("Office", "Subtle", "Moderate", "Intense");
        DocumentEffectSet.Default.Name.Should().Be("Office");
    }

    [Fact]
    public void FindByName_IsCaseInsensitive_AndReturnsNullForUnknown()
    {
        DocumentEffectSet.FindByName("moderate").Should().BeSameAs(DocumentEffectSet.Catalog[2]);
        DocumentEffectSet.FindByName("Nope").Should().BeNull();
    }

    [Fact]
    public void Apply_RewritesOnlyTheThemeEffectSet()
    {
        var doc = TextDocument.CreateEmpty();
        DocumentTheme.Apply(doc, DocumentTheme.FindByName("Berlin")!);
        var before = doc.Theme;

        DocumentEffectSet.Apply(doc, DocumentEffectSet.FindByName("Intense")!);

        doc.Theme.Name.Should().Be(before.Name);
        doc.Theme.HeadingFont.Should().Be(before.HeadingFont);
        doc.Theme.BodyFont.Should().Be(before.BodyFont);
        doc.Theme.PrimaryColorHex.Should().Be(before.PrimaryColorHex);
        doc.Theme.EffectSetName.Should().Be("Intense");
    }

    [Fact]
    public void FromTheme_FallsBackToOfficeForUnknownThemeValue()
    {
        var custom = DocumentTheme.Default with { EffectSetName = "Foreign" };

        DocumentEffectSet.FromTheme(custom).Should().BeSameAs(DocumentEffectSet.Default);
    }
}
