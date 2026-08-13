using Free.Shared.AppServices;
using Free.Shared.Opc;

namespace FreeP.App.Host;

/// <summary>Owns the temporary file used by WPF transition-sound playback.</summary>
internal static class TransitionSoundTempFile
{
    internal static TemporaryFileLease Write(byte[] bytes, string? contentType)
    {
        var extension = ContentTypeToExtension(contentType);
        var temporaryFile = TemporaryFileLease.Create("freep_transition_", extension);
        temporaryFile.WriteAllBytes(bytes);
        return temporaryFile;
    }

    internal static string ContentTypeToExtension(string? contentType) =>
        OpcMediaTypes.GetMediaFileExtension(
            contentType,
            OpcMediaExtensionProfile.TransitionSound,
            includeDot: true);
}
