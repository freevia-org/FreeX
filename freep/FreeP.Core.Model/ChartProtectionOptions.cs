namespace FreeP.Core.Model;

/// <summary>
/// Editable chart protection flags. A null value omits the corresponding OOXML token,
/// true protects the feature, and false explicitly leaves it unprotected.
/// </summary>
public sealed record ChartProtectionOptions(
    bool? ChartObject,
    bool? Data,
    bool? Formatting,
    bool? Selection);
