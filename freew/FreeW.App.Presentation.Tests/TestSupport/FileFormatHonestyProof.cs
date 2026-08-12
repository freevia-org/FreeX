namespace FreeW.App.Presentation.Shell;

public static class FileFormatHonestyProof
{
    public static IReadOnlyList<FileFormatHonestyProofRow> BuildDefaultRows(bool includeXpsExport = true)
    {
        var workflow = new DocumentPersistenceWorkflow();
        return BuildRows(workflow.BuildFormatCapabilityRows(includeXpsExport));
    }

    public static IReadOnlyList<FileFormatHonestyProofRow> BuildRows(
        IEnumerable<DocumentFormatCapabilityRow> capabilityRows)
    {
        ArgumentNullException.ThrowIfNull(capabilityRows);

        return capabilityRows
            .SelectMany(BuildRowsForCapability)
            .OrderBy(row => AreaRank(row.Area))
            .ThenBy(row => row.PrimaryExtension, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.FormatName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<FileFormatHonestyProofRow> BuildRowsForCapability(DocumentFormatCapabilityRow row)
    {
        if (IsNativeOoxml(row))
        {
            yield return Create(
                FileFormatHonestyProofArea.NativeOoxml,
                row,
                "Native OOXML formats are advertised as normal Open/Save formats, not compatibility exports.");
        }

        if (IsMacroAwareOoxml(row))
        {
            yield return Create(
                FileFormatHonestyProofArea.MacroPreservation,
                row,
                row.PrimaryExtension is ".docm" or ".dotm"
                    ? "Macro-enabled OOXML targets preserve existing VBA project bytes without inspecting or executing macros."
                    : "Non-macro OOXML targets explicitly warn that preserved VBA project bytes are dropped.");
        }

        if (row.OpensAsTemplate)
        {
            yield return Create(
                FileFormatHonestyProofArea.TemplateSemantics,
                row,
                "Template formats open as a new unsaved document, while Save As writes the reusable template file.");
        }

        if (row.Kind == DocumentFormatCapabilityKind.ImportOnly)
        {
            yield return Create(
                FileFormatHonestyProofArea.ImportOnly,
                row,
                "Import-only formats are not offered as Save targets.");
        }

        if (row.Kind == DocumentFormatCapabilityKind.ExportOnly)
        {
            yield return Create(
                FileFormatHonestyProofArea.ExportOnly,
                row,
                "Export-only formats create sharing/printing copies, not editable round-trip documents.");
        }

        if (IsFeatureLossFormat(row))
        {
            yield return Create(
                FileFormatHonestyProofArea.FeatureLoss,
                row,
                "Compatibility formats carry explicit feature-loss language before save/export.");
        }
    }

    private static FileFormatHonestyProofRow Create(
        FileFormatHonestyProofArea area,
        DocumentFormatCapabilityRow row,
        string userFacingTruth) =>
        new(
            area,
            row.FormatName,
            row.PrimaryExtension,
            row.Label,
            userFacingTruth,
            row.Description,
            row.Kind,
            row.CanOpen,
            row.CanSave,
            row.CanExport,
            row.OpensAsTemplate,
            row.IsLegacy);

    private static bool IsNativeOoxml(DocumentFormatCapabilityRow row) =>
        row.Family == DocumentFormatCapabilityFamily.Word &&
        row.Kind is DocumentFormatCapabilityKind.OpenSave or DocumentFormatCapabilityKind.Template &&
        row.PrimaryExtension is ".docx" or ".docm" or ".dotx" or ".dotm";

    private static bool IsMacroAwareOoxml(DocumentFormatCapabilityRow row) =>
        row.PrimaryExtension is ".docx" or ".docm" or ".dotx" or ".dotm";

    private static bool IsFeatureLossFormat(DocumentFormatCapabilityRow row) =>
        row.PrimaryExtension is ".html" or ".htm" or ".mhtml" or ".mht" or ".odt" or ".ott" or ".rtf" or ".txt" or ".text" or ".log" or ".doc" or ".dot" ||
        row.FormatName.Equals("Word 2003 XML Document", StringComparison.OrdinalIgnoreCase) ||
        row.Kind == DocumentFormatCapabilityKind.LegacyCompatibility;

    private static int AreaRank(FileFormatHonestyProofArea area) =>
        area switch
        {
            FileFormatHonestyProofArea.NativeOoxml => 0,
            FileFormatHonestyProofArea.MacroPreservation => 1,
            FileFormatHonestyProofArea.TemplateSemantics => 2,
            FileFormatHonestyProofArea.ImportOnly => 3,
            FileFormatHonestyProofArea.ExportOnly => 4,
            FileFormatHonestyProofArea.FeatureLoss => 5,
            _ => 99,
        };
}

public enum FileFormatHonestyProofArea
{
    NativeOoxml,
    MacroPreservation,
    TemplateSemantics,
    ImportOnly,
    ExportOnly,
    FeatureLoss,
}

public sealed record FileFormatHonestyProofRow(
    FileFormatHonestyProofArea Area,
    string FormatName,
    string PrimaryExtension,
    string Label,
    string UserFacingTruth,
    string Evidence,
    DocumentFormatCapabilityKind CapabilityKind,
    bool CanOpen,
    bool CanSave,
    bool CanExport,
    bool OpensAsTemplate,
    bool IsLegacy);
