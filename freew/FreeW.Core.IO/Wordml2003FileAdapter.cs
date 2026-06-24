using System.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Catalog-ready adapter for the Word 2003 WordprocessingML single-file XML format
/// (<c>&lt;w:wordDocument&gt;</c> root, extension <c>.xml</c>). Both opens and saves.
///
/// <para>
/// Load delegates to the existing <see cref="Wordml2003Reader"/> (and so honours the same sniff/dispatch
/// that <see cref="WordXmlFileAdapter"/> uses). Save delegates to the new <see cref="Wordml2003Writer"/>,
/// which is the exact inverse of the reader. The existing <see cref="WordXmlFileAdapter"/> (Flat OPC,
/// also <c>.xml</c>) is <em>not</em> changed — the two adapters are registered under the same extension
/// but with distinct <see cref="FileFormatDescriptor.FormatName"/>s so the file-dialog and
/// format-resolver can distinguish them.
/// </para>
///
/// <para>
/// Obtain an instance via the <see cref="Wordml2003()"/> factory (mirrors the <c>DocxFileAdapter</c>
/// pattern) rather than calling the constructor directly — the factory name is the stable integration
/// point and may gain options in the future.
/// </para>
/// </summary>
public sealed class Wordml2003FileAdapter : IDocumentFileAdapter
{
    /// <summary>Returns an adapter that reads and writes Word 2003 WordprocessingML (<c>.xml</c>).</summary>
    public static Wordml2003FileAdapter Wordml2003() => new();

    /// <inheritdoc/>
    public string Extension => ".xml";

    /// <inheritdoc/>
    public string FormatName => "Word 2003 XML Document";

    /// <inheritdoc/>
    public IReadOnlyList<FileFormatDescriptor> Formats =>
        [new FileFormatDescriptor(Extension, FormatName, CanOpen: true, CanSave: true)];

    /// <summary>
    /// Reads a Word 2003 WordML document from <paramref name="stream"/> using
    /// <see cref="Wordml2003Reader"/>. The stream must be positioned at the start.
    /// </summary>
    public TextDocument Load(Stream stream) => Wordml2003Reader.Read(stream);

    /// <summary>
    /// Writes <paramref name="document"/> to <paramref name="stream"/> as Word 2003 WordML using
    /// <see cref="Wordml2003Writer"/>. The stream is not disposed.
    /// </summary>
    public void Save(TextDocument document, Stream stream) =>
        Wordml2003Writer.Write(document, stream);
}
