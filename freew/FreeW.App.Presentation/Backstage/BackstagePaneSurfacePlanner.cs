using Free.Shared.AppServices;
using Free.Shared.Localization;
using Free.Shared.Shell;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Backstage;

public static class BackstagePaneSurfacePlanner
{
    public static BackstageOpenPaneVisualMetrics OpenPaneVisualMetrics { get; } =
        new(
            HeadingBottomMargin: new(0, 0, 0, 18),
            DescriptionBottomMargin: new(0, 0, 0, 16),
            SearchWidth: 520,
            SearchMinWidth: 360,
            SearchHeight: 30,
            SearchMargin: new(0, 0, 0, 12),
            SearchPadding: new(8, 3, 8, 3),
            TabsWidth: 640,
            TabsMargin: new(0, 0, 0, 14),
            ActionFontSize: 13,
            DescriptionFontSize: 11,
            ActionRowMargin: new(0, 0, 0, 10),
            DescriptionMargin: new(0, 2, 0, 0));

    private const string OpenSearchAutomationName = "Search recent documents";
    private const string OpenDocumentsTabLabel = "Documents";
    private const string OpenFoldersTabLabel = "Folders";
    private const string OpenEmptyDocumentsText = "No recent documents match this search.";
    private const string OpenEmptyFoldersText = "No recent folders match this search.";
    private const string OpenPlacesHeading = "Places";
    private const string OpenRecoveryHeading = "Recovery";
    private const string SaveAsFileNameHeading = "File name";
    private const string SaveAsTypeHeading = "Save as type";
    private const string SaveAsButtonLabel = "Save";

    public static BackstagePrintPaneSurfaceSpec BuildPrintPane(
        string displayName,
        PageSettings page,
        Action? print,
        Action? printPreview,
        BackstageDirectPrintCapability? directPrintCapability = null)
    {
        var effectiveDirectPrintCapability = directPrintCapability
            ?? (print is null
                ? BackstageDirectPrintCapability.Deferred()
                : BackstageDirectPrintCapability.NativeDialogAvailable(
                    "Direct native print is backed by the current host callback."));
        var plan = BackstagePrintPanePlanner.Build(displayName, page, effectiveDirectPrintCapability);

        return new BackstagePrintPaneSurfaceSpec(
            BackstageViewTextResources.Print.Title,
            plan.Description,
            plan.Fields,
            plan.Groups.Select(group => new BackstageSurfaceActionGroup(
                group.Heading,
                group.Actions.Select(action => new BackstageSurfaceActionRow(
                    action.Label,
                    action.Description,
                    "PrintAction_" + action.Kind,
                    ResolvePrintAction(action.Kind, print, printPreview))).ToArray())).ToArray(),
            plan.Evidence,
            effectiveDirectPrintCapability.DeferredNote);
    }

    public static BackstageInfoPaneSurfaceSpec BuildInfoPane(
        IEnumerable<BackstageFieldRow> documentFields,
        Action? markAsFinal,
        Action? restrictEditing,
        Action? inspectDocument,
        Action? checkAccessibility,
        TextDocument? document = null)
    {
        ArgumentNullException.ThrowIfNull(documentFields);

        return new BackstageInfoPaneSurfaceSpec(
            BackstageViewTextResources.Info.Title,
            BackstageViewTextResources.Info.Description,
            documentFields.ToArray(),
            BackstageInfoSafetyPanePlanner.Build(document)
                .Select(group => new BackstageSurfaceActionGroup(
                    group.Heading,
                    group.Actions.Select(action => new BackstageSurfaceActionRow(
                        action.Label,
                        action.Description,
                        "InfoAction_" + action.Kind,
                        ResolveInfoAction(
                            action.Kind,
                            markAsFinal,
                            restrictEditing,
                            inspectDocument,
                            checkAccessibility))).ToArray()))
                .ToArray());
    }

