using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// "XLTM Macro-Enabled Template" save adapter. There is no separate template or macro engine: the
/// workbook is written through the standard <see cref="XlsxFileAdapter"/> and the saved package's
/// workbook content-type is then flipped from the worksheet type
/// (<c>application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml</c>) to the
/// macro-enabled template type (<c>application/vnd.ms-excel.template.macroEnabled.main+xml</c>) —
/// the only structural difference between an .xlsx and an .xltm (besides the optional
/// vbaProject.bin part).
/// Loading reuses the .xlsx load pipeline (a template opens as an ordinary workbook).
/// If the workbook was opened from an .xltm/.xlsm that carried a <c>xl/vbaProject.bin</c>, the
/// source-package preservation layer in <see cref="XlsxFileAdapter"/> carries the part through
/// automatically on round-trip via <see cref="XlsxFileAdapter.SavePreservingVbaProject"/> — this
/// adapter does not need to handle the part itself specially, only request preservation.
/// </summary>
public sealed class XltmFileAdapter : IFileAdapter, IWarningCollectingFileAdapter
{
    private const string MacroEnabledTemplateContentType =
        "application/vnd.ms-excel.template.macroEnabled.main+xml";

    private readonly XlsxFileAdapter _xlsx = new();

    public string Extension => ".xltm";
    public string FormatName => "XLTM Macro-Enabled Template";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".xltm", "XLTM Macro-Enabled Template", CanOpen: true, CanSave: true, OpensAsTemplate: true)
    ];

    public Workbook Load(Stream stream) => _xlsx.Load(stream);

    public void Save(Workbook workbook, Stream stream) =>
        XlsxDerivedFormatSaveWorkflow.Save(
            _xlsx,
            workbook,
            stream,
            MacroEnabledTemplateContentType,
            preserveVbaProject: true,
            collectWarnings: false);

    // R123-io-xlsm-save-warnings: warnings-collecting counterpart to Save, reached by
    // WorkbookSaveService via IWarningCollectingFileAdapter so a comment/hyperlink/merged-region/
    // named-range/data-validation item that fails to serialize during an .xltm save is reported to
    // the user exactly as it already is for a plain .xlsx save, instead of being silently dropped.
    public XlsxSaveResult SaveWithWarnings(Workbook workbook, Stream stream) =>
        XlsxDerivedFormatSaveWorkflow.Save(
            _xlsx,
            workbook,
            stream,
            MacroEnabledTemplateContentType,
            preserveVbaProject: true,
            collectWarnings: true);
}
