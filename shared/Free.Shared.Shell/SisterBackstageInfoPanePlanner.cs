namespace Free.Shared.Shell;

public sealed record SisterBackstageInfoPaneContext(
    string DocumentKindLabel,
    string DisplayName,
    bool IsDirty,
    string? Location,
    BackstageCoreProperties CoreProperties,
    IReadOnlyList<BackstageFieldRow> Statistics,
    string? EditPropertiesText = null,
    Action? EditProperties = null,
    IReadOnlyList<BackstageActionGroup>? ActionGroups = null,
    BackstageInfoPaneTextSpec? Text = null);

/// <summary>
/// Converts sister-app document metadata into the shared Info pane spec. App hosts collect live model values;
/// this planner owns the common property/status shape.
/// </summary>
public static class SisterBackstageInfoPanePlanner
{
    public static BackstageInfoPaneSpec Build(SisterBackstageInfoPaneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.CoreProperties);
        ArgumentNullException.ThrowIfNull(context.Statistics);

        var text = context.Text ?? BackstageInfoPaneTextSpec.NeutralEnglish;
        return new BackstageInfoPaneSpec(
            DocumentKindLabel: context.DocumentKindLabel,
            DisplayName: context.DisplayName,
            IsDirty: context.IsDirty,
            Location: context.Location,
            Properties: BackstageCorePropertiesPlanner.Build(context.CoreProperties, text.CoreProperties),
            Statistics: context.Statistics,
            EditPropertiesText: context.EditPropertiesText,
            EditProperties: context.EditProperties,
            ActionGroups: context.ActionGroups,
            Text: text);
    }
}
