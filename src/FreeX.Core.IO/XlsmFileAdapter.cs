using System.IO.Compression;
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
    private const string WorkbookPartName = "/xl/workbook.xml";
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
        SaveCore(workbook, stream, warnings: null);

    // R123-io-xlsm-save-warnings: warnings-collecting counterpart to Save, reached by
    // WorkbookSaveService via IWarningCollectingFileAdapter so a comment/hyperlink/merged-region/
    // named-range/data-validation item that fails to serialize during an .xlsm save is reported to
    // the user exactly as it already is for a plain .xlsx save, instead of being silently dropped.
    public XlsxSaveResult SaveWithWarnings(Workbook workbook, Stream stream)
    {
        var warnings = new List<string>();
        SaveCore(workbook, stream, warnings);
        return warnings.Count == 0 ? XlsxSaveResult.Clean : new XlsxSaveResult(warnings.AsReadOnly());
    }

    private void SaveCore(Workbook workbook, Stream stream, List<string>? warnings)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(stream);

        // Build the .xlsx package in memory first; ClosedXML always writes the worksheet content-type,
        // and the source-copy/patch paths preserve whatever the loaded package carried — so flipping the
        // content-type as a uniform post-process on the finished bytes covers every save path.
        // R70-io-vba-6-1: use the VBA-preserving entry point -- this format IS macro-enabled, so a
        // loaded workbook's xl/vbaProject.bin must survive the save (unlike a plain .xlsx/.xltx save,
        // which must drop it).
        using var package = new MemoryStream();
        if (warnings is null)
            _xlsx.SavePreservingVbaProject(workbook, package);
        else
            warnings.AddRange(_xlsx.SaveWithWarningsPreservingVbaProject(workbook, package).Warnings);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, WorkbookPartName, MacroEnabledMainContentType);
        }

        package.Position = 0;
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);
        package.CopyTo(stream);
    }
}
