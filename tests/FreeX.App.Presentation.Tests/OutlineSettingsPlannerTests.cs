using FluentAssertions;

using FreeX.App.Presentation.SheetUI;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Unit tests for the portable planner backing the Outline Settings dialog: resolving stored nullable
/// flags to displayed checkbox state (applying Excel defaults) and change detection. No running UI.
/// </summary>
public sealed class OutlineSettingsPlannerTests
{
    [Fact]
    public void FromStored_AppliesExcelDefaultsForUnsetFlags()
    {
        var state = OutlineSettingsPlanner.FromStored(null, null, null);

        state.Should().Be(new OutlineSettingsState(
            OutlineSettingsPlanner.DefaultSummaryBelow,
            OutlineSettingsPlanner.DefaultSummaryRight,
            OutlineSettingsPlanner.DefaultApplyStyles));
        state.SummaryBelow.Should().BeTrue();
        state.SummaryRight.Should().BeTrue();
        state.ApplyStyles.Should().BeFalse();
    }

    [Fact]
    public void FromStored_HonoursExplicitStoredFlags()
    {
        var state = OutlineSettingsPlanner.FromStored(summaryBelow: false, summaryRight: false, applyStyles: true);

        state.Should().Be(new OutlineSettingsState(false, false, true));
    }

    [Fact]
    public void HasChanges_FalseWhenAcceptedMatchesResolvedDefaults()
    {
        var accepted = OutlineSettingsPlanner.FromStored(null, null, null);

        OutlineSettingsPlanner.HasChanges(accepted, null, null, null).Should().BeFalse();
    }

    [Fact]
    public void HasChanges_TrueWhenAToggleDiffers()
    {
        var accepted = new OutlineSettingsState(SummaryBelow: false, SummaryRight: true, ApplyStyles: false);

        OutlineSettingsPlanner.HasChanges(accepted, null, null, null).Should().BeTrue();
    }
}
