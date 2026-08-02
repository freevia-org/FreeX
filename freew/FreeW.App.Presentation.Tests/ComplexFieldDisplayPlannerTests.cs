using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class ComplexFieldDisplayPlannerTests
{
    [Theory]
    [InlineData("PAGE", RunFieldKind.PageNumber)]
    [InlineData("DATE", RunFieldKind.Date)]
    [InlineData("TIME", RunFieldKind.Time)]
    [InlineData("FILENAME", RunFieldKind.FileName)]
    [InlineData("AUTHOR", RunFieldKind.Author)]
    [InlineData("NUMPAGES", RunFieldKind.NumPages)]
    [InlineData("TITLE", RunFieldKind.Title)]
    [InlineData("SUBJECT", RunFieldKind.Subject)]
    [InlineData("KEYWORDS", RunFieldKind.Keywords)]
    [InlineData("COMMENTS", RunFieldKind.DocComments)]
    public void ResolveLiveKind_UsesOneSharedKeywordMap(string keyword, RunFieldKind expected)
    {
        ComplexFieldDisplayPlanner.ResolveLiveKind(keyword).Should().Be(expected);
    }

    [Fact]
    public void FormatInvariantTemporalValue_DistinguishesDateFromTime()
    {
        var value = new DateTime(2026, 7, 25, 16, 5, 0);

        ComplexFieldDisplayPlanner.FormatInvariantTemporalValue(RunFieldKind.Date, value)
            .Should().Be("7/25/2026");
        ComplexFieldDisplayPlanner.FormatInvariantTemporalValue(RunFieldKind.Time, value)
            .Should().Be("4:05 PM");
    }

    [Fact]
    public void Build_UsesWordFieldCodeShape_AndKeepsCodePresentationSeparateFromResult()
    {
        var document = TextDocument.CreateEmpty();
        var field = new ComplexField(" TITLE ", ShowCode: true);

        var plan = ComplexFieldDisplayPlanner.Build(field, "Current title", document);

        plan.Text.Should().Be("{ TITLE }");
        plan.IsFieldCode.Should().BeTrue();
        plan.SuppressedResult.Should().BeFalse();
    }

    [Fact]
    public void Build_KeepsBibliographyCacheVisibleWhenGeneratedRegionFollows()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("References") { StyleId = Citations.HeadingStyleId });
        var field = new ComplexField(" BIBLIOGRAPHY \\l 1033 ");

        var plan = ComplexFieldDisplayPlanner.Build(field, "Stale cache", document);

        plan.Text.Should().Be("Stale cache");
        plan.SuppressedResult.Should().BeFalse();
        plan.IsFieldCode.Should().BeFalse();
    }
}
