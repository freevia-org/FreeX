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

    private static HeaderFooterPageSectionPlan Page(
        int sectionIndex,
        int relativePage,
        PageSettings settings) =>
        new(
            sectionIndex,
            new SectionHeadersFooters(),
            relativePage,
            settings);
}