    public static BackstageAccountPaneSurfaceSpec BuildAccountPane(
        SisterBackstageAccountPaneContext context,
        Action? openOptions)
    {
        var plan = SisterBackstageAccountPanePlanner.Build(context);

        return new BackstageAccountPaneSurfaceSpec(
            plan.Heading,
            plan.Description,
            plan.Groups,
            new BackstageSurfaceActionRow(
                plan.OptionsText,
                string.Empty,
                "AccountOptionsButton",
                openOptions));
    }

    public static BackstageActionPaneSurfaceSpec BuildHomePane(
        IEnumerable<RecentFileEntry> recentEntries,
        Action newDocument,
        Action<string> openRecent,
        Action browse,
        Action openMore)
    {
        return new BackstageActionPaneSurfaceSpec(
            BackstageViewTextResources.Home.Title,
            BackstageViewTextResources.Home.Description,
            BackstageHomePanePlanner.Build(recentEntries, newDocument, openRecent, browse, openMore));
    }

    public static BackstageActionPaneSurfaceSpec BuildOpenActionPane(
        IEnumerable<RecentFileEntry> recentEntries,
        Action<string> openRecent,
        Action browse,
        Action recoverUnsaved)
    {
        return new BackstageActionPaneSurfaceSpec(
            BackstageViewTextResources.Open.Title,
            BackstageViewTextResources.Open.Description,
            BackstageOpenPanePlanner.Build(recentEntries, openRecent, browse, recoverUnsaved));
    }

    public static BackstageOpenPaneSurfaceSpec BuildOpenPane(
        IEnumerable<RecentFileEntry> recentEntries,
        string? filter,
        Action<string> openRecent,
        Action<string> openFolder,
        Action browse,
        Action recoverUnsaved)
    {
        return new BackstageOpenPaneSurfaceSpec(
            BackstageViewTextResources.Open.Title,
            BackstageViewTextResources.Open.Description,
            new BackstageOpenPaneSearchSurface(OpenSearchAutomationName),
            new BackstageOpenPaneTabSurface(
                OpenDocumentsTabLabel,
                OpenFoldersTabLabel,
                OpenEmptyDocumentsText,
                OpenEmptyFoldersText,
                OpenPlacesHeading,
                OpenRecoveryHeading),
            BackstageOpenPanePlanner.BuildPlan(
                recentEntries,
                filter,
                openRecent,
                openFolder,
                browse,
                recoverUnsaved));
    }

    public static BackstageSaveAsPaneSurfaceSpec BuildSaveAsPane(
        IEnumerable<FileFormatDescriptor> formats,
        string displayName,
        string? currentPath,
        Action saveAs,
        Action<string> saveAsExtension) =>
        BuildSaveAsPane(
            formats,
            displayName,
            currentPath,
            saveAs,
            (extension, _) => saveAsExtension(extension));

    public static BackstageSaveAsPaneSurfaceSpec BuildSaveAsPane(
        IEnumerable<FileFormatDescriptor> formats,
        string displayName,
        string? currentPath,
        Action saveAs,
        Action<string, int> saveAsFormat)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(saveAs);
        ArgumentNullException.ThrowIfNull(saveAsFormat);

        var formatList = formats.ToArray();
        var groups = new List<BackstageActionGroup>
        {
            new("Places",
            [
                new("This PC", "Save to local folders and connected drives.", saveAs),
                new("Browse", "Open the Windows save dialog.", saveAs),
            ]),
        };
        groups.AddRange(BackstageSaveAsFileTypePlanner.Build(formatList, saveAsFormat));

