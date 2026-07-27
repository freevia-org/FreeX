namespace FreeP.App.Compositor.Tests;

public sealed class PresentationMediaFileTypeCatalogTests
{
    [Fact]
    public void AudioFilePatterns_KeepWpfAndAvaloniaPickerRoutesEquivalent()
    {
        PresentationMediaFileTypeCatalog.AudioFilePatterns.Should().Equal(
            "*.mp3",
            "*.m4a",
            "*.wav",
            "*.wma",
            "*.aac",
            "*.ogg",
            "*.flac");

        PresentationMediaFileTypeCatalog.BuildWpfAudioFilter()
            .Should().Contain("*.mp3;*.m4a;*.wav;*.wma;*.aac;*.ogg;*.flac");
    }

    [Fact]
    public void AudioMimeTypes_CoverTheCanonicalAudioPatterns()
    {
        PresentationMediaFileTypeCatalog.AudioMimeTypes.Should().ContainInOrder(
            "audio/mpeg",
            "audio/mp4",
            "audio/wav",
            "audio/x-ms-wma",
            "audio/aac",
            "audio/ogg",
            "audio/flac");
    }
}
