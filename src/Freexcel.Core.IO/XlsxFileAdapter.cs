using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

/// <summary>
/// XLSX file adapter using ClosedXML.
/// Supports standard Excel .xlsx files.
/// </summary>
public sealed class XlsxFileAdapter : IFileAdapter
{
    public string Extension => ".xlsx";
    public string FormatName => "Excel Workbook";

    public Workbook Load(Stream stream)
    {
        using var packageStream = new MemoryStream();
        stream.CopyTo(packageStream);

        packageStream.Position = 0;
        var sheetXmlLayout = LoadSheetXmlLayout(packageStream);

        packageStream.Position = 0;
        using var xlWorkbook = new XLWorkbook(packageStream);
        var workbook = new Workbook("Untitled");
        workbook.CalculationMode = xlWorkbook.CalculateMode == XLCalculateMode.Manual
            ? WorkbookCalculationMode.Manual
            : WorkbookCalculationMode.Automatic;

        foreach (var xlSheet in xlWorkbook.Worksheets)
        {
            var sheet = workbook.AddSheet(xlSheet.Name);
            sheet.IsHidden = xlSheet.Visibility != XLWorksheetVisibility.Visible;
            if (xlSheet.TabColor.HasValue)
            {
                var color = xlSheet.TabColor.Color;
                sheet.TabColor = new CellColor(color.R, color.G, color.B);
            }

            foreach (var xlCell in xlSheet.CellsUsed())
            {
                var addr = new CellAddress(sheet.Id, (uint)xlCell.Address.RowNumber, (uint)xlCell.Address.ColumnNumber);

                Cell cell;
                if (xlCell.HasFormula)
                {
                    cell = Cell.FromFormula(xlCell.FormulaA1);
                    // Preserve the cached formula result so callers see the last-calculated value
                    // without needing to recalculate immediately.
                    var cached = MapValue(xlCell.Value);
                    if (cached is not BlankValue)
                        cell.Value = cached;
                }
                else
                {
                    cell = Cell.FromValue(MapValue(xlCell.Value));
                }

                var style = MapStyle(xlCell.Style);
                if (!style.Equals(CellStyle.Default))
                    cell.StyleId = workbook.RegisterStyle(style);

                sheet.SetCell(addr, cell);
            }

            foreach (var xlCell in xlSheet.CellsUsed(XLCellsUsedOptions.All))
            {
                try
                {
                    var comment = xlCell.GetComment();
                    if (comment.Length == 0) continue;

                    var addr = new CellAddress(sheet.Id, (uint)xlCell.Address.RowNumber, (uint)xlCell.Address.ColumnNumber);
                    sheet.Comments[addr] = comment.Text;
                }
                catch
                {
                    // Skip cells without comments or comments ClosedXML cannot expose.
                }
            }

            foreach (var hyperlink in xlSheet.Hyperlinks)
            {
                try
                {
                    var cell = hyperlink.Cell;
                    if (cell is null) continue;

                    var target = hyperlink.ExternalAddress?.ToString() ??
                                 hyperlink.InternalAddress ??
                                 string.Empty;
                    if (string.IsNullOrEmpty(target)) continue;

                    var addr = new CellAddress(sheet.Id, (uint)cell.Address.RowNumber, (uint)cell.Address.ColumnNumber);
                    sheet.Hyperlinks[addr] = target;
                }
                catch
                {
                    // Skip hyperlinks ClosedXML cannot expose.
                }
            }

            foreach (var row in xlSheet.RowsUsed(XLCellsUsedOptions.AllFormats))
            {
                var rowNumber = (uint)row.RowNumber();
                if (row.Height > 0)
                    sheet.RowHeights[rowNumber] = row.Height * (96.0 / 72.0);
                if (row.IsHidden)
                    sheet.HiddenRows.Add(rowNumber);
            }

            foreach (var col in xlSheet.ColumnsUsed(XLCellsUsedOptions.AllFormats))
            {
                var colNumber = (uint)col.ColumnNumber();
                if (col.Width > 0)
                    sheet.ColumnWidths[colNumber] = col.Width;
                if (col.IsHidden)
                    sheet.HiddenCols.Add(colNumber);
            }

            if (sheetXmlLayout.TryGetValue(xlSheet.Name, out var layout))
            {
                sheet.HiddenRows.UnionWith(layout.HiddenRows);
                sheet.HiddenCols.UnionWith(layout.HiddenCols);
                sheet.IsProtected = layout.IsProtected;
                sheet.ProtectionPassword = layout.ProtectionPasswordHash;

                foreach (var (rowNum, level) in layout.RowOutlineLevels)
                    sheet.RowOutlineLevels[rowNum] = level;
                foreach (var (colNum, level) in layout.ColOutlineLevels)
                    sheet.ColOutlineLevels[colNum] = level;
                sheet.GroupHiddenRows.UnionWith(layout.GroupHiddenRows);
                sheet.GroupHiddenCols.UnionWith(layout.GroupHiddenCols);
            }

            if (layout?.PaneState is "frozen" or "frozenSplit")
            {
                sheet.FrozenRows = layout.PaneRowSplit ?? 0;
                sheet.FrozenCols = layout.PaneColumnSplit ?? 0;
            }
            else
            {
                var splitRow = xlSheet.SheetView.SplitRow > 0
                    ? (uint)xlSheet.SheetView.SplitRow
                    : layout?.PaneRowSplit;
                var splitColumn = xlSheet.SheetView.SplitColumn > 0
                    ? (uint)xlSheet.SheetView.SplitColumn
                    : layout?.PaneColumnSplit;
                if (splitRow > 0)
                    sheet.SplitRow = splitRow;
                if (splitColumn > 0)
                    sheet.SplitColumn = splitColumn;
            }

            try { LoadPrintArea(xlSheet, sheet); }
            catch { /* ignore print-area load failures */ }

            sheet.PageOrientation = xlSheet.PageSetup.PageOrientation == XLPageOrientation.Landscape
                ? WorksheetPageOrientation.Landscape
                : WorksheetPageOrientation.Portrait;
            sheet.PaperSize = xlSheet.PageSetup.PaperSize switch
            {
                XLPaperSize.LetterPaper => WorksheetPaperSize.Letter,
                XLPaperSize.LegalPaper => WorksheetPaperSize.Legal,
                _ => WorksheetPaperSize.A4
            };
            sheet.PageMargins = new WorksheetPageMargins(
                xlSheet.PageSetup.Margins.Left,
                xlSheet.PageSetup.Margins.Right,
                xlSheet.PageSetup.Margins.Top,
                xlSheet.PageSetup.Margins.Bottom);
            sheet.HeaderMargin = xlSheet.PageSetup.Margins.Header;
            sheet.FooterMargin = xlSheet.PageSetup.Margins.Footer;
            sheet.PrintGridlines = xlSheet.PageSetup.ShowGridlines;
            sheet.PrintHeadings = xlSheet.PageSetup.ShowRowAndColumnHeadings;
            sheet.CenterHorizontallyOnPage = xlSheet.PageSetup.CenterHorizontally;
            sheet.CenterVerticallyOnPage = xlSheet.PageSetup.CenterVertically;
            sheet.PageOrder = xlSheet.PageSetup.PageOrder == XLPageOrderValues.OverThenDown
                ? WorksheetPageOrder.OverThenDown
                : WorksheetPageOrder.DownThenOver;
            sheet.FirstPageNumber = xlSheet.PageSetup.FirstPageNumber == 0
                ? null
                : xlSheet.PageSetup.FirstPageNumber;
            sheet.PrintBlackAndWhite = xlSheet.PageSetup.BlackAndWhite;
            sheet.PrintDraftQuality = xlSheet.PageSetup.DraftQuality;
            sheet.PrintQualityDpi = xlSheet.PageSetup.HorizontalDpi > 0
                ? xlSheet.PageSetup.HorizontalDpi
                : xlSheet.PageSetup.VerticalDpi > 0 ? xlSheet.PageSetup.VerticalDpi : null;
            sheet.PrintErrorValue = FromXlsxPrintErrorValue(xlSheet.PageSetup.PrintErrorValue);
            sheet.PrintComments = FromXlsxPrintComments(xlSheet.PageSetup.ShowComments);
            sheet.DifferentFirstPageHeaderFooter = xlSheet.PageSetup.DifferentFirstPageOnHF;
            sheet.DifferentOddEvenHeaderFooter = xlSheet.PageSetup.DifferentOddEvenPagesOnHF;
            sheet.HeaderFooterScaleWithDocument = xlSheet.PageSetup.ScaleHFWithDocument;
            sheet.HeaderFooterAlignWithMargins = xlSheet.PageSetup.AlignHFWithMargins;
            sheet.PageHeader = new WorksheetHeaderFooter(
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Header.Left, XLHFOccurrence.OddPages, XLHFOccurrence.AllPages)),
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Header.Center, XLHFOccurrence.OddPages, XLHFOccurrence.AllPages)),
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Header.Right, XLHFOccurrence.OddPages, XLHFOccurrence.AllPages)));
            sheet.PageFooter = new WorksheetHeaderFooter(
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Footer.Left, XLHFOccurrence.OddPages, XLHFOccurrence.AllPages)),
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Footer.Center, XLHFOccurrence.OddPages, XLHFOccurrence.AllPages)),
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Footer.Right, XLHFOccurrence.OddPages, XLHFOccurrence.AllPages)));
            sheet.FirstPageHeader = new WorksheetHeaderFooter(
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Header.Left, XLHFOccurrence.FirstPage)),
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Header.Center, XLHFOccurrence.FirstPage)),
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Header.Right, XLHFOccurrence.FirstPage)));
            sheet.FirstPageFooter = new WorksheetHeaderFooter(
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Footer.Left, XLHFOccurrence.FirstPage)),
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Footer.Center, XLHFOccurrence.FirstPage)),
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Footer.Right, XLHFOccurrence.FirstPage)));
            sheet.EvenPageHeader = new WorksheetHeaderFooter(
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Header.Left, XLHFOccurrence.EvenPages)),
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Header.Center, XLHFOccurrence.EvenPages)),
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Header.Right, XLHFOccurrence.EvenPages)));
            sheet.EvenPageFooter = new WorksheetHeaderFooter(
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Footer.Left, XLHFOccurrence.EvenPages)),
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Footer.Center, XLHFOccurrence.EvenPages)),
                FromXlsxHeaderFooterText(GetXlsxHeaderFooterText(xlSheet.PageSetup.Footer.Right, XLHFOccurrence.EvenPages)));
            if (xlSheet.PageSetup.FirstRowToRepeatAtTop > 0 && xlSheet.PageSetup.LastRowToRepeatAtTop > 0)
            {
                sheet.PrintTitleRows = new WorksheetRepeatRange(
                    (uint)xlSheet.PageSetup.FirstRowToRepeatAtTop,
                    (uint)xlSheet.PageSetup.LastRowToRepeatAtTop);
            }
            if (xlSheet.PageSetup.FirstColumnToRepeatAtLeft > 0 && xlSheet.PageSetup.LastColumnToRepeatAtLeft > 0)
            {
                sheet.PrintTitleColumns = new WorksheetRepeatRange(
                    (uint)xlSheet.PageSetup.FirstColumnToRepeatAtLeft,
                    (uint)xlSheet.PageSetup.LastColumnToRepeatAtLeft);
            }
            foreach (var rowBreak in xlSheet.PageSetup.RowBreaks)
                if (rowBreak > 0) sheet.RowPageBreaks.Add((uint)rowBreak);
            foreach (var columnBreak in xlSheet.PageSetup.ColumnBreaks)
                if (columnBreak > 0) sheet.ColumnPageBreaks.Add((uint)columnBreak);
            sheet.ScaleToFit = xlSheet.PageSetup.PagesWide > 0 || xlSheet.PageSetup.PagesTall > 0
                ? new WorksheetScaleToFit(null,
                    xlSheet.PageSetup.PagesWide > 0 ? xlSheet.PageSetup.PagesWide : null,
                    xlSheet.PageSetup.PagesTall > 0 ? xlSheet.PageSetup.PagesTall : null)
                : new WorksheetScaleToFit(xlSheet.PageSetup.Scale, null, null);

            // Load CellIs conditional format rules (best-effort; skip anything we can't map)
            try { LoadConditionalFormats(xlSheet, sheet, workbook); }
            catch { /* ignore CF load failures */ }

            // Load data validation rules (best-effort)
            try { LoadDataValidations(xlSheet, sheet); }
            catch { /* ignore DV load failures */ }

            // Load merged regions (best-effort)
            try { LoadMergedRegions(xlSheet, sheet); }
            catch { /* ignore merge load failures */ }
        }

        // Load named ranges (best-effort; skip any we cannot map)
        try { LoadNamedRanges(xlWorkbook, workbook); }
        catch { /* ignore named-range load failures */ }

        return workbook;
    }

    private sealed record SheetXmlLayout(
        HashSet<uint> HiddenRows,
        HashSet<uint> HiddenCols,
        bool IsProtected,
        string? ProtectionPasswordHash,
        string? PaneState,
        uint? PaneRowSplit,
        uint? PaneColumnSplit,
        Dictionary<uint, int> RowOutlineLevels,
        Dictionary<uint, int> ColOutlineLevels,
        HashSet<uint> GroupHiddenRows,
        HashSet<uint> GroupHiddenCols);

    private static Dictionary<string, SheetXmlLayout> LoadSheetXmlLayout(Stream xlsxStream)
    {
        var result = new Dictionary<string, SheetXmlLayout>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry is null || relsEntry is null)
                return result;

            var workbookXml = LoadXml(workbookEntry);
            var relsXml = LoadXml(relsEntry);

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var relTargets = relsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
                .ToDictionary(
                    e => e.Attribute("Id")!.Value,
                    e => NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                    StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
            {
                var name = sheetElement.Attribute("name")?.Value;
                var relId = sheetElement.Attribute(relNs + "id")?.Value;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId))
                    continue;
                if (!relTargets.TryGetValue(relId, out var worksheetPath))
                    continue;

                var worksheetEntry = archive.GetEntry(worksheetPath);
                if (worksheetEntry is null)
                    continue;

                result[name] = ReadHiddenSheetLayout(worksheetEntry);
            }
        }
        catch
        {
            // Worksheet XML metadata is best-effort; ClosedXML still loads workbook content.
        }

        return result;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string NormalizeWorkbookTarget(string target)
    {
        target = target.Replace('\\', '/').TrimStart('/');
        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? target
            : $"xl/{target}";
    }

    private static SheetXmlLayout ReadHiddenSheetLayout(ZipArchiveEntry worksheetEntry)
    {
        var hiddenRows = new HashSet<uint>();
        var hiddenCols = new HashSet<uint>();
        var rowOutlineLevels = new Dictionary<uint, int>();
        var colOutlineLevels = new Dictionary<uint, int>();
        var groupHiddenRows = new HashSet<uint>();
        var groupHiddenCols = new HashSet<uint>();
        var worksheetXml = LoadXml(worksheetEntry);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var row in worksheetXml.Descendants(worksheetNs + "row"))
        {
            if (!uint.TryParse(row.Attribute("r")?.Value, out var rowNumber))
                continue;

            if (IsTruthy(row.Attribute("hidden")?.Value))
                hiddenRows.Add(rowNumber);

            var outlineStr = row.Attribute("outlineLevel")?.Value;
            if (int.TryParse(outlineStr, out var outlineLevel) && outlineLevel > 0)
            {
                rowOutlineLevels[rowNumber] = outlineLevel;
                if (IsTruthy(row.Attribute("collapsed")?.Value))
                    groupHiddenRows.Add(rowNumber);
            }
        }

        foreach (var col in worksheetXml.Descendants(worksheetNs + "col"))
        {
            if (!uint.TryParse(col.Attribute("min")?.Value, out var min))
                continue;
            if (!uint.TryParse(col.Attribute("max")?.Value, out var max))
                continue;

            if (IsTruthy(col.Attribute("hidden")?.Value))
            {
                for (var colNumber = min; colNumber <= max; colNumber++)
                    hiddenCols.Add(colNumber);
            }

            var colOutlineStr = col.Attribute("outlineLevel")?.Value;
            if (int.TryParse(colOutlineStr, out var colOutlineLevel) && colOutlineLevel > 0)
            {
                var collapsed = IsTruthy(col.Attribute("collapsed")?.Value);
                for (var colNumber = min; colNumber <= max; colNumber++)
                {
                    colOutlineLevels[colNumber] = colOutlineLevel;
                    if (collapsed)
                        groupHiddenCols.Add(colNumber);
                }
            }
        }

        var protection = worksheetXml.Root?.Element(worksheetNs + "sheetProtection");
        var isProtected = IsTruthy(protection?.Attribute("sheet")?.Value);
        var passwordHash =
            protection?.Attribute("password")?.Value ??
            protection?.Attribute("hashValue")?.Value;

        var pane = worksheetXml.Root?
            .Element(worksheetNs + "sheetViews")?
            .Elements(worksheetNs + "sheetView")
            .FirstOrDefault()?
            .Element(worksheetNs + "pane");

        return new SheetXmlLayout(
            hiddenRows,
            hiddenCols,
            isProtected,
            passwordHash,
            pane?.Attribute("state")?.Value,
            ParsePaneSplit(pane?.Attribute("ySplit")?.Value),
            ParsePaneSplit(pane?.Attribute("xSplit")?.Value),
            rowOutlineLevels,
            colOutlineLevels,
            groupHiddenRows,
            groupHiddenCols);
    }

    private static uint? ParsePaneSplit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (uint.TryParse(value, out var integer))
            return integer;

        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var floating) &&
            floating > 0 &&
            floating <= uint.MaxValue)
            return (uint)Math.Round(floating);

        return null;
    }

    private static bool IsTruthy(string? value) =>
        value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static void LoadNamedRanges(XLWorkbook xlWorkbook, Workbook workbook)
    {
        foreach (var nr in xlWorkbook.DefinedNames)
        {
            try
            {
                // Take only the first range address if there are multiple
                var xlRange = nr.Ranges.FirstOrDefault();
                if (xlRange is null) continue;

                var firstCell = xlRange.FirstCell();
                var lastCell  = xlRange.LastCell();

                // Find the matching Freexcel sheet
                var sheetName = firstCell.Worksheet.Name;
                var sheet = workbook.GetSheet(sheetName);
                if (sheet is null) continue;

                var start = new CellAddress(sheet.Id,
                    (uint)firstCell.Address.RowNumber,
                    (uint)firstCell.Address.ColumnNumber);
                var end = new CellAddress(sheet.Id,
                    (uint)lastCell.Address.RowNumber,
                    (uint)lastCell.Address.ColumnNumber);

                workbook.DefineNamedRange(nr.Name, new GridRange(start, end));
            }
            catch
            {
                // Skip any named range that can't be mapped
            }
        }
    }

    public void Save(Workbook workbook, Stream stream)
    {
        using var xlWorkbook = new XLWorkbook();
        xlWorkbook.CalculateMode = workbook.CalculationMode == WorkbookCalculationMode.Manual
            ? XLCalculateMode.Manual
            : XLCalculateMode.Auto;

        foreach (var sheet in workbook.Sheets)
        {
            var xlSheet = xlWorkbook.Worksheets.Add(sheet.Name);
            xlSheet.Visibility = sheet.IsHidden ? XLWorksheetVisibility.Hidden : XLWorksheetVisibility.Visible;
            if (sheet.TabColor is { } tabColor)
                xlSheet.TabColor = XLColor.FromArgb(tabColor.R, tabColor.G, tabColor.B);

            foreach (var pair in sheet.GetUsedCells())
            {
                var cell = pair.Value;

                // Skip blank cells that carry no style
                if (cell.Value is BlankValue && !cell.HasFormula && cell.StyleId == StyleId.Default)
                    continue;

                var xlCell = xlSheet.Cell((int)pair.Key.Row, (int)pair.Key.Col);

                if (cell.HasFormula)
                {
                    xlCell.FormulaA1 = cell.FormulaText;
                }
                else if (cell.Value is not BlankValue)
                {
                    xlCell.Value = MapValueInverse(cell.Value);
                }

                var style = workbook.GetStyle(cell.StyleId);
                ApplyStyle(xlCell, style);
            }

            foreach (var (rowNum, height) in sheet.RowHeights)
                xlSheet.Row((int)rowNum).Height = height * (72.0 / 96.0);

            foreach (var rowNum in sheet.HiddenRows)
                xlSheet.Row((int)rowNum).Hide();

            foreach (var (rowNum, level) in sheet.RowOutlineLevels)
                xlSheet.Row((int)rowNum).OutlineLevel = level;

            foreach (var rowNum in sheet.GroupHiddenRows)
                xlSheet.Row((int)rowNum).Collapse();

            foreach (var (colNum, width) in sheet.ColumnWidths)
                xlSheet.Column((int)colNum).Width = width;

            foreach (var colNum in sheet.HiddenCols)
                xlSheet.Column((int)colNum).Hide();

            foreach (var (colNum, level) in sheet.ColOutlineLevels)
                xlSheet.Column((int)colNum).OutlineLevel = level;

            foreach (var colNum in sheet.GroupHiddenCols)
                xlSheet.Column((int)colNum).Collapse();

            foreach (var (address, commentText) in sheet.Comments)
            {
                try
                {
                    xlSheet.Cell((int)address.Row, (int)address.Col)
                        .CreateComment()
                        .AddText(commentText);
                }
                catch
                {
                    // Skip comments ClosedXML cannot serialize.
                }
            }

            foreach (var (address, target) in sheet.Hyperlinks)
            {
                try
                {
                    xlSheet.Cell((int)address.Row, (int)address.Col)
                        .SetHyperlink(new XLHyperlink(target));
                }
                catch
                {
                    // Skip hyperlinks ClosedXML cannot serialize.
                }
            }

            if (sheet.FrozenRows > 0 || sheet.FrozenCols > 0)
                xlSheet.SheetView.Freeze((int)sheet.FrozenRows, (int)sheet.FrozenCols);

            if (sheet.PrintArea is { } printArea)
            {
                xlSheet.PageSetup.PrintAreas.Clear();
                xlSheet.PageSetup.PrintAreas.Add(
                    (int)printArea.Start.Row,
                    (int)printArea.Start.Col,
                    (int)printArea.End.Row,
                    (int)printArea.End.Col);
            }

            xlSheet.PageSetup.PageOrientation = sheet.PageOrientation == WorksheetPageOrientation.Landscape
                ? XLPageOrientation.Landscape
                : XLPageOrientation.Portrait;
            xlSheet.PageSetup.PaperSize = sheet.PaperSize switch
            {
                WorksheetPaperSize.Letter => XLPaperSize.LetterPaper,
                WorksheetPaperSize.Legal => XLPaperSize.LegalPaper,
                _ => XLPaperSize.A4Paper
            };
            xlSheet.PageSetup.Margins.Left = sheet.PageMargins.Left;
            xlSheet.PageSetup.Margins.Right = sheet.PageMargins.Right;
            xlSheet.PageSetup.Margins.Top = sheet.PageMargins.Top;
            xlSheet.PageSetup.Margins.Bottom = sheet.PageMargins.Bottom;
            xlSheet.PageSetup.Margins.Header = sheet.HeaderMargin;
            xlSheet.PageSetup.Margins.Footer = sheet.FooterMargin;
            xlSheet.PageSetup.ShowGridlines = sheet.PrintGridlines;
            xlSheet.PageSetup.ShowRowAndColumnHeadings = sheet.PrintHeadings;
            xlSheet.PageSetup.CenterHorizontally = sheet.CenterHorizontallyOnPage;
            xlSheet.PageSetup.CenterVertically = sheet.CenterVerticallyOnPage;
            xlSheet.PageSetup.PageOrder = sheet.PageOrder == WorksheetPageOrder.OverThenDown
                ? XLPageOrderValues.OverThenDown
                : XLPageOrderValues.DownThenOver;
            if (sheet.FirstPageNumber is { } firstPageNumber)
                xlSheet.PageSetup.FirstPageNumber = firstPageNumber;
            xlSheet.PageSetup.BlackAndWhite = sheet.PrintBlackAndWhite;
            xlSheet.PageSetup.DraftQuality = sheet.PrintDraftQuality;
            if (sheet.PrintQualityDpi is { } printQualityDpi)
            {
                xlSheet.PageSetup.HorizontalDpi = printQualityDpi;
                xlSheet.PageSetup.VerticalDpi = printQualityDpi;
            }
            xlSheet.PageSetup.PrintErrorValue = ToXlsxPrintErrorValue(sheet.PrintErrorValue);
            xlSheet.PageSetup.ShowComments = ToXlsxPrintComments(sheet.PrintComments);
            xlSheet.PageSetup.DifferentFirstPageOnHF = sheet.DifferentFirstPageHeaderFooter;
            xlSheet.PageSetup.DifferentOddEvenPagesOnHF = sheet.DifferentOddEvenHeaderFooter;
            xlSheet.PageSetup.ScaleHFWithDocument = sheet.HeaderFooterScaleWithDocument;
            xlSheet.PageSetup.AlignHFWithMargins = sheet.HeaderFooterAlignWithMargins;
            SetXlsxHeaderFooter(
                xlSheet.PageSetup.Header,
                sheet.PageHeader,
                sheet.FirstPageHeader,
                sheet.EvenPageHeader,
                sheet.DifferentFirstPageHeaderFooter,
                sheet.DifferentOddEvenHeaderFooter);
            SetXlsxHeaderFooter(
                xlSheet.PageSetup.Footer,
                sheet.PageFooter,
                sheet.FirstPageFooter,
                sheet.EvenPageFooter,
                sheet.DifferentFirstPageHeaderFooter,
                sheet.DifferentOddEvenHeaderFooter);
            if (sheet.ScaleToFit.ScalePercent is { } scalePercent)
                xlSheet.PageSetup.Scale = scalePercent;
            else if (sheet.ScaleToFit.FitToPagesWide.HasValue || sheet.ScaleToFit.FitToPagesTall.HasValue)
                xlSheet.PageSetup.FitToPages(sheet.ScaleToFit.FitToPagesWide ?? 1, sheet.ScaleToFit.FitToPagesTall ?? 1);
            if (sheet.PrintTitleRows is { } titleRows)
                xlSheet.PageSetup.SetRowsToRepeatAtTop((int)titleRows.Start, (int)titleRows.End);
            if (sheet.PrintTitleColumns is { } titleColumns)
                xlSheet.PageSetup.SetColumnsToRepeatAtLeft((int)titleColumns.Start, (int)titleColumns.End);
            foreach (var rowBreak in sheet.RowPageBreaks)
                xlSheet.PageSetup.AddHorizontalPageBreak((int)rowBreak);
            foreach (var columnBreak in sheet.ColumnPageBreaks)
                xlSheet.PageSetup.AddVerticalPageBreak((int)columnBreak);

            if (sheet.IsProtected)
            {
                if (string.IsNullOrEmpty(sheet.ProtectionPassword))
                    xlSheet.Protect(XLProtectionAlgorithm.Algorithm.SimpleHash);
                else
                    xlSheet.Protect(sheet.ProtectionPassword, XLProtectionAlgorithm.Algorithm.SimpleHash);
            }

            // Save CellValue conditional format rules back to XLSX
            SaveConditionalFormats(sheet, xlSheet);

            // Save data validation rules back to XLSX
            try { SaveDataValidations(sheet, xlSheet); }
            catch { /* ignore DV save failures */ }

            // Save merged regions
            foreach (var region in sheet.MergedRegions)
            {
                try
                {
                    var rangeStr = $"{CellAddress.NumberToColumnName(region.Start.Col)}{region.Start.Row}" +
                                   $":{CellAddress.NumberToColumnName(region.End.Col)}{region.End.Row}";
                    xlSheet.Range(rangeStr).Merge();
                }
                catch { /* ignore individual merge failures */ }
            }
        }

        // Save named ranges
        try { SaveNamedRanges(workbook, xlWorkbook); }
        catch { /* ignore named-range save failures */ }

        using var packageStream = new MemoryStream();
        xlWorkbook.SaveAs(packageStream);

        if (workbook.Sheets.Any(sheet => sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
                                         (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue)))
        {
            packageStream.Position = 0;
            SaveSplitPaneSheetViews(packageStream, workbook);
        }

        packageStream.Position = 0;
        packageStream.CopyTo(stream);
    }

    private static void SaveSplitPaneSheetViews(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = LoadXml(workbookEntry);
        var relsXml = LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relTargets = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var splitSheets = workbook.Sheets
            .Where(sheet => sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
                            (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue))
            .ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId))
                continue;
            if (!splitSheets.TryGetValue(name, out var sheet))
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            UpdateSplitPaneSheetView(archive, worksheetPath, sheet);
        }
    }

    private static void UpdateSplitPaneSheetView(ZipArchive archive, string worksheetPath, Sheet sheet)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadXml(worksheetEntry);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var sheetViews = root.Element(worksheetNs + "sheetViews");
        if (sheetViews is null)
        {
            sheetViews = new XElement(worksheetNs + "sheetViews");
            root.AddFirst(sheetViews);
        }

        var sheetView = sheetViews.Elements(worksheetNs + "sheetView").FirstOrDefault();
        if (sheetView is null)
        {
            sheetView = new XElement(worksheetNs + "sheetView", new XAttribute("workbookViewId", "0"));
            sheetViews.Add(sheetView);
        }

        sheetView.Elements(worksheetNs + "pane").Remove();
        sheetView.AddFirst(new XElement(
            worksheetNs + "pane",
            sheet.SplitColumn is { } splitColumn ? new XAttribute("xSplit", splitColumn) : null,
            sheet.SplitRow is { } splitRow ? new XAttribute("ySplit", splitRow) : null,
            new XAttribute("state", "split")));

        worksheetEntry.Delete();
        var updatedEntry = archive.CreateEntry(worksheetPath);
        using var stream = updatedEntry.Open();
        worksheetXml.Save(stream);
    }

    private static ScalarValue MapValue(XLCellValue xlValue)
    {
        if (xlValue.IsBlank) return BlankValue.Instance;
        if (xlValue.IsNumber) return new NumberValue(xlValue.GetNumber());
        if (xlValue.IsText) return new TextValue(xlValue.GetText());
        if (xlValue.IsBoolean) return new BoolValue(xlValue.GetBoolean());
        if (xlValue.IsDateTime) return DateTimeValue.FromDateTime(xlValue.GetDateTime());
        if (xlValue.IsError) return MapErrorValue(xlValue.GetError());
        return new TextValue(xlValue.ToString());
    }

    private static XLCellValue MapValueInverse(ScalarValue value) => value switch
    {
        NumberValue n => n.Value,
        TextValue t => t.Value,
        BoolValue b => b.Value,
        DateTimeValue dt => DateTime.FromOADate(dt.Value),
        ErrorValue e => MapErrorValueInverse(e),
        _ => Blank.Value
    };

    private static ErrorValue MapErrorValue(XLError error) => error switch
    {
        XLError.NullValue => ErrorValue.Null,
        XLError.DivisionByZero => ErrorValue.DivByZero,
        XLError.IncompatibleValue => ErrorValue.Value,
        XLError.CellReference => ErrorValue.Ref,
        XLError.NameNotRecognized => ErrorValue.Name,
        XLError.NumberInvalid => ErrorValue.Num,
        XLError.NoValueAvailable => ErrorValue.NA,
        _ => new ErrorValue(error.ToString())
    };

    private static XLError MapErrorValueInverse(ErrorValue error) => error.Code.ToUpperInvariant() switch
    {
        "#NULL!" => XLError.NullValue,
        "#DIV/0!" => XLError.DivisionByZero,
        "#VALUE!" => XLError.IncompatibleValue,
        "#REF!" => XLError.CellReference,
        "#NAME?" => XLError.NameNotRecognized,
        "#NUM!" => XLError.NumberInvalid,
        "#N/A" => XLError.NoValueAvailable,
        _ => XLError.NoValueAvailable
    };

    private static CellStyle MapStyle(IXLStyle xlStyle)
    {
        return new CellStyle
        {
            FontName = xlStyle.Font.FontName,
            FontSize = xlStyle.Font.FontSize,
            Bold = xlStyle.Font.Bold,
            Italic = xlStyle.Font.Italic,
            Underline = xlStyle.Font.Underline != XLFontUnderlineValues.None,
            Strikethrough = xlStyle.Font.Strikethrough,
            FontColor = MapColor(xlStyle.Font.FontColor),
            FillColor = xlStyle.Fill.PatternType == XLFillPatternValues.Solid
                ? (CellColor?)MapColor(xlStyle.Fill.BackgroundColor)
                : null,
            BorderTop = MapBorder(xlStyle.Border.TopBorder, xlStyle.Border.TopBorderColor),
            BorderRight = MapBorder(xlStyle.Border.RightBorder, xlStyle.Border.RightBorderColor),
            BorderBottom = MapBorder(xlStyle.Border.BottomBorder, xlStyle.Border.BottomBorderColor),
            BorderLeft = MapBorder(xlStyle.Border.LeftBorder, xlStyle.Border.LeftBorderColor),
            // ClosedXML returns empty string for built-in format ID 0 (General) and some
            // other built-in IDs. Phase 2 limitation: built-in IDs without an explicit
            // format string are treated as General.
            NumberFormat = string.IsNullOrEmpty(xlStyle.NumberFormat.Format) ? "General" : xlStyle.NumberFormat.Format,
            HorizontalAlignment = xlStyle.Alignment.Horizontal switch
            {
                XLAlignmentHorizontalValues.General => HorizontalAlignment.General,
                XLAlignmentHorizontalValues.Left => HorizontalAlignment.Left,
                XLAlignmentHorizontalValues.Center => HorizontalAlignment.Center,
                XLAlignmentHorizontalValues.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.General,
            },
            VerticalAlignment = xlStyle.Alignment.Vertical switch
            {
                XLAlignmentVerticalValues.Top => VerticalAlignment.Top,
                XLAlignmentVerticalValues.Center => VerticalAlignment.Center,
                XLAlignmentVerticalValues.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Bottom,
            },
            WrapText = xlStyle.Alignment.WrapText,
            Locked = xlStyle.Protection.Locked,
        };
    }

    private static CellColor MapColor(XLColor xlColor)
    {
        if (xlColor.ColorType == XLColorType.Color)
            return new CellColor(xlColor.Color.R, xlColor.Color.G, xlColor.Color.B);
        // Theme and indexed colors require workbook theme context to resolve to RGB.
        // Phase 2 limitation: flattened to black. Track as a known gap.
        return CellColor.Black;
    }

    private static CellBorder MapBorder(XLBorderStyleValues style, XLColor color)
    {
        var mapped = style switch
        {
            XLBorderStyleValues.None => BorderStyle.None,
            XLBorderStyleValues.Thin => BorderStyle.Thin,
            XLBorderStyleValues.Medium => BorderStyle.Medium,
            XLBorderStyleValues.Thick => BorderStyle.Thick,
            XLBorderStyleValues.Dashed => BorderStyle.Dashed,
            XLBorderStyleValues.Dotted => BorderStyle.Dotted,
            XLBorderStyleValues.Double => BorderStyle.Double,
            _ => BorderStyle.None,
        };
        return new CellBorder(mapped, MapColor(color));
    }

    private static void ApplyStyle(IXLCell xlCell, CellStyle style)
    {
        var def = CellStyle.Default;

        if (style.Bold != def.Bold) xlCell.Style.Font.Bold = style.Bold;
        if (style.Italic != def.Italic) xlCell.Style.Font.Italic = style.Italic;
        if (style.Underline != def.Underline)
            xlCell.Style.Font.Underline = style.Underline ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None;
        if (style.Strikethrough != def.Strikethrough)
            xlCell.Style.Font.Strikethrough = style.Strikethrough;
        if (style.FontSize != def.FontSize) xlCell.Style.Font.FontSize = style.FontSize;
        if (style.FontName != def.FontName) xlCell.Style.Font.FontName = style.FontName;
        if (style.FontColor != def.FontColor)
            xlCell.Style.Font.FontColor = XLColor.FromArgb(255, style.FontColor.R, style.FontColor.G, style.FontColor.B);

        if (style.FillColor.HasValue)
        {
            xlCell.Style.Fill.PatternType = XLFillPatternValues.Solid;
            xlCell.Style.Fill.BackgroundColor = XLColor.FromArgb(255, style.FillColor.Value.R, style.FillColor.Value.G, style.FillColor.Value.B);
        }

        if (style.BorderTop.Style != BorderStyle.None)
        {
            xlCell.Style.Border.TopBorder = MapBorderStyleInverse(style.BorderTop.Style);
            xlCell.Style.Border.TopBorderColor = XLColor.FromArgb(255, style.BorderTop.Color.R, style.BorderTop.Color.G, style.BorderTop.Color.B);
        }
        if (style.BorderRight.Style != BorderStyle.None)
        {
            xlCell.Style.Border.RightBorder = MapBorderStyleInverse(style.BorderRight.Style);
            xlCell.Style.Border.RightBorderColor = XLColor.FromArgb(255, style.BorderRight.Color.R, style.BorderRight.Color.G, style.BorderRight.Color.B);
        }
        if (style.BorderBottom.Style != BorderStyle.None)
        {
            xlCell.Style.Border.BottomBorder = MapBorderStyleInverse(style.BorderBottom.Style);
            xlCell.Style.Border.BottomBorderColor = XLColor.FromArgb(255, style.BorderBottom.Color.R, style.BorderBottom.Color.G, style.BorderBottom.Color.B);
        }
        if (style.BorderLeft.Style != BorderStyle.None)
        {
            xlCell.Style.Border.LeftBorder = MapBorderStyleInverse(style.BorderLeft.Style);
            xlCell.Style.Border.LeftBorderColor = XLColor.FromArgb(255, style.BorderLeft.Color.R, style.BorderLeft.Color.G, style.BorderLeft.Color.B);
        }

        if (style.HorizontalAlignment != def.HorizontalAlignment)
            xlCell.Style.Alignment.Horizontal = style.HorizontalAlignment switch
            {
                HorizontalAlignment.Left => XLAlignmentHorizontalValues.Left,
                HorizontalAlignment.Center => XLAlignmentHorizontalValues.Center,
                HorizontalAlignment.Right => XLAlignmentHorizontalValues.Right,
                _ => XLAlignmentHorizontalValues.General,
            };

        if (style.VerticalAlignment != def.VerticalAlignment)
            xlCell.Style.Alignment.Vertical = style.VerticalAlignment switch
            {
                VerticalAlignment.Top => XLAlignmentVerticalValues.Top,
                VerticalAlignment.Center => XLAlignmentVerticalValues.Center,
                _ => XLAlignmentVerticalValues.Bottom,
            };

        if (style.WrapText != def.WrapText)
            xlCell.Style.Alignment.WrapText = style.WrapText;

        if (style.NumberFormat != def.NumberFormat)
            xlCell.Style.NumberFormat.Format = style.NumberFormat;

        if (style.Locked != def.Locked)
            xlCell.Style.Protection.Locked = style.Locked;
    }

    // ── Conditional formatting load ────────────────────────────────────────────

    private static void SaveNamedRanges(Workbook workbook, XLWorkbook xlWorkbook)
    {
        foreach (var (name, range) in workbook.NamedRanges)
        {
            try
            {
                var sheet = workbook.GetSheet(range.Start.Sheet);
                if (sheet is null) continue;

                // Find the matching ClosedXML worksheet by name
                if (!xlWorkbook.TryGetWorksheet(sheet.Name, out var xlSheet)) continue;

                var startA1 = range.Start.ToA1();
                var endA1   = range.End.ToA1();
                var sheetName = sheet.Name.Replace("'", "''");
                var addr    = $"'{sheetName}'!{startA1}:{endA1}";

                xlWorkbook.DefinedNames.Add(name, addr);
            }
            catch
            {
                // Skip any named range that can't be serialized
            }
        }
    }

    private static void LoadPrintArea(IXLWorksheet xlSheet, Sheet sheet)
    {
        var xlRange = xlSheet.PageSetup.PrintAreas.FirstOrDefault();
        if (xlRange is null)
            return;

        var start = new CellAddress(
            sheet.Id,
            (uint)xlRange.RangeAddress.FirstAddress.RowNumber,
            (uint)xlRange.RangeAddress.FirstAddress.ColumnNumber);
        var end = new CellAddress(
            sheet.Id,
            (uint)xlRange.RangeAddress.LastAddress.RowNumber,
            (uint)xlRange.RangeAddress.LastAddress.ColumnNumber);
        sheet.PrintArea = new GridRange(start, end);
    }

    private static void LoadConditionalFormats(IXLWorksheet xlSheet, Sheet sheet, Workbook workbook)
    {
        int priority = 1;
        foreach (var xlCf in xlSheet.ConditionalFormats)
        {
            // Map the range
            var xlRange = xlCf.Range;
            var sheetId = sheet.Id;
            var start = new CellAddress(sheetId,
                (uint)xlRange.RangeAddress.FirstAddress.RowNumber,
                (uint)xlRange.RangeAddress.FirstAddress.ColumnNumber);
            var end = new CellAddress(sheetId,
                (uint)xlRange.RangeAddress.LastAddress.RowNumber,
                (uint)xlRange.RangeAddress.LastAddress.ColumnNumber);
            var appliesTo = new GridRange(start, end);

            if (xlCf.ConditionalFormatType == XLConditionalFormatType.CellIs)
            {
                var op = MapOperator(xlCf.Operator);
                if (op is null) { priority++; continue; }

                var values = xlCf.Values;
                string? v1 = values.TryGetValue(1, out var xv1) ? xv1.Value : null;
                string? v2 = values.TryGetValue(2, out var xv2) ? xv2.Value : null;

                var fmt = new ConditionalFormat
                {
                    AppliesTo    = appliesTo,
                    Priority     = priority++,
                    RuleType     = CfRuleType.CellValue,
                    Operator     = op.Value,
                    Value1       = v1,
                    Value2       = v2,
                    FormatIfTrue = MapStyle(xlCf.Style)
                };
                sheet.ConditionalFormats.Add(fmt);
            }
            else if (xlCf.ConditionalFormatType == XLConditionalFormatType.Expression)
            {
                var values = xlCf.Values;
                string? formula = values.TryGetValue(1, out var xvf) ? xvf.Value : null;
                if (string.IsNullOrWhiteSpace(formula)) { priority++; continue; }

                // Strip leading = if present (Freexcel stores formula without it)
                if (formula.StartsWith('=')) formula = formula[1..];

                var fmt = new ConditionalFormat
                {
                    AppliesTo    = appliesTo,
                    Priority     = priority++,
                    RuleType     = CfRuleType.Formula,
                    FormulaText  = formula,
                    FormatIfTrue = MapStyle(xlCf.Style)
                };
                sheet.ConditionalFormats.Add(fmt);
            }
            // ColorScale, DataBar etc. are intentionally skipped on load for v1
        }
    }

    private static CfOperator? MapOperator(XLCFOperator op) => op switch
    {
        XLCFOperator.Equal              => CfOperator.Equal,
        XLCFOperator.NotEqual           => CfOperator.NotEqual,
        XLCFOperator.GreaterThan        => CfOperator.GreaterThan,
        XLCFOperator.EqualOrGreaterThan => CfOperator.GreaterThanOrEqual,
        XLCFOperator.LessThan           => CfOperator.LessThan,
        XLCFOperator.EqualOrLessThan    => CfOperator.LessThanOrEqual,
        XLCFOperator.Between            => CfOperator.Between,
        XLCFOperator.NotBetween         => CfOperator.NotBetween,
        _                               => (CfOperator?)null
    };

    // ── Conditional formatting save ────────────────────────────────────────────

    private static void SaveConditionalFormats(Sheet sheet, IXLWorksheet xlSheet)
    {
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (cf.FormatIfTrue is null && cf.RuleType != CfRuleType.ColorScale && cf.RuleType != CfRuleType.DataBar)
                continue;

            var rangeStr = $"{CellAddress.NumberToColumnName(cf.AppliesTo.Start.Col)}{cf.AppliesTo.Start.Row}" +
                           $":{CellAddress.NumberToColumnName(cf.AppliesTo.End.Col)}{cf.AppliesTo.End.Row}";

            try
            {
                var xlRange = xlSheet.Range(rangeStr);
                var xlCf    = xlRange.AddConditionalFormat();

                if (cf.RuleType == CfRuleType.Formula && !string.IsNullOrWhiteSpace(cf.FormulaText))
                {
                    // ClosedXML uses WhenIsTrue for formula-based CF rules
                    var xlStyle = xlCf.WhenIsTrue("=" + cf.FormulaText);
                    if (cf.FormatIfTrue is not null) ApplyCfStyle(xlStyle, cf.FormatIfTrue);
                }
                else if (cf.RuleType == CfRuleType.CellValue)
                {
                    var v1 = cf.Value1 ?? "";
                    var v2 = cf.Value2 ?? "";
                    IXLStyle xlStyle = cf.Operator switch
                    {
                        CfOperator.Equal              => xlCf.WhenEquals(v1),
                        CfOperator.NotEqual           => xlCf.WhenNotEquals(v1),
                        CfOperator.GreaterThan        => xlCf.WhenGreaterThan(v1),
                        CfOperator.GreaterThanOrEqual => xlCf.WhenEqualOrGreaterThan(v1),
                        CfOperator.LessThan           => xlCf.WhenLessThan(v1),
                        CfOperator.LessThanOrEqual    => xlCf.WhenEqualOrLessThan(v1),
                        CfOperator.Between            => xlCf.WhenBetween(v1, v2),
                        CfOperator.NotBetween         => xlCf.WhenNotBetween(v1, v2),
                        _                             => xlCf.WhenEquals(v1)
                    };
                    if (cf.FormatIfTrue is not null) ApplyCfStyle(xlStyle, cf.FormatIfTrue);
                }
                // ColorScale, DataBar, AboveAverage, Top10 — skip for now (partial support)
            }
            catch
            {
                // Skip rules that can't be serialized
            }
        }
    }

    /// <summary>Apply a <see cref="CellStyle"/> to an <see cref="IXLStyle"/> (used for CF rules).</summary>
    private static void ApplyCfStyle(IXLStyle xlStyle, CellStyle style)
    {
        var def = CellStyle.Default;

        if (style.Bold != def.Bold) xlStyle.Font.Bold = style.Bold;
        if (style.Italic != def.Italic) xlStyle.Font.Italic = style.Italic;
        if (style.Underline != def.Underline)
            xlStyle.Font.Underline = style.Underline ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None;
        if (style.FontColor != def.FontColor)
            xlStyle.Font.FontColor = XLColor.FromArgb(255, style.FontColor.R, style.FontColor.G, style.FontColor.B);

        if (style.FillColor.HasValue)
        {
            xlStyle.Fill.PatternType = XLFillPatternValues.Solid;
            xlStyle.Fill.BackgroundColor = XLColor.FromArgb(255,
                style.FillColor.Value.R,
                style.FillColor.Value.G,
                style.FillColor.Value.B);
        }
    }

    // ── Data validation load ───────────────────────────────────────────────────

    private static void LoadDataValidations(IXLWorksheet xlSheet, Sheet sheet)
    {
        foreach (var xlDv in xlSheet.DataValidations)
        {
            try
            {
                var rangeAddr = xlDv.Ranges.FirstOrDefault()?.RangeAddress;
                if (rangeAddr == null) continue;

                var sheetId = sheet.Id;
                var start = new CellAddress(sheetId,
                    (uint)rangeAddr.FirstAddress.RowNumber,
                    (uint)rangeAddr.FirstAddress.ColumnNumber);
                var end = new CellAddress(sheetId,
                    (uint)rangeAddr.LastAddress.RowNumber,
                    (uint)rangeAddr.LastAddress.ColumnNumber);
                var appliesTo = new GridRange(start, end);

                var dv = new DataValidation
                {
                    AppliesTo    = appliesTo,
                    AllowBlank   = xlDv.IgnoreBlanks,
                    ShowDropdown = !xlDv.InCellDropdown.Equals(false),
                    AlertStyle   = xlDv.ErrorStyle switch
                    {
                        XLErrorStyle.Warning => DvAlertStyle.Warning,
                        XLErrorStyle.Information => DvAlertStyle.Information,
                        _ => DvAlertStyle.Stop
                    },
                    ShowInputMessage = xlDv.ShowInputMessage,
                    ShowErrorMessage = xlDv.ShowErrorMessage,
                    ErrorTitle   = xlDv.ErrorTitle,
                    ErrorMessage = xlDv.ErrorMessage,
                    PromptTitle  = xlDv.InputTitle,
                    PromptMessage = xlDv.InputMessage,
                };

                // Map type
                dv.Type = xlDv.AllowedValues switch
                {
                    XLAllowedValues.WholeNumber => DvType.WholeNumber,
                    XLAllowedValues.Decimal     => DvType.Decimal,
                    XLAllowedValues.List        => DvType.List,
                    XLAllowedValues.Date        => DvType.Date,
                    XLAllowedValues.Time        => DvType.Time,
                    XLAllowedValues.TextLength  => DvType.TextLength,
                    XLAllowedValues.Custom      => DvType.Custom,
                    _                           => DvType.Any
                };

                // Map operator
                dv.Operator = xlDv.Operator switch
                {
                    XLOperator.Between            => DvOperator.Between,
                    XLOperator.NotBetween         => DvOperator.NotBetween,
                    XLOperator.EqualTo            => DvOperator.Equal,
                    XLOperator.NotEqualTo         => DvOperator.NotEqual,
                    XLOperator.GreaterThan        => DvOperator.GreaterThan,
                    XLOperator.LessThan           => DvOperator.LessThan,
                    XLOperator.EqualOrGreaterThan => DvOperator.GreaterThanOrEqual,
                    XLOperator.EqualOrLessThan    => DvOperator.LessThanOrEqual,
                    _                             => DvOperator.Between
                };

                // Map formula values
                if (dv.Type == DvType.List)
                {
                    // ClosedXML stores list items in MinValue as a quoted formula like "\"A,B,C\""
                    var raw = xlDv.MinValue ?? "";
                    // Strip surrounding quotes if present
                    if (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length > 1)
                        raw = raw.Substring(1, raw.Length - 2);
                    dv.Formula1 = raw.Replace("\"\"", "\"");
                }
                else
                {
                    dv.Formula1 = xlDv.MinValue;
                    dv.Formula2 = xlDv.MaxValue;
                }

                sheet.DataValidations.Add(dv);
            }
            catch
            {
                // Skip any individual validation we can't map
            }
        }
    }

    // ── Data validation save ───────────────────────────────────────────────────

    private static void SaveDataValidations(Sheet sheet, IXLWorksheet xlSheet)
    {
        foreach (var dv in sheet.DataValidations)
        {
            try
            {
                var rangeStr = $"{CellAddress.NumberToColumnName(dv.AppliesTo.Start.Col)}{dv.AppliesTo.Start.Row}" +
                               $":{CellAddress.NumberToColumnName(dv.AppliesTo.End.Col)}{dv.AppliesTo.End.Row}";

                var xlRange = xlSheet.Range(rangeStr);
#pragma warning disable CS0618 // SetDataValidation is obsolete in newer ClosedXML but CreateDataValidation may not exist in 0.105
                var xlDv    = xlRange.CreateDataValidation();
#pragma warning restore CS0618

                xlDv.IgnoreBlanks  = dv.AllowBlank;
                xlDv.InCellDropdown = dv.ShowDropdown;
                xlDv.ErrorStyle = dv.AlertStyle switch
                {
                    DvAlertStyle.Warning => XLErrorStyle.Warning,
                    DvAlertStyle.Information => XLErrorStyle.Information,
                    _ => XLErrorStyle.Stop
                };
                xlDv.ShowInputMessage = dv.ShowInputMessage;
                xlDv.ShowErrorMessage = dv.ShowErrorMessage;

                if (!string.IsNullOrEmpty(dv.ErrorTitle))   xlDv.ErrorTitle   = dv.ErrorTitle;
                if (!string.IsNullOrEmpty(dv.ErrorMessage)) xlDv.ErrorMessage = dv.ErrorMessage;
                if (!string.IsNullOrEmpty(dv.PromptTitle))  xlDv.InputTitle   = dv.PromptTitle;
                if (!string.IsNullOrEmpty(dv.PromptMessage)) xlDv.InputMessage = dv.PromptMessage;

                var f1 = dv.Formula1 ?? "";
                var f2 = dv.Formula2 ?? "";

                switch (dv.Type)
                {
                    case DvType.List:
                        xlDv.List(f1, dv.ShowDropdown);
                        break;

                    case DvType.WholeNumber:
                        ApplyNumericDv(xlDv.WholeNumber, dv.Operator, f1, f2);
                        break;

                    case DvType.Decimal:
                        ApplyNumericDv(xlDv.Decimal, dv.Operator, f1, f2);
                        break;

                    case DvType.Date:
                        ApplyNumericDv(xlDv.Date, dv.Operator, f1, f2);
                        break;

                    case DvType.Time:
                        ApplyNumericDv(xlDv.Time, dv.Operator, f1, f2);
                        break;

                    case DvType.TextLength:
                        ApplyNumericDv(xlDv.TextLength, dv.Operator, f1, f2);
                        break;

                    case DvType.Custom:
                        xlDv.Custom(f1);
                        break;

                    // DvType.Any — leave as-is (ClosedXML default = no restriction)
                }
            }
            catch
            {
                // Skip rules that can't be serialized
            }
        }
    }

    private static void ApplyNumericDv(IXLValidationCriteria rule, DvOperator op, string f1, string f2)
    {
        switch (op)
        {
            case DvOperator.Between:            rule.Between(f1, f2); break;
            case DvOperator.NotBetween:         rule.NotBetween(f1, f2); break;
            case DvOperator.Equal:              rule.EqualTo(f1); break;
            case DvOperator.NotEqual:           rule.NotEqualTo(f1); break;
            case DvOperator.GreaterThan:        rule.GreaterThan(f1); break;
            case DvOperator.LessThan:           rule.LessThan(f1); break;
            case DvOperator.GreaterThanOrEqual: rule.EqualOrGreaterThan(f1); break;
            case DvOperator.LessThanOrEqual:    rule.EqualOrLessThan(f1); break;
        }
    }

    private static void SetXlsxHeaderFooter(
        IXLHeaderFooter target,
        WorksheetHeaderFooter oddOrAllPages,
        WorksheetHeaderFooter firstPage,
        WorksheetHeaderFooter evenPages,
        bool differentFirstPage,
        bool differentOddEvenPages)
    {
        foreach (var occurrence in new[]
                 {
                     XLHFOccurrence.AllPages,
                     XLHFOccurrence.OddPages,
                     XLHFOccurrence.EvenPages,
                     XLHFOccurrence.FirstPage
                 })
        {
            target.Left.Clear(occurrence);
            target.Center.Clear(occurrence);
            target.Right.Clear(occurrence);
        }

        var primaryOccurrence = differentOddEvenPages ? XLHFOccurrence.OddPages : XLHFOccurrence.AllPages;
        AddXlsxHeaderFooterText(target, oddOrAllPages, primaryOccurrence);
        if (differentFirstPage)
            AddXlsxHeaderFooterText(target, firstPage, XLHFOccurrence.FirstPage);
        if (differentOddEvenPages)
            AddXlsxHeaderFooterText(target, evenPages, XLHFOccurrence.EvenPages);
    }

    private static void AddXlsxHeaderFooterText(
        IXLHeaderFooter target,
        WorksheetHeaderFooter value,
        XLHFOccurrence occurrence)
    {
        if (!string.IsNullOrEmpty(value.Left))
            target.Left.AddText(ToXlsxHeaderFooterText(value.Left), occurrence);
        if (!string.IsNullOrEmpty(value.Center))
            target.Center.AddText(ToXlsxHeaderFooterText(value.Center), occurrence);
        if (!string.IsNullOrEmpty(value.Right))
            target.Right.AddText(ToXlsxHeaderFooterText(value.Right), occurrence);
    }

    private static string GetXlsxHeaderFooterText(IXLHFItem item, params XLHFOccurrence[] occurrences)
    {
        foreach (var occurrence in occurrences)
        {
            var text = item.GetText(occurrence);
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        return "";
    }

    private static string ToXlsxHeaderFooterText(string text) =>
        text
            .Replace("&[Page]", "&P", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Pages]", "&N", StringComparison.OrdinalIgnoreCase);

    private static string FromXlsxHeaderFooterText(string text) =>
        text
            .Replace("&P", "&[Page]", StringComparison.Ordinal)
            .Replace("&N", "&[Pages]", StringComparison.Ordinal);

    private static XLPrintErrorValues ToXlsxPrintErrorValue(WorksheetPrintErrorValue value) =>
        value switch
        {
            WorksheetPrintErrorValue.Blank => XLPrintErrorValues.Blank,
            WorksheetPrintErrorValue.Dash => XLPrintErrorValues.Dash,
            WorksheetPrintErrorValue.NotAvailable => XLPrintErrorValues.NA,
            _ => XLPrintErrorValues.Displayed
        };

    private static WorksheetPrintErrorValue FromXlsxPrintErrorValue(XLPrintErrorValues value) =>
        value switch
        {
            XLPrintErrorValues.Blank => WorksheetPrintErrorValue.Blank,
            XLPrintErrorValues.Dash => WorksheetPrintErrorValue.Dash,
            XLPrintErrorValues.NA => WorksheetPrintErrorValue.NotAvailable,
            _ => WorksheetPrintErrorValue.Displayed
        };

    private static XLShowCommentsValues ToXlsxPrintComments(WorksheetPrintComments value) =>
        value switch
        {
            WorksheetPrintComments.AtEnd => XLShowCommentsValues.AtEnd,
            WorksheetPrintComments.AsDisplayed => XLShowCommentsValues.AsDisplayed,
            _ => XLShowCommentsValues.None
        };

    private static WorksheetPrintComments FromXlsxPrintComments(XLShowCommentsValues value) =>
        value switch
        {
            XLShowCommentsValues.AtEnd => WorksheetPrintComments.AtEnd,
            XLShowCommentsValues.AsDisplayed => WorksheetPrintComments.AsDisplayed,
            _ => WorksheetPrintComments.None
        };

    private static XLBorderStyleValues MapBorderStyleInverse(BorderStyle style) => style switch
    {
        BorderStyle.Thin => XLBorderStyleValues.Thin,
        BorderStyle.Medium => XLBorderStyleValues.Medium,
        BorderStyle.Thick => XLBorderStyleValues.Thick,
        BorderStyle.Dashed => XLBorderStyleValues.Dashed,
        BorderStyle.Dotted => XLBorderStyleValues.Dotted,
        BorderStyle.Double => XLBorderStyleValues.Double,
        _ => XLBorderStyleValues.None,
    };

    // ── Merged regions load ────────────────────────────────────────────────────

    private static void LoadMergedRegions(IXLWorksheet xlSheet, Sheet sheet)
    {
        foreach (var xlMerge in xlSheet.MergedRanges)
        {
            var sheetId = sheet.Id;
            var start = new CellAddress(sheetId,
                (uint)xlMerge.RangeAddress.FirstAddress.RowNumber,
                (uint)xlMerge.RangeAddress.FirstAddress.ColumnNumber);
            var end = new CellAddress(sheetId,
                (uint)xlMerge.RangeAddress.LastAddress.RowNumber,
                (uint)xlMerge.RangeAddress.LastAddress.ColumnNumber);
            sheet.MergedRegions.Add(new GridRange(start, end));
        }
    }
}
