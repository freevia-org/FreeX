using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class ReviewRevisionColorPlannerTests
{
    [Fact]
    public void BuildAuthorColors_assigns_current_visible_word_palette_in_first_revision_order()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("added") { Revision = RevisionKind.Inserted, RevisionAuthor = " Alice " });
        paragraph.Runs.Add(new Run("removed") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob" });
        paragraph.Runs.Add(new Run("formatted")
        {
            FormatRevision = new FormatRevision(RunFormatting.Default, "Carol", "2026-07-20T12:00:00Z")
        });
        paragraph.Runs.Add(new Run("again") { Revision = RevisionKind.Inserted, RevisionAuthor = "alice" });
        document.Blocks.Add(paragraph);

        var colors = ReviewRevisionColorPlanner.BuildAuthorColors(document);

        colors.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Alice"] = "#D13438",
            ["Bob"] = "#0078D4",
            ["Carol"] = "#5C2E91",
        });
        ReviewRevisionColorPlanner.ResolveColorHex(colors, " ALICE ").Should().Be("#D13438");
    }

    [Fact]
    public void ResolveColorHex_uses_fallback_for_missing_or_unknown_author()
    {
        var colors = ReviewRevisionColorPlanner.BuildAuthorColors(TextDocument.CreateEmpty());

        ReviewRevisionColorPlanner.ResolveColorHex(colors, null).Should().Be(ReviewRevisionColorPlanner.FallbackColorHex);
        ReviewRevisionColorPlanner.ResolveColorHex(colors, "Unknown").Should().Be(ReviewRevisionColorPlanner.FallbackColorHex);
    }
}
