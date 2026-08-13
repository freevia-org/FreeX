using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

/// <summary>Portable product-specific values consumed by the WPF and Avalonia application frames.</summary>
public static class FreePApplicationFrameDescriptor
{
    private static ApplicationFrameDescriptor Descriptor { get; } =
        ApplicationFrameDescriptor.Create(
            "FreeP",
            FileCommandSession.DefaultUntitledDisplayName);

    public static ApplicationWindowTitleSpec Title => Descriptor.Title;

    public static string ResolveDataFolderLabel() =>
        Descriptor.ResolveDataFolderLabel();

    public static string ResolveDataFolderLabel(IApplicationDataPathProvider pathProvider) =>
        Descriptor.ResolveDataFolderLabel(pathProvider);
}
