using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

/// <summary>Portable product-specific values consumed by the WPF and Avalonia application frames.</summary>
public static class FreePApplicationFrameDescriptor
{
    /// <summary>
    /// r280: the header comment FreeP stamps into exported PDFs. The shared writer default named
    /// FreeX, and FreeP took it on every vector export through the Skia portable fallback.
    /// </summary>
    public const string PdfHeaderComment = "FreeP portable PDF";

    private static ApplicationFrameDescriptor Descriptor { get; } =
        ApplicationFrameDescriptor.Create(
            "FreeP",
            FileCommandSession.DefaultUntitledDisplayName);

    public static ApplicationWindowTitleSpec Title => Descriptor.Title;

    public static string ResolveDataFolderLabel() =>
        Descriptor.ResolveDataFolderLabel();

    public static string ResolveDataFolderLabel(IApplicationDataPathProvider pathProvider) =>
        Descriptor.ResolveDataFolderLabel(pathProvider);

    // r169 follow-up: the store-path overloads FreeW already exposed. FreeP's shells were calling the
    // parameterless one, which defaults to %LOCALAPPDATA% while FreeP's options live under %APPDATA%,
    // so the status bar and backstage named a folder the app never writes to.
    public static string ResolveDataFolderLabel(string optionsStorePath) =>
        Descriptor.ResolveDataFolderLabel(optionsStorePath);

    public static string ResolveDataFolderLabel(
        string optionsStorePath,
        IApplicationDataPathProvider fallbackPathProvider) =>
        Descriptor.ResolveDataFolderLabel(optionsStorePath, fallbackPathProvider);
}
