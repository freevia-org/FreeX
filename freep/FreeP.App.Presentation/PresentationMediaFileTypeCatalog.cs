namespace FreeP.App.Compositor;

/// <summary>
/// Canonical media file types shared by the WPF and Avalonia presentation hosts.
/// Keeping the host picker filters together prevents a presentation created on one
/// platform from becoming unreachable through the equivalent command on another.
/// </summary>
public static class PresentationMediaFileTypeCatalog
{
    public static IReadOnlyList<string> AudioFilePatterns { get; } =
    [
        "*.mp3",
        "*.m4a",
        "*.wav",
        "*.wma",
        "*.aac",
        "*.ogg",
        "*.flac",
    ];

    public static IReadOnlyList<string> AudioMimeTypes { get; } =
    [
        "audio/mpeg",
        "audio/mp4",
        "audio/wav",
        "audio/x-ms-wma",
        "audio/aac",
        "audio/ogg",
        "audio/flac",
        "audio/x-flac",
    ];

    public static string BuildWpfAudioFilter() =>
        $"{PresentationFileTextResources.AudioFileTypeName}|{string.Join(';', AudioFilePatterns)}|All files|*.*";
}
