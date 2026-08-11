using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

/// <summary>Portable product-specific values consumed by the WPF and Avalonia application frames.</summary>
public static class FreePApplicationFrameDescriptor
{
    public static ApplicationWindowTitleSpec Title { get; } = new(
        ApplicationName: "FreeP",
        DefaultDocumentDisplayName: FileCommandSession.DefaultUntitledDisplayName,
        DirtyMarker: " *",
        Separator: " \u2014 ",
        ApplicationPlacement: WindowTitleApplicationPlacement.DocumentThenApplication);

    public static string ResolveDataFolderLabel() =>
        ResolveDataFolderLabel(PlatformApplicationDataPathProvider.LocalInstance);

    public static string ResolveDataFolderLabel(IApplicationDataPathProvider pathProvider) =>
        AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(pathProvider);
}
