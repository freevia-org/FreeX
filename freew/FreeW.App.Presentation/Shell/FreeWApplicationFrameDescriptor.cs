using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Shell;

/// <summary>Portable product-specific values consumed by the WPF and Avalonia application frames.</summary>
public static class FreeWApplicationFrameDescriptor
{
    public static string ResolveDataFolderLabel() =>
        ResolveDataFolderLabel(PlatformApplicationDataPathProvider.LocalInstance);

    public static string ResolveDataFolderLabel(IApplicationDataPathProvider pathProvider) =>
        AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback(pathProvider);

    public static string ResolveDataFolderLabel(string optionsStorePath) =>
        ResolveDataFolderLabel(optionsStorePath, PlatformApplicationDataPathProvider.LocalInstance);

    public static string ResolveDataFolderLabel(
        string optionsStorePath,
        IApplicationDataPathProvider fallbackPathProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsStorePath);
        ArgumentNullException.ThrowIfNull(fallbackPathProvider);

        try
        {
            return Path.GetDirectoryName(optionsStorePath) ?? optionsStorePath;
        }
        catch
        {
            return ResolveDataFolderLabel(fallbackPathProvider);
        }
    }
}
