namespace Free.Shared.AppServices;

/// <summary>
/// Renderer-neutral application-frame conventions shared by product presentation layers.
/// Native hosts retain window construction and binding while this descriptor owns title and
/// application-data label policy.
/// </summary>
public sealed class ApplicationFrameDescriptor
{
    public ApplicationFrameDescriptor(ApplicationWindowTitleSpec title)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title;
    }

    public ApplicationWindowTitleSpec Title { get; }

    public static ApplicationFrameDescriptor Create(
        string applicationName,
        string defaultDocumentDisplayName,
        string dirtyMarker = " *",
        string separator = " \u2014 ",
        WindowTitleApplicationPlacement applicationPlacement =
            WindowTitleApplicationPlacement.DocumentThenApplication,
        bool collapseCleanDefaultDocumentTitle = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultDocumentDisplayName);
        ArgumentNullException.ThrowIfNull(dirtyMarker);
        ArgumentNullException.ThrowIfNull(separator);

        return new ApplicationFrameDescriptor(new ApplicationWindowTitleSpec(
            applicationName,
            defaultDocumentDisplayName,
            dirtyMarker,
            separator,
            applicationPlacement,
            collapseCleanDefaultDocumentTitle));
    }

    public string ResolveDataFolderLabel() =>
        ResolveDataFolderLabel(PlatformApplicationDataPathProvider.LocalInstance);

    public string ResolveDataFolderLabel(IApplicationDataPathProvider pathProvider) =>
        AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(pathProvider);

    public string ResolveDataFolderLabel(string optionsStorePath) =>
        ResolveDataFolderLabel(optionsStorePath, PlatformApplicationDataPathProvider.LocalInstance);

    public string ResolveDataFolderLabel(
        string optionsStorePath,
        IApplicationDataPathProvider fallbackPathProvider) =>
        AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(
            fallbackPathProvider,
            optionsStorePath);
}
