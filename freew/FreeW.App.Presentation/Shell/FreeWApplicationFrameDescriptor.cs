using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Shell;

/// <summary>Portable product-specific values consumed by the WPF and Avalonia application frames.</summary>
public static class FreeWApplicationFrameDescriptor
{
    private static ApplicationFrameDescriptor Descriptor { get; } =
        ApplicationFrameDescriptor.Create(
            "FreeW",
            FileCommandSession.DefaultUntitledDisplayName);

    public static ApplicationWindowTitleSpec Title => Descriptor.Title;

    public static string ResolveDataFolderLabel() =>
        Descriptor.ResolveDataFolderLabel();

    public static string ResolveDataFolderLabel(IApplicationDataPathProvider pathProvider) =>
        Descriptor.ResolveDataFolderLabel(pathProvider);

    public static string ResolveDataFolderLabel(string optionsStorePath) =>
        Descriptor.ResolveDataFolderLabel(optionsStorePath);

    public static string ResolveDataFolderLabel(
        string optionsStorePath,
        IApplicationDataPathProvider fallbackPathProvider) =>
        Descriptor.ResolveDataFolderLabel(optionsStorePath, fallbackPathProvider);
}
