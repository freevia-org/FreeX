using System.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// A document file-format adapter: loads a <see cref="TextDocument"/> from a stream and/or saves one to a
/// stream. The data-driven seam that lets FreeW support many formats without per-format branching in the
/// file-command layer — adding a format is "write one adapter + add one catalog line + add one registration
/// test tuple". This is the WordprocessingML/<see cref="TextDocument"/> analogue of the sibling FreeX app's
/// workbook adapter interface; FreeW and FreeX share no code, only this pattern.
///
/// <para>
/// Adapters must be stateless (a fresh list is created per file-command host), so a single instance can be
/// reused across loads/saves without races. Implementations that handle more than one extension — or a
/// read-only / template variant — override <see cref="Formats"/>; single-format adapters inherit the
/// default one-descriptor implementation.
/// </para>
/// </summary>
public interface IDocumentFileAdapter
{
    /// <summary>Primary extension this adapter handles (e.g. <c>.docx</c>).</summary>
    string Extension { get; }

    /// <summary>Human-readable format name for the file dialog.</summary>
    string FormatName { get; }

    /// <summary>
    /// The formats this adapter can open and/or save. Defaults to a single open+save descriptor built from
    /// <see cref="Extension"/>/<see cref="FormatName"/>. Multi-extension, read-only, or template adapters
    /// override this — if they do not, their extra extensions are invisible to the resolver and dialog.
    /// </summary>
    IReadOnlyList<FileFormatDescriptor> Formats =>
        [new FileFormatDescriptor(Extension, FormatName)];

    /// <summary>Loads a document from the given stream. The stream is not disposed by the adapter.</summary>
    TextDocument Load(Stream stream);

    /// <summary>Saves the document to the given stream. The stream is not disposed by the adapter.</summary>
    void Save(TextDocument document, Stream stream);
}
