using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Backstage;

/// <summary>
/// The framework-neutral application contract behind FreeW's Backstage renderer.
/// Native hosts provide file pickers, dialogs, services, focus, and window lifecycle through these callbacks.
/// </summary>
public sealed record BackstageCallbacks(
    string DisplayName,
    string? CurrentPath,
    Func<IEnumerable<RecentFileEntry>> GetRecentEntries,
    Func<IEnumerable<FileFormatDescriptor>> GetFileFormats,
    Func<PageSettings> GetPageSettings,
    Func<FreeWOptions> GetCurrentOptions,
    Func<string> GetDataFolder,
    Func<TextDocument> GetDocument,
    Func<bool> GetIsDirty,
    Action NewDocument,
    Action<string> OpenRecent,
    Action<string> OpenFolder,
    Action Browse,
    Action RecoverUnsaved,
    Action ImportPdfText,
    Action Save,
    Action SaveAs,
    Action<string, int> SaveAsFormat,
    Action SaveCopy,
    Action<string> OpenContainingFolder,
    Action ExportPdf,
    Action? ExportXps,
    Action EditProperties,
    Action MarkAsFinal,
    Action RestrictEditing,
    Action InspectDocument,
    Action CheckAccessibility,
    Action OpenOptions,
    Action CloseDocument,
    BackstageDirectPrintCapability? DirectPrintCapability = null,
    Action? Print = null,
    Action? PrintPreview = null,
    Action<string?, string?>? SaveAsSuggested = null,
    Action? OnClosed = null,
    Func<string>? GetDisplayName = null,
    Func<string?>? GetCurrentPath = null,
    Func<string, bool>? FileExists = null,
    // File > New templates. Null means the host cannot create a document from that kind of template,
    // and the gallery then offers only what it can actually do (see FreeWTemplateGalleryPlanner).
    Action<string>? NewFromBuiltInTemplate = null,
    Action<string>? NewFromTemplateFile = null,
    Func<string>? GetPersonalTemplatesFolder = null,
    Func<string, IReadOnlyList<PersonalTemplateEntry>>? ListPersonalTemplates = null,
    // File > Info > Protect Document > Encrypt with Password. Null hides the action on hosts that
    // cannot prompt for a password.
    Action? EncryptWithPassword = null);

/// <summary>
/// Owns FreeW Backstage state projection, command policy, enablement, and pane planning.
/// Renderers consume the returned specs and keep only native controls and interaction plumbing.
/// </summary>
public sealed class FreeWBackstageSession
{
    private readonly BackstageCallbacks _callbacks;
    private readonly BackstageActionBinder _binder;
    private readonly Func<string, string?>? _getText;

