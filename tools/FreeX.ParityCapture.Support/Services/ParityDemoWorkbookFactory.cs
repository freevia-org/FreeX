using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Builds the deterministic demo workbook used by BOTH shells' <c>--parity-capture</c> modes so the
/// cross-platform visual comparison renders identical CONTENT (only rendering differences — gridlines,
/// fonts, selection chrome — should remain). Previously the WPF capture opened an empty Book1 while the
/// Avalonia capture loaded the rich <see cref="PortPreviewWorkbookFactory"/> demo, producing a false
/// ~4% "regression" on the data-dependent <c>grid.demo</c> surface.
///
/// <para>
/// The single source of truth is the committed CSV at <c>docs/parity/parity-demo.csv</c>, embedded into
/// this assembly as <c>FreeX.Parity.DemoWorkbook.csv</c>. Embedding (rather than resolving a repo-relative
/// path at runtime) guarantees the SAME bytes are available to the WPF host on Windows and the Avalonia
/// app inside the headless Linux Docker container, where the repo tree is not present.
/// </para>
///
/// <para>
/// Parsing is intentionally minimal and culture-invariant: the first row is a header (bold), every field
/// that parses as an invariant <see cref="double"/> becomes a <see cref="NumberValue"/>, everything else is
/// a <see cref="TextValue"/>. No date/currency/percent heuristics are applied, so the produced workbook is
/// byte-for-byte identical regardless of the host's current culture.
/// </para>
/// </summary>
public static class ParityDemoWorkbookFactory
{
    /// <summary>Logical name of the embedded copy of <c>docs/parity/parity-demo.csv</c>.</summary>
    public const string EmbeddedResourceName = "FreeX.Parity.DemoWorkbook.csv";

    /// <summary>Stable workbook + sheet names so the title bar / sheet tab compare identically too.</summary>
    public const string WorkbookName = "Parity Demo";
    public const string SheetName = "Demo";

    /// <summary>
    /// Creates the demo <see cref="Workbook"/> from the embedded CSV. The first sheet is the demo data with
    /// a bold header row; it is the active sheet. Safe to call on any thread; performs no I/O beyond reading
    /// the embedded resource.
    /// </summary>
    public static Workbook Create()
    {
        var workbook = new Workbook(WorkbookName);
        var sheet = workbook.AddSheet(SheetName);
        workbook.ActiveSheetIndex = workbook.SheetCount - 1;

        var headerStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = CellColor.FromArgb(232, 238, 247),
            FontColor = CellColor.FromArgb(25, 31, 40),
        });

        var rows = ReadDemoRows();
        for (var r = 0; r < rows.Count; r++)
        {
            var fields = rows[r];
            var isHeader = r == 0;
            for (var c = 0; c < fields.Count; c++)
            {
                var text = fields[c];
                if (text.Length == 0)
                    continue; // leave genuinely-empty cells blank, matching the CSV

                var value = !isHeader && double.TryParse(
                        text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? (ScalarValue)new NumberValue(number)
                    : new TextValue(text);

                var cell = Cell.FromValue(value);
                if (isHeader)
                    cell.StyleId = headerStyle;

                sheet.SetCell(new CellAddress(sheet.Id, (uint)(r + 1), (uint)(c + 1)), cell);
            }
        }

        // Keep the Page Setup Header/Footer capture deterministic across WPF and Avalonia.
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageMargins = WorksheetPageMargins.Normal;
        sheet.HeaderMargin = 0.3;
        sheet.FooterMargin = 0.3;
        sheet.ScaleToFit = new WorksheetScaleToFit(90, null, null);
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 9, 7));
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.PageHeader = new WorksheetHeaderFooter("", "Page Layout Tour", "");
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &[Page] of &[Pages]", "");

        return workbook;
    }

    /// <summary>The raw demo CSV text (the embedded copy of <c>docs/parity/parity-demo.csv</c>).</summary>
    public static string ReadDemoCsv()
    {
        var assembly = typeof(ParityDemoWorkbookFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded parity demo CSV '{EmbeddedResourceName}' not found in {assembly.GetName().Name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadDemoRows()
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var line in ReadDemoCsv().Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length == 0)
                continue; // skip the trailing newline / blank lines

            rows.Add(line.Split(','));
        }
        return rows;
    }
}
