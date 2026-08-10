using Free.Shared.AppServices;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWEditorStatusPlannerTests
{
    [Fact]
    public void Project_ComputesCanonicalDocumentCountsAndPreservesNativePosition()
    {
        var document = new TextDocument
        {
            Blocks =
            {
                new Paragraph { Runs = { new Run("one two") } },
                new Paragraph { Runs = { new Run("three") } },
            },
        };

        var snapshot = FreeWEditorStatusPlanner.Project(new FreeWEditorStatusContext(
            document,
            CurrentPage: 3,
            TotalPages: 7,
            CurrentSection: 2,
            TotalSections: 4));

        snapshot.Words.Should().Be(3);
        snapshot.CharactersWithSpaces.Should().Be(12);
        snapshot.Paragraphs.Should().Be(2);
        snapshot.CurrentPage.Should().Be(3);
        snapshot.TotalPages.Should().Be(7);
        snapshot.CurrentSection.Should().Be(2);
        snapshot.TotalSections.Should().Be(4);
    }

    [Fact]
    public void Build_ContextUsesSelectionProjectionInsteadOfDocumentCounts()
    {
        var document = new TextDocument
        {
            Blocks = { new Paragraph { Runs = { new Run("whole document text") } } },
        };

        var plan = FreeWEditorStatusPlanner.Build(new FreeWEditorStatusContext(
            document,
            SelectionText: "chosen words"));

        plan.CountsStatus.Should().Be("Selection: 2 words, 12 characters");
    }

    [Fact]
    public void Build_FormatsStatusSegmentsFromDocumentCounts()
    {
        var plan = FreeWEditorStatusPlanner.Build(new FreeWEditorStatusSnapshot(
            Words: 42,
            CharactersWithSpaces: 350,
            Paragraphs: 6,
            CurrentPage: 2,
            TotalPages: 5,
            CurrentSection: 1,
            TotalSections: 3));

        plan.PageStatus.Should().Be("Page 2 of 5");
        plan.SectionStatus.Should().Be("Section 1 of 3");
        plan.CountsStatus.Should().Be("Words: 42   Characters: 350   Paragraphs: 6");
    }

    [Fact]
    public void Build_UsesSelectionTextForCountsWithoutRequiringDocumentTotals()
    {
        var plan = FreeWEditorStatusPlanner.Build(new FreeWEditorStatusSnapshot(
            Words: 0,
            CharactersWithSpaces: 0,
            Paragraphs: 0,
            SelectionText: "hello world"));

        plan.CountsStatus.Should().Be("Selection: 2 words, 11 characters");
        plan.SummaryStatus.Should().Be("Page 1 of 1   Selection: 2 words, 11 characters");
    }

    [Fact]
    public void Build_ClampsInvalidCountsThroughSharedStatusFormatter()
    {
        var plan = FreeWEditorStatusPlanner.Build(new FreeWEditorStatusSnapshot(
            Words: -1,
            CharactersWithSpaces: -2,
            Paragraphs: -3,
            CurrentPage: -4,
            TotalPages: 0,
            CurrentSection: -5,
            TotalSections: 0));

        plan.PageStatus.Should().Be("Page 1 of 1");
        plan.SectionStatus.Should().Be("Section 1 of 1");
        plan.CountsStatus.Should().Be("Words: 0   Characters: 0   Paragraphs: 0");
    }

    [Fact]
    public void Build_CanOmitPageAndSectionForContinuousCompactStatus()
    {
        var plan = FreeWEditorStatusPlanner.Build(new FreeWEditorStatusSnapshot(
            Words: 8,
            CharactersWithSpaces: 64,
            Paragraphs: 2,
            IncludePageStatus: false,
            IncludeSectionStatus: false,
            IsEdited: true));

        plan.PageStatus.Should().BeEmpty();
        plan.SectionStatus.Should().BeEmpty();
        plan.SummaryStatus.Should().Be(
            $"8 words{SisterAppStatusBarTextPlanner.SegmentSeparator}64 characters{SisterAppStatusBarTextPlanner.SegmentSeparator}2 paragraphs{SisterAppStatusBarTextPlanner.SegmentSeparator}\u2022 edited");
    }
}
