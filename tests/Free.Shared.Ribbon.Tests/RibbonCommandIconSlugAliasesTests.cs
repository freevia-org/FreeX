namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonCommandIconSlugAliasesTests
{
    [Theory]
    [InlineData("align-center", "center")]
    [InlineData("datetime", "date-time")]
    [InlineData("image-size", "size")]
    [InlineData("shape-textbox", "text-box")]
    [InlineData("style-heading3", "headings")]
    [InlineData("reject-all", "reject-change")]
    [InlineData("style-heading1", "heading-1")]
    [InlineData("style-heading2", "heading-2")]
    [InlineData("style-title", "title")]
    [InlineData("tof-figure", "caption")]
    [InlineData("zoom-dialog", "zoom")]
    public void Canonical_alias_is_first_candidate(string alias, string canonical)
    {
        Free.Shared.Ribbon.Icons.RibbonCommandIconSlugAliases.GetCandidates(alias)
            .First()
            .Should().Be(canonical);
    }

    [Fact]
    public void Unknown_slug_is_preserved() =>
        Free.Shared.Ribbon.Icons.RibbonCommandIconSlugAliases.GetCandidates("wordart")
            .Should().Equal("wordart");
}
