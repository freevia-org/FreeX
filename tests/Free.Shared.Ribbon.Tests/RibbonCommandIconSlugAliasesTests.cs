namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonCommandIconSlugAliasesTests
{
    public static IEnumerable<object[]> ConsolidatedAliases =>
    [
        ["custom-paragraph-spacing", "paragraph-spacing"],
        ["allow-edit-ranges", "allow-users-to-edit-ranges"],
        ["date-and-time", "date-time"],
        ["lookup-and-reference", "lookup-reference"],
        ["math-and-trig", "math-trig"],
        ["customize-colors", "theme-colors"],
        ["customize-fonts", "theme-fonts"],
        ["draftview", "draft-view"],
        ["chart-colors-colorful1", "chart-color-colorful1"],
        ["chart-colors-colorful2", "chart-color-colorful2"],
        ["chart-colors-colorful3", "chart-color-colorful3"],
        ["chart-colors-colorful4", "chart-color-colorful4"],
        ["chart-colors-mono-blue", "chart-color-mono-blue"],
        ["chart-colors-mono-grey", "chart-color-mono-grey"],
        ["chart-colors-mono-orange", "chart-color-mono-orange"],
        ["image-brightness-minus40", "image-brightness-minus20"],
        ["image-brightness-plus40", "image-brightness-plus20"],
        ["image-saturation-0", "image-saturation-50"],
        ["image-saturation-200", "image-saturation-50"],
        ["image-transparency-25", "image-transparency-50"],
        ["image-transparency-75", "image-transparency-50"],
        ["shape-flip-horizontal", "image-flip-horizontal"],
        ["shape-flip-vertical", "image-flip-vertical"],
        ["shape-position", "image-position"],
        ["shape-rotate-left90", "image-rotate-left90"],
        ["shape-rotate-right90", "image-rotate-right90"],
        ["shape-rotate", "image-rotate"],
        ["shape-wrap", "image-wrap"],
        ["shape-wrap-behind", "image-wrap-behind"],
        ["shape-wrap-front", "image-wrap-front"],
        ["shape-wrap-inline", "image-wrap-inline"],
        ["shape-wrap-square", "image-wrap-square"],
        ["shape-wrap-tight", "image-wrap-tight"],
        ["shape-wrap-top-bottom", "image-wrap-top-bottom"],
        ["index-insert", "index"],
        ["index-mark", "index"],
        ["insert-quickpart", "paste-special"],
        ["merge-rule-ask", "field"],
        ["merge-rule-fill-in", "field"],
        ["merge-rule-next-record-if", "merge-next-record"],
        ["merge-rule-ref", "field"],
        ["merge-rule-set", "field"],
        ["merge-rule-skip-record-if", "merge-rule-if"],
        ["merge-rules", "merge-rule-if"],
        ["multilevel-list", "multilevel-define"],
        ["multilevel-preset-0", "multilevel-define"],
        ["multilevel-preset-1", "multilevel-define"],
        ["multilevel-preset-2", "multilevel-define"],
        ["printlayout", "print-layout"],
        ["reset-style-set", "style-set"],
        ["reviewingpane", "reviewing-pane"],
        ["smartart-colors", "smartart-change-colors"],
        ["smartart-layout", "smartart-change-layout"],
        ["table-borders", "cell-borders"],
        ["table-insert-below", "table-insert-row"],
        ["table-insert-col-right", "table-insert-col"],
        ["table-merge-cells", "merge-center"],
        ["table-shading", "fill-color"],
        ["table-split-cell", "split-cell"],
        ["toc", "table-of-contents"],
        ["tof", "table-of-contents"],
        ["weblayout", "web-layout"],
    ];

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

    [Theory]
    [MemberData(nameof(ConsolidatedAliases))]
    public void Every_removed_exact_duplicate_slug_has_a_canonical_alias(string alias, string canonical)
    {
        Free.Shared.Ribbon.Icons.RibbonCommandIconSlugAliases.TryGetCanonicalSlug(alias, out var actual)
            .Should().BeTrue();
        actual.Should().Be(canonical);
        Free.Shared.Ribbon.Icons.RibbonCommandIconSlugAliases.GetCandidates(alias)
            .Take(2)
            .Should().Equal(canonical, alias);
    }

    [Fact]
    public void Unknown_slug_is_preserved() =>
        Free.Shared.Ribbon.Icons.RibbonCommandIconSlugAliases.GetCandidates("wordart")
            .Should().Equal("wordart");

    [Fact]
    public void Chart_quick_layout_labels_resolve_to_the_Wpf_command_assets()
    {
        for (var id = 1; id <= 9; id++)
        {
            Free.Shared.Ribbon.Icons.RibbonCommandIconSlugAliases.GetCandidates($"layout-{id}")
                .Take(2)
                .Should().Equal($"chart-quick-layout-{id}", $"layout-{id}");
        }
    }
}
