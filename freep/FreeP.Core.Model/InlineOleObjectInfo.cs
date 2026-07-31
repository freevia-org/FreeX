namespace FreeP.Core.Model;

/// <summary>
/// Payload for an embedded object carried by an inline rich-text run.
/// The run text is the object-replacement character (U+FFFC), which keeps caret and
/// selection offsets stable while the object bytes remain available for paste/fallback.
/// </summary>
public sealed class InlineOleObjectInfo
{
    public byte[] EmbeddedBytes { get; set; } = Array.Empty<byte>();

    public string FileName { get; set; } = "Embedded.bin";

    public string? ClassName { get; set; }
}
