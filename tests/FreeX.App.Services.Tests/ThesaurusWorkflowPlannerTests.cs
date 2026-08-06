using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class ThesaurusWorkflowPlannerTests
{
    [Fact]
    public void TryCreateLookup_InterpretsFirstAlphabeticWordAndReturnsSynonyms()
    {
        ThesaurusWorkflowPlanner.TryCreateLookup("  2026 Profit growth", out var plan)
            .Should().BeTrue();

        plan.Word.Should().Be("Profit");
        plan.StartIndex.Should().Be(7);
        plan.Length.Should().Be(6);
        plan.Synonyms.Should().Contain(["gain", "earnings", "return", "income"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" 123 -- ")]
    public void TryCreateLookup_RejectsTextWithoutAlphabeticWords(string? text)
    {
        ThesaurusWorkflowPlanner.TryCreateLookup(text, out _).Should().BeFalse();
    }

    [Fact]
    public void ApplyReplacement_ReplacesOnlyThePlannedWordRange()
    {
        ThesaurusWorkflowPlanner.TryCreateLookup("#profit + profit", out var plan)
            .Should().BeTrue();

        ThesaurusWorkflowPlanner.ApplyReplacement(plan, " earnings ")
            .Should().Be("#earnings + profit");
    }

    [Fact]
    public void UnknownWord_StillProducesLookupPlanWithEmptySuggestions()
    {
        ThesaurusWorkflowPlanner.TryCreateLookup("FreeX value", out var plan)
            .Should().BeTrue();

        plan.Word.Should().Be("FreeX");
        plan.Synonyms.Should().BeEmpty();
        ThesaurusWorkflowPlanner.ApplyReplacement(plan, null).Should().Be("FreeX value");
    }
}
