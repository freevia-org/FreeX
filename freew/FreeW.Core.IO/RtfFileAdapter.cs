using System.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Rich Text Format (<c>.rtf</c>) adapter — a stateless wrapper over the native
/// <see cref="RtfReader"/>/<see cref="RtfWriter"/>, mapping the modelled subset of RTF to/from
/// <see cref="TextDocument"/>.
/// </summary>
public sealed class RtfFileAdapter : IDocumentFileAdapter
{
    public string Extension => ".rtf";
    public string FormatName => "Rich Text Format";

    public TextDocument Load(Stream stream) => RtfReader.Read(stream);

    public void Save(TextDocument document, Stream stream) => RtfWriter.Write(document, stream);
}
