namespace FreeX.App.Presentation.Backstage;

public sealed record FreeXBackstagePaneProjectionPlan(
    IReadOnlyList<FreeXBackstagePaneProjectionElement> Elements);

public abstract record FreeXBackstagePaneProjectionElement;

public sealed record FreeXBackstageHeadingProjectionElement(string TextKey)
    : FreeXBackstagePaneProjectionElement;

public sealed record FreeXBackstageSectionHeaderProjectionElement(string TextKey)
    : FreeXBackstagePaneProjectionElement;

public sealed record FreeXBackstageNoteProjectionElement(
    FreeXBackstageTextValue Text,
    string AutomationId)
    : FreeXBackstagePaneProjectionElement;

public sealed record FreeXBackstageDetailRowsProjectionElement(
    IReadOnlyList<FreeXBackstageDetailRowProjection> Rows)
    : FreeXBackstagePaneProjectionElement;

public sealed record FreeXBackstageDetailRowProjection(
    string LabelKey,
    FreeXBackstageTextValue Value,
    string ValueAutomationId);

public sealed record FreeXBackstageInfoActionRowProjectionElement(
    IReadOnlyList<FreeXBackstageInfoActionPlan> Actions)
    : FreeXBackstagePaneProjectionElement;

public sealed record FreeXBackstageAccountActionRowProjectionElement(
    IReadOnlyList<FreeXBackstageAccountActionDefinition> Actions)
    : FreeXBackstagePaneProjectionElement;

public sealed record FreeXBackstageExportRadioGroupProjectionElement(
    string GroupAutomationId,
    IReadOnlyList<FreeXBackstageExportRadioOptionProjection> Options)
    : FreeXBackstagePaneProjectionElement;

public abstract record FreeXBackstageExportRadioOptionProjection(
    string LabelKey,
    string AutomationId,
    bool IsEnabled,
    bool IsDefault);

public sealed record FreeXBackstageExportScopeRadioOptionProjection(
    FreeXBackstageExportScopeId Scope,
    string LabelKey,
    string AutomationId,
    bool IsEnabled,
    bool IsDefault)
    : FreeXBackstageExportRadioOptionProjection(LabelKey, AutomationId, IsEnabled, IsDefault);

public sealed record FreeXBackstageExportOutputKindRadioOptionProjection(
    FreeXBackstageExportOutputKindId OutputKind,
    string LabelKey,
    string AutomationId,
    bool IsEnabled,
    bool IsDefault)
    : FreeXBackstageExportRadioOptionProjection(LabelKey, AutomationId, IsEnabled, IsDefault);

/// <summary>
/// Projects FreeX Backstage pane plans into renderer-neutral pane element specs. Renderers localize text
/// keys and bind callbacks; section order and row/action/radio grouping stay portable.
/// </summary>
public static class FreeXBackstagePaneProjectionPlanner
{
    public static FreeXBackstagePaneProjectionPlan BuildInfoPane(
        FreeXBackstageInfoPanePlan pane)
    {
        ArgumentNullException.ThrowIfNull(pane);

        var elements = new List<FreeXBackstagePaneProjectionElement>
        {
            new FreeXBackstageHeadingProjectionElement(pane.TitleKey),
            new FreeXBackstageSectionHeaderProjectionElement(pane.ActionsHeadingKey),
            new FreeXBackstageInfoActionRowProjectionElement(pane.Actions),
            new FreeXBackstageSectionHeaderProjectionElement(pane.PropertiesHeadingKey),
            new FreeXBackstageDetailRowsProjectionElement(ProjectInfoDetails(pane.Details)),
        };

        return new FreeXBackstagePaneProjectionPlan(elements);
    }

