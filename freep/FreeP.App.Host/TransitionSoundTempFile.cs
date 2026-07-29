using System.IO;

namespace FreeP.App.Host;

/// <summary>Owns the temporary file used by WPF transition-sound playback.</summary>
internal static class TransitionSoundTempFile
{
    internal static string Write(byte[] bytes, string? contentType)
    {
        var extension = ContentTypeToExtension(contentType);
        var path = Path.Combine(Path.GetTempPath(), $"freep_transition_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    internal static void Delete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort */ }
    }

    internal static string ContentTypeToExtension(string? contentType) =>
        contentType?.ToLowerInvariant() switch
        {
            "audio/mpeg" or "audio/mp3" => ".mp3",
            "audio/wav" or "audio/x-wav" => ".wav",
            "audio/ogg" => ".ogg",
            "audio/aac" => ".aac",
            "audio/x-ms-wma" => ".wma",
            "audio/flac" or "audio/x-flac" => ".flac",
            "audio/mp4" or "audio/m4a" or "audio/x-m4a" => ".m4a",
            _ => ".mp3"
        };
}
