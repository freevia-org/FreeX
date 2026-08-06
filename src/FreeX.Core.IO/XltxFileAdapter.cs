using System.IO.Compression;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// "XLTX Template" save adapter. There is no separate template engine: the workbook is written through
/// the standard <see cref="XlsxFileAdapter"/> and the saved package's workbook content-type is then
/// flipped from the worksheet type
/// (<c>application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml</c>) to the template
/// type (<c>…template.main+xml</c>) — the only structural difference between an .xlsx and an .xltx.
/// Loading reuses the .xlsx load pipeline (a template opens as an ordinary workbook).
/// </summary>
public sealed class XltxFileAdapter : IFileAdapter, IWarningCollectingFileAdapter
{
    private const string WorkbookPartName = "/xl/workbook.xml";
    private const string TemplateMainContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml";

    private readonly XlsxFileAdapter _xlsx = new();

    public string Extension => ".xltx";
    public string FormatName => "XLTX Template";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".xltx", "XLTX Template", CanOpen: true, CanSave: true, OpensAsTemplate: true)
    ];

    public Workbook Load(Stream stream) => _xlsx.Load(stream);

    public void Save(Workbook workbook, Stream stream) =>
        SaveCore(workbook, stream, warnings: null);

    // R123-io-xlsm-save-warnings: warnings-collecting counterpart to Save, reached by
    // WorkbookSaveService via IWarningCollectingFileAdapter so a comment/hyperlink/merged-region/
    // named-range/data-validation item that fails to serialize during an .xltx save is reported to
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
        using var package = new MemoryStream();
        if (warnings is null)
            _xlsx.Save(workbook, package);
        else
            warnings.AddRange(_xlsx.SaveWithWarnings(workbook, package).Warnings);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, WorkbookPartName, TemplateMainContentType);
        }

        package.Position = 0;
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);
        package.CopyTo(stream);
    }
}
