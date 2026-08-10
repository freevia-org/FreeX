using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

public sealed record FreePApplicationFrameTitleSpec(
    string ApplicationName,
    string Separator,
    string DirtyMarker,
    WindowTitleApplicationPlacement ApplicationPlacement)
{
    public ApplicationWindowTitleSpec ToApplicationWindowTitleSpec() => new(
        ApplicationName,
        FileCommandSession.DefaultUntitledDisplayName,
        DirtyMarker,
        Separator,
        ApplicationPlacement);
}

/// <summary>Portable product-specific values consumed by the WPF and Avalonia application frames.</summary>
public static class FreePApplicationFrameDescriptor
{
    public static FreePApplicationFrameTitleSpec Title { get; } = new(
        "FreeP",
        " \u2014 ",
        " *",
        WindowTitleApplicationPlacement.DocumentThenApplication);

    public static string ResolveDataFolderLabel() =>
        ResolveDataFolderLabel(PlatformApplicationDataPathProvider.LocalInstance);

    public static string ResolveDataFolderLabel(IApplicationDataPathProvider pathProvider) =>
        AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(pathProvider);
}
