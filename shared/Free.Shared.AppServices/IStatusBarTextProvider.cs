namespace Free.Shared.AppServices;

/// <summary>
/// Supplies the localized ready, label, and format strings the status-bar model needs, decoupling the
/// neutral <see cref="StatusBarDisplayModelBuilder"/> from any particular resource system
/// (the WPF host backs this with its <c>UiText</c> resources; other shells can supply their own).
/// </summary>
public interface IStatusBarTextProvider
{
    /// <summary>
    /// The default ready/cell-mode text used when no active-cell prompt or edit mode overrides it.
    /// </summary>
    string GetReadyText();

    /// <summary>
    /// R128-status-bar-calculate-indicator: calc-mode-aware variant of <see cref="GetReadyText()"/>,
    /// resolving to Excel's "Calculate" cell-mode indicator (in place of "Ready") when
    /// <paramref name="isManualCalculationMode"/> and <paramref name="hasPendingRecalculation"/> are
    /// both true (see <see cref="StatusBarTextResourceKeys.CellModeResourceKey"/>). Defaults to the
    /// plain <see cref="GetReadyText()"/> text so existing implementations (including test doubles)
    /// keep compiling without needing calc-mode awareness.
    /// </summary>
    string GetReadyText(bool isManualCalculationMode, bool hasPendingRecalculation) => GetReadyText();

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
