using System.IO;
using Free.Shared.Opc;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// The native WordprocessingML adapter, covering the OOXML package family: <c>.docx</c>, the macro-enabled
/// <c>.docm</c>, and the templates <c>.dotx</c>/<c>.dotm</c>. All four share one document body — only the
/// package framing (the <c>document.xml</c> content type and whether macro parts are kept) differs — so they
/// are pure data over the existing static <see cref="DocxReader"/>/<see cref="DocxWriter"/> engine. Reading is
/// variant-agnostic (it keys on <c>word/document.xml</c>); writing selects the right
/// <see cref="DocxWriteOptions"/> per variant. One instance is registered per extension.
/// </summary>
public sealed class DocxFileAdapter : IDocumentFileAdapter
{
    private readonly DocxWriteOptions _writeOptions;
    private readonly bool _opensAsTemplate;
    private readonly bool _strictMode;

    public string Extension { get; }
    public string FormatName { get; }

    private DocxFileAdapter(string extension, string formatName, DocxWriteOptions writeOptions, bool opensAsTemplate, bool strictMode = false)
    {
        Extension = extension;
        FormatName = formatName;
        _writeOptions = writeOptions;
        _opensAsTemplate = opensAsTemplate;
        _strictMode = strictMode;
    }

    /// <summary>The plain <c>.docx</c> Word Document (default).</summary>
    public DocxFileAdapter() : this(".docx", "Word Document", DocxWriteOptions.Docx, opensAsTemplate: false) { }

    public static DocxFileAdapter Docx() => new(".docx", "Word Document", DocxWriteOptions.Docx, opensAsTemplate: false);
    public static DocxFileAdapter Docm() => new(".docm", "Word Macro-Enabled Document", DocxWriteOptions.Docm, opensAsTemplate: false);
    public static DocxFileAdapter Dotx() => new(".dotx", "Word Template", DocxWriteOptions.Dotx, opensAsTemplate: true);
    public static DocxFileAdapter Dotm() => new(".dotm", "Word Macro-Enabled Template", DocxWriteOptions.Dotm, opensAsTemplate: true);

    /// <summary>
    /// ISO/IEC 29500 Strict Open XML Document adapter.  Load auto-detects whether the supplied stream
    /// is Strict or Transitional and hands the (rewritten) package to the existing
    /// <see cref="DocxReader"/> engine.  Save emits a Strict OOXML package by writing a transitional
    /// package first (via the existing <see cref="DocxWriter"/> engine) then rewriting all namespace
    /// URIs to their Strict equivalents.
    /// </summary>
    public static DocxFileAdapter Strict() =>
        new(".docx", "Strict Open XML Document", DocxWriteOptions.Docx, opensAsTemplate: false, strictMode: true);

    public IReadOnlyList<FileFormatDescriptor> Formats =>
        [new FileFormatDescriptor(Extension, FormatName, CanOpen: true, CanSave: true, OpensAsTemplate: _opensAsTemplate)];

    public TextDocument Load(Stream stream)
    {
        // The strict branch below decompresses the package twice -- IsStrict opens it to read
        // word/document.xml's namespace, then RewriteStrictToTransitional expands every part to
        // rewrite its namespaces -- and both run BEFORE DocxReader, which is where the zip-bomb guard
        // lives. A guard that only runs after the expansion it is meant to prevent protects nothing:
        // measured, an archive this guard REJECTS (WorkbookTooLargeException) was rewritten by the
        // strict path without complaint. Checked here, ahead of both, so the strict and transitional
        // routes are bounded by the same rule. DocxReader checks again on its own stream, which is
        // cheap (the central directory only) and keeps that path safe for its other callers.
        WorkbookOpenSizeGuard.EnsureArchiveWithinLimits(stream);

        if (_strictMode && StrictOoxmlTransform.IsStrict(stream))
        {
            // Rewrite strict → transitional in-memory, then feed to the transitional engine.
            using var transitional = StrictOoxmlTransform.RewriteStrictToTransitional(stream);
            return DocxReader.Read(transitional);
        }
        return DocxReader.Read(stream);
    }

    public void Save(TextDocument document, Stream stream)
    {
        if (_strictMode)
        {
            // Write transitional into a temporary buffer, then rewrite → strict.
            using var transitionalMs = new System.IO.MemoryStream();
            DocxWriter.Write(document, transitionalMs, _writeOptions);
            transitionalMs.Position = 0;
            using var strictMs = StrictOoxmlTransform.RewriteTransitionalToStrict(transitionalMs);
            strictMs.CopyTo(stream);
            return;
        }
        DocxWriter.Write(document, stream, _writeOptions);
    }
}
