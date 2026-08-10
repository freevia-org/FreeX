using System.IO;
using Free.Shared.Opc;

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
        OpcMediaTypes.GetMediaFileExtension(
            contentType,
            OpcMediaExtensionProfile.TransitionSound,
            includeDot: true);
}
