namespace Free.Shared.AppServices;

/// <summary>
/// Identifies which aggregate statistic a <see cref="StatusBarReadoutItem"/> represents,
/// so a renderer can route the item to the matching control without parsing its text.
/// </summary>
public enum StatusBarReadoutKind
{
    Average,
    Count,
    NumericalCount,
    Sum,
    Minimum,
    Maximum
}

/// <summary>
/// A single platform-neutral status-bar aggregate readout: a stable <see cref="Kind"/>,
/// the localized <see cref="Label"/> (used as an accessibility fallback name), the formatted
/// <see cref="Value"/> shown to the user, and whether it is currently <see cref="IsVisible"/>.
/// </summary>
public readonly record struct StatusBarReadoutItem(
    StatusBarReadoutKind Kind,
    string Label,
    string Value,
    bool IsVisible);
