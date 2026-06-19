using System.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// The native WordprocessingML adapter. A stateless wrapper over the existing static
/// <see cref="DocxReader"/>/<see cref="DocxWriter"/>, so the mature <c>.docx</c> engine is reused unchanged.
/// (M2 extends this class to the macro/template variants <c>.docm</c>/<c>.dotx</c>/<c>.dotm</c> as data.)
/// </summary>
public sealed class DocxFileAdapter : IDocumentFileAdapter
{
    public string Extension => ".docx";
    public string FormatName => "Word Document";

    public TextDocument Load(Stream stream) => DocxReader.Read(stream);

    public void Save(TextDocument document, Stream stream) => DocxWriter.Write(document, stream);
}
