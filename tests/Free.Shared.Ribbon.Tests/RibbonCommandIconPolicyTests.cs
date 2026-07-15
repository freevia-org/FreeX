using Free.Shared.Ribbon.Icons;

namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonCommandIconPolicyTests
{
    public static IEnumerable<object[]> LegacyAliases =>
    [
        ["increase-font-size", "grow-font"], ["decrease-font-size", "shrink-font"],
        ["accounting-number-format", "accounting-currency"], ["increase-decimal-places", "increase-decimal"],
        ["decrease-decimal-places", "decrease-decimal"], ["merge-and-center", "merge-center"],
        ["sort-and-filter", "sort"], ["find-and-select", "find"], ["insert-link", "hyperlink"],
        ["header-and-footer", "header-footer"], ["pictures", "picture"], ["percent-style", "percent-style"],
        ["advanced", "advanced-filter"], ["clear-filter", "clear-filter"],
        ["page-setup-dialog", "page-setup"], ["view-gridlines", "gridlines"],
        ["print-gridlines", "print-gridlines"], ["view-headings", "headings"],
        ["print-headings", "print-headings"], ["object-fill", "fill"], ["object-outline", "outline-color"],
        ["object-size", "size"], ["object-rotate", "rotate"], ["shape-gradient", "gradient"],
        ["shape-fill", "fill"], ["shape-outline", "outline-color"], ["shape-effects", "effects"],
        ["object-effects", "effects"], ["selection-pane", "selection-pane"],
        ["ink-to-shape", "shapes"], ["ink-to-math", "math-trig"], ["math", "math-trig"],
        ["recently-used", "recent"], ["date", "date-time"], ["lookup", "lookup-reference"],
        ["formula-auditing", "evaluate-formula"], ["calculation", "calculate-now"],
        ["workbook-stats", "statistics"], ["workbook-statistics", "statistics"],
        ["accessibility", "accessibility-checker"], ["refresh-pivot", "refresh-all"],
        ["show-details", "show-detail"], ["links-and-objects", "hyperlink"], ["help-online", "help"],
        ["contact-support", "contact-support"], ["what-s-new", "what-s-new"], ["whats-new", "what-s-new"],
        ["about-freex", "about"], ["side-by-side", "view-side-by-side"],
        ["sync-scrolling", "synchronous-scrolling"], ["reset-position", "reset-window-position"],
        ["100", "zoom-to-100"], ["save-as", "save-as"], ["export-pdf-xps", "export"],
        ["page-orientation", "page-orientation"], ["hide", "hide-sheet"], ["unhide", "unhide-sheet"],
        ["show-detail", "show-detail"], ["hide-detail", "hide-detail"], ["collapse-group", "hide-detail"],
        ["expand-group", "show-detail"], ["add-watch", "watch-add"], ["delete-watch", "watch-delete"],
        ["reapply", "reapply-filter"], ["reapply-filter", "reapply-filter"],
        ["sort-a-to-z", "sort-ascending"], ["sort-z-to-a", "sort-descending"],
        ["pick-from-drop-down-list", "pick-from-dropdown"], ["macros", "macros"], ["macro", "macros"],
        ["queries-connections", "queries-connections"], ["check-for-updates", "check-for-updates"],
        ["pin-to-list", "pin-to-list"], ["unpin-from-list", "unpin-from-list"],
        ["remove-from-list", "remove-from-list"], ["rename", "rename-sheet"], ["duplicate", "duplicate-sheet"],
        ["plus-minus-buttons", "show-detail"], ["buttons", "show-detail"]
    ];

    [Theory]
    [MemberData(nameof(LegacyAliases))]
    public void LegacyAlias_IsIncludedWithoutRecursiveExpansion(string slug, string alias)
    {
        var candidates = RibbonCommandIconPolicy.GetCommandIconSlugCandidates(slug).ToList();

        candidates.Should().Contain(slug);
        candidates.Should().Contain(alias);
        candidates.Should().Equal(candidates.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData("Selection Pane#SelectionPaneBtn_Click", "Selection Pane")]
    [InlineData("Clear#ClearFilterButton_Click", "Clear Filter")]
    [InlineData("Remove Duplicates#RemoveDuplicatesBtn_Click#ignored", "Remove Duplicates")]
    public void NormalizeCommandIconName_RemovesOnlyTheCommandHandlerSuffix(string? input, string expected)
    {
        RibbonCommandIconPolicy.NormalizeCommandIconName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(" Sort & Filter ", "sort-and-filter")]
    [InlineData("Export PDF/XPS", "export-pdf-xps")]
    [InlineData("Pick From Drop-down List...", "pick-from-drop-down-list")]
    [InlineData("A__B", "a-b")]
    [InlineData("100%", "100")]
    public void ToCommandIconSlug_MatchesCommandIconFilenameNormalization(string? input, string expected)
    {
        RibbonCommandIconPolicy.ToCommandIconSlug(input).Should().Be(expected);
    }

    [Fact]
    public void ExistingAliasesComposeCanonicalThenHistoricalThenLegacyWithoutCycles()
    {
        RibbonCommandIconPolicy.GetCommandIconSlugCandidates("date-and-time")
            .Should().Equal("date-time", "date-and-time");
        RibbonCommandIconPolicy.GetCommandIconSlugCandidates("pictures")
            .Should().Equal("picture", "pictures");
        RibbonCommandIconPolicy.GetCommandIconSlugCandidates("sort-and-filter")
            .Should().Equal("sort-and-filter", "sort");
        RibbonCommandIconPolicy.GetCommandIconSlugCandidates("date-time")
            .Should().Equal("date-time");
    }

    [Fact]
    public void UnknownSlugIsTheOnlyFallbackCandidate()
    {
        RibbonCommandIconPolicy.GetCommandIconSlugCandidates("unknown-command")
            .Should().Equal("unknown-command");
    }
}
