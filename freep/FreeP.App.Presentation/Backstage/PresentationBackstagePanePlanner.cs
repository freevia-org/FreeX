using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationBackstageExportActions(
    Action ExportPdf,
    Action ExportNotesPagePdf,
    Action ExportImages,
    Action ExportVideo);

/// <summary>
/// Owns FreeP Backstage pane semantics while WPF and Avalonia retain only native control projection.
/// </summary>
public sealed class PresentationBackstagePanePlanner
{
    // Not resx-backed -- mirrors FreeW's own Open pane, which keeps its heading/description/group
    // text as plain constants in BackstageOpenPanePlanner rather than routing them through its
    // localization catalog. Only the recoverable command's own label (AutosaveRecoveryText.
    // BackstageLabel) is localized, per FreeP's resx conventions for user-invokable command text.
    private const string OpenPaneHeading = "Open";
    private const string OpenPaneDescription =
        "Open an existing presentation, or recover one FreeP didn't get to save.";
    private const string OpenPanePlacesGroupHeading = "Places";
    private const string OpenPaneBrowseLabel = "Browse";
    private const string OpenPaneBrowseDescription = "Open an existing presentation from this PC.";
    private const string OpenPaneRecoveryGroupHeading = "Recovery";
    private const string OpenPaneRecoveryRowDescription =
        "Open the latest autosave recovery snapshot saved by FreeP.";

    private readonly SisterBackstagePaneSpecPlanner _paneSpecs;
    private readonly Func<string, string?>? _getText;
    private readonly bool _usePresentationExportPlannerText;

    public PresentationBackstagePanePlanner(
        Func<string, string?>? getText = null,
        bool usePresentationExportPlannerText = false)
    {
        _paneSpecs = new SisterBackstagePaneSpecPlanner(
            FreePBackstagePaneTextCatalog.BuildTextSpec(getText));
        _getText = getText;
        _usePresentationExportPlannerText = usePresentationExportPlannerText;
    }

