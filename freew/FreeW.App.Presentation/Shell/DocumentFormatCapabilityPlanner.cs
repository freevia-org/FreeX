using Free.Shared.IO;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Shell;

public static class DocumentFormatCapabilityPlanner
{
    private static readonly IReadOnlyList<string> ExtensionOrder =
    [
        ".docx",
        ".docm",
        ".dotx",
        ".dotm",
        ".xml",
        ".htm",
        ".html",
        ".mht",
        ".mhtml",
        ".odt",
        ".ott",
        ".rtf",
        ".txt",
        ".text",
        ".log",
        ".doc",
        ".dot",
        ".pdf",
        ".xps",
    ];

    public static IReadOnlyList<DocumentFormatCapabilityRow> BuildCapabilities(
        IEnumerable<FileFormatDescriptor> catalogFormats,
        IEnumerable<FileFormatDescriptor>? explicitImportFormats = null,
        IEnumerable<DocumentFixedLayoutExportFormat>? fixedLayoutExportFormats = null)
    {
        ArgumentNullException.ThrowIfNull(catalogFormats);

        return BuildCatalogRows(catalogFormats)
            .Concat(BuildExplicitImportRows(explicitImportFormats ?? []))
            .Concat(BuildFixedLayoutExportRows(fixedLayoutExportFormats ?? []))
            .ToArray();
    }

    public static IReadOnlyList<DocumentFormatCapabilityRow> BuildSaveRows(
        IEnumerable<FileFormatDescriptor> catalogFormats)
    {
        ArgumentNullException.ThrowIfNull(catalogFormats);

        return BuildCatalogRows(catalogFormats)
            .Where(row => row.CanSave)
            .ToArray();
    }

