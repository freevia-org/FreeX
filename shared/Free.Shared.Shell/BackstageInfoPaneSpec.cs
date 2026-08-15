namespace Free.Shared.Shell;

/// <summary>
/// UI-neutral data for an Office-style Backstage Info pane.
/// </summary>
public sealed record BackstageInfoPaneSpec(
    string DocumentKindLabel,
    string DisplayName,
    bool IsDirty,
    string? Location,
    IReadOnlyList<BackstageFieldRow> Properties,
    IReadOnlyList<BackstageFieldRow> Statistics,
    string? EditPropertiesText = null,
    Action? EditProperties = null,
    IReadOnlyList<BackstageActionGroup>? ActionGroups = null,
    BackstageInfoPaneTextSpec? Text = null)
{
    public BackstageInfoPaneTextSpec EffectiveText => Text ?? BackstageInfoPaneTextSpec.NeutralEnglish;
}
