namespace FreeP.App.Compositor;

public sealed record PresentationAssetPickerFileTypeProfile(
    string DisplayName,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<string> MimeTypes)
{
    public string BuildWpfFilter() =>
        $"{DisplayName}|{string.Join(';', Patterns)}|All files|*.*";
}

public sealed record PresentationAssetPickerProfile(
    PresentationAssetPickerFileTypeProfile Wpf,
    PresentationAssetPickerFileTypeProfile Avalonia,
    bool UseUnownedWpfDialog = false);

/// <summary>
/// Owns the native picker profiles for every presentation asset-import route. The renderer ports
/// only translate these profiles into WPF or Avalonia picker objects.
/// </summary>
public static class PresentationAssetPickerProfileCatalog
{
    private static readonly PresentationAssetPickerFileTypeProfile AvaloniaPicture = new(
        PresentationFileTextResources.PictureFileTypeName,
        ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.svg", "*.wmf", "*.emf"],
        ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/svg+xml", "image/x-wmf", "image/x-emf"]);

    private static readonly PresentationAssetPickerFileTypeProfile AvaloniaVideo = new(
        PresentationFileTextResources.VideoFileTypeName,
        ["*.mp4", "*.mov", "*.avi", "*.wmv", "*.m4v"],
        ["video/mp4", "video/quicktime", "video/x-msvideo", "video/x-ms-wmv", "video/x-m4v"]);

    private static readonly PresentationAssetPickerFileTypeProfile AvaloniaAudio = new(
        PresentationFileTextResources.AudioFileTypeName,
        PresentationMediaFileTypeCatalog.AudioFilePatterns,
        PresentationMediaFileTypeCatalog.AudioMimeTypes);

    private static readonly PresentationAssetPickerFileTypeProfile AvaloniaEmbeddedObject = new(
        OleInsertionPlanner.PickerTitle,
        ["*.xlsx", "*.xlsm", "*.xls", "*.docx", "*.doc", "*.pptx", "*.ppt"],
        [
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.ms-excel.sheet.macroEnabled.12",
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "application/vnd.ms-powerpoint",
        ]);

    public static PresentationAssetPickerProfile For(PresentationAssetImportKind kind) => kind switch
    {
        PresentationAssetImportKind.Picture => new(
            Wpf("Image files", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.svg", "*.wmf", "*.emf"),
            AvaloniaPicture,
            UseUnownedWpfDialog: true),
        PresentationAssetImportKind.Video => new(
            Wpf(PresentationFileTextResources.VideoFileTypeName, "*.mp4", "*.mov", "*.avi", "*.wmv", "*.m4v"),
            AvaloniaVideo,
            UseUnownedWpfDialog: true),
        PresentationAssetImportKind.Audio => new(
            Wpf(PresentationFileTextResources.AudioFileTypeName, "*.mp3", "*.m4a", "*.wav", "*.wma"),
            AvaloniaAudio,
            UseUnownedWpfDialog: true),
        PresentationAssetImportKind.EmbeddedObject => new(
            Wpf("Office files", "*.xlsx", "*.xlsm", "*.xls", "*.docx", "*.doc", "*.pptx", "*.ppt"),
            AvaloniaEmbeddedObject),
        PresentationAssetImportKind.TransitionSound => new(
            new PresentationAssetPickerFileTypeProfile(
                PresentationFileTextResources.AudioFileTypeName,
                PresentationMediaFileTypeCatalog.AudioFilePatterns,
                []),
            AvaloniaAudio),
        PresentationAssetImportKind.PictureBullet => new(
            Wpf("Image files", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.svg"),
            AvaloniaPicture,
            UseUnownedWpfDialog: true),
        PresentationAssetImportKind.SmartArtPicture => new(
            Wpf("Picture files", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.svg", "*.bmp"),
            AvaloniaPicture),
        PresentationAssetImportKind.ZoomCoverImage => new(
            Wpf("Picture files", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.svg", "*.webp"),
            AvaloniaPicture),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static PresentationAssetPickerFileTypeProfile Wpf(
        string displayName,
        params string[] patterns) =>
        new(displayName, patterns, []);
}
