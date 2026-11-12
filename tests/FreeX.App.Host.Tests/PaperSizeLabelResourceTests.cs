using FluentAssertions;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Page Setup listed Tabloid, Executive, A3 and A5 with label keys that had no resource, so the
/// combo box showed missing-text placeholders for them. Every paper size offered by Page Setup and
/// by the Page Layout &gt; Size menu must resolve to real text.
/// </summary>
public sealed class PaperSizeLabelResourceTests
{
    [Fact]
    public void EveryPageSetupPaperSizeChoice_HasALabelResource()
    {
        var keys = UiText.GetNeutralResourceKeys();

        PageSetupDialogModel.PaperSizeChoices
            .Select(choice => choice.LabelResourceKey)
            .Should().OnlyContain(key => keys.Contains(key));
    }

    [Fact]
    public void EveryRibbonPaperSizePreset_HasAStatusResource()
    {
        var keys = UiText.GetNeutralResourceKeys();

        Enum.GetValues<PageLayoutPaperSizePreset>()
            .Select(preset => PageLayoutRibbonActionPlanner.PlanPaperSizePreset(preset).StatusResourceKey)
            .Should().OnlyContain(key => keys.Contains(key));
    }
}