    public FreeWBackstageSession(
        BackstageCallbacks callbacks,
        BackstageActionBinder? binder = null,
        Func<string, string?>? getText = null)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        _binder = binder ?? BackstageActionBinder.Identity;
        _getText = getText;
    }

    public string DisplayName =>
        NormalizeDisplayName(CurrentDisplayName);

    public string? CurrentPath => _callbacks.GetCurrentPath?.Invoke() ?? _callbacks.CurrentPath;

    public BackstageHomePaneSurfaceSpec BuildHomePane(Action openMore)
    {
        ArgumentNullException.ThrowIfNull(openMore);

        var surface = BackstagePaneSurfacePlanner.BuildHomePane(
            _callbacks.GetRecentEntries(),
            Bind(_callbacks.NewDocument),
            Bind(_callbacks.OpenRecent),
            Bind(_callbacks.Browse),
            openMore);
        return _callbacks.NewFromBuiltInTemplate is { } newFromBuiltIn
            ? surface with { Groups = AddTemplateRows(surface.Groups, Bind(newFromBuiltIn)) }
            : surface;
    }

    /// <summary>
    /// File &gt; New: the built-in templates plus the user's personal .dotx/.dotm files, each as a
    /// tile with a miniature page. Every tile action dismisses the Backstage first, like the other panes.
    /// </summary>
    public FreeWTemplateGallerySpec BuildTemplateGallery()
    {
        var canUseTemplateFiles = _callbacks.NewFromTemplateFile is not null;
        var folder = canUseTemplateFiles
            ? _callbacks.GetPersonalTemplatesFolder?.Invoke() ?? PersonalTemplatesFolder.Resolve(null)
            : null;
        var personal = folder is null
            ? null
            : (_callbacks.ListPersonalTemplates ?? PersonalTemplateCatalog.List)(folder);

        return FreeWTemplateGalleryPlanner.Build(
            Bind(_callbacks.NewDocument),
            _callbacks.NewFromBuiltInTemplate is { } newFromBuiltIn ? Bind(newFromBuiltIn) : null,
            _callbacks.NewFromTemplateFile is { } newFromFile ? Bind(newFromFile) : null,
            personal,
            folder,
            _getText);
    }

    // The Home pane's "New" group lists the built-in templates after "Blank document".
    private IReadOnlyList<BackstageActionGroup> AddTemplateRows(
        IReadOnlyList<BackstageActionGroup> groups,
        Action<string> newFromBuiltIn)
    {
        if (groups.Count == 0)
            return groups;

        var rows = BuiltInDocumentTemplates.All
            .Where(template => template.Id != BuiltInDocumentTemplates.BlankId)
            .Select(template => new BackstageActionRow(
                FreeWTemplateGalleryPlanner.TemplateName(template, _getText),
                FreeWTemplateGalleryPlanner.TemplateDescription(template, _getText),
                () => newFromBuiltIn(template.Id))
            {
                AutomationId = "BackstageAction_NewFrom"
                    + AutomationIdToken.KeepLettersAndDigits(template.Id),
            });
        return [groups[0] with { Actions = [.. groups[0].Actions, .. rows] }, .. groups.Skip(1)];
    }

    public BackstageOpenPaneSurfaceSpec BuildOpenPane(string? filter) =>
        BackstagePaneSurfacePlanner.BuildOpenPane(
            _callbacks.GetRecentEntries(),
            filter,
            Bind(_callbacks.OpenRecent),
            Bind(_callbacks.OpenFolder),
            Bind(_callbacks.Browse),
            Bind(_callbacks.RecoverUnsaved));

    public BackstageSaveAsPaneSurfaceSpec BuildSaveAsPane() =>
        BackstagePaneSurfacePlanner.BuildSaveAsPane(
            _callbacks.GetFileFormats(),
            CurrentDisplayName ?? string.Empty,
            CurrentPath,
            Bind(_callbacks.SaveAs),
            Bind(_callbacks.SaveAsFormat));

    public BackstagePrintPaneSurfaceSpec BuildPrintPane()
    {
        var capability = _callbacks.DirectPrintCapability
            ?? (_callbacks.Print is null
                ? BackstageDirectPrintCapability.Deferred()
                : BackstageDirectPrintCapability.NativeDialogAvailable(
                    "Direct native print is backed by the current host callback."));
        var print = capability.IsAvailable && _callbacks.Print is { } printAction
            ? Bind(printAction)
            : null;
        var preview = _callbacks.PrintPreview is { } previewAction
            ? Bind(previewAction)
            : null;

        return BackstagePaneSurfacePlanner.BuildPrintPane(
            DisplayName,
            _callbacks.GetPageSettings(),
            print,
            preview,
            capability);
    }

    public BackstageActionPaneSurfaceSpec BuildSharePane() =>
        BackstagePaneSurfacePlanner.BuildSharePane(
            CurrentPath,
            _callbacks.FileExists ?? File.Exists,
            Bind(_callbacks.SaveAs),
            Bind(_callbacks.OpenContainingFolder),
            Bind(_callbacks.SaveCopy),
            Bind(_callbacks.ExportPdf));

    public BackstageActionPaneSurfaceSpec BuildExportPane(
        BackstageExportPaneSurfaceText? text = null) =>
        BackstagePaneSurfacePlanner.BuildExportPane(
            _callbacks.GetFileFormats(),
            Bind(_callbacks.ExportPdf),
            _callbacks.ExportXps is { } exportXps ? Bind(exportXps) : null,
            Bind(_callbacks.SaveAsFormat),
            text);

    public BackstageInfoPaneSpec BuildInfoPane()
    {
        var document = _callbacks.GetDocument();
        var paneText = FreeWBackstagePaneTextCatalog.BuildTextSpec(_getText);
        var safetySurface = BackstagePaneSurfacePlanner.BuildInfoPane(
            [],
            Bind(_callbacks.MarkAsFinal),
            Bind(_callbacks.RestrictEditing),
            Bind(_callbacks.InspectDocument),
            Bind(_callbacks.CheckAccessibility),
            document,
            _getText,
            _callbacks.EncryptWithPassword is { } encryptWithPassword ? Bind(encryptWithPassword) : null);

        return SisterBackstageInfoPanePlanner.Build(new SisterBackstageInfoPaneContext(
            DocumentKindLabel: BackstageViewTextResources.DocumentLabel,
            DisplayName: DisplayName,
            IsDirty: _callbacks.GetIsDirty(),
            Location: CurrentPath,
            CoreProperties: new BackstageCoreProperties(
                document.Properties.Title,
                document.Properties.Author,
                document.Properties.Subject,
                document.Properties.Keywords),
            Statistics: BackstageInfoStatisticsPlanner.Build(document),
            EditPropertiesText: "Edit document properties\u2026",
            EditProperties: Bind(_callbacks.EditProperties),
            ActionGroups: ToActionGroups(safetySurface.SafetyGroups),
            Text: paneText.Info));
    }

    public BackstageAccountPaneSurfaceSpec BuildAccountPane(string productVersion) =>
        BackstagePaneSurfacePlanner.BuildAccountPane(
            SisterBackstageAccountPaneContextPlanner.BuildLocal(
                BackstageViewTextResources.ProductName,
                productVersion,
                _callbacks.GetDataFolder()),
            Bind(_callbacks.OpenOptions));

    public BackstageRecentPaneSpec BuildRecentPaneSpec(SisterBackstagePaneSpecPlanner paneSpecs)
    {
        ArgumentNullException.ThrowIfNull(paneSpecs);

        return paneSpecs.BuildRecentPaneSpec(
            _callbacks.GetRecentEntries().Select(entry => entry.Path),
            Bind(_callbacks.OpenRecent));
    }

    public BackstageTemplatePaneSpec BuildNewPaneSpec(SisterBackstagePaneSpecPlanner paneSpecs)
    {
        ArgumentNullException.ThrowIfNull(paneSpecs);
        return paneSpecs.BuildNewPaneSpec(Bind(_callbacks.NewDocument));
    }

    public BackstageOptionsPaneSpec BuildOptionsPaneSpec(SisterBackstagePaneSpecPlanner paneSpecs)
    {
        ArgumentNullException.ThrowIfNull(paneSpecs);

        return paneSpecs.BuildOptionsPaneSpec(
            _callbacks.GetCurrentOptions(),
            _callbacks.GetDataFolder(),
            Bind(_callbacks.OpenOptions));
    }

    public string ChangeInlineFileType(string? fileName, string extension) =>
        BackstageSaveAsFileTypePlanner.ReplaceFileNameExtension(fileName, extension);

    public void SaveInline(
        string? fileName,
        BackstageSaveAsFileTypeChoice? choice,
        string fallbackExtension)
    {
        var extension = choice?.PrimaryExtension ?? fallbackExtension;
        if (_callbacks.SaveAsSuggested is { } saveSuggested)
        {
            Bind(saveSuggested)(fileName, extension);
            return;
        }

        Bind(_callbacks.SaveAsFormat)(extension, choice?.SaveFilterIndex ?? 0);
    }

    private Action Bind(Action action) => _binder.Bind(action);

    private Action<string> Bind(Action<string> action) => _binder.Bind(action);

    private Action<string, int> Bind(Action<string, int> action) => _binder.Bind(action);

    private Action<string?, string?> Bind(Action<string?, string?> action) => _binder.Bind(action);

    private string? CurrentDisplayName =>
        _callbacks.GetDisplayName?.Invoke() ?? _callbacks.DisplayName;

    private static IReadOnlyList<BackstageActionGroup> ToActionGroups(
        IReadOnlyList<BackstageSurfaceActionGroup> groups) =>
        groups.Select(group => new BackstageActionGroup(
            group.Heading,
            group.Actions
                .Where(action => action.Invoke is not null)
                .Select(action => new BackstageActionRow(
                    action.Label,
                    action.Description,
                    action.Invoke!)
                {
                    AutomationId = action.AutomationId,
                })
                .ToArray()))
            .ToArray();

    private static string NormalizeDisplayName(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName)
            ? BackstageViewTextResources.UntitledValue
            : displayName;
}

/// <summary>The panes addressable by the FreeW Backstage frame.</summary>
public enum BackstagePane
{
    Home,
    Open,
    SaveAs,
    Print,
    Share,
    Export,
    Info,
    Account,
}
