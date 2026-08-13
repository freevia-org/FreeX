namespace Free.Shared.AppServices.Tests;

public sealed class SisterAppClientFrameContractTests
{
    [Fact]
    public void Plan_PartitionsChromeWorkareaAndStatusRegions()
    {
        var contract = SisterAppClientFrameContractPlanner.Plan(
            topPanelsBelowChrome: 2,
            bottomPanelsAboveStatus: 2);

        contract.SlotsBeforeWorkArea.Should().Equal(
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.Chrome, 0),
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.TopPanelBelowChrome, 0),
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.TopPanelBelowChrome, 1));
        contract.WorkAreaSlot.Should().Be(
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.WorkArea, 0));
        contract.SlotsAfterWorkArea.Should().Equal(
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.BottomPanelAboveStatus, 0),
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.BottomPanelAboveStatus, 1),
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.StatusBar, 0));
        contract.Slots.Should().Equal(
            contract.SlotsBeforeWorkArea
                .Append(contract.WorkAreaSlot)
                .Concat(contract.SlotsAfterWorkArea));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Plan_RejectsNegativePanelCounts(int topPanelCount, int bottomPanelCount)
    {
        var act = () => SisterAppClientFrameContractPlanner.Plan(topPanelCount, bottomPanelCount);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Contract_RejectsMalformedSlotSequences()
    {
        var duplicateWorkarea = () => new SisterAppClientFrameContract(
        [
            new(SisterAppClientFrameSlotRole.Chrome, 0),
            new(SisterAppClientFrameSlotRole.WorkArea, 0),
            new(SisterAppClientFrameSlotRole.WorkArea, 0),
            new(SisterAppClientFrameSlotRole.StatusBar, 0),
        ]);
        var nonContiguousTopIndex = () => new SisterAppClientFrameContract(
        [
            new(SisterAppClientFrameSlotRole.Chrome, 0),
            new(SisterAppClientFrameSlotRole.TopPanelBelowChrome, 1),
            new(SisterAppClientFrameSlotRole.WorkArea, 0),
            new(SisterAppClientFrameSlotRole.StatusBar, 0),
        ]);

        duplicateWorkarea.Should().Throw<ArgumentException>();
        nonContiguousTopIndex.Should().Throw<ArgumentException>();
    }
}