        return new BackstageSaveAsPaneSurfaceSpec(
            BackstageViewTextResources.SaveAs.Title,
            BackstageViewTextResources.SaveAs.Description,
            new BackstageSaveAsInlineSurface(
                SaveAsFileNameHeading,
                SaveAsTypeHeading,
                SaveAsButtonLabel),
            BackstageSaveAsFileTypePlanner.BuildInlinePlan(formatList, displayName, currentPath),
            groups);
    }

    public static BackstageActionPaneSurfaceSpec BuildSharePane(
        string? currentPath,
        Func<string, bool> fileExists,
        Action saveAs,
        Action<string> openContainingFolder,
        Action saveCopy,
        Action exportPdf)
    {
        return new BackstageActionPaneSurfaceSpec(
            BackstageViewTextResources.Share.Title,
            BackstageViewTextResources.Share.Description,
            BackstageSharePanePlanner.Build(
                currentPath,
                fileExists,
                saveAs,
                openContainingFolder,
                saveCopy,
                exportPdf));
    }

    public static BackstageActionPaneSurfaceSpec BuildExportPane(
        IEnumerable<FileFormatDescriptor> formats,
        Action exportPdf,
        Action? exportXps,
        Action<string> saveAsExtension,
        BackstageExportPaneSurfaceText? text = null) =>
        BuildExportPane(
            formats,
            exportPdf,
            exportXps,
            (extension, _) => saveAsExtension(extension),
            text);

    public static BackstageActionPaneSurfaceSpec BuildExportPane(
        IEnumerable<FileFormatDescriptor> formats,
        Action exportPdf,
        Action? exportXps,
        Action<string, int> saveAsFormat,
        BackstageExportPaneSurfaceText? text = null)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(exportPdf);
        ArgumentNullException.ThrowIfNull(saveAsFormat);

        text ??= BackstageExportPaneSurfaceText.FreeW;
        var fixedLayoutCapabilities = DocumentFormatCapabilityPlanner
            .BuildFixedLayoutExportRows(DocumentFormatCapabilityPlanner.BuildFixedLayoutExportFormats(exportXps is not null));
        var fixedLayoutRows = BackstageExportPanePlanner.BuildFixedLayoutActions(
            fixedLayoutCapabilities,
            exportPdf,
            exportXps,
            text);

        return new BackstageActionPaneSurfaceSpec(
            text.Title,
            text.Description,
            [
                new(text.FixedLayoutGroupHeading, fixedLayoutRows),
                BackstageExportFileTypePlanner.BuildChangeFileTypeGroup(formats.ToArray(), saveAsFormat),
            ]);
    }

    private static Action? ResolvePrintAction(
        BackstagePrintActionKind kind,
        Action? print,
        Action? printPreview) =>
        kind switch
        {
            BackstagePrintActionKind.Print => print,
            BackstagePrintActionKind.PrintPreview => printPreview,
            _ => null
        };

    private static Action? ResolveInfoAction(
        BackstageInfoSafetyActionKind kind,
        Action? markAsFinal,
        Action? restrictEditing,
        Action? inspectDocument,
        Action? checkAccessibility) =>
        kind switch
        {
            BackstageInfoSafetyActionKind.MarkAsFinal => markAsFinal,
            BackstageInfoSafetyActionKind.RestrictEditing => restrictEditing,
            BackstageInfoSafetyActionKind.InspectDocument => inspectDocument,
            BackstageInfoSafetyActionKind.CheckAccessibility => checkAccessibility,
            _ => null
        };
}

public sealed record BackstageActionPaneSurfaceSpec(
    string Title,
    string Description,
    IReadOnlyList<BackstageActionGroup> Groups);

public sealed record BackstagePrintPaneSurfaceSpec(
    string Title,
    string Description,
    IReadOnlyList<BackstageFieldRow> Fields,
    IReadOnlyList<BackstageSurfaceActionGroup> Groups,
    IReadOnlyList<BackstagePrintEvidenceRow> Evidence,
    string? DeferredNote);

public sealed record BackstageInfoPaneSurfaceSpec(
    string Title,
    string Description,
    IReadOnlyList<BackstageFieldRow> DocumentFields,
    IReadOnlyList<BackstageSurfaceActionGroup> SafetyGroups);

