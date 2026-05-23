namespace Freexcel.Core.IO;

/// <summary>
/// Placeholder for file I/O adapters.
/// Phase 1 uses a simple JSON-based native format.
/// Phase 2 adds XLSX via ClosedXML behind these interfaces.
/// </summary>
public interface IFileAdapter
{
    /// <summary>File extension this adapter handles (e.g. ".xlsx", ".csv").</summary>
    string Extension { get; }

    /// <summary>Human-readable format name for file dialogs.</summary>
    string FormatName { get; }

    /// <summary>File formats this adapter can open and/or save.</summary>
    IReadOnlyList<FileFormatDescriptor> Formats =>
    [
        new FileFormatDescriptor(Extension, FormatName)
    ];

    /// <summary>Loads a workbook from the given stream.</summary>
    Freexcel.Core.Model.Workbook Load(System.IO.Stream stream);

    /// <summary>Saves a workbook to the given stream.</summary>
    void Save(Freexcel.Core.Model.Workbook workbook, System.IO.Stream stream);
}
