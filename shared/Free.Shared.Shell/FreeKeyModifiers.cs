namespace Free.Shared.Shell;

/// <summary>
/// Toolkit-neutral keyboard modifier flags shared by the portable planners of every app.
/// </summary>
/// <remarks>
/// <see cref="Meta"/> is the Windows key under WPF and the Command key under Avalonia on macOS. What
/// Meta means is an explicit per-call-site decision (<see cref="KeyModifierMetaPolicy"/>) made when the
/// native modifiers are mapped, not something this enum guesses. The numeric values are an in-memory
/// contract only; nothing persists or serializes them.
/// </remarks>
[Flags]
public enum FreeKeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Meta = 8,
}

/// <summary>How a native Windows/Command key is treated when mapping to <see cref="FreeKeyModifiers"/>.</summary>
public enum KeyModifierMetaPolicy
{
    /// <summary>Surface it as <see cref="FreeKeyModifiers.Meta"/> and let the planner decide (FreeX default).</summary>
    Keep,

    /// <summary>Drop it: the planner must not see a bare Windows/Command key (FreeX WPF workbook shortcuts).</summary>
    Drop,

    /// <summary>Treat it as <see cref="FreeKeyModifiers.Control"/> and clear Meta (macOS Cmd as Ctrl, FreeX worksheet navigation).</summary>
    AsControl,
}
