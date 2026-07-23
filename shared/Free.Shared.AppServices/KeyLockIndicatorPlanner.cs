namespace Free.Shared.AppServices;

/// <summary>
/// Visibility for the status bar's CAPS LOCK / NUM LOCK warning indicators.
/// </summary>
public readonly record struct KeyLockIndicatorPlan(bool CapsLockVisible, bool NumLockVisible);

/// <summary>
/// Maps the raw toggle-key state (as read from the keyboard) to the status bar's CAPS LOCK /
/// NUM LOCK indicator visibility. Matches real Excel: each indicator is shown only while its
/// key is currently toggled on, so a user typing labels with Caps Lock accidentally engaged
/// gets an in-app warning instead of only noticing after the fact.
/// </summary>
public static class KeyLockIndicatorPlanner
{
    public static KeyLockIndicatorPlan Build(bool capsLockOn, bool numLockOn) =>
        new(CapsLockVisible: capsLockOn, NumLockVisible: numLockOn);
}
