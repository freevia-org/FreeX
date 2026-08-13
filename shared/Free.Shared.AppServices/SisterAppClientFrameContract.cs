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

public sealed record SisterAppClientFrameContract
{
    public SisterAppClientFrameContract(IReadOnlyList<SisterAppClientFrameSlotPlan> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        var snapshot = slots.ToArray();
        Validate(snapshot);

        var workAreaIndex = Array.FindIndex(
            snapshot,
            slot => slot.Role == SisterAppClientFrameSlotRole.WorkArea);

        Slots = snapshot;
        SlotsBeforeWorkArea = snapshot[..workAreaIndex];
        WorkAreaSlot = snapshot[workAreaIndex];
        SlotsAfterWorkArea = snapshot[(workAreaIndex + 1)..];
    }

    public IReadOnlyList<SisterAppClientFrameSlotPlan> Slots { get; }

    public IReadOnlyList<SisterAppClientFrameSlotPlan> SlotsBeforeWorkArea { get; }

    public SisterAppClientFrameSlotPlan WorkAreaSlot { get; }

    public IReadOnlyList<SisterAppClientFrameSlotPlan> SlotsAfterWorkArea { get; }

    private static void Validate(IReadOnlyList<SisterAppClientFrameSlotPlan> slots)
    {
        if (slots.Count < 3)
            throw new ArgumentException("A client frame requires chrome, workarea, and status slots.", nameof(slots));

        ValidateSlot(slots[0], SisterAppClientFrameSlotRole.Chrome, 0, nameof(slots));
        ValidateSlot(slots[^1], SisterAppClientFrameSlotRole.StatusBar, 0, nameof(slots));

        var workAreaSlots = slots
            .Select((slot, position) => (slot, position))
            .Where(item => item.slot.Role == SisterAppClientFrameSlotRole.WorkArea)
            .ToArray();
        if (workAreaSlots.Length != 1)
            throw new ArgumentException("A client frame requires exactly one workarea slot.", nameof(slots));

        var workArea = workAreaSlots[0];
        ValidateSlot(workArea.slot, SisterAppClientFrameSlotRole.WorkArea, 0, nameof(slots));

        for (var position = 1; position < workArea.position; position++)
        {
            ValidateSlot(
                slots[position],
                SisterAppClientFrameSlotRole.TopPanelBelowChrome,
                position - 1,
                nameof(slots));
        }

        for (var position = workArea.position + 1; position < slots.Count - 1; position++)
        {
            ValidateSlot(
                slots[position],
                SisterAppClientFrameSlotRole.BottomPanelAboveStatus,
                position - workArea.position - 1,
                nameof(slots));
        }
    }

    private static void ValidateSlot(
        SisterAppClientFrameSlotPlan slot,
        SisterAppClientFrameSlotRole expectedRole,
        int expectedIndex,
        string parameterName)
    {
        if (slot.Role != expectedRole || slot.Index != expectedIndex)
        {
            throw new ArgumentException(
                $"Expected frame slot {expectedRole}[{expectedIndex}], but found {slot.Role}[{slot.Index}].",
                parameterName);
        }
    }
}

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
