using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class PageNumberFormatDialogPlannerTests
{
    [Fact]
    public void BuildInitialState_DefaultsToDecimalContinue()
    {
        var state = PageNumberFormatDialogPlanner.BuildInitialState(new PageSettings());

        state.FormatIndex.Should().Be(0);
        state.ContinueFromPreviousSection.Should().BeTrue();
        state.StartAtText.Should().Be("1");
        state.IncludeChapterNumber.Should().BeFalse();
    }

    [Fact]
    public void TryBuildResult_StartAtUpperRomanBuildsModelResult()
    {
        var input = new PageNumberFormatDialogInput(
            FormatIndex: 2,
            ContinueFromPreviousSection: false,
            StartAtText: "4");

        var ok = PageNumberFormatDialogPlanner.TryBuildResult(input, out var result, out var error);

        ok.Should().BeTrue(error);
        result.Format.Should().Be(PageNumberFormat.UpperRoman);
        result.StartAt.Should().Be(4);
        result.ChapterStyleLevel.Should().BeNull();
    }

    [Fact]
    public void BuildInitialState_LoadsChapterNumbering()
    {
        var state = PageNumberFormatDialogPlanner.BuildInitialState(new PageSettings
        {
            PageNumberChapterStyleLevel = 2,
            PageNumberChapterSeparator = PageNumberChapterSeparator.Colon
        });

        state.IncludeChapterNumber.Should().BeTrue();
        PageNumberFormatDialogPlanner.ChapterStyleItems[state.ChapterStyleIndex].Level.Should().Be(2);
        PageNumberFormatDialogPlanner.ChapterSeparatorItems[state.ChapterSeparatorIndex].Separator
            .Should().Be(PageNumberChapterSeparator.Colon);
    }

    [Fact]
    public void TryBuildResult_IncludesChapterNumbering()
    {
        var input = new PageNumberFormatDialogInput(
            FormatIndex: 1,
            ContinueFromPreviousSection: false,
            StartAtText: "2",
            IncludeChapterNumber: true,
            ChapterStyleIndex: 2,
            ChapterSeparatorIndex: 1);

        var ok = PageNumberFormatDialogPlanner.TryBuildResult(input, out var result, out var error);

        ok.Should().BeTrue(error);
        result.Format.Should().Be(PageNumberFormat.LowerRoman);
        result.StartAt.Should().Be(2);
        result.ChapterStyleLevel.Should().Be(3);
        result.ChapterSeparator.Should().Be(PageNumberChapterSeparator.Period);
    }

    [Fact]
    public void BuildDisplayPlans_HonorsStartAtThenContinueAcrossSections()
    {
        var section1 = new PageSettings
        {
            PageNumberFormat = PageNumberFormat.UpperRoman,
            PageNumberStartAt = 4
        };
        var section2 = new PageSettings
        {
            PageNumberFormat = PageNumberFormat.LowerLetter,
            PageNumberStartAt = null
        };
        var plans = new[]
        {
            Page(0, 1, section1),
            Page(0, 2, section1),
            Page(1, 1, section2),
            Page(1, 2, section2),
        };

        var display = PageNumberFormatDialogPlanner.BuildDisplayPlans(plans);

        display.Select(p => p.LogicalPageNumber).Should().Equal(4, 5, 6, 7);
        display.Select(p => p.Text).Should().Equal("IV", "V", "f", "g");
    }

    [Fact]
    public void BuildBlockPageReferenceResolver_UsesSectionRestartAndFormat()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var frontMatterPage = document.Page.Clone();
        frontMatterPage.PageNumberFormat = PageNumberFormat.LowerRoman;
        frontMatterPage.PageNumberStartAt = 1;
        document.Page.PageNumberFormat = PageNumberFormat.Decimal;
        document.Page.PageNumberStartAt = 1;
        document.Blocks.Add(new Paragraph("Front target") { BookmarkName = "front" });
        document.Blocks.Add(new Paragraph("Section end")
        {
            SectionBreak = new Section(frontMatterPage, SectionBreakKind.NextPage),
        });
        document.Blocks.Add(new Paragraph("Main target") { BookmarkName = "main" });

        var resolver = PageNumberFormatDialogPlanner.BuildBlockPageReferenceResolver(
            document,
            blockIndex => blockIndex < 2 ? 1 : 2);

        resolver(0).Should().Be("i");
        resolver(2).Should().Be("1");
        resolver(3).Should().BeNull();

        var addressResolver = PageNumberFormatDialogPlanner.BuildBlockPageReferenceAddressResolver(
            document,
            blockIndex => blockIndex < 2 ? 1 : 2);
        addressResolver(0).Should().Be(new IndexPageReferenceAddress(0, "i"));
        addressResolver(2).Should().Be(new IndexPageReferenceAddress(1, "1"));
        addressResolver(3).Should().BeNull();
    }

    [Fact]
    public void BuildDisplayPlans_PrefixesPageNumbersFromMappedHeadingOutline()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Intro") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("Intro body"));
        document.Blocks.Add(new Paragraph("Second") { StyleId = "Heading1" });

        var settings = new PageSettings
        {
            PageNumberChapterStyleLevel = 1,
            PageNumberChapterSeparator = PageNumberChapterSeparator.Hyphen
        };
        var plans = new[]
        {
            Page(0, 1, settings),
            Page(0, 2, settings),
            Page(0, 3, settings),
        };

        var display = PageNumberFormatDialogPlanner.BuildDisplayPlans(
            plans,
            document,
            [0, 0, 2]);

        display.Select(plan => plan.Text).Should().Equal("1-1", "1-2", "2-3");
        display.Select(plan => plan.ChapterNumber).Should().Equal("1", "1", "2");
    }

    [Fact]
    public void BuildCitationPageReferencePlans_PreservesPhysicalPageAndSectionDisplayText()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var frontMatterPage = document.Page.Clone();
        frontMatterPage.PageNumberFormat = PageNumberFormat.LowerRoman;
        frontMatterPage.PageNumberStartAt = 1;
        document.Page.PageNumberFormat = PageNumberFormat.Decimal;
        document.Page.PageNumberStartAt = 1;
        var citation = new Citation("Case A", CitationCategory.Cases);
        document.Blocks.Add(CitationParagraph("Front", citation));
        document.Blocks.Add(new Paragraph("section end")
        {
            SectionBreak = new Section(frontMatterPage, SectionBreakKind.NextPage)
        });
        document.Blocks.Add(CitationParagraph("Main", citation));

        var plans = PageNumberFormatDialogPlanner.BuildCitationPageReferencePlans(document);

        plans.Select(plan => plan.PhysicalPageNumber).Should().Equal(1, 2);
        plans.Select(plan => plan.SectionRelativePageNumber).Should().Equal(1, 1);
        plans.Select(plan => plan.LogicalPageNumber).Should().Equal(1, 1);
        plans.Select(plan => plan.DisplayText).Should().Equal("i", "1");

        var resolver = PageNumberFormatDialogPlanner.BuildCitationPageReferenceResolver(document);
        resolver(document, 0, 1, citation).Should().Be(new ToaCitationPageReference(1, "i"));
        resolver(document, 2, 1, citation).Should().Be(new ToaCitationPageReference(2, "1"));
    }

    [Fact]
    public void CommandValue_RoundTripsContinueAndStartAt()
    {
        var start = PageNumberFormatDialogPlanner.BuildCommandValue(PageNumberFormat.LowerRoman, 12);
        var cont = PageNumberFormatDialogPlanner.BuildCommandValue(PageNumberFormat.UpperLetter, null);

        PageNumberFormatDialogPlanner.TryBuildResultFromCommandValue(start, out var startResult)
            .Should().BeTrue();
        startResult.Should().Be(new PageNumberFormatDialogResult(PageNumberFormat.LowerRoman, 12));

        PageNumberFormatDialogPlanner.TryBuildResultFromCommandValue(cont, out var contResult)
            .Should().BeTrue();
        contResult.Should().Be(new PageNumberFormatDialogResult(PageNumberFormat.UpperLetter, null));
    }

    [Fact]
    public void CommandValue_RoundTripsChapterNumbering()
    {
        var value = PageNumberFormatDialogPlanner.BuildCommandValue(
            PageNumberFormat.LowerRoman,
            12,
            chapterStyleLevel: 2,
            chapterSeparator: PageNumberChapterSeparator.Colon);

        PageNumberFormatDialogPlanner.TryBuildResultFromCommandValue(value, out var result)
            .Should().BeTrue();

        result.Should().Be(new PageNumberFormatDialogResult(
            PageNumberFormat.LowerRoman,
            12,
            ChapterStyleLevel: 2,
            ChapterSeparator: PageNumberChapterSeparator.Colon));
    }

    private static HeaderFooterPageSectionPlan Page(
        int sectionIndex,
        int relativePage,
        PageSettings settings) =>
        new(
            sectionIndex,
            new SectionHeadersFooters(),
            relativePage,
            settings);

    private static Paragraph CitationParagraph(string text, Citation citation)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(text));
        paragraph.Runs.Add(Run.CitationMark(citation));
        return paragraph;
    }
}
