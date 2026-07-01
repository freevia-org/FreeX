namespace Free.Shared.AppServices;

public enum SisterAppClientFrameSlotRole
{
    Chrome,
    TopPanelBelowChrome,
    WorkArea,
    BottomPanelAboveStatus,
    StatusBar
}

public sealed record SisterAppClientFrameSlotPlan(
    SisterAppClientFrameSlotRole Role,
    int Index);

public sealed record SisterAppClientFrameContract(
    IReadOnlyList<SisterAppClientFrameSlotPlan> Slots);

public static class SisterAppClientFrameContractPlanner
{
    public static SisterAppClientFrameContract Plan(
        int topPanelsBelowChrome = 0,
        int bottomPanelsAboveStatus = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(topPanelsBelowChrome);
        ArgumentOutOfRangeException.ThrowIfNegative(bottomPanelsAboveStatus);

        var slots = new List<SisterAppClientFrameSlotPlan>
        {
            new(SisterAppClientFrameSlotRole.Chrome, 0),
        };

        for (var i = 0; i < topPanelsBelowChrome; i++)
            slots.Add(new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.TopPanelBelowChrome, i));

        slots.Add(new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.WorkArea, 0));

        for (var i = 0; i < bottomPanelsAboveStatus; i++)
            slots.Add(new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.BottomPanelAboveStatus, i));

        slots.Add(new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.StatusBar, 0));

        return new SisterAppClientFrameContract(slots.ToArray());
    }
}
