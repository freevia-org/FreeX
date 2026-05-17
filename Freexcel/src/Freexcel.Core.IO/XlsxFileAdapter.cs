using System.Globalization;
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
    private const int CategoryAxisId = 48650112;
    private const int ValueAxisId = 48672768;
    private const int SecondaryValueAxisId = 48672769;

    public string Extension => ".xlsx";
    public string FormatName => "Excel Workbook";

    public Workbook Load(Stream stream)
    {
        using var packageStream = new MemoryStream();
        stream.CopyTo(packageStream);

        packageStream.Position = 0;
        var sheetXmlLayout = LoadSheetXmlLayout(packageStream);
        packageStream.Position = 0;
        var workbookTheme = LoadWorkbookTheme(packageStream);
        packageStream.Position = 0;
        var workbookProtection = LoadWorkbookProtection(packageStream);

        packageStream.Position = 0;
        using var xlWorkbook = new XLWorkbook(packageStream);
        var workbook = new Workbook("Untitled");
        workbook.Theme = workbookTheme;
        workbook.IsStructureProtected = workbookProtection.IsStructureProtected;
        workbook.StructureProtectionPassword = workbookProtection.PasswordHash;
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

                var style = MapStyle(xlCell.Style, workbook.Theme);
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
                sheet.ViewMode = layout.ViewMode;
                sheet.ShowGridlines = layout.ShowGridlines;
                sheet.ShowHeadings = layout.ShowHeadings;
                sheet.ShowRulers = layout.ShowRulers;
                sheet.ZoomPercent = layout.ZoomPercent;
                sheet.ShowFormulas = layout.ShowFormulas;
                sheet.BackgroundImage = layout.BackgroundImage;

                foreach (var (rowNum, level) in layout.RowOutlineLevels)
                    sheet.RowOutlineLevels[rowNum] = level;
                foreach (var (colNum, level) in layout.ColOutlineLevels)
                    sheet.ColOutlineLevels[colNum] = level;
                sheet.GroupHiddenRows.UnionWith(layout.GroupHiddenRows);
                sheet.GroupHiddenCols.UnionWith(layout.GroupHiddenCols);
                foreach (var chartPart in layout.ChartParts)
                {
                    if (XlsxChartPartReader.TryReadSupportedChart(chartPart.Xml, sheet.Id, out var chart))
                    {
                        ApplyChartAnchor(chart, chartPart.Anchor, sheet);
                        sheet.Charts.Add(chart);
                    }
                }
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Print-area load failed: {ex.Message}"); }

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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] CF load failed: {ex.Message}"); }

            // Load data validation rules (best-effort)
            try { LoadDataValidations(xlSheet, sheet); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] DV load failed: {ex.Message}"); }

            // Load merged regions (best-effort)
            try { LoadMergedRegions(xlSheet, sheet); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Merge load failed: {ex.Message}"); }
        }

        // Load named ranges (best-effort; skip any we cannot map)
        try { LoadNamedRanges(xlWorkbook, workbook); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Named-range load failed: {ex.Message}"); }

        return workbook;
    }

    private sealed record SheetXmlLayout(
        HashSet<uint> HiddenRows,
        HashSet<uint> HiddenCols,
        bool IsProtected,
        string? ProtectionPasswordHash,
        WorksheetViewMode ViewMode,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas,
        string? PaneState,
        uint? PaneRowSplit,
        uint? PaneColumnSplit,
        WorksheetBackgroundImage? BackgroundImage,
        Dictionary<uint, int> RowOutlineLevels,
        Dictionary<uint, int> ColOutlineLevels,
        HashSet<uint> GroupHiddenRows,
        HashSet<uint> GroupHiddenCols,
        IReadOnlyList<XlsxChartPackagePart> ChartParts);

    private sealed record XlsxChartPackagePart(XDocument Xml, XlsxDrawingAnchor? Anchor);

    private sealed record XlsxDrawingAnchor(
        uint FromRowZeroBased,
        uint FromColumnZeroBased,
        double FromRowOffset,
        double FromColumnOffset,
        double? AbsoluteLeft,
        double? AbsoluteTop,
        uint? ToRowZeroBased,
        uint? ToColumnZeroBased,
        double? ToRowOffset,
        double? ToColumnOffset,
        double? Width,
        double? Height);

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

                result[name] = ReadHiddenSheetLayout(archive, worksheetPath, worksheetEntry);
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

    private static WorkbookTheme LoadWorkbookTheme(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var themeEntry = archive.GetEntry("xl/theme/theme1.xml");
            if (themeEntry is null)
                return WorkbookTheme.Office;

            var themeXml = LoadXml(themeEntry);
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

            var theme = WorkbookTheme.Office
                .WithName(themeXml.Root?.Attribute("name")?.Value ?? WorkbookTheme.Office.Name);

            var themeElements = themeXml.Root?.Element(drawingNs + "themeElements");
            if (themeElements is null)
                return theme;

            var fontScheme = themeElements.Element(drawingNs + "fontScheme");
            if (fontScheme is not null)
            {
                theme = theme.WithFonts(
                    ReadThemeTypeface(fontScheme.Element(drawingNs + "majorFont"), drawingNs) ?? theme.MajorFontName,
                    ReadThemeTypeface(fontScheme.Element(drawingNs + "minorFont"), drawingNs) ?? theme.MinorFontName);
            }

            var effectsName = themeElements.Element(drawingNs + "fmtScheme")?.Attribute("name")?.Value;
            if (!string.IsNullOrWhiteSpace(effectsName))
                theme = theme.WithEffects(effectsName);

            var colorScheme = themeElements.Element(drawingNs + "clrScheme");
            if (colorScheme is null)
                return theme;

            foreach (var (slot, elementName) in ThemeColorElements)
            {
                if (ReadThemeColor(colorScheme.Element(drawingNs + elementName), drawingNs) is { } color)
                    theme = theme.WithColor(slot, color);
            }

            return theme;
        }
        catch
        {
            return WorkbookTheme.Office;
        }
    }

    private static WorkbookProtectionState LoadWorkbookProtection(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return WorkbookProtectionState.None;

            var workbookXml = LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var protection = workbookXml.Root?.Element(workbookNs + "workbookProtection");
            if (protection is null)
                return WorkbookProtectionState.None;

            var isStructureProtected =
                IsTruthy(protection.Attribute("lockStructure")?.Value) ||
                IsTruthy(protection.Attribute("lockWindows")?.Value);

            if (!isStructureProtected)
                return WorkbookProtectionState.None;

            var passwordHash =
                protection.Attribute("workbookPassword")?.Value ??
                protection.Attribute("revisionsPassword")?.Value;

            return new WorkbookProtectionState(true, passwordHash);
        }
        catch
        {
            return WorkbookProtectionState.None;
        }
    }

    private sealed record WorkbookProtectionState(bool IsStructureProtected, string? PasswordHash)
    {
        public static WorkbookProtectionState None { get; } = new(false, null);
    }

    private static readonly (WorkbookThemeColorSlot Slot, string ElementName)[] ThemeColorElements =
    [
        (WorkbookThemeColorSlot.Dark1, "dk1"),
        (WorkbookThemeColorSlot.Light1, "lt1"),
        (WorkbookThemeColorSlot.Dark2, "dk2"),
        (WorkbookThemeColorSlot.Light2, "lt2"),
        (WorkbookThemeColorSlot.Accent1, "accent1"),
        (WorkbookThemeColorSlot.Accent2, "accent2"),
        (WorkbookThemeColorSlot.Accent3, "accent3"),
        (WorkbookThemeColorSlot.Accent4, "accent4"),
        (WorkbookThemeColorSlot.Accent5, "accent5"),
        (WorkbookThemeColorSlot.Accent6, "accent6"),
        (WorkbookThemeColorSlot.Hyperlink, "hlink"),
        (WorkbookThemeColorSlot.FollowedHyperlink, "folHlink")
    ];

    private static string? ReadThemeTypeface(XElement? fontElement, XNamespace drawingNs) =>
        fontElement?
            .Element(drawingNs + "latin")?
            .Attribute("typeface")?
            .Value;

    private static CellColor? ReadThemeColor(XElement? colorElement, XNamespace drawingNs)
    {
        var srgb = colorElement?.Element(drawingNs + "srgbClr")?.Attribute("val")?.Value;
        if (TryParseHexColor(srgb, out var color))
            return color;

        var systemFallback = colorElement?.Element(drawingNs + "sysClr")?.Attribute("lastClr")?.Value;
        return TryParseHexColor(systemFallback, out color)
            ? color
            : null;
    }

    private static bool TryParseHexColor(string? text, out CellColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim().TrimStart('#');
        if (normalized.Length != 6 ||
            !byte.TryParse(normalized[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(normalized[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(normalized[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = new CellColor(r, g, b);
        return true;
    }

    private static string NormalizeWorkbookTarget(string target)
    {
        target = target.Replace('\\', '/').TrimStart('/');
        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? target
            : $"xl/{target}";
    }

    private static string GetWorksheetRelsPath(string worksheetPath)
    {
        var normalized = worksheetPath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        if (slash < 0)
            return $"_rels/{normalized}.rels";

        return $"{normalized[..slash]}/_rels/{normalized[(slash + 1)..]}.rels";
    }

    private static string ResolveRelationshipTarget(string sourcePath, string target)
    {
        var normalizedTarget = target.Replace('\\', '/');
        if (normalizedTarget.StartsWith('/'))
            return normalizedTarget.TrimStart('/');
        if (normalizedTarget.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
            return normalizedTarget;

        var sourceDirectory = sourcePath.Replace('\\', '/');
        var slash = sourceDirectory.LastIndexOf('/');
        sourceDirectory = slash >= 0 ? sourceDirectory[..slash] : "";
        return NormalizeZipPath($"{sourceDirectory}/{normalizedTarget}");
    }

    private static string GetRelativeTarget(string sourcePath, string targetPath)
    {
        var sourceDirectory = sourcePath.Replace('\\', '/');
        var slash = sourceDirectory.LastIndexOf('/');
        sourceDirectory = slash >= 0 ? sourceDirectory[..slash] : "";

        if (sourceDirectory.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase))
        {
            return $"../media/{targetPath["xl/media/".Length..]}";
        }

        if (sourceDirectory.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase))
        {
            return $"../drawings/{targetPath["xl/drawings/".Length..]}";
        }

        if (sourceDirectory.Equals("xl/drawings", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase))
        {
            return $"../charts/{targetPath["xl/charts/".Length..]}";
        }

        return targetPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? targetPath["xl/".Length..]
            : targetPath;
    }

    private static string NormalizeZipPath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return string.Join('/', parts);
    }

    private static string GetContentTypeFromPath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "image/png"
        };
    }

    private static string GetExtensionFromContentType(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/bmp" => ".bmp",
            "image/gif" => ".gif",
            _ => ".png"
        };

    private static string GetWorksheetBackgroundMediaFileName(string? fileName, int backgroundIndex, string extension)
    {
        var candidate = Path.GetFileName(fileName ?? "");
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return $"freexcelBackground{backgroundIndex}{extension}";
        }

        return Path.HasExtension(candidate)
            ? candidate
            : $"{candidate}{extension}";
    }

    private static string NextRelationshipId(XDocument relsXml, XNamespace packageRelNs)
    {
        var used = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Select(e => e.Attribute("Id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];

        for (var i = 1; ; i++)
        {
            var candidate = $"rId{i}";
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    private static void EnsureContentType(ZipArchive archive, string extension, string contentType)
    {
        const string contentTypesPath = "[Content_Types].xml";
        var entry = archive.GetEntry(contentTypesPath);
        if (entry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var xml = LoadXml(entry);
        var hasDefault = xml.Root?
            .Elements(contentTypeNs + "Default")
            .Any(e => string.Equals(e.Attribute("Extension")?.Value, extension, StringComparison.OrdinalIgnoreCase))
            == true;
        if (hasDefault)
            return;

        xml.Root?.Add(new XElement(
            contentTypeNs + "Default",
            new XAttribute("Extension", extension),
            new XAttribute("ContentType", contentType)));

        entry.Delete();
        var updatedEntry = archive.CreateEntry(contentTypesPath);
        using var stream = updatedEntry.Open();
        xml.Save(stream);
    }

    private static SheetXmlLayout ReadHiddenSheetLayout(ZipArchive archive, string worksheetPath, ZipArchiveEntry worksheetEntry)
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

        var sheetView = worksheetXml.Root?
            .Element(worksheetNs + "sheetViews")?
            .Elements(worksheetNs + "sheetView")
            .FirstOrDefault();
        var pane = sheetView?.Element(worksheetNs + "pane");
        var background = ReadWorksheetBackground(archive, worksheetPath, worksheetXml);
        var chartParts = ReadWorksheetChartParts(archive, worksheetPath, worksheetXml);

        return new SheetXmlLayout(
            hiddenRows,
            hiddenCols,
            isProtected,
            passwordHash,
            ParseWorksheetViewMode(sheetView?.Attribute("view")?.Value),
            !IsFalse(sheetView?.Attribute("showGridLines")?.Value),
            !IsFalse(sheetView?.Attribute("showRowColHeaders")?.Value),
            !IsFalse(sheetView?.Attribute("showRuler")?.Value),
            ParseZoomPercent(sheetView?.Attribute("zoomScale")?.Value),
            IsTruthy(sheetView?.Attribute("showFormulas")?.Value),
            pane?.Attribute("state")?.Value,
            ParsePaneSplit(pane?.Attribute("ySplit")?.Value),
            ParsePaneSplit(pane?.Attribute("xSplit")?.Value),
            background,
            rowOutlineLevels,
            colOutlineLevels,
            groupHiddenRows,
            groupHiddenCols,
            chartParts);
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

    private static bool IsFalse(string? value) =>
        value is "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    private static int ParseZoomPercent(string? value) =>
        int.TryParse(value, out var zoom) && zoom is >= 10 and <= 400 ? zoom : 100;

    private static WorksheetViewMode ParseWorksheetViewMode(string? value) =>
        value switch
        {
            "pageBreakPreview" => WorksheetViewMode.PageBreakPreview,
            "pageLayout" => WorksheetViewMode.PageLayout,
            _ => WorksheetViewMode.Normal
        };

    private static WorksheetBackgroundImage? ReadWorksheetBackground(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relId = worksheetXml.Root?
            .Element(worksheetNs + "picture")?
            .Attribute(relNs + "id")?
            .Value;
        if (string.IsNullOrWhiteSpace(relId))
            return null;

        var relsEntry = archive.GetEntry(GetWorksheetRelsPath(worksheetPath));
        if (relsEntry is null)
            return null;

        var relsXml = LoadXml(relsEntry);
        var relationship = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, relId, StringComparison.Ordinal));
        var target = relationship?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return null;

        var imagePath = ResolveRelationshipTarget(worksheetPath, target);
        var imageEntry = archive.GetEntry(imagePath);
        if (imageEntry is null)
            return null;

        using var imageStream = imageEntry.Open();
        using var ms = new MemoryStream();
        imageStream.CopyTo(ms);
        return new WorksheetBackgroundImage(
            ms.ToArray(),
            GetContentTypeFromPath(imagePath),
            Path.GetFileName(imagePath));
    }

    private static IReadOnlyList<XlsxChartPackagePart> ReadWorksheetChartParts(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        var charts = new List<XlsxChartPackagePart>();
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        var drawingRelId = worksheetXml.Root?
            .Element(worksheetNs + "drawing")?
            .Attribute(relNs + "id")?
            .Value;
        if (string.IsNullOrWhiteSpace(drawingRelId))
            return charts;

        var worksheetRelsEntry = archive.GetEntry(GetWorksheetRelsPath(worksheetPath));
        if (worksheetRelsEntry is null)
            return charts;

        var worksheetRelsXml = LoadXml(worksheetRelsEntry);
        var drawingTarget = worksheetRelsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, drawingRelId, StringComparison.Ordinal))?
            .Attribute("Target")?
            .Value;
        if (string.IsNullOrWhiteSpace(drawingTarget))
            return charts;

        var drawingPath = ResolveRelationshipTarget(worksheetPath, drawingTarget);
        var drawingEntry = archive.GetEntry(drawingPath);
        if (drawingEntry is null)
            return charts;

        var drawingXml = LoadXml(drawingEntry);
        var chartElements = drawingXml
            .Descendants(chartNs + "chart")
            .ToList();
        if (chartElements.Count == 0)
            return charts;

        var drawingRelsEntry = archive.GetEntry(GetWorksheetRelsPath(drawingPath));
        if (drawingRelsEntry is null)
            return charts;

        var drawingRelsXml = LoadXml(drawingRelsEntry);
        foreach (var chartElement in chartElements)
        {
            var chartRelId = chartElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(chartRelId))
                continue;

            var chartTarget = drawingRelsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, chartRelId, StringComparison.Ordinal))?
                .Attribute("Target")?
                .Value;
            if (string.IsNullOrWhiteSpace(chartTarget))
                continue;

            var chartPath = ResolveRelationshipTarget(drawingPath, chartTarget);
            var chartEntry = archive.GetEntry(chartPath);
            if (chartEntry is null)
                continue;

            var anchor = chartElement
                .Ancestors(spreadsheetDrawingNs + "twoCellAnchor")
                .Select(TryReadTwoCellAnchor)
                .FirstOrDefault(candidate => candidate is not null)
                ?? chartElement
                    .Ancestors(spreadsheetDrawingNs + "oneCellAnchor")
                    .Select(TryReadOneCellAnchor)
                    .FirstOrDefault(candidate => candidate is not null)
                ?? chartElement
                    .Ancestors(spreadsheetDrawingNs + "absoluteAnchor")
                    .Select(TryReadAbsoluteAnchor)
                    .FirstOrDefault(candidate => candidate is not null);

            charts.Add(new XlsxChartPackagePart(LoadXml(chartEntry), anchor));
        }

        return charts;
    }

    private static XlsxDrawingAnchor? TryReadTwoCellAnchor(XElement anchor)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var from = anchor.Element(spreadsheetDrawingNs + "from");
        var to = anchor.Element(spreadsheetDrawingNs + "to");
        if (from is null || to is null)
            return null;

        if (!TryReadAnchorCoordinate(from, spreadsheetDrawingNs, out var fromRow, out var fromCol, out var fromRowOffset, out var fromColOffset) ||
            !TryReadAnchorCoordinate(to, spreadsheetDrawingNs, out var toRow, out var toCol, out var toRowOffset, out var toColOffset))
        {
            return null;
        }

        if (toRow <= fromRow || toCol <= fromCol)
            return null;

        return new XlsxDrawingAnchor(
            fromRow,
            fromCol,
            fromRowOffset,
            fromColOffset,
            AbsoluteLeft: null,
            AbsoluteTop: null,
            toRow,
            toCol,
            toRowOffset,
            toColOffset,
            Width: null,
            Height: null);
    }

    private static XlsxDrawingAnchor? TryReadOneCellAnchor(XElement anchor)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var from = anchor.Element(spreadsheetDrawingNs + "from");
        var ext = anchor.Element(spreadsheetDrawingNs + "ext");
        if (from is null || ext is null)
            return null;

        if (!TryReadAnchorCoordinate(from, spreadsheetDrawingNs, out var fromRow, out var fromCol, out var fromRowOffset, out var fromColOffset))
            return null;

        var width = EmusToPixels(ext.Attribute("cx")?.Value);
        var height = EmusToPixels(ext.Attribute("cy")?.Value);
        if (width <= 0 || height <= 0)
            return null;

        return new XlsxDrawingAnchor(
            fromRow,
            fromCol,
            fromRowOffset,
            fromColOffset,
            AbsoluteLeft: null,
            AbsoluteTop: null,
            ToRowZeroBased: null,
            ToColumnZeroBased: null,
            ToRowOffset: null,
            ToColumnOffset: null,
            width,
            height);
    }

    private static XlsxDrawingAnchor? TryReadAbsoluteAnchor(XElement anchor)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var pos = anchor.Element(spreadsheetDrawingNs + "pos");
        var ext = anchor.Element(spreadsheetDrawingNs + "ext");
        if (pos is null || ext is null)
            return null;

        var left = EmusToPixels(pos.Attribute("x")?.Value);
        var top = EmusToPixels(pos.Attribute("y")?.Value);
        var width = EmusToPixels(ext.Attribute("cx")?.Value);
        var height = EmusToPixels(ext.Attribute("cy")?.Value);
        if (width <= 0 || height <= 0)
            return null;

        return new XlsxDrawingAnchor(
            FromRowZeroBased: 0,
            FromColumnZeroBased: 0,
            FromRowOffset: 0,
            FromColumnOffset: 0,
            left,
            top,
            ToRowZeroBased: null,
            ToColumnZeroBased: null,
            ToRowOffset: null,
            ToColumnOffset: null,
            width,
            height);
    }

    private static bool TryReadAnchorCoordinate(
        XElement marker,
        XNamespace spreadsheetDrawingNs,
        out uint rowZeroBased,
        out uint columnZeroBased,
        out double rowOffset,
        out double columnOffset)
    {
        rowZeroBased = 0;
        columnZeroBased = 0;
        rowOffset = EmusToPixels(marker.Element(spreadsheetDrawingNs + "rowOff")?.Value);
        columnOffset = EmusToPixels(marker.Element(spreadsheetDrawingNs + "colOff")?.Value);
        return uint.TryParse(marker.Element(spreadsheetDrawingNs + "row")?.Value, out rowZeroBased) &&
               uint.TryParse(marker.Element(spreadsheetDrawingNs + "col")?.Value, out columnZeroBased);
    }

    private static double EmusToPixels(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var emus)
            ? emus / 9525.0
            : 0;

    private static bool IsValidWorksheetRow(uint row) =>
        row is >= 1 and <= CellAddress.MaxRow;

    private static bool IsValidWorksheetColumn(uint column) =>
        column is >= 1 and <= CellAddress.MaxCol;

    private static TEnum ValidEnumOrDefault<TEnum>(TEnum value, TEnum defaultValue)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? value : defaultValue;

    private static double NonNegativeFiniteOrDefault(double value, double defaultValue) =>
        double.IsFinite(value) && value >= 0 ? value : defaultValue;

    private static WorksheetPageMargins ValidPageMarginsOrDefault(
        WorksheetPageMargins margins,
        WorksheetPageMargins defaultValue) =>
        IsNonNegativeFinite(margins.Left) &&
        IsNonNegativeFinite(margins.Right) &&
        IsNonNegativeFinite(margins.Top) &&
        IsNonNegativeFinite(margins.Bottom)
            ? margins
            : defaultValue;

    private static WorksheetScaleToFit ValidScaleToFitOrDefault(
        WorksheetScaleToFit scaleToFit,
        WorksheetScaleToFit defaultValue) =>
        scaleToFit.ScalePercent is < 10 or > 400 ||
        scaleToFit.FitToPagesWide is < 1 ||
        scaleToFit.FitToPagesTall is < 1
            ? defaultValue
            : scaleToFit;

    private static bool IsNonNegativeFinite(double value) =>
        double.IsFinite(value) && value >= 0;

    private static bool IsValidRepeatRange(WorksheetRepeatRange range, uint max) =>
        range.Start >= 1 && range.End >= range.Start && range.End <= max;

    private static bool IsSupportedTextRotation(int rotation) =>
        rotation == 255 || rotation is >= -90 and <= 90;

    private static uint ValidFrozenRowsOrZero(uint row) =>
        row <= CellAddress.MaxRow ? row : 0;

    private static uint ValidFrozenColumnsOrZero(uint column) =>
        column <= CellAddress.MaxCol ? column : 0;

    private static bool IsSupportedFontSize(double fontSize) =>
        double.IsFinite(fontSize) && fontSize is >= 1 and <= 409;

    private static string? ToXlsxWorksheetViewMode(WorksheetViewMode viewMode) =>
        viewMode switch
        {
            WorksheetViewMode.PageBreakPreview => "pageBreakPreview",
            WorksheetViewMode.PageLayout => "pageLayout",
            _ => null
        };

    private static void ApplyChartAnchor(ChartModel chart, XlsxDrawingAnchor? anchor, Sheet sheet)
    {
        if (anchor is null)
            return;

        chart.Left = anchor.AbsoluteLeft ?? (SumColumnPixels(sheet, 1, anchor.FromColumnZeroBased) + anchor.FromColumnOffset);
        chart.Top = anchor.AbsoluteTop ?? (SumRowPixels(sheet, 1, anchor.FromRowZeroBased) + anchor.FromRowOffset);

        var width = anchor.Width ?? (
            SumColumnPixels(sheet, anchor.FromColumnZeroBased + 1, anchor.ToColumnZeroBased!.Value - anchor.FromColumnZeroBased)
            + anchor.ToColumnOffset!.Value
            - anchor.FromColumnOffset);
        var height = anchor.Height ?? (
            SumRowPixels(sheet, anchor.FromRowZeroBased + 1, anchor.ToRowZeroBased!.Value - anchor.FromRowZeroBased)
            + anchor.ToRowOffset!.Value
            - anchor.FromRowOffset);
        if (width > 0)
            chart.Width = width;
        if (height > 0)
            chart.Height = height;
    }

    private static double SumColumnPixels(Sheet sheet, uint firstColumn, uint count)
    {
        double width = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var col = firstColumn + offset;
            if (!sheet.IsColEffectivelyHidden(col))
                width += sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth) * 8;
        }

        return width;
    }

    private static double SumRowPixels(Sheet sheet, uint firstRow, uint count)
    {
        double height = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var row = firstRow + offset;
            if (!sheet.IsRowEffectivelyHidden(row))
                height += sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);
        }

        return height;
    }

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
            {
                if (IsValidWorksheetRow(rowNum) && double.IsFinite(height) && height > 0)
                    xlSheet.Row((int)rowNum).Height = height * (72.0 / 96.0);
            }

            foreach (var rowNum in sheet.HiddenRows)
            {
                if (IsValidWorksheetRow(rowNum))
                    xlSheet.Row((int)rowNum).Hide();
            }

            foreach (var (rowNum, level) in sheet.RowOutlineLevels)
            {
                if (IsValidWorksheetRow(rowNum))
                    xlSheet.Row((int)rowNum).OutlineLevel = level;
            }

            foreach (var rowNum in sheet.GroupHiddenRows)
            {
                if (IsValidWorksheetRow(rowNum))
                    xlSheet.Row((int)rowNum).Collapse();
            }

            foreach (var (colNum, width) in sheet.ColumnWidths)
            {
                if (IsValidWorksheetColumn(colNum) && double.IsFinite(width) && width > 0)
                    xlSheet.Column((int)colNum).Width = width;
            }

            foreach (var colNum in sheet.HiddenCols)
            {
                if (IsValidWorksheetColumn(colNum))
                    xlSheet.Column((int)colNum).Hide();
            }

            foreach (var (colNum, level) in sheet.ColOutlineLevels)
            {
                if (IsValidWorksheetColumn(colNum))
                    xlSheet.Column((int)colNum).OutlineLevel = level;
            }

            foreach (var colNum in sheet.GroupHiddenCols)
            {
                if (IsValidWorksheetColumn(colNum))
                    xlSheet.Column((int)colNum).Collapse();
            }

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

            var frozenRows = ValidFrozenRowsOrZero(sheet.FrozenRows);
            var frozenCols = ValidFrozenColumnsOrZero(sheet.FrozenCols);
            if (frozenRows > 0 || frozenCols > 0)
                xlSheet.SheetView.Freeze((int)frozenRows, (int)frozenCols);

            if (sheet.PrintArea is { } printArea)
            {
                xlSheet.PageSetup.PrintAreas.Clear();
                xlSheet.PageSetup.PrintAreas.Add(
                    (int)printArea.Start.Row,
                    (int)printArea.Start.Col,
                    (int)printArea.End.Row,
                    (int)printArea.End.Col);
            }

            var pageOrientation = ValidEnumOrDefault(sheet.PageOrientation, WorksheetPageOrientation.Portrait);
            var paperSize = ValidEnumOrDefault(sheet.PaperSize, WorksheetPaperSize.A4);
            var pageMargins = ValidPageMarginsOrDefault(sheet.PageMargins, WorksheetPageMargins.Narrow);
            var headerMargin = NonNegativeFiniteOrDefault(sheet.HeaderMargin, 0.3);
            var footerMargin = NonNegativeFiniteOrDefault(sheet.FooterMargin, 0.3);
            var scaleToFit = ValidScaleToFitOrDefault(sheet.ScaleToFit, WorksheetScaleToFit.Default);
            var pageOrder = ValidEnumOrDefault(sheet.PageOrder, WorksheetPageOrder.DownThenOver);
            var printErrorValue = ValidEnumOrDefault(sheet.PrintErrorValue, WorksheetPrintErrorValue.Displayed);
            var printComments = ValidEnumOrDefault(sheet.PrintComments, WorksheetPrintComments.None);

            xlSheet.PageSetup.PageOrientation = pageOrientation == WorksheetPageOrientation.Landscape
                ? XLPageOrientation.Landscape
                : XLPageOrientation.Portrait;
            xlSheet.PageSetup.PaperSize = paperSize switch
            {
                WorksheetPaperSize.Letter => XLPaperSize.LetterPaper,
                WorksheetPaperSize.Legal => XLPaperSize.LegalPaper,
                _ => XLPaperSize.A4Paper
            };
            xlSheet.PageSetup.Margins.Left = pageMargins.Left;
            xlSheet.PageSetup.Margins.Right = pageMargins.Right;
            xlSheet.PageSetup.Margins.Top = pageMargins.Top;
            xlSheet.PageSetup.Margins.Bottom = pageMargins.Bottom;
            xlSheet.PageSetup.Margins.Header = headerMargin;
            xlSheet.PageSetup.Margins.Footer = footerMargin;
            xlSheet.PageSetup.ShowGridlines = sheet.PrintGridlines;
            xlSheet.PageSetup.ShowRowAndColumnHeadings = sheet.PrintHeadings;
            xlSheet.PageSetup.CenterHorizontally = sheet.CenterHorizontallyOnPage;
            xlSheet.PageSetup.CenterVertically = sheet.CenterVerticallyOnPage;
            xlSheet.PageSetup.PageOrder = pageOrder == WorksheetPageOrder.OverThenDown
                ? XLPageOrderValues.OverThenDown
                : XLPageOrderValues.DownThenOver;
            if (sheet.FirstPageNumber is { } firstPageNumber && firstPageNumber > 0)
                xlSheet.PageSetup.FirstPageNumber = firstPageNumber;
            xlSheet.PageSetup.BlackAndWhite = sheet.PrintBlackAndWhite;
            xlSheet.PageSetup.DraftQuality = sheet.PrintDraftQuality;
            if (sheet.PrintQualityDpi is { } printQualityDpi && printQualityDpi > 0)
            {
                xlSheet.PageSetup.HorizontalDpi = printQualityDpi;
                xlSheet.PageSetup.VerticalDpi = printQualityDpi;
            }
            xlSheet.PageSetup.PrintErrorValue = ToXlsxPrintErrorValue(printErrorValue);
            xlSheet.PageSetup.ShowComments = ToXlsxPrintComments(printComments);
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
            if (scaleToFit.ScalePercent is { } scalePercent)
                xlSheet.PageSetup.Scale = scalePercent;
            else if (scaleToFit.FitToPagesWide.HasValue || scaleToFit.FitToPagesTall.HasValue)
                xlSheet.PageSetup.FitToPages(scaleToFit.FitToPagesWide ?? 1, scaleToFit.FitToPagesTall ?? 1);
            if (sheet.PrintTitleRows is { } titleRows && IsValidRepeatRange(titleRows, CellAddress.MaxRow))
                xlSheet.PageSetup.SetRowsToRepeatAtTop((int)titleRows.Start, (int)titleRows.End);
            if (sheet.PrintTitleColumns is { } titleColumns && IsValidRepeatRange(titleColumns, CellAddress.MaxCol))
                xlSheet.PageSetup.SetColumnsToRepeatAtLeft((int)titleColumns.Start, (int)titleColumns.End);
            foreach (var rowBreak in sheet.RowPageBreaks)
                if (rowBreak is >= 2 and <= CellAddress.MaxRow)
                    xlSheet.PageSetup.AddHorizontalPageBreak((int)rowBreak);
            foreach (var columnBreak in sheet.ColumnPageBreaks)
                if (columnBreak is >= 2 and <= CellAddress.MaxCol)
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

        if (workbook.IsStructureProtected)
        {
            packageStream.Position = 0;
            SaveWorkbookProtection(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.BackgroundImage is not null))
        {
            packageStream.Position = 0;
            SaveWorksheetBackgrounds(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet =>
                !sheet.ShowGridlines ||
                !sheet.ShowHeadings ||
                !sheet.ShowRulers ||
                ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal) != WorksheetViewMode.Normal ||
                sheet.ZoomPercent != 100 ||
                sheet.ShowFormulas ||
                (sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
                 (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue))))
        {
            packageStream.Position = 0;
            SaveSplitPaneSheetViews(packageStream, workbook);
        }

        packageStream.Position = 0;
        SaveWorkbookTheme(packageStream, workbook.Theme);

        if (workbook.Sheets.Any(sheet => sheet.Charts.Any(IsSupportedXlsxChart)))
        {
            packageStream.Position = 0;
            SaveWorksheetCharts(packageStream, workbook);
        }

        packageStream.Position = 0;
        packageStream.CopyTo(stream);
    }

    private static void SaveWorkbookTheme(Stream xlsxStream, WorkbookTheme theme)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        const string themePath = "xl/theme/theme1.xml";
        archive.GetEntry(themePath)?.Delete();
        var themeEntry = archive.CreateEntry(themePath);
        using var stream = themeEntry.Open();
        ToThemeXml(theme).Save(stream);
    }

    private static void SaveWorkbookProtection(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = LoadXml(workbookEntry);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = workbookXml.Root;
        if (root is null)
            return;

        root.Element(workbookNs + "workbookProtection")?.Remove();
        var protection = new XElement(workbookNs + "workbookProtection",
            new XAttribute("lockStructure", "1"));
        if (!string.IsNullOrWhiteSpace(workbook.StructureProtectionPassword))
            protection.SetAttributeValue("workbookPassword", ToLegacyPasswordHash(workbook.StructureProtectionPassword));

        var sheets = root.Element(workbookNs + "sheets");
        if (sheets is not null)
            sheets.AddBeforeSelf(protection);
        else
            root.Add(protection);

        workbookEntry.Delete();
        var replacement = archive.CreateEntry("xl/workbook.xml");
        using var stream = replacement.Open();
        workbookXml.Save(stream);
    }

    private static string ToLegacyPasswordHash(string passwordOrHash)
    {
        if (IsLegacyPasswordHash(passwordOrHash))
            return passwordOrHash.ToUpperInvariant();

        var hash = 0;
        for (var i = 0; i < passwordOrHash.Length; i++)
        {
            var value = passwordOrHash[i] << (i + 1);
            var rotatedBits = value >> 15;
            value &= 0x7fff;
            hash ^= value | rotatedBits;
        }

        hash ^= passwordOrHash.Length;
        hash ^= 0xCE4B;
        return hash.ToString("X4", CultureInfo.InvariantCulture);
    }

    private static bool IsLegacyPasswordHash(string value) =>
        value.Length is > 0 and <= 4 &&
        value.All(ch =>
            ch is >= '0' and <= '9' ||
            ch is >= 'A' and <= 'F' ||
            ch is >= 'a' and <= 'f');

    private static XDocument ToThemeXml(WorkbookTheme theme)
    {
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(drawingNs + "theme",
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute("name", theme.Name),
                new XElement(drawingNs + "themeElements",
                    new XElement(drawingNs + "clrScheme",
                        new XAttribute("name", $"{theme.Name} Colors"),
                        ThemeColorElements.Select(color =>
                            new XElement(drawingNs + color.ElementName,
                                new XElement(drawingNs + "srgbClr",
                                    new XAttribute("val", FormatThemeColor(theme.GetColor(color.Slot))))))),
                    new XElement(drawingNs + "fontScheme",
                        new XAttribute("name", $"{theme.Name} Fonts"),
                        new XElement(drawingNs + "majorFont",
                            new XElement(drawingNs + "latin",
                                new XAttribute("typeface", theme.MajorFontName))),
                        new XElement(drawingNs + "minorFont",
                            new XElement(drawingNs + "latin",
                                new XAttribute("typeface", theme.MinorFontName)))),
                    new XElement(drawingNs + "fmtScheme",
                        new XAttribute("name", theme.EffectsName)))));
    }

    private static string FormatThemeColor(CellColor color) =>
        $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void SaveWorksheetCharts(Stream xlsxStream, Workbook workbook)
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

        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);
        var drawingIndex = 1;
        var chartIndex = 1;
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId))
                continue;
            if (!sheetsByName.TryGetValue(name, out var sheet))
                continue;
            var supportedCharts = sheet.Charts
                .Where(IsSupportedXlsxChart)
                .ToList();
            if (supportedCharts.Count == 0)
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            WriteWorksheetCharts(archive, worksheetPath, sheet, supportedCharts, drawingIndex++, ref chartIndex);
        }
    }

    private static void WriteWorksheetCharts(
        ZipArchive archive,
        string worksheetPath,
        Sheet sheet,
        IReadOnlyList<ChartModel> charts,
        int drawingIndex,
        ref int chartIndex)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

        var drawingPath = $"xl/drawings/drawing{drawingIndex}.xml";
        var drawingRelsPath = GetWorksheetRelsPath(drawingPath);
        archive.GetEntry(drawingPath)?.Delete();
        archive.GetEntry(drawingRelsPath)?.Delete();

        var drawingRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        var anchors = new List<XElement>();
        foreach (var chart in charts)
        {
            var currentChartIndex = chartIndex++;
            var chartPath = $"xl/charts/chart{currentChartIndex}.xml";
            archive.GetEntry(chartPath)?.Delete();
            var chartEntry = archive.CreateEntry(chartPath);
            using (var chartStream = chartEntry.Open())
                ToChartXml(chart, sheet).Save(chartStream);

            var chartRelId = $"rIdFreexcelChart{currentChartIndex}";
            drawingRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", chartRelId),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"),
                new XAttribute("Target", GetRelativeTarget(drawingPath, chartPath))));

            anchors.Add(ToAbsoluteChartAnchor(chart, currentChartIndex, chartRelId, spreadsheetDrawingNs, drawingNs, chartNs, relNs));
        }

        var drawingXml = new XDocument(
            new XElement(spreadsheetDrawingNs + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute(XNamespace.Xmlns + "c", chartNs),
                new XAttribute(XNamespace.Xmlns + "r", relNs),
                anchors));
        var drawingEntry = archive.CreateEntry(drawingPath);
        using (var drawingStream = drawingEntry.Open())
            drawingXml.Save(drawingStream);

        var drawingRelsEntry = archive.CreateEntry(drawingRelsPath);
        using (var drawingRelsStream = drawingRelsEntry.Open())
            drawingRelsXml.Save(drawingRelsStream);

        EnsureContentTypeOverride(archive, $"/{drawingPath}", "application/vnd.openxmlformats-officedocument.drawing+xml");
        for (var i = chartIndex - charts.Count; i < chartIndex; i++)
            EnsureContentTypeOverride(archive, $"/xl/charts/chart{i}.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml");

        var relsPath = GetWorksheetRelsPath(worksheetPath);
        var relsEntry = archive.GetEntry(relsPath);
        XDocument worksheetRelsXml;
        if (relsEntry is null)
        {
            worksheetRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        }
        else
        {
            worksheetRelsXml = LoadXml(relsEntry);
            relsEntry.Delete();
        }

        var drawingRelId = NextRelationshipId(worksheetRelsXml, packageRelNs);
        worksheetRelsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", drawingRelId),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
            new XAttribute("Target", GetRelativeTarget(worksheetPath, drawingPath))));
        var updatedRelsEntry = archive.CreateEntry(relsPath);
        using (var relsStream = updatedRelsEntry.Open())
            worksheetRelsXml.Save(relsStream);

        var worksheetXml = LoadXml(worksheetEntry);
        var root = worksheetXml.Root;
        if (root is null)
            return;

        root.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
        root.Elements(worksheetNs + "drawing").Remove();
        root.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", drawingRelId)));

        worksheetEntry.Delete();
        var updatedWorksheetEntry = archive.CreateEntry(worksheetPath);
        using var worksheetStream = updatedWorksheetEntry.Open();
        worksheetXml.Save(worksheetStream);
    }

    private static XElement ToAbsoluteChartAnchor(
        ChartModel chart,
        int chartIndex,
        string chartRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace relNs) =>
        new(spreadsheetDrawingNs + "absoluteAnchor",
            new XElement(spreadsheetDrawingNs + "pos",
                new XAttribute("x", PixelsToEmus(chart.Left)),
                new XAttribute("y", PixelsToEmus(chart.Top))),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", PixelsToEmus(chart.Width)),
                new XAttribute("cy", PixelsToEmus(chart.Height))),
            new XElement(spreadsheetDrawingNs + "graphicFrame",
                new XElement(spreadsheetDrawingNs + "nvGraphicFramePr",
                    new XElement(spreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", chartIndex + 1),
                        new XAttribute("name", $"Chart {chartIndex}")),
                    new XElement(spreadsheetDrawingNs + "cNvGraphicFramePr")),
                new XElement(spreadsheetDrawingNs + "xfrm"),
                new XElement(drawingNs + "graphic",
                    new XElement(drawingNs + "graphicData",
                        new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/chart"),
                        new XElement(chartNs + "chart", new XAttribute(relNs + "id", chartRelId))))),
            new XElement(spreadsheetDrawingNs + "clientData"));

    private static XDocument ToChartXml(ChartModel chart, Sheet sheet)
    {
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var plotCharts = ToPlotChartXml(chart, sheet, chartNs, drawingNs).ToList();

        return new XDocument(
            new XElement(chartNs + "chartSpace",
                new XAttribute(XNamespace.Xmlns + "c", chartNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                ToChartAreaShapeProperties(chart, chartNs, drawingNs),
                new XElement(chartNs + "chart",
                    string.IsNullOrWhiteSpace(chart.Title)
                        ? null
                        : ToChartTitleXml(chart, chartNs, drawingNs),
                    new XElement(chartNs + "plotArea",
                        plotCharts,
                        ShouldWriteChartAxes(chart.Type)
                            ? ToChartAxesXml(chart, chartNs, drawingNs)
                            : null,
                        ToPlotAreaShapeProperties(chart, chartNs, drawingNs)),
                    ToLegendXml(chart, chartNs, drawingNs))));
    }

    private static bool ShouldWriteChartAxes(ChartType chartType) =>
        chartType is not ChartType.Pie and not ChartType.Doughnut;

    private static IEnumerable<XElement> ToPlotChartXml(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var dataLabelWritten = false;
        foreach (var (plotChart, usesSecondaryAxis) in CreatePlotCharts(chart, sheet, chartNs, drawingNs))
        {
            AddPlotChartCommonElements(plotChart, chart, chartNs, drawingNs, usesSecondaryAxis, includeDataLabels: !dataLabelWritten);
            dataLabelWritten = true;
            yield return plotChart;
        }
    }

    private static IEnumerable<(XElement PlotChart, bool UsesSecondaryAxis)> CreatePlotCharts(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        var secondaryIndexes = GetSecondaryAxisSeriesIndexes(chart, seriesCount);
        var comboLineIndexes = GetComboLineSeriesIndexes(chart, seriesCount);
        if (secondaryIndexes.Count > 0 && chart.Type == ChartType.Scatter)
        {
            var primaryScatter = Enumerable.Range(0, seriesCount)
                .Where(index => !secondaryIndexes.Contains(index))
                .ToHashSet();
            if (primaryScatter.Count > 0)
                yield return (CreateScatterPlotChart(chart, sheet, chartNs, drawingNs, primaryScatter.Contains), false);
            yield return (CreateScatterPlotChart(chart, sheet, chartNs, drawingNs, secondaryIndexes.Contains), true);
            yield break;
        }

        if (secondaryIndexes.Count > 0 && chart.Type == ChartType.Line)
        {
            var primaryLine = Enumerable.Range(0, seriesCount)
                .Where(index => !secondaryIndexes.Contains(index))
                .ToHashSet();
            if (primaryLine.Count > 0)
                yield return (CreateLinePlotChart(chart, sheet, chartNs, drawingNs, primaryLine.Contains), false);
            yield return (CreateLinePlotChart(chart, sheet, chartNs, drawingNs, secondaryIndexes.Contains), true);
            yield break;
        }

        if ((secondaryIndexes.Count > 0 || comboLineIndexes.Count > 0) &&
            chart.Type is ChartType.Column or ChartType.StackedColumn or ChartType.PercentStackedColumn or ChartType.Area)
        {
            var primaryBase = Enumerable.Range(0, seriesCount)
                .Where(index => !secondaryIndexes.Contains(index) && !comboLineIndexes.Contains(index))
                .ToHashSet();
            var secondaryBase = secondaryIndexes
                .Where(index => !comboLineIndexes.Contains(index))
                .ToHashSet();
            var primaryLine = comboLineIndexes
                .Where(index => !secondaryIndexes.Contains(index))
                .ToHashSet();
            var secondaryLine = comboLineIndexes
                .Where(secondaryIndexes.Contains)
                .ToHashSet();

            if (primaryBase.Count > 0)
                yield return (CreateNativePlotChart(chart, sheet, chartNs, drawingNs, primaryBase.Contains), false);
            if (secondaryBase.Count > 0)
                yield return (CreateNativePlotChart(chart, sheet, chartNs, drawingNs, secondaryBase.Contains), true);
            if (primaryLine.Count > 0)
                yield return (CreateLinePlotChart(chart, sheet, chartNs, drawingNs, primaryLine.Contains), false);
            if (secondaryLine.Count > 0)
                yield return (CreateLinePlotChart(chart, sheet, chartNs, drawingNs, secondaryLine.Contains), true);

            yield break;
        }

        yield return (CreateNativePlotChart(chart, sheet, chartNs, drawingNs, _ => true), false);
    }

    private static XElement CreateNativePlotChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        chart.Type switch
        {
            ChartType.Line => CreateLinePlotChart(chart, sheet, chartNs, drawingNs, includeSeries),
            ChartType.Scatter => CreateScatterPlotChart(chart, sheet, chartNs, drawingNs, includeSeries),
            ChartType.Area => new XElement(chartNs + "areaChart",
                new XElement(chartNs + "grouping", new XAttribute("val", "standard")),
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries)),
            ChartType.Bubble => new XElement(chartNs + "bubbleChart",
                BuildBubbleChartSeries(chart, sheet, chartNs, drawingNs)),
            ChartType.Pie => new XElement(chartNs + "pieChart",
                ToFirstSliceAngleXml(chart, chartNs),
                BuildPieFamilyChartSeries(chart, sheet, chartNs, drawingNs)),
            ChartType.Doughnut => new XElement(chartNs + "doughnutChart",
                ToFirstSliceAngleXml(chart, chartNs),
                BuildPieFamilyChartSeries(chart, sheet, chartNs, drawingNs),
                new XElement(chartNs + "holeSize",
                    new XAttribute("val", Math.Clamp((int)Math.Round(chart.DoughnutHoleSize * 100), 10, 90)))),
            _ => new XElement(chartNs + "barChart",
                new XElement(chartNs + "barDir", new XAttribute("val", ToXlsxBarDirection(chart.Type))),
                new XElement(chartNs + "grouping", new XAttribute("val", ToXlsxBarGrouping(chart.Type))),
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries))
        };

    private static XElement CreateLinePlotChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        new(chartNs + "lineChart",
            BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries, forceLineShapeProperties: true));

    private static XElement CreateScatterPlotChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        new(chartNs + "scatterChart",
            new XElement(chartNs + "scatterStyle", new XAttribute("val", "lineMarker")),
            BuildScatterChartSeries(chart, sheet, chartNs, drawingNs, includeSeries));

    private static void AddPlotChartCommonElements(
        XElement plotChart,
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs,
        bool usesSecondaryAxis,
        bool includeDataLabels)
    {
        if (includeDataLabels && ToDataLabelsXml(chart, chartNs, drawingNs) is { } dataLabels)
            plotChart.Add(dataLabels);

        if (!ShouldWriteChartAxes(chart.Type))
            return;

        plotChart.Add(
            new XElement(chartNs + "axId", new XAttribute("val", CategoryAxisId)),
            new XElement(chartNs + "axId", new XAttribute("val", usesSecondaryAxis ? SecondaryValueAxisId : ValueAxisId)));
    }

    private static IEnumerable<XElement> ToChartAxesXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.Type is ChartType.Scatter or ChartType.Bubble)
        {
            yield return ToValueAxisXml(
                chart.XAxisTitle,
                CategoryAxisId,
                ValueAxisId,
                "b",
                chart.XAxisMinimum,
                chart.XAxisMaximum,
                chart.XAxisMajorUnit,
                chart.XAxisMinorUnit,
                chart.XAxisLogScale,
                chart.XAxisNumberFormat,
                chart.ShowXAxisMajorGridlines,
                chart.ShowXAxisMinorGridlines,
                chart.XAxisMajorGridlineColor,
                chart.XAxisMinorGridlineColor,
                chart.XAxisGridlineThickness,
                chart.XAxisMajorTickStyle,
                chart.XAxisMinorTickStyle,
                chart.XAxisLineColor,
                chart.XAxisLineThickness,
                chart.ShowXAxisLabels,
                chart.AxisTitleTextColor,
                chart.AxisTitleFontSize,
                chartNs,
                drawingNs);
            yield return ToValueAxisXml(
                chart.YAxisTitle,
                ValueAxisId,
                CategoryAxisId,
                "l",
                chart.YAxisMinimum,
                chart.YAxisMaximum,
                chart.YAxisMajorUnit,
                chart.YAxisMinorUnit,
                chart.YAxisLogScale,
                chart.YAxisNumberFormat,
                chart.ShowYAxisMajorGridlines,
                chart.ShowYAxisMinorGridlines,
                chart.YAxisMajorGridlineColor,
                chart.YAxisMinorGridlineColor,
                chart.YAxisGridlineThickness,
                chart.YAxisMajorTickStyle,
                chart.YAxisMinorTickStyle,
                chart.YAxisLineColor,
                chart.YAxisLineThickness,
                chart.ShowYAxisLabels,
                chart.AxisTitleTextColor,
                chart.AxisTitleFontSize,
                chartNs,
                drawingNs);
            var scatterSecondaryIndexes = GetSecondaryAxisSeriesIndexes(chart, ChartTypeSupport.GetDataSeriesCount(chart));
            if (chart.Type == ChartType.Scatter && scatterSecondaryIndexes.Count > 0)
            {
                yield return ToValueAxisXml(
                    null,
                    SecondaryValueAxisId,
                    CategoryAxisId,
                    "r",
                    chart.YAxisMinimum,
                    chart.YAxisMaximum,
                    chart.YAxisMajorUnit,
                    chart.YAxisMinorUnit,
                    chart.YAxisLogScale,
                    chart.YAxisNumberFormat,
                    false,
                    false,
                    null,
                    null,
                    chart.YAxisGridlineThickness,
                    chart.YAxisMajorTickStyle,
                    chart.YAxisMinorTickStyle,
                    chart.YAxisLineColor,
                    chart.YAxisLineThickness,
                    chart.ShowYAxisLabels,
                    chart.AxisTitleTextColor,
                    chart.AxisTitleFontSize,
                    chartNs,
                    drawingNs);
            }
            yield break;
        }

        yield return ToCategoryAxisXml(chart, chartNs, drawingNs);
        yield return ToValueAxisXml(
            chart.YAxisTitle,
            ValueAxisId,
            CategoryAxisId,
            "l",
            chart.YAxisMinimum,
            chart.YAxisMaximum,
            chart.YAxisMajorUnit,
            chart.YAxisMinorUnit,
            chart.YAxisLogScale,
            chart.YAxisNumberFormat,
            chart.ShowYAxisMajorGridlines,
            chart.ShowYAxisMinorGridlines,
            chart.YAxisMajorGridlineColor,
            chart.YAxisMinorGridlineColor,
            chart.YAxisGridlineThickness,
            chart.YAxisMajorTickStyle,
            chart.YAxisMinorTickStyle,
            chart.YAxisLineColor,
            chart.YAxisLineThickness,
            chart.ShowYAxisLabels,
            chart.AxisTitleTextColor,
            chart.AxisTitleFontSize,
            chartNs,
            drawingNs);

        var secondaryIndexes = GetSecondaryAxisSeriesIndexes(chart, ChartTypeSupport.GetDataSeriesCount(chart));
        if (secondaryIndexes.Count > 0)
        {
            yield return ToValueAxisXml(
                null,
                SecondaryValueAxisId,
                CategoryAxisId,
                "r",
                chart.YAxisMinimum,
                chart.YAxisMaximum,
                chart.YAxisMajorUnit,
                chart.YAxisMinorUnit,
                chart.YAxisLogScale,
                chart.YAxisNumberFormat,
                false,
                false,
                null,
                null,
                chart.YAxisGridlineThickness,
                chart.YAxisMajorTickStyle,
                chart.YAxisMinorTickStyle,
                chart.YAxisLineColor,
                chart.YAxisLineThickness,
                chart.ShowYAxisLabels,
                chart.AxisTitleTextColor,
                chart.AxisTitleFontSize,
                chartNs,
                drawingNs);
        }
    }

    private static XElement ToCategoryAxisXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs) =>
        new(chartNs + "catAx",
            new XElement(chartNs + "axId", new XAttribute("val", CategoryAxisId)),
            new XElement(chartNs + "scaling",
                new XElement(chartNs + "orientation", new XAttribute("val", "minMax"))),
            new XElement(chartNs + "delete", new XAttribute("val", "0")),
            new XElement(chartNs + "axPos", new XAttribute("val", "b")),
            ToAxisTitleXml(chart.XAxisTitle, chart.AxisTitleTextColor, chart.AxisTitleFontSize, chartNs, drawingNs),
            ToAxisGridlinesXml("majorGridlines", chart.ShowXAxisMajorGridlines, chart.XAxisMajorGridlineColor, chart.XAxisGridlineThickness, chartNs, drawingNs),
            ToAxisGridlinesXml("minorGridlines", chart.ShowXAxisMinorGridlines, chart.XAxisMinorGridlineColor, chart.XAxisGridlineThickness, chartNs, drawingNs),
            new XElement(chartNs + "majorTickMark", new XAttribute("val", ToXlsxTickMark(chart.XAxisMajorTickStyle))),
            new XElement(chartNs + "minorTickMark", new XAttribute("val", ToXlsxTickMark(chart.XAxisMinorTickStyle))),
            new XElement(chartNs + "tickLblPos", new XAttribute("val", ToXlsxTickLabelPosition(chart.ShowXAxisLabels))),
            ToAxisLineShapeProperties(chart.XAxisLineColor, chart.XAxisLineThickness, chartNs, drawingNs),
            new XElement(chartNs + "crossAx", new XAttribute("val", ValueAxisId)),
            new XElement(chartNs + "crosses", new XAttribute("val", "autoZero")));

    private static XElement ToValueAxisXml(
        string? title,
        int axisId,
        int crossAxisId,
        string axisPosition,
        double? minimum,
        double? maximum,
        double? majorUnit,
        double? minorUnit,
        bool logScale,
        ChartDataLabelNumberFormat numberFormat,
        bool showMajorGridlines,
        bool showMinorGridlines,
        CellColor? majorGridlineColor,
        CellColor? minorGridlineColor,
        double gridlineThickness,
        ChartAxisTickStyle majorTickStyle,
        ChartAxisTickStyle minorTickStyle,
        CellColor? lineColor,
        double lineThickness,
        bool showLabels,
        CellColor? axisTitleTextColor,
        double axisTitleFontSize,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        new(chartNs + "valAx",
            new XElement(chartNs + "axId", new XAttribute("val", axisId)),
            new XElement(chartNs + "scaling",
                logScale ? new XElement(chartNs + "logBase", new XAttribute("val", "10")) : null,
                new XElement(chartNs + "orientation", new XAttribute("val", "minMax")),
                ToAxisBoundXml("max", maximum, chartNs),
                ToAxisBoundXml("min", minimum, chartNs)),
            new XElement(chartNs + "delete", new XAttribute("val", "0")),
            new XElement(chartNs + "axPos", new XAttribute("val", axisPosition)),
            ToAxisTitleXml(title, axisTitleTextColor, axisTitleFontSize, chartNs, drawingNs),
            new XElement(chartNs + "numFmt",
                new XAttribute("formatCode", ToXlsxNumberFormatCode(numberFormat)),
                new XAttribute("sourceLinked", numberFormat == ChartDataLabelNumberFormat.General ? "1" : "0")),
            ToAxisGridlinesXml("majorGridlines", showMajorGridlines, majorGridlineColor, gridlineThickness, chartNs, drawingNs),
            ToAxisGridlinesXml("minorGridlines", showMinorGridlines, minorGridlineColor, gridlineThickness, chartNs, drawingNs),
            ToAxisUnitXml("majorUnit", majorUnit, chartNs),
            ToAxisUnitXml("minorUnit", minorUnit, chartNs),
            new XElement(chartNs + "majorTickMark", new XAttribute("val", ToXlsxTickMark(majorTickStyle))),
            new XElement(chartNs + "minorTickMark", new XAttribute("val", ToXlsxTickMark(minorTickStyle))),
            new XElement(chartNs + "tickLblPos", new XAttribute("val", ToXlsxTickLabelPosition(showLabels))),
            ToAxisLineShapeProperties(lineColor, lineThickness, chartNs, drawingNs),
            new XElement(chartNs + "crossAx", new XAttribute("val", crossAxisId)),
            new XElement(chartNs + "crosses", new XAttribute("val", "autoZero")));

    private static XElement? ToAxisGridlinesXml(
        string elementName,
        bool visible,
        CellColor? color,
        double thickness,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (!visible)
            return null;

        return new XElement(chartNs + elementName,
            ToShapeProperties(
                chartNs,
                drawingNs,
                fillThemeColor: null,
                fillColor: null,
                borderThemeColor: null,
                borderColor: color,
                borderThickness: Math.Clamp(thickness, 0.25, 10)));
    }

    private static XElement? ToAxisLineShapeProperties(
        CellColor? lineColor,
        double lineThickness,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        ToShapeProperties(
            chartNs,
            drawingNs,
            fillThemeColor: null,
            fillColor: null,
            borderThemeColor: null,
            borderColor: lineColor,
            borderThickness: Math.Clamp(lineThickness, 0.5, 10));

    private static string ToXlsxTickMark(ChartAxisTickStyle tickStyle) =>
        tickStyle switch
        {
            ChartAxisTickStyle.None => "none",
            ChartAxisTickStyle.Inside => "in",
            ChartAxisTickStyle.Cross => "cross",
            _ => "out"
        };

    private static string ToXlsxTickLabelPosition(bool showLabels) =>
        showLabels ? "nextTo" : "none";

    private static XElement? ToAxisBoundXml(string elementName, double? value, XNamespace chartNs) =>
        value is { } numeric && double.IsFinite(numeric)
            ? new XElement(chartNs + elementName, new XAttribute("val", numeric.ToString(CultureInfo.InvariantCulture)))
            : null;

    private static XElement? ToAxisUnitXml(string elementName, double? value, XNamespace chartNs) =>
        value is { } numeric && double.IsFinite(numeric)
            ? new XElement(chartNs + elementName, new XAttribute("val", Math.Max(numeric, double.Epsilon).ToString(CultureInfo.InvariantCulture)))
            : null;

    private static string ToXlsxNumberFormatCode(ChartDataLabelNumberFormat format) =>
        format switch
        {
            ChartDataLabelNumberFormat.Number => "0.00",
            ChartDataLabelNumberFormat.Currency => "$#,##0.00",
            ChartDataLabelNumberFormat.Percent => "0%",
            _ => "General"
        };

    private static XElement ToChartTitleXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs) =>
        new(chartNs + "title",
            new XElement(chartNs + "tx",
                new XElement(chartNs + "rich",
                    new XElement(drawingNs + "p",
                        new XElement(drawingNs + "r",
                            ToTextRunProperties(null, chart.ChartTitleTextColor, chart.ChartTitleFontSize, drawingNs),
                            new XElement(drawingNs + "t", chart.Title))))));

    private static XElement? ToAxisTitleXml(
        string? title,
        CellColor? textColor,
        double fontSize,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        string.IsNullOrWhiteSpace(title)
            ? null
            : new XElement(chartNs + "title",
                new XElement(chartNs + "tx",
                    new XElement(chartNs + "rich",
                        new XElement(drawingNs + "p",
                            new XElement(drawingNs + "r",
                                ToTextRunProperties(null, textColor, fontSize, drawingNs),
                                new XElement(drawingNs + "t", title))))));

    private static XElement? ToTextRunProperties(
        WorkbookThemeColorReference? textThemeColor,
        CellColor? textColor,
        double fontSize,
        XNamespace drawingNs)
    {
        var size = Math.Clamp((int)Math.Round(fontSize * 100), 600, 7200);
        return new XElement(drawingNs + "rPr",
            new XAttribute("sz", size),
            ToTextRunPropertiesContent(textThemeColor, textColor, fontSize, drawingNs));
    }

    private static IEnumerable<object> ToTextRunPropertiesContent(
        WorkbookThemeColorReference? textThemeColor,
        CellColor? textColor,
        double fontSize,
        XNamespace drawingNs)
    {
        var fill = ToSolidFill(textThemeColor, textColor, drawingNs);
        if (fill is not null)
        {
            yield return fill;
        }
    }

    private static XElement? ToChartAreaShapeProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        ToShapeProperties(
            chartNs,
            drawingNs,
            chart.ChartAreaFillThemeColor,
            chart.ChartAreaFillColor,
            borderThemeColor: null,
            borderColor: null,
            borderThickness: null);

    private static XElement? ToPlotAreaShapeProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        ToShapeProperties(
            chartNs,
            drawingNs,
            chart.PlotAreaFillThemeColor,
            chart.PlotAreaFillColor,
            chart.PlotAreaBorderThemeColor,
            chart.PlotAreaBorderColor,
            chart.PlotAreaBorderThickness);

    private static XElement? ToShapeProperties(
        XNamespace chartNs,
        XNamespace drawingNs,
        WorkbookThemeColorReference? fillThemeColor,
        CellColor? fillColor,
        WorkbookThemeColorReference? borderThemeColor,
        CellColor? borderColor,
        double? borderThickness)
    {
        var fill = ToSolidFill(fillThemeColor, fillColor, drawingNs);
        var lineFill = ToSolidFill(borderThemeColor, borderColor, drawingNs);
        var line = lineFill is null && borderThickness is null
            ? null
            : new XElement(drawingNs + "ln",
                borderThickness is null
                    ? null
                    : new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(borderThickness.Value, 0, 10) * 12700))),
                lineFill);

        return fill is null && line is null
            ? null
            : new XElement(chartNs + "spPr", fill, line);
    }

    private static XElement? ToSolidFill(
        WorkbookThemeColorReference? themeColor,
        CellColor? color,
        XNamespace drawingNs)
    {
        XElement? colorElement = null;
        if (themeColor is { } theme)
        {
            colorElement = new XElement(drawingNs + "schemeClr",
                new XAttribute("val", ToDrawingSchemeColor(theme.Slot)));
            ApplyTint(colorElement, theme.Tint, drawingNs);
        }
        else if (color is { } concrete)
        {
            colorElement = new XElement(drawingNs + "srgbClr",
                new XAttribute("val", FormatThemeColor(concrete)));
        }

        return colorElement is null
            ? null
            : new XElement(drawingNs + "solidFill", colorElement);
    }

    private static void ApplyTint(XElement colorElement, double tint, XNamespace drawingNs)
    {
        if (tint > 0)
        {
            colorElement.Add(
                new XElement(drawingNs + "lumMod", new XAttribute("val", Math.Clamp((int)Math.Round((1 - tint) * 100000), 0, 100000))),
                new XElement(drawingNs + "lumOff", new XAttribute("val", Math.Clamp((int)Math.Round(tint * 100000), 0, 100000))));
        }
        else if (tint < 0)
        {
            colorElement.Add(new XElement(drawingNs + "lumMod",
                new XAttribute("val", Math.Clamp((int)Math.Round((1 + tint) * 100000), 0, 100000))));
        }
    }

    private static XElement? ToLegendXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return null;

        return new XElement(chartNs + "legend",
            new XElement(chartNs + "legendPos",
                new XAttribute("val", ToXlsxLegendPosition(chart.LegendPosition))),
            new XElement(chartNs + "overlay",
                new XAttribute("val", chart.LegendOverlay ? "1" : "0")),
            ToShapeProperties(
                chartNs,
                drawingNs,
                chart.LegendFillThemeColor,
                chart.LegendFillColor,
                chart.LegendBorderThemeColor,
                chart.LegendBorderColor,
                chart.LegendBorderThickness),
            ToLegendTextProperties(chart, chartNs, drawingNs));
    }

    private static XElement? ToLegendTextProperties(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.LegendTextColor is null && chart.LegendTextThemeColor is null && chart.LegendFontSize == 12)
            return null;

        return new XElement(chartNs + "txPr",
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        new XAttribute("sz", Math.Clamp((int)Math.Round(chart.LegendFontSize * 100), 600, 7200)),
                        ToTextRunPropertiesContent(chart.LegendTextThemeColor, chart.LegendTextColor, chart.LegendFontSize, drawingNs)))));
    }

    private static XElement? ToDataLabelsXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (!chart.ShowDataLabels)
            return null;

        return new XElement(chartNs + "dLbls",
            new XElement(chartNs + "dLblPos", new XAttribute("val", ToXlsxDataLabelPosition(chart.DataLabelPosition))),
            new XElement(chartNs + "numFmt",
                new XAttribute("formatCode", ToXlsxNumberFormatCode(chart.DataLabelNumberFormat)),
                new XAttribute("sourceLinked", chart.DataLabelNumberFormat == ChartDataLabelNumberFormat.General ? "1" : "0")),
            ToShapeProperties(
                chartNs,
                drawingNs,
                chart.DataLabelFillThemeColor,
                chart.DataLabelFillColor,
                chart.DataLabelBorderThemeColor,
                chart.DataLabelBorderColor,
                chart.DataLabelBorderThickness),
            ToDataLabelTextProperties(chart, chartNs, drawingNs),
            new XElement(chartNs + "showLegendKey", new XAttribute("val", "0")),
            new XElement(chartNs + "showVal", new XAttribute("val", "1")),
            new XElement(chartNs + "showCatName", new XAttribute("val", chart.ShowDataLabelCategoryName ? "1" : "0")),
            new XElement(chartNs + "showSerName", new XAttribute("val", chart.ShowDataLabelSeriesName ? "1" : "0")),
            new XElement(chartNs + "showPercent", new XAttribute("val", chart.ShowDataLabelPercentage && ChartTypeSupport.SupportsPercentageDataLabels(chart.Type) ? "1" : "0")),
            new XElement(chartNs + "showBubbleSize", new XAttribute("val", "0")),
            new XElement(chartNs + "separator",
                chart.DataLabelSeparator == ChartDataLabelSeparator.NewLine
                    ? new XAttribute(XNamespace.Xml + "space", "preserve")
                    : null,
                ToXlsxDataLabelSeparator(chart.DataLabelSeparator)),
            new XElement(chartNs + "showLeaderLines", new XAttribute("val", chart.ShowDataLabelCallouts ? "1" : "0")));
    }

    private static XElement? ToDataLabelTextProperties(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.DataLabelTextColor is null && chart.DataLabelTextThemeColor is null && chart.DataLabelFontSize == 11 && chart.DataLabelAngle == 0)
            return null;

        var textFill = ToSolidFill(chart.DataLabelTextThemeColor, chart.DataLabelTextColor, drawingNs);
        return new XElement(chartNs + "txPr",
            ToTextBodyProperties(chart.DataLabelAngle, drawingNs),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        new XAttribute("sz", Math.Clamp((int)Math.Round(chart.DataLabelFontSize * 100), 600, 7200)),
                        textFill))));
    }

    private static XElement? ToTextBodyProperties(double angle, XNamespace drawingNs) =>
        angle == 0
            ? null
            : new XElement(drawingNs + "bodyPr",
                new XAttribute("rot", Math.Clamp((int)Math.Round(angle * 60000), -5400000, 5400000)));

    private static string ToXlsxDataLabelPosition(ChartDataLabelPosition position) =>
        position switch
        {
            ChartDataLabelPosition.Center => "ctr",
            ChartDataLabelPosition.InsideEnd => "inEnd",
            ChartDataLabelPosition.OutsideEnd => "outEnd",
            _ => "bestFit"
        };

    private static string ToXlsxDataLabelSeparator(ChartDataLabelSeparator separator) =>
        separator switch
        {
            ChartDataLabelSeparator.Semicolon => "; ",
            ChartDataLabelSeparator.NewLine => "\n",
            ChartDataLabelSeparator.Space => " ",
            _ => ", "
        };

    private static string ToXlsxLegendPosition(ChartLegendPosition position) =>
        position switch
        {
            ChartLegendPosition.Left => "l",
            ChartLegendPosition.Top => "t",
            ChartLegendPosition.Bottom => "b",
            _ => "r"
        };

    private static bool IsSupportedXlsxChart(ChartModel chart) =>
        ChartTypeSupport.GetDataSeriesCount(chart) > 0 &&
        ChartTypeSupport.GetDataPointCount(chart) > 0 &&
        (!Enum.IsDefined(chart.Type) ||
            chart.Type is ChartType.Column
                or ChartType.StackedColumn
                or ChartType.PercentStackedColumn
                or ChartType.Bar
                or ChartType.StackedBar
                or ChartType.PercentStackedBar
                or ChartType.Line
                or ChartType.Scatter
                or ChartType.Area
                or ChartType.Bubble
                or ChartType.Pie
                or ChartType.Doughnut);

    private static string ToXlsxBarDirection(ChartType chartType) =>
        chartType is ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar
            ? "bar"
            : "col";

    private static string ToXlsxBarGrouping(ChartType chartType) =>
        chartType switch
        {
            ChartType.StackedColumn or ChartType.StackedBar => "stacked",
            ChartType.PercentStackedColumn or ChartType.PercentStackedBar => "percentStacked",
            _ => "clustered"
        };

    private static IEnumerable<XElement> BuildChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool>? includeSeries = null,
        bool forceLineShapeProperties = false)
    {
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var seriesStartCol = chart.FirstColIsCategories ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        var categoryRange = chart.FirstColIsCategories
            ? FormatSheetRange(sheet.Name, dataStartRow, chart.DataRange.Start.Col, chart.DataRange.End.Row, chart.DataRange.Start.Col)
            : null;

        var seriesIndex = 0;
        for (var col = seriesStartCol; col <= chart.DataRange.End.Col; col++)
        {
            if (includeSeries is not null && !includeSeries(seriesIndex))
            {
                seriesIndex++;
                continue;
            }

            var valueRange = FormatSheetRange(sheet.Name, dataStartRow, col, chart.DataRange.End.Row, col);
            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                ToSeriesTitleXml(chart, sheet, col, chartNs),
                chart.Type == ChartType.Line || forceLineShapeProperties
                    ? ToSeriesLineShapeProperties(chart, seriesIndex, chartNs, drawingNs)
                    : ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                chart.Type == ChartType.Line || forceLineShapeProperties
                    ? ToSeriesMarkerXml(chart, seriesIndex, chartNs, drawingNs)
                    : null,
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToCategoryRangeXml(categoryRange, chartNs),
                new XElement(chartNs + "val",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", valueRange))));
            seriesIndex++;
        }
    }

    private static XElement? ToCategoryRangeXml(string? categoryRange, XNamespace chartNs) =>
        string.IsNullOrWhiteSpace(categoryRange)
            ? null
            : new XElement(chartNs + "cat",
                new XElement(chartNs + "strRef",
                    new XElement(chartNs + "f", categoryRange)));

    private static IEnumerable<XElement> BuildScatterChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool>? includeSeries = null)
    {
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var xValueCol = chart.DataRange.Start.Col;
        var seriesStartCol = chart.DataRange.Start.Col + 1;
        var xValueRange = FormatSheetRange(sheet.Name, dataStartRow, xValueCol, chart.DataRange.End.Row, xValueCol);

        var seriesIndex = 0;
        for (var col = seriesStartCol; col <= chart.DataRange.End.Col; col++)
        {
            if (includeSeries is not null && !includeSeries(seriesIndex))
            {
                seriesIndex++;
                continue;
            }

            var yValueRange = FormatSheetRange(sheet.Name, dataStartRow, col, chart.DataRange.End.Row, col);
            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                ToSeriesTitleXml(chart, sheet, col, chartNs),
                ToSeriesLineShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToSeriesMarkerXml(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                new XElement(chartNs + "xVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", xValueRange))),
                new XElement(chartNs + "yVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", yValueRange))));
            seriesIndex++;
        }
    }

    private static HashSet<int> GetSecondaryAxisSeriesIndexes(ChartModel chart, int seriesCount)
    {
        if (!chart.ShowSecondaryAxis || !ChartTypeSupport.SupportsSecondaryAxis(chart.Type) || seriesCount < 2)
            return [];

        if (chart.SecondaryAxisSeriesIndexes.Count == 0)
            return Enumerable.Range(1, seriesCount - 1).ToHashSet();

        return chart.SecondaryAxisSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .ToHashSet();
    }

    private static HashSet<int> GetComboLineSeriesIndexes(ChartModel chart, int seriesCount)
    {
        if (!chart.UseComboLineForSecondarySeries || !ChartTypeSupport.SupportsComboLineOverlay(chart) || seriesCount < 2)
            return [];

        return chart.ComboLineSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .ToHashSet();
    }

    private static IEnumerable<XElement> BuildBubbleChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (chart.DataRange.End.Col - chart.DataRange.Start.Col < 2)
            yield break;

        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var xValueCol = chart.DataRange.Start.Col;
        var xValueRange = FormatSheetRange(sheet.Name, dataStartRow, xValueCol, chart.DataRange.End.Row, xValueCol);

        var seriesIndex = 0;
        for (var yValueCol = chart.DataRange.Start.Col + 1; yValueCol < chart.DataRange.End.Col; yValueCol += 2)
        {
            var sizeCol = yValueCol + 1;
            var yValueRange = FormatSheetRange(sheet.Name, dataStartRow, yValueCol, chart.DataRange.End.Row, yValueCol);
            var sizeRange = FormatSheetRange(sheet.Name, dataStartRow, sizeCol, chart.DataRange.End.Row, sizeCol);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                ToSeriesTitleXml(chart, sheet, yValueCol, chartNs),
                ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                new XElement(chartNs + "xVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", xValueRange))),
                new XElement(chartNs + "yVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", yValueRange))),
                new XElement(chartNs + "bubbleSize",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", sizeRange))));
            seriesIndex++;
        }
    }

    private static IEnumerable<XElement> BuildPieFamilyChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (chart.FirstColIsCategories && chart.DataRange.End.Col <= chart.DataRange.Start.Col)
            yield break;

        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var firstValueCol = chart.FirstColIsCategories ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        var categoryRange = chart.FirstColIsCategories
            ? FormatSheetRange(sheet.Name, dataStartRow, chart.DataRange.Start.Col, chart.DataRange.End.Row, chart.DataRange.Start.Col)
            : null;

        var seriesIndex = 0;
        for (var valueCol = firstValueCol; valueCol <= chart.DataRange.End.Col; valueCol++)
        {
            var valueRange = FormatSheetRange(sheet.Name, dataStartRow, valueCol, chart.DataRange.End.Row, valueCol);
            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                ToSeriesTitleXml(chart, sheet, valueCol, chartNs),
                ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                seriesIndex == 0 ? ToExplodedSliceXml(chart, chartNs) : null,
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToCategoryRangeXml(categoryRange, chartNs),
                new XElement(chartNs + "val",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", valueRange))));
            seriesIndex++;
        }
    }

    private static XElement? ToSeriesTitleXml(
        ChartModel chart,
        Sheet sheet,
        uint seriesColumn,
        XNamespace chartNs)
    {
        if (!chart.FirstRowIsHeader)
            return null;

        var titleRange = FormatSheetRange(sheet.Name, chart.DataRange.Start.Row, seriesColumn, chart.DataRange.Start.Row, seriesColumn);
        return new XElement(chartNs + "tx",
            new XElement(chartNs + "strRef",
                new XElement(chartNs + "f", titleRange)));
    }

    private static XElement? ToFirstSliceAngleXml(ChartModel chart, XNamespace chartNs)
    {
        var normalized = chart.FirstSliceAngle % 360;
        if (normalized < 0)
            normalized += 360;

        return normalized == 0
            ? null
            : new XElement(chartNs + "firstSliceAng",
                new XAttribute("val", Math.Clamp((int)Math.Round(normalized), 0, 360)));
    }

    private static XElement? ToExplodedSliceXml(ChartModel chart, XNamespace chartNs)
    {
        var pointCount = ChartTypeSupport.GetDataPointCount(chart);
        if (chart.ExplodedSliceIndex < 0 || chart.ExplodedSliceIndex >= pointCount || chart.ExplodedSliceDistance <= 0)
            return null;

        return new XElement(chartNs + "dPt",
            new XElement(chartNs + "idx", new XAttribute("val", chart.ExplodedSliceIndex)),
            new XElement(chartNs + "explosion",
                new XAttribute("val", Math.Clamp((int)Math.Round(chart.ExplodedSliceDistance * 100), 0, 50))));
    }

    private static XElement? ToSeriesLineShapeProperties(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var format = GetSeriesFormat(chart, seriesIndex);
        if (format is null)
            return null;

        var fill = ToSolidFill(format.StrokeThemeColor, format.StrokeColor, drawingNs);
        var hasLineFormatting = fill is not null ||
            format.StrokeThickness is not null ||
            format.DashStyle is not null;

        return !hasLineFormatting
            ? null
            : new XElement(chartNs + "spPr",
                new XElement(drawingNs + "ln",
                    format.StrokeThickness is { } strokeThickness
                        ? new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(strokeThickness, 0.5, 10) * 12700)))
                        : null,
                    fill,
                    format.DashStyle is { } dashStyle
                        ? ToPresetDash(dashStyle, drawingNs)
                        : null));
    }

    private static XElement? ToSeriesMarkerXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (!ChartTypeSupport.SupportsSeriesMarkers(chart.Type))
            return null;

        var format = GetSeriesFormat(chart, seriesIndex);
        if (format is null)
            return null;

        var fill = ToSolidFill(format.FillThemeColor, format.FillColor, drawingNs);
        if (format.MarkerStyle is null && format.MarkerSize is null && fill is null)
            return null;

        return new XElement(chartNs + "marker",
            format.MarkerStyle is { } markerStyle
                ? new XElement(chartNs + "symbol", new XAttribute("val", ToXlsxMarkerStyle(markerStyle)))
                : null,
            format.MarkerSize is { } markerSize
                ? new XElement(chartNs + "size", new XAttribute("val", Math.Clamp((int)Math.Round(markerSize), 1, 30)))
                : null,
            fill is not null
                ? new XElement(chartNs + "spPr", fill)
                : null);
    }

    private static XElement? ToSeriesShapeProperties(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var format = GetSeriesFormat(chart, seriesIndex);
        if (format is null)
            return null;

        var fill = ToSolidFill(format.FillThemeColor, format.FillColor, drawingNs);
        var lineFill = ToSolidFill(format.StrokeThemeColor, format.StrokeColor, drawingNs);
        var hasLineFormatting = lineFill is not null ||
            format.StrokeThickness is not null ||
            format.DashStyle is not null;

        return fill is null && !hasLineFormatting
            ? null
            : new XElement(chartNs + "spPr",
                fill,
                hasLineFormatting
                    ? new XElement(drawingNs + "ln",
                        format.StrokeThickness is { } strokeThickness
                            ? new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(strokeThickness, 0.5, 10) * 12700)))
                            : null,
                        lineFill,
                        format.DashStyle is { } dashStyle
                            ? ToPresetDash(dashStyle, drawingNs)
                            : null)
                    : null);
    }

    private static ChartSeriesFormat? GetSeriesFormat(ChartModel chart, int seriesIndex)
    {
        var format = chart.SeriesFormats.LastOrDefault(item => item.SeriesIndex == seriesIndex);
        return format is null
            ? null
            : format with
            {
                DashStyle = ValidNullableEnumOrNull(format.DashStyle),
                MarkerStyle = ValidNullableEnumOrNull(format.MarkerStyle)
            };
    }

    private static TEnum? ValidNullableEnumOrNull<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value is { } enumValue && Enum.IsDefined(enumValue) ? enumValue : null;

    private static XElement? ToPointDataLabelsXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var pointCount = ChartTypeSupport.GetDataPointCount(chart);
        var labels = chart.PointDataLabelFormats
            .Where(format => format.SeriesIndex == seriesIndex && format.PointIndex >= 0 && format.PointIndex < pointCount)
            .GroupBy(format => format.PointIndex)
            .Select(group => group.Last())
            .Where(HasPointDataLabelFormatting)
            .OrderBy(format => format.PointIndex)
            .Select(format => ToPointDataLabelXml(format, chartNs, drawingNs))
            .ToArray();

        return labels.Length == 0
            ? null
            : new XElement(chartNs + "dLbls", labels);
    }

    private static bool HasPointDataLabelFormatting(ChartPointDataLabelFormat format) =>
        format.FillColor is not null
        || format.BorderColor is not null
        || format.BorderThickness is not null
        || format.TextColor is not null
        || format.FontSize is not null
        || format.FillThemeColor is not null
        || format.BorderThemeColor is not null
        || format.TextThemeColor is not null;

    private static XElement ToPointDataLabelXml(
        ChartPointDataLabelFormat format,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        new(chartNs + "dLbl",
            new XElement(chartNs + "idx", new XAttribute("val", format.PointIndex)),
            ToShapeProperties(
                chartNs,
                drawingNs,
                format.FillThemeColor,
                format.FillColor,
                format.BorderThemeColor,
                format.BorderColor,
                format.BorderThickness),
            ToPointDataLabelTextProperties(format, chartNs, drawingNs));

    private static XElement? ToPointDataLabelTextProperties(
        ChartPointDataLabelFormat format,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var textFill = ToSolidFill(format.TextThemeColor, format.TextColor, drawingNs);
        if (textFill is null && format.FontSize is null)
            return null;

        return new XElement(chartNs + "txPr",
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        format.FontSize is { } fontSize
                            ? new XAttribute("sz", Math.Clamp((int)Math.Round(fontSize * 100), 600, 7200))
                            : null,
                        textFill))));
    }

    private static XElement? ToTrendlineXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (!chart.ShowLinearTrendline || seriesIndex != 0 || !ChartTypeSupport.SupportsTrendlines(chart.Type))
            return null;

        return new XElement(chartNs + "trendline",
            new XElement(chartNs + "trendlineType",
                new XAttribute("val", ToXlsxTrendlineType(chart.TrendlineType))),
            chart.TrendlineType == ChartTrendlineType.Polynomial
                ? new XElement(chartNs + "order", new XAttribute("val", Math.Clamp(chart.TrendlineOrder, 2, 6)))
                : null,
            chart.TrendlineType == ChartTrendlineType.MovingAverage
                ? new XElement(chartNs + "period", new XAttribute("val", Math.Max(2, chart.TrendlinePeriod)))
                : null,
            ToTrendlineShapeProperties(chart, chartNs, drawingNs),
            new XElement(chartNs + "dispEq", new XAttribute("val", chart.ShowTrendlineEquation ? "1" : "0")),
            new XElement(chartNs + "dispRSqr", new XAttribute("val", chart.ShowTrendlineRSquared ? "1" : "0")));
    }

    private static XElement? ToTrendlineShapeProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var fill = ToSolidFill(chart.TrendlineThemeColor, chart.TrendlineColor, drawingNs);
        if (fill is null && chart.TrendlineThickness == 1.5 && chart.TrendlineDashStyle == ChartLineDashStyle.Solid)
            return null;

        return new XElement(chartNs + "spPr",
            new XElement(drawingNs + "ln",
                new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(chart.TrendlineThickness, 0.5, 10) * 12700))),
                fill,
                ToPresetDash(chart.TrendlineDashStyle, drawingNs)));
    }

    private static XElement? ToPresetDash(ChartLineDashStyle dashStyle, XNamespace drawingNs) =>
        dashStyle == ChartLineDashStyle.Solid
            ? null
            : new XElement(drawingNs + "prstDash",
                new XAttribute("val", dashStyle == ChartLineDashStyle.Dot ? "dot" : "dash"));

    private static string ToXlsxMarkerStyle(ChartMarkerStyle markerStyle) =>
        markerStyle switch
        {
            ChartMarkerStyle.None => "none",
            ChartMarkerStyle.Square => "square",
            ChartMarkerStyle.Diamond => "diamond",
            ChartMarkerStyle.Triangle => "triangle",
            _ => "circle"
        };

    private static string ToXlsxTrendlineType(ChartTrendlineType type) =>
        type switch
        {
            ChartTrendlineType.Exponential => "exp",
            ChartTrendlineType.Logarithmic => "log",
            ChartTrendlineType.Power => "power",
            ChartTrendlineType.MovingAverage => "movingAvg",
            ChartTrendlineType.Polynomial => "poly",
            _ => "linear"
        };

    private static string ToDrawingSchemeColor(WorkbookThemeColorSlot slot) =>
        slot switch
        {
            WorkbookThemeColorSlot.Dark1 => "dk1",
            WorkbookThemeColorSlot.Light1 => "lt1",
            WorkbookThemeColorSlot.Dark2 => "dk2",
            WorkbookThemeColorSlot.Light2 => "lt2",
            WorkbookThemeColorSlot.Accent1 => "accent1",
            WorkbookThemeColorSlot.Accent2 => "accent2",
            WorkbookThemeColorSlot.Accent3 => "accent3",
            WorkbookThemeColorSlot.Accent4 => "accent4",
            WorkbookThemeColorSlot.Accent5 => "accent5",
            WorkbookThemeColorSlot.Accent6 => "accent6",
            WorkbookThemeColorSlot.Hyperlink => "hlink",
            WorkbookThemeColorSlot.FollowedHyperlink => "folHlink",
            _ => "accent1"
        };

    private static string FormatSheetRange(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var quotedSheet = $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'";
        var start = $"${CellAddress.NumberToColumnName(startCol)}${startRow}";
        var end = $"${CellAddress.NumberToColumnName(endCol)}${endRow}";
        return start == end
            ? $"{quotedSheet}!{start}"
            : $"{quotedSheet}!{start}:{end}";
    }

    private static long PixelsToEmus(double pixels) =>
        (long)Math.Round(Math.Max(0, pixels) * 9525.0);

    private static void EnsureContentTypeOverride(ZipArchive archive, string partName, string contentType)
    {
        const string contentTypesPath = "[Content_Types].xml";
        var entry = archive.GetEntry(contentTypesPath);
        if (entry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var xml = LoadXml(entry);
        var existing = xml.Root?
            .Elements(contentTypeNs + "Override")
            .FirstOrDefault(e => string.Equals(e.Attribute("PartName")?.Value, partName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.SetAttributeValue("ContentType", contentType);
        }
        else
        {
            xml.Root?.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", partName),
                new XAttribute("ContentType", contentType)));
        }

        entry.Delete();
        var updatedEntry = archive.CreateEntry(contentTypesPath);
        using var stream = updatedEntry.Open();
        xml.Save(stream);
    }

    private static void SaveWorksheetBackgrounds(Stream xlsxStream, Workbook workbook)
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

        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);
        var backgroundIndex = 1;
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId))
                continue;
            if (!sheetsByName.TryGetValue(name, out var sheet) || sheet.BackgroundImage is null)
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            WriteWorksheetBackground(archive, worksheetPath, sheet.BackgroundImage, backgroundIndex++);
        }
    }

    private static void WriteWorksheetBackground(
        ZipArchive archive,
        string worksheetPath,
        WorksheetBackgroundImage background,
        int backgroundIndex)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var extension = GetExtensionFromContentType(background.ContentType);
        var mediaFileName = GetWorksheetBackgroundMediaFileName(background.FileName, backgroundIndex, extension);
        var imagePath = $"xl/media/{mediaFileName}";
        var existingImageEntry = archive.GetEntry(imagePath);
        existingImageEntry?.Delete();
        var imageEntry = archive.CreateEntry(imagePath);
        using (var imageStream = imageEntry.Open())
            imageStream.Write(background.ImageBytes);

        EnsureContentType(archive, extension.TrimStart('.'), background.ContentType);

        var relsPath = GetWorksheetRelsPath(worksheetPath);
        var relsEntry = archive.GetEntry(relsPath);
        XDocument relsXml;
        if (relsEntry is null)
        {
            relsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        }
        else
        {
            relsXml = LoadXml(relsEntry);
            relsEntry.Delete();
        }

        var relId = NextRelationshipId(relsXml, packageRelNs);
        relsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", relId),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
            new XAttribute("Target", GetRelativeTarget(worksheetPath, imagePath))));

        var updatedRelsEntry = archive.CreateEntry(relsPath);
        using (var relsStream = updatedRelsEntry.Open())
            relsXml.Save(relsStream);

        var worksheetXml = LoadXml(worksheetEntry);
        var root = worksheetXml.Root;
        if (root is null)
            return;

        root.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
        root.Elements(worksheetNs + "picture").Remove();
        root.Add(new XElement(worksheetNs + "picture", new XAttribute(relNs + "id", relId)));

        worksheetEntry.Delete();
        var updatedWorksheetEntry = archive.CreateEntry(worksheetPath);
        using var worksheetStream = updatedWorksheetEntry.Open();
        worksheetXml.Save(worksheetStream);
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
            .Where(sheet =>
                !sheet.ShowGridlines ||
                !sheet.ShowHeadings ||
                !sheet.ShowRulers ||
                ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal) != WorksheetViewMode.Normal ||
                sheet.ZoomPercent != 100 ||
                sheet.ShowFormulas ||
                (sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
                 (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue)))
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

        sheetView.SetAttributeValue("view", ToXlsxWorksheetViewMode(
            ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal)));
        sheetView.SetAttributeValue("showGridLines", sheet.ShowGridlines ? null : "0");
        sheetView.SetAttributeValue("showRowColHeaders", sheet.ShowHeadings ? null : "0");
        sheetView.SetAttributeValue("showRuler", sheet.ShowRulers ? null : "0");
        sheetView.SetAttributeValue("zoomScale", sheet.ZoomPercent == 100 ? null : sheet.ZoomPercent);
        sheetView.SetAttributeValue("showFormulas", sheet.ShowFormulas ? "1" : null);

        if (sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
            (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue))
        {
            sheetView.Elements(worksheetNs + "pane").Remove();
            sheetView.AddFirst(new XElement(
                worksheetNs + "pane",
                sheet.SplitColumn is { } splitColumn ? new XAttribute("xSplit", splitColumn) : null,
                sheet.SplitRow is { } splitRow ? new XAttribute("ySplit", splitRow) : null,
                new XAttribute("state", "split")));
        }

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

    private static CellStyle MapStyle(IXLStyle xlStyle, WorkbookTheme theme)
    {
        return new CellStyle
        {
            FontName = xlStyle.Font.FontName,
            FontSize = IsSupportedFontSize(xlStyle.Font.FontSize)
                ? xlStyle.Font.FontSize
                : CellStyle.Default.FontSize,
            Bold = xlStyle.Font.Bold,
            Italic = xlStyle.Font.Italic,
            Underline = xlStyle.Font.Underline != XLFontUnderlineValues.None,
            Strikethrough = xlStyle.Font.Strikethrough,
            FontColor = MapColor(xlStyle.Font.FontColor, theme),
            FillColor = xlStyle.Fill.PatternType == XLFillPatternValues.Solid
                ? (CellColor?)MapColor(xlStyle.Fill.BackgroundColor, theme)
                : null,
            BorderTop = MapBorder(xlStyle.Border.TopBorder, xlStyle.Border.TopBorderColor, theme),
            BorderRight = MapBorder(xlStyle.Border.RightBorder, xlStyle.Border.RightBorderColor, theme),
            BorderBottom = MapBorder(xlStyle.Border.BottomBorder, xlStyle.Border.BottomBorderColor, theme),
            BorderLeft = MapBorder(xlStyle.Border.LeftBorder, xlStyle.Border.LeftBorderColor, theme),
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
            TextRotation = IsSupportedTextRotation(xlStyle.Alignment.TextRotation)
                ? xlStyle.Alignment.TextRotation
                : 0,
            Locked = xlStyle.Protection.Locked,
        };
    }

    private static CellColor MapColor(XLColor xlColor, WorkbookTheme theme)
    {
        if (xlColor.ColorType == XLColorType.Theme)
            return theme.ResolveColor(ToWorkbookThemeColorSlot(xlColor.ThemeColor), xlColor.ThemeTint);

        System.Drawing.Color c;
        try
        {
            c = xlColor.Color;
        }
        catch (InvalidOperationException)
        {
            return new CellColor(0, 0, 0);
        }

        return new CellColor(c.R, c.G, c.B);
    }

    private static WorkbookThemeColorSlot ToWorkbookThemeColorSlot(XLThemeColor themeColor) => themeColor switch
    {
        XLThemeColor.Text1 => WorkbookThemeColorSlot.Dark1,
        XLThemeColor.Background1 => WorkbookThemeColorSlot.Light1,
        XLThemeColor.Text2 => WorkbookThemeColorSlot.Dark2,
        XLThemeColor.Background2 => WorkbookThemeColorSlot.Light2,
        XLThemeColor.Accent1 => WorkbookThemeColorSlot.Accent1,
        XLThemeColor.Accent2 => WorkbookThemeColorSlot.Accent2,
        XLThemeColor.Accent3 => WorkbookThemeColorSlot.Accent3,
        XLThemeColor.Accent4 => WorkbookThemeColorSlot.Accent4,
        XLThemeColor.Accent5 => WorkbookThemeColorSlot.Accent5,
        XLThemeColor.Accent6 => WorkbookThemeColorSlot.Accent6,
        XLThemeColor.Hyperlink => WorkbookThemeColorSlot.Hyperlink,
        XLThemeColor.FollowedHyperlink => WorkbookThemeColorSlot.FollowedHyperlink,
        _ => WorkbookThemeColorSlot.Dark1
    };

    private static CellBorder MapBorder(XLBorderStyleValues style, XLColor color, WorkbookTheme theme)
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
        return new CellBorder(mapped, MapColor(color, theme));
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
        if (style.FontSize != def.FontSize && IsSupportedFontSize(style.FontSize))
            xlCell.Style.Font.FontSize = style.FontSize;
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

        if (style.TextRotation != def.TextRotation && IsSupportedTextRotation(style.TextRotation))
            xlCell.Style.Alignment.TextRotation = style.TextRotation;

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
                    FormatIfTrue = MapStyle(xlCf.Style, workbook.Theme)
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
                    FormatIfTrue = MapStyle(xlCf.Style, workbook.Theme)
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
            if (!Enum.IsDefined(cf.RuleType) || !Enum.IsDefined(cf.Operator))
                continue;
            if (cf.RuleType is not (CfRuleType.CellValue or CfRuleType.Formula))
                continue;
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
                        _                             => throw new InvalidOperationException("Unsupported conditional format operator.")
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
            if (!Enum.IsDefined(dv.Type) || !Enum.IsDefined(dv.Operator) || !Enum.IsDefined(dv.AlertStyle))
                continue;

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
            sheet.AddMergedRegion(new GridRange(start, end));
        }
    }
}