    public static FreeXBackstagePaneProjectionPlan BuildInfoDialog(
        FreeXBackstageInfoPanePlan pane)
    {
        ArgumentNullException.ThrowIfNull(pane);

        var elements = new List<FreeXBackstagePaneProjectionElement>
        {
            new FreeXBackstageSectionHeaderProjectionElement(pane.FileSectionHeaderKey),
            new FreeXBackstageDetailRowsProjectionElement(ProjectInfoDetails(pane.Details)),
        };

        if (pane.UnsavedChangesNote is { } unsavedChangesNote)
        {
            elements.Add(new FreeXBackstageNoteProjectionElement(
                unsavedChangesNote,
                "BackstageInfoUnsaved"));
        }

        elements.Add(new FreeXBackstageSectionHeaderProjectionElement(pane.ProtectionSectionHeaderKey));
        elements.Add(new FreeXBackstageNoteProjectionElement(
            pane.WorkbookProtectionSummary,
            "BackstageInfoProtection"));
        elements.Add(new FreeXBackstageNoteProjectionElement(
            pane.ActiveSheetProtectionSummary,
            "BackstageInfoActiveSheetProtection"));
        elements.Add(new FreeXBackstageInfoActionRowProjectionElement(pane.Actions));
        elements.Add(new FreeXBackstageSectionHeaderProjectionElement(pane.StatisticsSectionHeaderKey));
        elements.Add(new FreeXBackstageNoteProjectionElement(
            pane.StatisticsSummary,
            "BackstageInfoStatistics"));

        return new FreeXBackstagePaneProjectionPlan(elements);
    }

    public static FreeXBackstagePaneProjectionPlan BuildExportDialog(
        FreeXBackstageExportPanePlan pane)
    {
        ArgumentNullException.ThrowIfNull(pane);

        var elements = new List<FreeXBackstagePaneProjectionElement>();
        if (pane.ShowUnavailableNote)
        {
            elements.Add(new FreeXBackstageNoteProjectionElement(
                FreeXBackstageTextValue.Key(pane.UnavailableNoteKey),
                pane.UnavailableAutomationId));
        }

        elements.Add(new FreeXBackstageSectionHeaderProjectionElement(pane.ScopeHeaderKey));
        elements.Add(new FreeXBackstageExportRadioGroupProjectionElement(
            pane.ScopeGroupAutomationId,
            pane.ScopeOptions
                .Select(option => new FreeXBackstageExportScopeRadioOptionProjection(
                    option.Scope,
                    option.LabelKey,
                    option.AutomationId,
                    option.IsEnabled,
                    option.IsDefault))
                .ToArray()));

        elements.Add(new FreeXBackstageSectionHeaderProjectionElement(pane.FormatHeaderKey));
        elements.Add(new FreeXBackstageExportRadioGroupProjectionElement(
            pane.FormatGroupAutomationId,
            pane.OutputKindOptions
                .Select(option => new FreeXBackstageExportOutputKindRadioOptionProjection(
                    option.OutputKind,
                    option.LabelKey,
                    option.AutomationId,
                    IsEnabled: true,
                    option.IsDefault))
                .ToArray()));

        return new FreeXBackstagePaneProjectionPlan(elements);
    }

    public static FreeXBackstagePaneProjectionPlan BuildAccountDialog(
        FreeXBackstageAccountPanePlan pane)
    {
        ArgumentNullException.ThrowIfNull(pane);

        var elements = new List<FreeXBackstagePaneProjectionElement>
        {
            new FreeXBackstageHeadingProjectionElement(pane.TitleKey),
            new FreeXBackstageSectionHeaderProjectionElement(pane.LocalInfoHeadingKey),
            new FreeXBackstageDetailRowsProjectionElement(ProjectAccountDetails(pane.Details)),
            new FreeXBackstageAccountActionRowProjectionElement(pane.Actions),
            new FreeXBackstageSectionHeaderProjectionElement(pane.NoticesHeadingKey),
        };

        foreach (var notice in pane.Notices)
        {
            elements.Add(new FreeXBackstageNoteProjectionElement(
                FreeXBackstageTextValue.Literal(notice.Text),
                notice.AutomationId));
        }

        return new FreeXBackstagePaneProjectionPlan(elements);
    }

    private static IReadOnlyList<FreeXBackstageDetailRowProjection> ProjectInfoDetails(
        IReadOnlyList<FreeXBackstageInfoDetailPlan> details) =>
        details
            .Select(detail => new FreeXBackstageDetailRowProjection(
                detail.LabelKey,
                detail.Value,
                detail.ValueAutomationId))
            .ToArray();

    private static IReadOnlyList<FreeXBackstageDetailRowProjection> ProjectAccountDetails(
        IReadOnlyList<FreeXBackstageAccountDetailPlan> details) =>
        details
            .Select(detail => new FreeXBackstageDetailRowProjection(
                detail.LabelKey,
                detail.Value,
                detail.ValueAutomationId))
            .ToArray();
}
