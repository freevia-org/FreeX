namespace Free.Shared.AppServices;

/// <summary>
/// Supplies the localized label/format strings the status-bar model needs, decoupling the
/// neutral <see cref="StatusBarDisplayModelBuilder"/> from any particular resource system
/// (the WPF host backs this with its <c>UiText</c> resources; other shells can supply their own).
/// </summary>
public interface IStatusBarTextProvider
{
    /// <summary>
    /// The composite format string for a readout (e.g. <c>"Average: {0}"</c>) for the given kind.
    /// </summary>
    string GetReadoutFormat(StatusBarReadoutKind kind);

    /// <summary>
    /// The bare label for a readout (e.g. <c>"Average"</c>) for the given kind, used as the
    /// accessibility fallback name when the value is empty.
    /// </summary>
    string GetReadoutLabel(StatusBarReadoutKind kind);
}