public sealed record BackstageAccountPaneSurfaceSpec(
    string Title,
    string Description,
    IReadOnlyList<SisterBackstageAccountFieldGroup> Groups,
    BackstageSurfaceActionRow OptionsAction);

public sealed record BackstageSurfaceActionGroup(
    string Heading,
    IReadOnlyList<BackstageSurfaceActionRow> Actions);

public sealed record BackstageSurfaceActionRow(
    string Label,
    string Description,
    string AutomationId,
    Action? Invoke)
{
    public bool IsEnabled => Invoke is not null;
}

public sealed record BackstageOpenPaneSurfaceSpec(
    string Title,
    string Description,
    BackstageOpenPaneSearchSurface Search,
    BackstageOpenPaneTabSurface Tabs,
    BackstageOpenPanePlan Plan);

public sealed record BackstageOpenPaneSearchSurface(string AutomationName);

public sealed record BackstageOpenPaneTabSurface(
    string DocumentsTabLabel,
    string FoldersTabLabel,
    string EmptyDocumentsText,
    string EmptyFoldersText,
    string PlacesHeading,
    string RecoveryHeading);

public readonly record struct BackstageOpenPaneVisualMetrics(
    BackstageThickness HeadingBottomMargin,
    BackstageThickness DescriptionBottomMargin,
    double SearchWidth,
    double SearchMinWidth,
    double SearchHeight,
    BackstageThickness SearchMargin,
    BackstageThickness SearchPadding,
    double TabsWidth,
    BackstageThickness TabsMargin,
    double ActionFontSize,
    double DescriptionFontSize,
    BackstageThickness ActionRowMargin,
    BackstageThickness DescriptionMargin);

public readonly record struct BackstageThickness(double Left, double Top, double Right, double Bottom)
{
    public BackstageThickness(double horizontal, double vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }
}

public sealed record BackstageSaveAsPaneSurfaceSpec(
    string Title,
    string Description,
    BackstageSaveAsInlineSurface Inline,
    BackstageSaveAsInlinePlan InlinePlan,
    IReadOnlyList<BackstageActionGroup> Groups);

public sealed record BackstageSaveAsInlineSurface(
    string FileNameHeading,
    string SaveAsTypeHeading,
    string SaveButtonLabel);

public sealed record BackstageExportPaneSurfaceText(
    string Title,
    string Description,
    string FixedLayoutGroupHeading,
    string PdfActionLabel,
    string PdfActionDescription,
    string? XpsActionLabel = null,
    string? XpsActionDescription = null,
    string PdfOnlyActionLabel = BackstageViewTextResources.CreatePdfLabel)
{
    public static BackstageExportPaneSurfaceText FreeW { get; } =
        FromDescriptor(SisterBackstagePaneTextDescriptorPlanner.Build(SisterBackstageAppKind.FreeW).Export);

    public static BackstageExportPaneSurfaceText FromDescriptor(
        SisterBackstageExportPaneTextDescriptor descriptor,
        Func<string, string?>? getText = null,
        string pdfOnlyActionLabel = BackstageViewTextResources.CreatePdfLabel)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return new BackstageExportPaneSurfaceText(
            Resolve(descriptor.Heading, getText),
            Resolve(descriptor.Description, getText),
            Resolve(descriptor.FixedLayoutGroupHeading, getText),
            Resolve(descriptor.PdfActionLabel, getText),
            Resolve(descriptor.PdfActionDescription, getText),
            descriptor.XpsActionLabel is null ? null : Resolve(descriptor.XpsActionLabel, getText),
            descriptor.XpsActionDescription is null ? null : Resolve(descriptor.XpsActionDescription, getText),
            pdfOnlyActionLabel);
    }

    private static string Resolve(ResourceTextDescriptor descriptor, Func<string, string?>? getText)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return LocalizedFallbackTextResolver.Resolve(
            descriptor.ResourceKey,
            descriptor.FallbackText,
            getText);
    }
}
