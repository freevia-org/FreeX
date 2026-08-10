using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Shell;

/// <summary>Portable product-specific values consumed by the WPF and Avalonia application frames.</summary>
public static class FreeWApplicationFrameDescriptor
{
    public static ApplicationWindowTitleSpec Title { get; } = new(
        ApplicationName: "FreeW",
        DefaultDocumentDisplayName: FileCommandSession.DefaultUntitledDisplayName,
        DirtyMarker: " *",
        Separator: " \u2014 ",
        ApplicationPlacement: WindowTitleApplicationPlacement.DocumentThenApplication);

    public static string ResolveDataFolderLabel() =>
        ResolveDataFolderLabel(PlatformApplicationDataPathProvider.LocalInstance);

    public static string ResolveDataFolderLabel(IApplicationDataPathProvider pathProvider) =>
        AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(pathProvider);

    public static string ResolveDataFolderLabel(string optionsStorePath) =>
        ResolveDataFolderLabel(optionsStorePath, PlatformApplicationDataPathProvider.LocalInstance);

    public static string ResolveDataFolderLabel(
        string optionsStorePath,
        IApplicationDataPathProvider fallbackPathProvider) =>
        AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(
            fallbackPathProvider,
            optionsStorePath);
}
