using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class TabsDialogPlannerTests
{
    [Fact]
    public void Choices_ExposeWordTabsDialogOptionsInDisplayOrder()
    {
        TabsDialogPlanner.Alignments.Select(choice => choice.Label)
            .Should().Equal("Left", "Center", "Right", "Decimal");
        TabsDialogPlanner.Alignments.Select(choice => choice.Value)
            .Should().Equal(
                TabStopAlignment.Left,
                TabStopAlignment.Center,
                TabStopAlignment.Right,
                TabStopAlignment.Decimal);

        TabsDialogPlanner.Leaders.Select(choice => choice.Label)
            .Should().Equal("1 None", "2 ....", "3 ----", "4 ____");
        TabsDialogPlanner.Leaders.Select(choice => choice.Value)
            .Should().Equal(TabLeader.None, TabLeader.Dots, TabLeader.Dashes, TabLeader.Underline);
    }

    [Fact]
    public void BuildInitialState_SortsDeduplicatesAndFormatsRows()
    {
        var state = TabsDialogPlanner.BuildInitialState(
            [
                new TabStop(72, TabStopAlignment.Left),
                new TabStop(36, TabStopAlignment.Left, TabLeader.Dots),
                new TabStop(72.004, TabStopAlignment.Center, TabLeader.Dashes)
            ],
            defaultTabStopPt: 42.5,
            CultureInfo.InvariantCulture);

        state.DefaultTabStopText.Should().Be("42.5");
        state.TabStops.Should().Equal(
            new TabStop(36, TabStopAlignment.Left, TabLeader.Dots),
            new TabStop(72.004, TabStopAlignment.Center, TabLeader.Dashes));
        state.Rows.Select(row => row.DisplayText)
            .Should().Equal("36 pt  Left  Dots", "72 pt  Center  Dashes");
    }

    [Fact]
    public void ProjectSelectedStop_ReflectsEditablePositionAndChoiceIndexes()
    {
        var state = TabsDialogPlanner.BuildInitialState(
            [new TabStop(108.25, TabStopAlignment.Decimal, TabLeader.Underline)],
            defaultTabStopPt: 36,
            CultureInfo.InvariantCulture);

        var selection = TabsDialogPlanner.ProjectSelectedStop(state, selectedIndex: 0, CultureInfo.InvariantCulture);

        selection.Should().NotBeNull();
        selection!.PositionText.Should().Be("108.25");
        selection.AlignmentIndex.Should().Be(3);
        selection.LeaderIndex.Should().Be(3);
    }

    [Fact]
    public void TrySetStop_AddsOrReplacesWithinToleranceAndSelectsPlannedRow()
    {
        var state = TabsDialogPlanner.BuildInitialState(
            [new TabStop(72, TabStopAlignment.Left)],
            defaultTabStopPt: 36,
            CultureInfo.InvariantCulture);

        TabsDialogPlanner.TrySetStop(
                state,
                new TabsDialogSetRequest("144", AlignmentIndex: 1, LeaderIndex: 1),
                CultureInfo.InvariantCulture,
                out var added,
                out var addError)
            .Should().BeTrue();
        addError.Should().BeNull();
        added!.SelectedIndex.Should().Be(1);
        added.State.TabStops.Should().Equal(
            new TabStop(72, TabStopAlignment.Left),
            new TabStop(144, TabStopAlignment.Center, TabLeader.Dots));

        TabsDialogPlanner.TrySetStop(
                added.State,
                new TabsDialogSetRequest("72.005", AlignmentIndex: 2, LeaderIndex: 2),
                CultureInfo.InvariantCulture,
                out var replaced,
                out var replaceError)
            .Should().BeTrue();
        replaceError.Should().BeNull();
        replaced!.SelectedIndex.Should().Be(0);
        replaced.State.TabStops.Should().Equal(
            new TabStop(72.005, TabStopAlignment.Right, TabLeader.Dashes),
            new TabStop(144, TabStopAlignment.Center, TabLeader.Dots));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    public void TrySetStop_RejectsInvalidPosition(string input)
    {
        var state = TabsDialogPlanner.BuildInitialState([], defaultTabStopPt: 36, CultureInfo.InvariantCulture);

        TabsDialogPlanner.TrySetStop(
                state,
                new TabsDialogSetRequest(input, AlignmentIndex: 0, LeaderIndex: 0),
                CultureInfo.InvariantCulture,
                out var plan,
                out var error)
            .Should().BeFalse();

        plan.Should().BeNull();
        error.Should().Be(TabsDialogValidationError.NonNegativePositionRequired);
    }

    [Fact]
    public void ClearStop_RemovesSelectedRowOrTypedPosition()
    {
        var state = TabsDialogPlanner.BuildInitialState(
            [new TabStop(36), new TabStop(72), new TabStop(144)],
            defaultTabStopPt: 36,
            CultureInfo.InvariantCulture);

        TabsDialogPlanner.ClearStop(state, selectedIndex: 1, positionText: "", CultureInfo.InvariantCulture)
            .TabStops.Should().Equal(new TabStop(36), new TabStop(144));

        TabsDialogPlanner.ClearStop(state, selectedIndex: -1, positionText: "144", CultureInfo.InvariantCulture)
            .TabStops.Should().Equal(new TabStop(36), new TabStop(72));
    }

    [Fact]
    public void ClearAll_RemovesEveryStopButKeepsDefaultTabStopText()
    {
        var state = TabsDialogPlanner.BuildInitialState(
            [new TabStop(36), new TabStop(72)],
            defaultTabStopPt: 48,
            CultureInfo.InvariantCulture);

        var cleared = TabsDialogPlanner.ClearAll(state);

        cleared.TabStops.Should().BeEmpty();
        cleared.Rows.Should().BeEmpty();
        cleared.DefaultTabStopText.Should().Be("48");
    }

    [Fact]
    public void TryBuildResult_ReturnsSortedStopsAndParsedDefaultInterval()
    {
        var state = TabsDialogPlanner.BuildInitialState(
            [new TabStop(72), new TabStop(36, TabStopAlignment.Right)],
            defaultTabStopPt: 36,
            CultureInfo.InvariantCulture);

        TabsDialogPlanner.TryBuildResult(
                state,
                defaultTabStopText: "42.5",
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.DefaultTabStopPt.Should().Be(42.5);
        result.TabStops.Should().Equal(
            new TabStop(36, TabStopAlignment.Right),
            new TabStop(72));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-0.5")]
    public void TryBuildResult_RejectsInvalidDefaultInterval(string input)
    {
        var state = TabsDialogPlanner.BuildInitialState([], defaultTabStopPt: 36, CultureInfo.InvariantCulture);

        TabsDialogPlanner.TryBuildResult(
                state,
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeFalse();

        result.Should().BeNull();
        error.Should().Be(TabsDialogValidationError.PositiveDefaultTabStopRequired);
    }
}
