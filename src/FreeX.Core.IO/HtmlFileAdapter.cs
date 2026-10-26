using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// HTML (.html/.htm) file adapter — READ + WRITE, matching Excel which both opens and saves HTML.
///
/// <para><b>Import</b> parses the first (or every) <c>&lt;table&gt;</c> into a cell grid: each
/// <c>&lt;tr&gt;</c> is a row, each <c>&lt;td&gt;</c>/<c>&lt;th&gt;</c> a cell. Cell text is decoded
/// (entities + tag stripping) and coerced to a typed value (number/bool/error) when it looks numeric,
/// otherwise kept as text. <c>colspan</c>/<c>rowspan</c> become merged regions, with intervening
/// columns left blank so later cells keep their grid position. The inline CSS that <b>Export</b> emits
/// (font weight/style/underline, family/size/color, background fill, text-align, per-edge borders) is
/// parsed back into the cell's <see cref="CellStyle"/> so an xlsx→html→xlsx round-trip preserves the
/// styling HTML can carry.</para>
///
/// <para><b>Export</b> writes a single styled <c>&lt;table&gt;</c> of the first sheet's used range. Each
/// cell emits its display value (numbers/dates rendered like the delimited-text writer); a compact set
/// of visual attributes — bold/italic/underline, font family/size/color, background fill, text-align,
/// and per-edge borders — is mapped to inline CSS. Merged regions emit <c>colspan</c>/<c>rowspan</c>.</para>
///
/// Single table ⇒ single sheet. Formulas, number-format strings, multi-sheet structure, charts, etc.
/// are not representable and are dropped (the format ceiling, not a bug).
/// </summary>
public sealed class HtmlFileAdapter : IFileAdapter, ISingleSheetFileAdapter
{
    public string Extension => ".html";
    public string FormatName => "Web Page (HTML)";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".html", "Web Page (HTML)", CanOpen: true, CanSave: true),
        new FileFormatDescriptor(".htm", "Web Page (HTM)", CanOpen: true, CanSave: true),
    ];

    public Workbook Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var html = HtmlText.ReadAll(stream);
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        HtmlTableReader.Populate(html, workbook, sheet);
        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(stream);
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);
        HtmlTableWriter.Write(workbook, stream);
    }
}