    public static IReadOnlyList<DocumentFormatCapabilityRow> BuildExplicitImportRows(
        IEnumerable<FileFormatDescriptor> importFormats)
    {
        ArgumentNullException.ThrowIfNull(importFormats);

        return GroupRows(importFormats, forceKind: DocumentFormatCapabilityKind.ImportOnly)
            .OrderBy(RowOrder)
            .ThenBy(row => row.FormatName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<DocumentFixedLayoutExportFormat> BuildFixedLayoutExportFormats(bool includeXps) =>
        includeXps
            ? [DocumentFixedLayoutExportFormat.Pdf, DocumentFixedLayoutExportFormat.Xps]
            : [DocumentFixedLayoutExportFormat.Pdf];

    public static IReadOnlyList<DocumentFormatCapabilityRow> BuildFixedLayoutExportRows(
        IEnumerable<DocumentFixedLayoutExportFormat> exportFormats)
    {
        ArgumentNullException.ThrowIfNull(exportFormats);

        return exportFormats
            .Select(format =>
            {
                var extension = DocumentFileFormatResolver.NormalizeExtension(format.Extension);
                return new DocumentFormatCapabilityRow(
                    DocumentFormatCapabilityKind.ExportOnly,
                    DocumentFormatCapabilityFamily.FixedLayout,
                    format.FormatName,
                    [extension],
                    extension,
                    Label(format.FormatName, [extension]),
                    DescribeExportOnly(format.FormatName),
                    CanOpen: false,
                    CanSave: false,
                    CanExport: true,
                    OpensAsTemplate: false,
                    IsLegacy: false,
                    SaveFilterIndex: 0);
            })
            .OrderBy(RowOrder)
            .ThenBy(row => row.FormatName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<DocumentFormatCapabilityRow> BuildCatalogRows(
        IEnumerable<FileFormatDescriptor> catalogFormats) =>
        GroupRows(catalogFormats, forceKind: null)
            .OrderBy(RowOrder)
            .ThenBy(row => row.FormatName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<DocumentFormatCapabilityRow> GroupRows(
        IEnumerable<FileFormatDescriptor> formats,
        DocumentFormatCapabilityKind? forceKind)
    {
        var indexedFormats = BuildIndexedFormats(formats).ToArray();
        foreach (var group in indexedFormats.GroupBy(format => new CapabilityKey(
                     forceKind ?? Classify(format.Format),
                     ClassifyFamily(format.Format),
                     format.Format.FormatName,
                     format.Format.CanOpen,
                     format.Format.CanSave,
                     format.Format.OpensAsTemplate,
                     format.Format.IsLegacy)))
        {
            var ordered = group
                .OrderBy(format => ExtensionRank(format.Format.Extension))
                .ThenBy(format => format.SourceIndex)
                .ToArray();
            if (ordered.Length == 0)
                continue;

            var key = group.Key;
            var extensions = ordered
                .Select(format => format.Format.Extension)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var primaryExtension = extensions[0];
            var saveFilterIndex = ordered
                .FirstOrDefault(format => string.Equals(format.Format.Extension, primaryExtension, StringComparison.OrdinalIgnoreCase))
                ?.SaveFilterIndex ?? 0;

            yield return new DocumentFormatCapabilityRow(
                key.Kind,
                key.Family,
                key.FormatName,
                extensions,
                primaryExtension,
                Label(key.FormatName, extensions),
                Describe(key, extensions),
                key.CanOpen,
                key.CanSave && key.Kind != DocumentFormatCapabilityKind.ImportOnly,
                CanExport: false,
                key.OpensAsTemplate,
                key.IsLegacy,
                saveFilterIndex);
        }
    }

    private static IEnumerable<IndexedFormat> BuildIndexedFormats(IEnumerable<FileFormatDescriptor> formats)
    {
        var sourceIndex = 0;
        var saveFilterIndex = 0;

        foreach (var format in formats)
        {
            var extension = DocumentFileFormatResolver.NormalizeExtension(format.Extension);
            if (extension.Length == 0)
            {
                sourceIndex++;
                continue;
            }

            if (format.CanSave)
                saveFilterIndex++;

            yield return new IndexedFormat(
                format with { Extension = extension },
                sourceIndex,
                format.CanSave ? saveFilterIndex : 0);
            sourceIndex++;
        }
    }

    private static DocumentFormatCapabilityKind Classify(FileFormatDescriptor format)
    {
        if (format.IsLegacy)
            return DocumentFormatCapabilityKind.LegacyCompatibility;
        if (format.OpensAsTemplate)
            return DocumentFormatCapabilityKind.Template;
        if (format.CanOpen && !format.CanSave)
            return DocumentFormatCapabilityKind.ImportOnly;
        if (!format.CanOpen && format.CanSave)
            return DocumentFormatCapabilityKind.SaveOnly;

        return DocumentFormatCapabilityKind.OpenSave;
    }

    private static DocumentFormatCapabilityFamily ClassifyFamily(FileFormatDescriptor format)
    {
        if (format.IsLegacy)
            return DocumentFormatCapabilityFamily.Compatibility;

        var extension = DocumentFileFormatResolver.NormalizeExtension(format.Extension);
        var name = format.FormatName;
        if (extension is ".docx" or ".docm" or ".dotx" or ".dotm" or ".xml")
            return DocumentFormatCapabilityFamily.Word;
        if (name.Contains("Web Page", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("MHTML", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentFormatCapabilityFamily.Web;
        }

        return DocumentFormatCapabilityFamily.Other;
    }

    private static string Label(string formatName, IReadOnlyList<string> extensions) =>
        $"{DisplayName(formatName)} ({string.Join(", ", extensions.Select(extension => "*" + extension))})";

    private static string DisplayName(string formatName) =>
        formatName switch
        {
            "MHTML document" => "Single File Web Page",
            "Plain text" => "Plain Text",
            "Log file" => "Log File",
            _ => formatName,
        };

    private static string Describe(CapabilityKey key, IReadOnlyList<string> extensions)
    {
        if (DescribeWordOoxmlFormat(key, extensions) is { } wordOoxmlDescription)
            return wordOoxmlDescription;

        if (key.Kind == DocumentFormatCapabilityKind.ImportOnly)
        {
            return $"{key.FormatName} is import-only. FreeW can read it into a new document, but does not save back to {ExtensionsText(extensions)}.";
        }

        if (key.Kind == DocumentFormatCapabilityKind.SaveOnly)
        {
            return $"{key.FormatName} is save-only. FreeW can create this file type, but it is not advertised as an editable open format.";
        }

        if (key.Kind == DocumentFormatCapabilityKind.LegacyCompatibility)
        {
            return key.OpensAsTemplate
                ? "Compatibility template format for Word 97-2003. Opening it creates a new unsaved document; saving may simplify unsupported modern features."
                : "Compatibility format for Word 97-2003. FreeW can open and save it, but unsupported modern features may be simplified.";
        }

        if (key.Kind == DocumentFormatCapabilityKind.Template)
            return "Template format. Opening it creates a new unsaved document; Save As writes the reusable template file.";

        return key.FormatName switch
        {
            "Word XML Document" => "Editable Flat OPC Word XML format. FreeW can open and save it through the Word XML adapter.",
            "Word 2003 XML Document" => "Editable Word 2003 XML format. Use for XML compatibility, with fidelity limited to supported document features.",
            "Web Page, Filtered" => "Clean HTML format. Word-specific layout and advanced features may not round-trip.",
            "Web Page" => "HTML with Office-style markup. It remains a web conversion rather than a full Word document round-trip.",
            "MHTML document" => "Single-file web archive. FreeW opens and saves supported HTML content with embedded resources.",
            "OpenDocument Text" => "OpenDocument Text format. Unsupported ODF constructs are skipped instead of implied as fully round-trippable.",
            "Rich Text Format" => "Editable rich text interchange format. Advanced Word features may be simplified to FreeW's model.",
            "Plain text" => "Text-only format. Formatting, images, tables, and document structure are not preserved.",
            "Log file" => "Plain-text log file. Formatting, images, tables, and document structure are not preserved.",
            _ => "Editable format that FreeW can open and save through the document adapter catalog.",
        };
    }

    private static string? DescribeWordOoxmlFormat(CapabilityKey key, IReadOnlyList<string> extensions)
    {
        bool HasExtension(string extension) =>
            extensions.Any(candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));

        if (key.FormatName == "Word Document" && HasExtension(".docx"))
        {
            return "Default editable non-macro Word document format. FreeW can open and save it normally; saving here drops macro parts because VBA project bytes are not written to this .docx target.";
        }

        if (key.FormatName == "Strict Open XML Document" && HasExtension(".docx"))
        {
            return "Strict non-macro Open XML package. FreeW opens and saves it through the OOXML adapter; saving here drops macro parts because VBA project bytes are not written to this .docx target.";
        }

        if (key.FormatName == "Word Macro-Enabled Document" && HasExtension(".docm"))
        {
            return "Macro-enabled OOXML document. FreeW preserves existing VBA project bytes when saving as .docm, but does not inspect or execute macros; saving to non-macro targets such as .docx or .dotx drops macro parts.";
        }

        if (key.FormatName == "Word Template" && HasExtension(".dotx"))
        {
            return "Non-macro OOXML template. Opening it creates a new unsaved document; Save As writes the reusable template file, and saving here drops macro parts because VBA project bytes are not written to this .dotx target.";
        }

        if (key.FormatName == "Word Macro-Enabled Template" && HasExtension(".dotm"))
        {
            return "Macro-enabled OOXML template. Opening it creates a new unsaved document; FreeW preserves existing VBA project bytes when saving as .dotm, but does not inspect or execute macros; saving to non-macro targets such as .docx or .dotx drops macro parts.";
        }

        return null;
    }

    private static string DescribeExportOnly(string formatName) =>
        formatName switch
        {
            "PDF Document" => "Export-only fixed-layout PDF copy. PDF text import is a separate lossy import path, not editable round-trip support.",
            "XPS Document" => "Export-only fixed-layout XPS copy. FreeW creates it for sharing/printing, but does not open it as an editable document.",
            _ => "Export-only fixed-layout copy. FreeW creates this output, but does not open it as an editable document.",
        };

    private static string ExtensionsText(IReadOnlyList<string> extensions) =>
        string.Join("/", extensions);

    private static int RowOrder(DocumentFormatCapabilityRow row) =>
        (FamilyRank(row.Family) * 100) + FormatRank(row.FormatName, row.PrimaryExtension);

    private static int FamilyRank(DocumentFormatCapabilityFamily family) =>
        family switch
        {
            DocumentFormatCapabilityFamily.Word => 0,
            DocumentFormatCapabilityFamily.Web => 1,
            DocumentFormatCapabilityFamily.Other => 2,
            DocumentFormatCapabilityFamily.Compatibility => 3,
            DocumentFormatCapabilityFamily.FixedLayout => 4,
            _ => 9,
        };

    private static int FormatRank(string formatName, string primaryExtension) =>
        formatName switch
        {
            "Word Document" => 0,
            "Strict Open XML Document" => 1,
            "Word Macro-Enabled Document" => 2,
            "Word Template" => 3,
            "Word Macro-Enabled Template" => 4,
            "Word XML Document" => 5,
            "Word 2003 XML Document" => 6,
            "Web Page, Filtered" => 0,
            "Web Page" => 1,
            "MHTML document" => 2,
            "OpenDocument Text" => 0,
            "OpenDocument Text Template" => 1,
            "Rich Text Format" => 2,
            "Plain text" => 3,
            "Log file" => 4,
            "Word 97-2003 Document" => 0,
            "Word 97-2003 Template" => 1,
            "PDF Document" => 0,
            "XPS Document" => 1,
            _ => 50 + ExtensionRank(primaryExtension),
        };

    private static int ExtensionRank(string extension)
    {
        var normalized = DocumentFileFormatResolver.NormalizeExtension(extension);
        var index = ExtensionOrder
            .Select((candidate, rank) => new { candidate, rank })
            .FirstOrDefault(row => string.Equals(row.candidate, normalized, StringComparison.OrdinalIgnoreCase));

        return index?.rank ?? 100;
    }

    private sealed record CapabilityKey(
        DocumentFormatCapabilityKind Kind,
        DocumentFormatCapabilityFamily Family,
        string FormatName,
        bool CanOpen,
        bool CanSave,
        bool OpensAsTemplate,
        bool IsLegacy);

    private sealed record IndexedFormat(FileFormatDescriptor Format, int SourceIndex, int SaveFilterIndex);
}

public enum DocumentFormatCapabilityKind
{
    OpenSave,
    Template,
    LegacyCompatibility,
    ImportOnly,
    SaveOnly,
    ExportOnly,
}

public enum DocumentFormatCapabilityFamily
{
    Word,
    Web,
    Other,
    Compatibility,
    FixedLayout,
}

public sealed record DocumentFormatCapabilityRow(
    DocumentFormatCapabilityKind Kind,
    DocumentFormatCapabilityFamily Family,
    string FormatName,
    IReadOnlyList<string> Extensions,
    string PrimaryExtension,
    string Label,
    string Description,
    bool CanOpen,
    bool CanSave,
    bool CanExport,
    bool OpensAsTemplate,
    bool IsLegacy,
    int SaveFilterIndex);

public sealed record DocumentFixedLayoutExportFormat(string Extension, string FormatName)
{
    public static DocumentFixedLayoutExportFormat Pdf { get; } = new(".pdf", "PDF Document");

    public static DocumentFixedLayoutExportFormat Xps { get; } = new(".xps", "XPS Document");
}
