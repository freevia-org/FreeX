namespace Free.Shared.IO;

/// <summary>
/// One openable/savable document format exposed by a file adapter. Pure data, so adding a format
/// (or a read-only / template variant) is a catalog edit rather than control-flow surgery.
/// Shared between FreeX workbook adapters and FreeW document adapters.
/// </summary>
/// <param name="Extension">The file extension, e.g. <c>.docx</c> (leading dot optional; normalized on use).</param>
/// <param name="FormatName">Human-readable name shown in the open/save dialog filter.</param>
/// <param name="CanOpen">Whether the format can be opened (read).</param>
/// <param name="CanSave">Whether the format can be saved (written). Read-only formats set this false.</param>
/// <param name="OpensAsTemplate">
/// Whether opening this format seeds a new untitled document rather than editing the file in place
/// (templates: <c>.dotx</c>/<c>.dotm</c>). The single observable effect is that the current file path is
/// cleared after load, so the next Save becomes Save-As.
/// </param>
public sealed record FileFormatDescriptor(
    string Extension,
    string FormatName,
    bool CanOpen = true,
    bool CanSave = true,
    bool OpensAsTemplate = false);
