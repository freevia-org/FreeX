using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Backstage;

public static class BackstagePaneSurfacePlanner
{
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
        Action<string> saveAsExtension)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(saveAs);
        ArgumentNullException.ThrowIfNull(saveAsExtension);

        var formatList = formats.ToArray();
        var groups = new List<BackstageActionGroup>
        {
            new("Places",
            [
                new("This PC", "Save to local folders and connected drives.", saveAs),
                new("Browse", "Open the Windows save dialog.", saveAs),
            ]),
        };
        groups.AddRange(BackstageSaveAsFileTypePlanner.Build(formatList, saveAsExtension));

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
        BackstageExportPaneSurfaceText? text = null)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(exportPdf);
        ArgumentNullException.ThrowIfNull(saveAsExtension);

        text ??= BackstageExportPaneSurfaceText.FreeW;

        var fixedLayoutRows = new List<BackstageActionRow>
        {
            new(
                exportXps is null ? text.PdfOnlyActionLabel : text.PdfActionLabel,
                text.PdfActionDescription,
                exportPdf),
        };

        if (exportXps is not null &&
            !string.IsNullOrWhiteSpace(text.XpsActionLabel) &&
            !string.IsNullOrWhiteSpace(text.XpsActionDescription))
        {
            fixedLayoutRows.Add(new BackstageActionRow(
                text.XpsActionLabel,
                text.XpsActionDescription,
                exportXps));
        }

        return new BackstageActionPaneSurfaceSpec(
            text.Title,
            text.Description,
            [
                new(text.FixedLayoutGroupHeading, fixedLayoutRows),
                BackstageExportFileTypePlanner.BuildChangeFileTypeGroup(formats.ToArray(), saveAsExtension),
            ]);
    }
}

public sealed record BackstageActionPaneSurfaceSpec(
    string Title,
    string Description,
    IReadOnlyList<BackstageActionGroup> Groups);

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

        if (getText is not null)
        {
            var resolved = getText(descriptor.ResourceKey);
            if (IsResolvedText(descriptor, resolved))
                return resolved!;
        }

        return descriptor.FallbackText;
    }

    private static bool IsResolvedText(ResourceTextDescriptor descriptor, string? value) =>
        !string.IsNullOrEmpty(value) &&
        !string.Equals(value, descriptor.ResourceKey, StringComparison.Ordinal) &&
        !string.Equals(value, "[[" + descriptor.ResourceKey + "]]", StringComparison.Ordinal);
}