    public BackstageInfoPaneSpec BuildInfoPane(
        Presentation presentation,
        string displayName,
        bool isDirty,
        string? currentPath)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var properties = presentation.Properties;
        return SisterBackstageInfoPanePlanner.Build(new SisterBackstageInfoPaneContext(
            DocumentKindLabel: "Presentation",
            DisplayName: displayName,
            IsDirty: isDirty,
            Location: currentPath,
            CoreProperties: new BackstageCoreProperties(
                properties.Title,
                properties.Author,
                properties.Subject,
                properties.Keywords),
            Statistics:
            [
                new BackstageFieldRow("Slides", presentation.Slides.Count.ToString()),
            ],
            Text: _paneSpecs.Text.Info));
    }

    public BackstageRecentPaneSpec BuildRecentPane(
        IEnumerable<RecentFileEntry> entries,
        Action<string> openPath)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return _paneSpecs.BuildRecentPaneSpec(
            entries.Select(entry => entry.Path),
            openPath);
    }

    public BackstageTemplatePaneSpec BuildNewPane(Action create) =>
        _paneSpecs.BuildNewPaneSpec(create);

    /// <summary>
    /// The "Open" Backstage pane: a Places group that browses for a presentation, plus a Recovery
    /// group carrying the manual "Recover Unsaved Presentations" command (FreeP's autosave/
    /// crash-recovery feature). Mirrors the "Places" + "Recovery" groups of FreeW's
    /// <c>BackstageOpenPanePlanner</c>, minus FreeW's recent-documents/search surface — FreeP
    /// already has its own top-level "Recent" Backstage entry.
    ///
    /// <para>
    /// The <paramref name="browse"/> row is NOT optional garnish. Adding a recovery command turned
    /// this Backstage entry from a direct Command (which opened the file picker on click) into a
    /// Pane, so without a Browse row here "Backstage &gt; Open" would silently stop being able to
    /// open a presentation at all. <c>BuildOpenPane_ExposesBrowseSoTheOpenEntryStillOpensFiles</c>
    /// pins that.
    /// </para>
    /// </summary>
    public BackstageActionPaneSpec BuildOpenPane(Action browse, Action recoverUnsaved)
    {
        ArgumentNullException.ThrowIfNull(browse);
        ArgumentNullException.ThrowIfNull(recoverUnsaved);

        var text = AutosaveRecoveryTextCatalog.Resolve(_getText);
        return new BackstageActionPaneSpec(
            OpenPaneHeading,
            OpenPaneDescription,
            [
                new BackstageActionGroup(OpenPanePlacesGroupHeading,
                [
                    new BackstageActionRow(
                        OpenPaneBrowseLabel,
                        OpenPaneBrowseDescription,
                        browse)
                    {
                        AutomationId = "BackstageOpen_Browse",
                    },
                ]),
                new BackstageActionGroup(OpenPaneRecoveryGroupHeading,
                [
                    new BackstageActionRow(
                        text.BackstageLabel,
                        OpenPaneRecoveryRowDescription,
                        recoverUnsaved)
                    {
                        AutomationId = "BackstageOpen_RecoverUnsavedPresentations",
                    },
                ]),
            ]);
    }

    public BackstageOptionsPaneSpec BuildOptionsPane(
        FreePOptions options,
        string dataFolder,
        Action? edit = null) =>
        _paneSpecs.BuildOptionsPaneSpec(options, dataFolder, edit);

    public BackstageAccountPaneSpec BuildAccountPane(
        string productName,
        string version,
        string dataFolder,
        Action? openOptions,
        Func<string>? getUserName = null,
        Func<string>? getMachineName = null) =>
        _paneSpecs.BuildAccountPaneSpec(
            SisterBackstageAccountPaneContextPlanner.BuildLocal(
                productName,
                version,
                dataFolder,
                getUserName,
                getMachineName),
            openOptions);

    public BackstageActionPaneSpec BuildExportPane(
        bool videoExportAvailable,
        PresentationBackstageExportActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var plan = PresentationExportPlanner.BuildBackstageExportPlan(videoExportAvailable);
        var text = _usePresentationExportPlannerText
            ? new SisterBackstageExportPaneTextSpec(
                plan.Heading,
                plan.Description,
                plan.FixedLayoutGroupHeading,
                plan.FixedLayoutActions.Single(action =>
                    action.CommandId == PresentationExportPlanner.PdfExportCommandId).Label,
                plan.FixedLayoutActions.Single(action =>
                    action.CommandId == PresentationExportPlanner.PdfExportCommandId).Description)
            : _paneSpecs.Text.Export;
        var fixedLayout = plan.FixedLayoutActions
            .Where(action => action.IsEnabled)
            .Select(action => BuildExportAction(action, actions, text))
            .ToArray();
        var groups = new List<BackstageActionGroup>
        {
            new(text.FixedLayoutGroupHeading, fixedLayout),
        };

        var deferred = plan.DeferredActions
            .Where(action => action.IsEnabled)
            .Select(action => BuildExportAction(action, actions, text))
            .ToArray();
        if (deferred.Length > 0)
            groups.Add(new BackstageActionGroup(plan.DeferredGroupHeading, deferred));

        return new BackstageActionPaneSpec(
            text.Heading,
            text.Description,
            groups);
    }

    private static BackstageActionRow BuildExportAction(
        PresentationBackstageExportActionPlan plan,
        PresentationBackstageExportActions actions,
        SisterBackstageExportPaneTextSpec text)
    {
        var label = plan.CommandId == PresentationExportPlanner.PdfExportCommandId
            ? text.PdfActionLabel
            : plan.Label;
        var description = plan.CommandId == PresentationExportPlanner.PdfExportCommandId
            ? text.PdfActionDescription
            : plan.Description;

        return new BackstageActionRow(
            label,
            description,
            ResolveExportAction(plan.CommandId, actions))
        {
            AutomationId = "BackstageExport_" + AutomationIdToken.KeepLettersAndDigits(plan.CommandId),
            IsEnabled = plan.IsEnabled,
        };
    }

    private static Action ResolveExportAction(
        string commandId,
        PresentationBackstageExportActions actions) =>
        commandId switch
        {
            PresentationExportPlanner.PdfExportCommandId => actions.ExportPdf,
            PresentationExportPlanner.NotesPagePdfExportCommandId => actions.ExportNotesPagePdf,
            PresentationExportPlanner.ImageExportCommandId => actions.ExportImages,
            PresentationExportPlanner.VideoExportCommandId => actions.ExportVideo,
            _ => throw new InvalidOperationException($"Unsupported FreeP export command '{commandId}'."),
        };

}
