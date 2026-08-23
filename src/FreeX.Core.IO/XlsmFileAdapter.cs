using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// "XLSM Macro-Enabled Workbook" save adapter. There is no separate macro engine: the workbook is
/// written through the standard <see cref="XlsxFileAdapter"/> and the saved package's workbook
/// content-type is then flipped from the worksheet type
/// (<c>application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml</c>) to the
/// macro-enabled type (<c>application/vnd.ms-excel.sheet.macroEnabled.main+xml</c>) — the only
/// structural difference between an .xlsx and an .xlsm (besides the optional vbaProject.bin part).
/// Loading reuses the .xlsx load pipeline.
/// If the workbook was opened from a .xlsm that carried a <c>xl/vbaProject.bin</c>, the source-
/// package preservation layer in <see cref="XlsxFileAdapter"/> carries the part through
/// automatically on round-trip via <see cref="XlsxFileAdapter.SavePreservingVbaProject"/> — this
/// adapter does not need to handle the part itself specially, only request preservation.
/// </summary>
public sealed class XlsmFileAdapter : IFileAdapter, IWarningCollectingFileAdapter
{
    private const string MacroEnabledMainContentType =
        "application/vnd.ms-excel.sheet.macroEnabled.main+xml";

    private readonly XlsxFileAdapter _xlsx = new();

    public string Extension => ".xlsm";
    public string FormatName => "XLSM Macro-Enabled Workbook";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream) => _xlsx.Load(stream);

    public void Save(Workbook workbook, Stream stream) =>
        XlsxDerivedFormatSaveWorkflow.Save(
            _xlsx,
            workbook,
            stream,
            MacroEnabledMainContentType,
            preserveVbaProject: true,
            collectWarnings: false);

    // R123-io-xlsm-save-warnings: warnings-collecting counterpart to Save, reached by
    // WorkbookSaveService via IWarningCollectingFileAdapter so a comment/hyperlink/merged-region/
    // named-range/data-validation item that fails to serialize during an .xlsm save is reported to
    // the user exactly as it already is for a plain .xlsx save, instead of being silently dropped.
    public XlsxSaveResult SaveWithWarnings(Workbook workbook, Stream stream) =>
        XlsxDerivedFormatSaveWorkflow.Save(
            _xlsx,
            workbook,
            stream,
            MacroEnabledMainContentType,
            preserveVbaProject: true,
            collectWarnings: true);
}
