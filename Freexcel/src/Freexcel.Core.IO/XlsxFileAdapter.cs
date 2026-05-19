using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Reflection;
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
    private static readonly ConditionalWeakTable<Workbook, XlsxSourcePackage> SourcePackages = new();
    private static readonly FieldInfo? XlCellValueNumberField =
        typeof(XLCellValue).GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic);

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
        var calculationProperties = LoadWorkbookCalculationProperties(packageStream);
        packageStream.Position = 0;
        var pivotMetadata = LoadPivotMetadata(packageStream);
        packageStream.Position = 0;
        var slicerTimelineMetadata = LoadSlicerTimelineMetadata(packageStream);
        packageStream.Position = 0;
        var externalLinkMetadata = LoadExternalLinkMetadata(packageStream);
        packageStream.Position = 0;
        var structuredTableMetadata = LoadStructuredTableMetadata(packageStream);
        packageStream.Position = 0;
        var pivotTableStyleMetadata = LoadPivotTableStyleMetadata(packageStream);

        packageStream.Position = 0;
        using var closedXmlPackageStream = CreateClosedXmlLoadPackage(packageStream);
        using var xlWorkbook = new XLWorkbook(closedXmlPackageStream);
        var workbook = new Workbook("Untitled");
        SourcePackages.Remove(workbook);
        SourcePackages.Add(workbook, new XlsxSourcePackage(packageStream.ToArray()));
        workbook.Theme = workbookTheme;
        workbook.IsStructureProtected = workbookProtection.IsStructureProtected;
        workbook.StructureProtectionPassword = workbookProtection.PasswordHash;
        workbook.CalculationMode = xlWorkbook.CalculateMode == XLCalculateMode.Manual
            ? WorkbookCalculationMode.Manual
            : WorkbookCalculationMode.Automatic;
        if (calculationProperties.Mode is { } calculationMode)
            workbook.CalculationMode = calculationMode;
        workbook.FullCalculationOnLoad = calculationProperties.FullCalculationOnLoad;
        workbook.ForceFullCalculation = calculationProperties.ForceFullCalculation;
        workbook.IterativeCalculation = calculationProperties.IterativeCalculation;
        workbook.MaxCalculationIterations = calculationProperties.MaxIterations;
        workbook.MaxCalculationChange = calculationProperties.MaxChange;
        foreach (var pivotCache in pivotMetadata.PivotCaches)
            workbook.PivotCaches.Add(pivotCache);
        foreach (var slicer in slicerTimelineMetadata.Slicers)
            workbook.Slicers.Add(slicer);
        foreach (var timeline in slicerTimelineMetadata.Timelines)
            workbook.Timelines.Add(timeline);
        foreach (var externalLink in externalLinkMetadata)
            workbook.ExternalLinks.Add(externalLink);
        foreach (var pivotTableStyle in pivotTableStyleMetadata)
            workbook.PivotTableStyles.Add(pivotTableStyle);

        foreach (var xlSheet in xlWorkbook.Worksheets)
        {
            var sheet = workbook.AddSheet(xlSheet.Name);
            sheet.IsVeryHidden = xlSheet.Visibility == XLWorksheetVisibility.VeryHidden;
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
                    cell = Cell.FromFormula(NormalizeExcelFormulaText(xlCell.FormulaA1));
                    // Preserve the cached formula result so callers see the last-calculated value
                    // without needing to recalculate immediately.
                    var cached = MapValue(xlCell);
                    if (cached is not BlankValue)
                        cell.Value = cached;
                }
                else
                {
                    cell = Cell.FromValue(MapValue(xlCell));
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
                foreach (var range in layout.AllowEditRanges)
                    sheet.AllowEditRanges.Add(new GridRange(
                        new CellAddress(sheet.Id, range.Start.Row, range.Start.Col),
                        new CellAddress(sheet.Id, range.End.Row, range.End.Col)));
                sheet.ViewMode = layout.ViewMode;
                sheet.ShowGridlines = layout.ShowGridlines;
                sheet.ShowHeadings = layout.ShowHeadings;
                sheet.ShowRulers = layout.ShowRulers;
                sheet.ZoomPercent = layout.ZoomPercent;
                sheet.ShowFormulas = layout.ShowFormulas;
                sheet.BackgroundImage = layout.BackgroundImage;
                sheet.CodeName = layout.CodeName;

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
                foreach (var picturePart in layout.PictureParts)
                {
                    var picture = new PictureModel
                    {
                        Anchor = new CellAddress(
                            sheet.Id,
                            picturePart.Anchor?.FromRowZeroBased + 1 ?? 1,
                            picturePart.Anchor?.FromColumnZeroBased + 1 ?? 1),
                        Kind = PictureKind.Image,
                        ImageBytes = picturePart.ImageBytes.ToArray(),
                        ContentType = picturePart.ContentType,
                        AltText = picturePart.AltText,
                        CropLeft = picturePart.CropLeft,
                        CropTop = picturePart.CropTop,
                        CropRight = picturePart.CropRight,
                        CropBottom = picturePart.CropBottom
                    };
                    ApplyPictureAnchor(picture, picturePart.Anchor, sheet);
                    sheet.Pictures.Add(picture);
                }
                foreach (var textBoxPart in layout.TextBoxParts)
                {
                    var textBox = new TextBoxModel
                    {
                        Anchor = new CellAddress(
                            sheet.Id,
                            textBoxPart.Anchor?.FromRowZeroBased + 1 ?? 1,
                            textBoxPart.Anchor?.FromColumnZeroBased + 1 ?? 1),
                        Text = textBoxPart.Text,
                        AltText = textBoxPart.AltText,
                        RotationDegrees = textBoxPart.RotationDegrees,
                        FillColor = textBoxPart.FillColor,
                        OutlineColor = textBoxPart.OutlineColor
                    };
                    ApplyTextBoxAnchor(textBox, textBoxPart.Anchor, sheet);
                    sheet.TextBoxes.Add(textBox);
                }
                foreach (var shapePart in layout.ShapeParts)
                {
                    var shape = new DrawingShapeModel
                    {
                        Anchor = new CellAddress(
                            sheet.Id,
                            shapePart.Anchor?.FromRowZeroBased + 1 ?? 1,
                            shapePart.Anchor?.FromColumnZeroBased + 1 ?? 1),
                        Kind = shapePart.Kind,
                        AltText = shapePart.AltText,
                        RotationDegrees = shapePart.RotationDegrees,
                        FillColor = shapePart.FillColor,
                        OutlineColor = shapePart.OutlineColor,
                        GradientFillEndColor = shapePart.GradientFillEndColor,
                        HasShadowEffect = shapePart.HasShadowEffect
                    };
                    ApplyDrawingShapeAnchor(shape, shapePart.Anchor, sheet);
                    sheet.DrawingShapes.Add(shape);
                }
                foreach (var sparkline in layout.Sparklines)
                {
                    sheet.Sparklines.Add(new SparklineModel
                    {
                        DataRange = new GridRange(
                            new CellAddress(sheet.Id, sparkline.DataRange.Start.Row, sparkline.DataRange.Start.Col),
                            new CellAddress(sheet.Id, sparkline.DataRange.End.Row, sparkline.DataRange.End.Col)),
                        Location = new CellAddress(sheet.Id, sparkline.Location.Row, sparkline.Location.Col),
                        Kind = sparkline.Kind
                    });
                }
                foreach (var conditionalFormat in layout.AdvancedConditionalFormats)
                    sheet.ConditionalFormats.Add(RemapConditionalFormat(conditionalFormat, sheet.Id));
            }
            if (pivotMetadata.PivotTablesBySheetName.TryGetValue(xlSheet.Name, out var pivotTables))
            {
                foreach (var pivotTable in pivotTables)
                    sheet.PivotTables.Add(ToPivotTableModel(pivotTable, sheet.Id));
            }
            ResolvePivotChartCacheBindings(workbook, sheet);
            if (structuredTableMetadata.TablesBySheetName.TryGetValue(xlSheet.Name, out var structuredTables))
            {
                foreach (var structuredTable in structuredTables)
                    sheet.StructuredTables.Add(ToStructuredTableModel(structuredTable, sheet.Id));
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
            sheet.ViewTopRow = layout?.ViewTopRow;
            sheet.ViewLeftCol = layout?.ViewLeftCol;
            sheet.ActiveRow = layout?.ActiveRow;
            sheet.ActiveCol = layout?.ActiveCol;

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
        IReadOnlyList<GridRange> AllowEditRanges,
        WorksheetViewMode ViewMode,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas,
        string? PaneState,
        uint? PaneRowSplit,
        uint? PaneColumnSplit,
        uint? ViewTopRow,
        uint? ViewLeftCol,
        uint? ActiveRow,
        uint? ActiveCol,
        WorksheetBackgroundImage? BackgroundImage,
        Dictionary<uint, int> RowOutlineLevels,
        Dictionary<uint, int> ColOutlineLevels,
        HashSet<uint> GroupHiddenRows,
        HashSet<uint> GroupHiddenCols,
        IReadOnlyList<XlsxChartPackagePart> ChartParts,
        IReadOnlyList<XlsxPicturePackagePart> PictureParts,
        IReadOnlyList<XlsxTextBoxPackagePart> TextBoxParts,
        IReadOnlyList<XlsxShapePackagePart> ShapeParts,
        IReadOnlyList<SparklineModel> Sparklines,
        IReadOnlyList<ConditionalFormat> AdvancedConditionalFormats,
        string? CodeName);

    private sealed record XlsxChartPackagePart(XDocument Xml, XlsxDrawingAnchor? Anchor);

    private sealed record XlsxPicturePackagePart(
        byte[] ImageBytes,
        string ContentType,
        string? AltText,
        XlsxDrawingAnchor? Anchor,
        double CropLeft,
        double CropTop,
        double CropRight,
        double CropBottom);

    private sealed record XlsxTextBoxPackagePart(
        string Text,
        string? AltText,
        XlsxDrawingAnchor? Anchor,
        double RotationDegrees,
        CellColor? FillColor,
        CellColor? OutlineColor);

    private sealed record XlsxShapePackagePart(
        DrawingShapeKind Kind,
        string? AltText,
        XlsxDrawingAnchor? Anchor,
        double RotationDegrees,
        CellColor? FillColor,
        CellColor? OutlineColor,
        CellColor? GradientFillEndColor,
        bool HasShadowEffect);

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

            var differentialStyles = ReadDifferentialStyles(archive, workbookNs);

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

                result[name] = ReadHiddenSheetLayout(archive, worksheetPath, worksheetEntry, differentialStyles);
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

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static IReadOnlyList<CellStyle> ReadDifferentialStyles(ZipArchive archive, XNamespace workbookNs)
    {
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        if (stylesEntry is null)
            return [];

        try
        {
            var stylesXml = LoadXml(stylesEntry);
            return stylesXml.Root?
                .Element(workbookNs + "dxfs")?
                .Elements(workbookNs + "dxf")
                .Select(dxf => ReadDifferentialStyle(dxf, workbookNs))
                .ToList()
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static CellStyle ReadDifferentialStyle(XElement dxf, XNamespace workbookNs)
    {
        var style = new CellStyle();
        var font = dxf.Element(workbookNs + "font");
        if (font is not null)
        {
            style.Bold = font.Element(workbookNs + "b") is not null;
            style.Italic = font.Element(workbookNs + "i") is not null;
            style.Underline = font.Element(workbookNs + "u") is not null;
            style.Strikethrough = font.Element(workbookNs + "strike") is not null;
            var verticalAlignment = font.Element(workbookNs + "vertAlign")?.Attribute("val")?.Value;
            style.Superscript = string.Equals(verticalAlignment, "superscript", StringComparison.OrdinalIgnoreCase);
            style.Subscript = string.Equals(verticalAlignment, "subscript", StringComparison.OrdinalIgnoreCase);
            if (double.TryParse(font.Element(workbookNs + "sz")?.Attribute("val")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) &&
                IsSupportedFontSize(size))
            {
                style.FontSize = size;
            }

            var fontName = font.Element(workbookNs + "name")?.Attribute("val")?.Value;
            if (!string.IsNullOrWhiteSpace(fontName))
                style.FontName = fontName;

            if (TryReadCellColor(font.Element(workbookNs + "color"), out var fontColor))
                style.FontColor = fontColor;
        }

        var patternFill = dxf
            .Element(workbookNs + "fill")?
            .Element(workbookNs + "patternFill");
        if (TryReadCellColor(patternFill?.Element(workbookNs + "fgColor"), out var fillColor))
            style.FillColor = fillColor;

        var border = dxf.Element(workbookNs + "border");
        if (border is not null)
        {
            style.BorderTop = ReadDifferentialBorder(border.Element(workbookNs + "top"), workbookNs);
            style.BorderRight = ReadDifferentialBorder(border.Element(workbookNs + "right"), workbookNs);
            style.BorderBottom = ReadDifferentialBorder(border.Element(workbookNs + "bottom"), workbookNs);
            style.BorderLeft = ReadDifferentialBorder(border.Element(workbookNs + "left"), workbookNs);
        }

        var numberFormat = dxf.Element(workbookNs + "numFmt")?.Attribute("formatCode")?.Value;
        if (!string.IsNullOrWhiteSpace(numberFormat))
            style.NumberFormat = numberFormat;

        return style;
    }

    private static CellBorder ReadDifferentialBorder(XElement? edge, XNamespace workbookNs)
    {
        if (edge is null)
            return default;

        var style = edge.Attribute("style")?.Value switch
        {
            "thin" => BorderStyle.Thin,
            "medium" => BorderStyle.Medium,
            "thick" => BorderStyle.Thick,
            "dashed" => BorderStyle.Dashed,
            "dotted" => BorderStyle.Dotted,
            "double" => BorderStyle.Double,
            _ => BorderStyle.None
        };
        if (style == BorderStyle.None)
            return default;

        return new CellBorder(
            style,
            TryReadCellColor(edge.Element(workbookNs + "color"), out var color) ? color : CellColor.Black);
    }

    private static PivotPackageMetadata LoadPivotMetadata(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry is null || workbookRelsEntry is null)
                return PivotPackageMetadata.Empty;

            var workbookXml = LoadXml(workbookEntry);
            var workbookRelsXml = LoadXml(workbookRelsEntry);

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var workbookRels = workbookRelsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
                .ToDictionary(
                    e => e.Attribute("Id")!.Value,
                    e => ResolveRelationshipTarget("xl/workbook.xml", e.Attribute("Target")!.Value),
                    StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var pivotCaches = LoadPivotCaches(archive, workbookXml, workbookRels, workbookNs, relNs);
            var sheetsByPath = GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
                .ToDictionary(pair => pair.WorksheetPath, pair => pair.SheetName, StringComparer.OrdinalIgnoreCase);
            var pivotTablesBySheetName = LoadPivotTablesBySheetName(archive, sheetsByPath, workbookNs, relNs, packageRelNs);

            return new PivotPackageMetadata(pivotCaches, pivotTablesBySheetName);
        }
        catch
        {
            return PivotPackageMetadata.Empty;
        }
    }

    private static List<PivotCacheModel> LoadPivotCaches(
        ZipArchive archive,
        XDocument workbookXml,
        IReadOnlyDictionary<string, string> workbookRels,
        XNamespace workbookNs,
        XNamespace relNs)
    {
        var result = new List<PivotCacheModel>();
        foreach (var pivotCacheElement in workbookXml.Root?
                     .Element(workbookNs + "pivotCaches")?
                     .Elements(workbookNs + "pivotCache") ?? [])
        {
            var cacheId = ReadIntAttribute(pivotCacheElement, "cacheId") ?? 0;
            var relId = pivotCacheElement.Attribute(relNs + "id")?.Value;
            if (cacheId <= 0 || string.IsNullOrWhiteSpace(relId) || !workbookRels.TryGetValue(relId, out var cachePath))
                continue;

            var cacheEntry = archive.GetEntry(cachePath);
            if (cacheEntry is null)
                continue;

            var cacheXml = LoadXml(cacheEntry);
            var root = cacheXml.Root;
            if (root is null)
                continue;

            var cacheSource = root.Element(workbookNs + "cacheSource");
            var worksheetSource = cacheSource?.Element(workbookNs + "worksheetSource");
            var cache = new PivotCacheModel
            {
                CacheId = cacheId,
                SourceType = GetPivotCacheSourceType(cacheSource, worksheetSource),
                SourceSheetName = worksheetSource?.Attribute("sheet")?.Value,
                SourceReference = worksheetSource?.Attribute("ref")?.Value,
                SourceTableName = worksheetSource?.Attribute("name")?.Value,
                ConnectionId = cacheSource is null ? null : ReadIntAttribute(cacheSource, "connectionId"),
                IsOlap = ReadBoolAttribute(root, "olap"),
                PackagePart = cachePath,
                RefreshOnLoad = ReadBoolAttribute(root, "refreshOnLoad", defaultValue: true),
                SaveData = ReadBoolAttribute(root, "saveData", defaultValue: true),
                EnableRefresh = ReadBoolAttribute(root, "enableRefresh", defaultValue: true),
                RefreshedVersion = ReadIntAttribute(root, "refreshedVersion"),
                RefreshedBy = root.Attribute("refreshedBy")?.Value
            };

            foreach (var field in root
                         .Element(workbookNs + "cacheFields")?
                         .Elements(workbookNs + "cacheField") ?? [])
            {
                var sharedItems = field.Element(workbookNs + "sharedItems");
                cache.Fields.Add(new PivotCacheFieldModel(
                    field.Attribute("name")?.Value ?? "",
                    ReadIntAttribute(field, "numFmtId"),
                    sharedItems is null ? null : ReadIntAttribute(sharedItems, "count"),
                    ReadBoolAttribute(sharedItems, "containsBlank"),
                    ReadBoolAttribute(sharedItems, "containsString") || (sharedItems?.Elements(workbookNs + "s").Any() ?? false),
                    ReadBoolAttribute(sharedItems, "containsNumber") || (sharedItems?.Elements(workbookNs + "n").Any() ?? false),
                    ReadBoolAttribute(sharedItems, "containsDate") || (sharedItems?.Elements(workbookNs + "d").Any() ?? false),
                    ReadBoolAttribute(sharedItems, "containsMixedTypes"),
                    ReadBoolAttribute(sharedItems, "containsSemiMixedTypes"),
                    ReadBoolAttribute(sharedItems, "containsNonDate"),
                    ReadBoolAttribute(sharedItems, "containsInteger"),
                    ReadBoolAttribute(sharedItems, "longText"),
                    sharedItems is null ? null : ReadDoubleAttribute(sharedItems, "minValue"),
                    sharedItems is null ? null : ReadDoubleAttribute(sharedItems, "maxValue"),
                    sharedItems?.Attribute("minDate")?.Value,
                    sharedItems?.Attribute("maxDate")?.Value));
            }

            result.Add(cache);
        }

        return result;
    }

    private static PivotCacheSourceType GetPivotCacheSourceType(XElement? cacheSource, XElement? worksheetSource)
    {
        var sourceType = cacheSource?.Attribute("type")?.Value;
        if (string.Equals(sourceType, "external", StringComparison.OrdinalIgnoreCase))
            return PivotCacheSourceType.External;
        if (string.Equals(sourceType, "consolidation", StringComparison.OrdinalIgnoreCase))
            return PivotCacheSourceType.Consolidation;
        if (string.Equals(sourceType, "scenario", StringComparison.OrdinalIgnoreCase))
            return PivotCacheSourceType.Scenario;
        if (worksheetSource is null)
            return PivotCacheSourceType.Unknown;
        if (!string.IsNullOrWhiteSpace(worksheetSource.Attribute("name")?.Value))
            return PivotCacheSourceType.Table;
        if (!string.IsNullOrWhiteSpace(worksheetSource.Attribute("ref")?.Value))
            return PivotCacheSourceType.WorksheetRange;
        return PivotCacheSourceType.Unknown;
    }

    private static SlicerTimelinePackageMetadata LoadSlicerTimelineMetadata(Stream xlsxStream)
    {
        var slicers = new List<SlicerModel>();
        var timelines = new List<TimelineModel>();
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var slicerCaches = archive.Entries
                .Where(entry => entry.FullName.Replace('\\', '/').StartsWith("xl/slicerCaches/", StringComparison.OrdinalIgnoreCase))
                .Select(entry => (Path: entry.FullName.Replace('\\', '/'), Xml: LoadXml(entry)))
                .Select(item => (item.Path, Cache: ReadSlicerCache(item.Xml)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Cache.Name))
                .ToDictionary(item => item.Cache.Name, item => item.Cache, StringComparer.OrdinalIgnoreCase);

            foreach (var slicerEntry in archive.Entries.Where(entry =>
                         entry.FullName.Replace('\\', '/').StartsWith("xl/slicers/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            {
                var slicerXml = LoadXml(slicerEntry);
                var root = slicerXml.Root;
                var cacheName = root?.Attribute("cache")?.Value ?? "";
                slicerCaches.TryGetValue(cacheName, out var cache);
                slicers.Add(new SlicerModel
                {
                    Name = root?.Attribute("name")?.Value ?? "",
                    Caption = root?.Attribute("caption")?.Value,
                    CacheName = cacheName,
                    SourcePivotTableName = cache?.PivotTableName,
                    SourceFieldName = cache?.SourceFieldName,
                    StyleName = root?.Attribute("style")?.Value,
                    PackagePart = slicerEntry.FullName.Replace('\\', '/')
                });
                slicers[^1].SelectedItems.AddRange(cache?.SelectedItems ?? []);
            }

            var timelineCaches = archive.Entries
                .Where(entry => entry.FullName.Replace('\\', '/').StartsWith("xl/timelineCaches/", StringComparison.OrdinalIgnoreCase))
                .Select(entry => (Path: entry.FullName.Replace('\\', '/'), Xml: LoadXml(entry)))
                .Select(item => (item.Path, Cache: ReadTimelineCache(item.Xml)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Cache.Name))
                .ToDictionary(item => item.Cache.Name, item => item.Cache, StringComparer.OrdinalIgnoreCase);

            foreach (var timelineEntry in archive.Entries.Where(entry =>
                         entry.FullName.Replace('\\', '/').StartsWith("xl/timelines/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            {
                var timelineXml = LoadXml(timelineEntry);
                var root = timelineXml.Root;
                var cacheName = root?.Attribute("cache")?.Value ?? "";
                timelineCaches.TryGetValue(cacheName, out var cache);
                timelines.Add(new TimelineModel
                {
                    Name = root?.Attribute("name")?.Value ?? "",
                    Caption = root?.Attribute("caption")?.Value,
                    CacheName = cacheName,
                    SourcePivotTableName = cache?.PivotTableName,
                    SourceFieldName = cache?.SourceFieldName,
                    StyleName = root?.Attribute("style")?.Value,
                    StartDate = cache?.StartDate,
                    EndDate = cache?.EndDate,
                    SelectedStartDate = cache?.SelectedStartDate,
                    SelectedEndDate = cache?.SelectedEndDate,
                    PackagePart = timelineEntry.FullName.Replace('\\', '/')
                });
            }
        }
        catch
        {
            // Slicer/timeline metadata should never block loading ordinary workbook content.
        }

        return new SlicerTimelinePackageMetadata(slicers, timelines);
    }

    private static SlicerCacheMetadata ReadSlicerCache(XDocument xml)
    {
        var root = xml.Root;
        return new SlicerCacheMetadata(
            root?.Attribute("name")?.Value ?? "",
            root?.Attribute("sourceName")?.Value,
            root?.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "pivotTable", StringComparison.OrdinalIgnoreCase))?.Attribute("name")?.Value,
            root?.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "selectedItem", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Attribute("value")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList() ?? []);
    }

    private static TimelineCacheMetadata ReadTimelineCache(XDocument xml)
    {
        var root = xml.Root;
        return new TimelineCacheMetadata(
            root?.Attribute("name")?.Value ?? "",
            root?.Attribute("sourceName")?.Value,
            root?.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "pivotTable", StringComparison.OrdinalIgnoreCase))?.Attribute("name")?.Value,
            root?.Attribute("startDate")?.Value,
            root?.Attribute("endDate")?.Value,
            root?.Attribute("selectedStartDate")?.Value,
            root?.Attribute("selectedEndDate")?.Value);
    }

    private static IReadOnlyList<ExternalLinkModel> LoadExternalLinkMetadata(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return [];

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var workbookXml = LoadXml(workbookEntry);
            var workbookRels = LoadRelationshipTargets(
                archive,
                "xl/_rels/workbook.xml.rels",
                "xl/workbook.xml",
                packageRelNs);
            var result = new List<ExternalLinkModel>();
            foreach (var externalReference in workbookXml.Root?
                         .Element(workbookNs + "externalReferences")?
                         .Elements(workbookNs + "externalReference") ?? [])
            {
                var relId = externalReference.Attribute(relNs + "id")?.Value;
                if (string.IsNullOrWhiteSpace(relId) ||
                    !workbookRels.TryGetValue(relId, out var externalLinkPath))
                {
                    continue;
                }

                var model = new ExternalLinkModel { PackagePart = externalLinkPath };
                var externalLinkRelsEntry = archive.GetEntry(GetWorksheetRelsPath(externalLinkPath));
                if (externalLinkRelsEntry is not null)
                {
                    var externalLinkRelsXml = LoadXml(externalLinkRelsEntry);
                    var pathRelationship = externalLinkRelsXml.Root?
                        .Elements(packageRelNs + "Relationship")
                        .FirstOrDefault(relationship =>
                            (relationship.Attribute("Type")?.Value ?? "").EndsWith("/externalLinkPath", StringComparison.OrdinalIgnoreCase));
                    model.TargetUri = pathRelationship?.Attribute("Target")?.Value;
                    model.TargetMode = pathRelationship?.Attribute("TargetMode")?.Value;
                }

                result.Add(model);
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private static StructuredTablePackageMetadata LoadStructuredTableMetadata(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry is null || workbookRelsEntry is null)
                return StructuredTablePackageMetadata.Empty;

            var workbookXml = LoadXml(workbookEntry);
            var workbookRelsXml = LoadXml(workbookRelsEntry);

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var workbookRels = workbookRelsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
                .ToDictionary(
                    e => e.Attribute("Id")!.Value,
                    e => ResolveRelationshipTarget("xl/workbook.xml", e.Attribute("Target")!.Value),
                    StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var sheetsByPath = GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
                .ToDictionary(pair => pair.WorksheetPath, pair => pair.SheetName, StringComparer.OrdinalIgnoreCase);
            var tablesBySheetName = LoadStructuredTablesBySheetName(archive, sheetsByPath, workbookNs, relNs, packageRelNs);
            return new StructuredTablePackageMetadata(tablesBySheetName);
        }
        catch
        {
            return StructuredTablePackageMetadata.Empty;
        }
    }

    private static Dictionary<string, List<PendingStructuredTableModel>> LoadStructuredTablesBySheetName(
        ZipArchive archive,
        IReadOnlyDictionary<string, string> sheetsByPath,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var result = new Dictionary<string, List<PendingStructuredTableModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (worksheetPath, sheetName) in sheetsByPath)
        {
            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = LoadXml(worksheetEntry);
            var tableRelIds = worksheetXml.Root?
                .Element(workbookNs + "tableParts")?
                .Elements(workbookNs + "tablePart")
                .Select(e => e.Attribute(relNs + "id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList() ?? [];
            if (tableRelIds.Count == 0)
                continue;

            var worksheetRels = LoadRelationshipTargets(archive, GetWorksheetRelsPath(worksheetPath), worksheetPath, packageRelNs);
            foreach (var tableRelId in tableRelIds)
            {
                if (!worksheetRels.TryGetValue(tableRelId, out var tablePath))
                    continue;

                var tableEntry = archive.GetEntry(tablePath);
                if (tableEntry is null)
                    continue;

                var tableXml = LoadXml(tableEntry);
                if (TryReadStructuredTable(tableXml, tablePath, out var table))
                {
                    if (!result.TryGetValue(sheetName, out var sheetTables))
                    {
                        sheetTables = [];
                        result[sheetName] = sheetTables;
                    }

                    sheetTables.Add(table);
                }
            }
        }

        return result;
    }

    private static bool TryReadStructuredTable(XDocument tableXml, string tablePath, out PendingStructuredTableModel table)
    {
        table = new PendingStructuredTableModel(
            0,
            "",
            "",
            "",
            false,
            false,
            null,
            false,
            false,
            false,
            false,
            tablePath,
            [],
            []);
        var root = tableXml.Root;
        if (root is null)
            return false;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var id = ReadIntAttribute(root, "id") ?? 0;
        var name = root.Attribute("name")?.Value ?? "";
        var displayName = root.Attribute("displayName")?.Value ?? name;
        var rangeReference = root.Attribute("ref")?.Value ?? "";
        if (id <= 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rangeReference))
            return false;

        var style = root.Element(workbookNs + "tableStyleInfo");
        table = new PendingStructuredTableModel(
            id,
            name,
            displayName,
            rangeReference,
            root.Element(workbookNs + "autoFilter") is not null,
            ReadBoolAttribute(root, "totalsRowShown"),
            style?.Attribute("name")?.Value,
            ReadBoolAttribute(style, "showFirstColumn"),
            ReadBoolAttribute(style, "showLastColumn"),
            ReadBoolAttribute(style, "showRowStripes"),
            ReadBoolAttribute(style, "showColumnStripes"),
            tablePath,
            root.Element(workbookNs + "tableColumns")?
                .Elements(workbookNs + "tableColumn")
                .Select(column => new StructuredTableColumnModel(
                    ReadIntAttribute(column, "id") ?? 0,
                    column.Attribute("name")?.Value ?? "",
                    column.Attribute("totalsRowLabel")?.Value,
                    column.Attribute("totalsRowFunction")?.Value))
                .Where(column => column.Id > 0 && !string.IsNullOrWhiteSpace(column.Name))
                .ToList() ?? [],
            ReadStructuredTableFilterColumns(root.Element(workbookNs + "autoFilter"), workbookNs));
        return true;
    }

    private static List<StructuredTableFilterColumnModel> ReadStructuredTableFilterColumns(
        XElement? autoFilter,
        XNamespace workbookNs)
    {
        if (autoFilter is null)
            return [];

        return autoFilter
            .Elements(workbookNs + "filterColumn")
            .Select(column =>
            {
                var filters = column.Element(workbookNs + "filters");
                return new StructuredTableFilterColumnModel(
                    ReadIntAttribute(column, "colId") ?? -1,
                    filters?
                        .Elements(workbookNs + "filter")
                        .Select(filter => filter.Attribute("val")?.Value)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!)
                        .ToList() ?? [],
                    ReadBoolAttribute(filters, "blank"));
            })
            .Where(column => column.ColumnId >= 0 && (column.Values.Count > 0 || column.IncludeBlank))
            .ToList();
    }

    private static StructuredTableModel ToStructuredTableModel(PendingStructuredTableModel pending, SheetId sheetId)
    {
        var table = new StructuredTableModel
        {
            Id = pending.Id,
            Name = pending.Name,
            DisplayName = pending.DisplayName,
            Range = GridRange.Parse(pending.RangeReference, sheetId),
            HasAutoFilter = pending.HasAutoFilter,
            TotalsRowShown = pending.TotalsRowShown,
            StyleName = pending.StyleName,
            ShowFirstColumn = pending.ShowFirstColumn,
            ShowLastColumn = pending.ShowLastColumn,
            ShowRowStripes = pending.ShowRowStripes,
            ShowColumnStripes = pending.ShowColumnStripes,
            PackagePart = pending.PackagePart
        };
        table.Columns.AddRange(pending.Columns);
        table.FilterColumns.AddRange(pending.FilterColumns);
        return table;
    }

    private static IEnumerable<(string SheetName, string WorksheetPath)> GetWorkbookSheetPaths(
        XDocument workbookXml,
        IReadOnlyDictionary<string, string> workbookRels,
        XNamespace workbookNs,
        XNamespace relNs)
    {
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (!string.IsNullOrWhiteSpace(name) &&
                !string.IsNullOrWhiteSpace(relId) &&
                workbookRels.TryGetValue(relId, out var worksheetPath))
            {
                yield return (name, worksheetPath);
            }
        }
    }

    private static Dictionary<string, List<PendingPivotTableModel>> LoadPivotTablesBySheetName(
        ZipArchive archive,
        IReadOnlyDictionary<string, string> sheetsByPath,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var result = new Dictionary<string, List<PendingPivotTableModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (worksheetPath, sheetName) in sheetsByPath)
        {
            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = LoadXml(worksheetEntry);
            var pivotRelIds = worksheetXml.Root?
                .Elements(workbookNs + "pivotTableDefinition")
                .Select(e => e.Attribute(relNs + "id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList() ?? [];
            if (pivotRelIds.Count == 0)
                continue;

            var worksheetRels = LoadRelationshipTargets(archive, GetWorksheetRelsPath(worksheetPath), worksheetPath, packageRelNs);
            foreach (var pivotRelId in pivotRelIds)
            {
                if (!worksheetRels.TryGetValue(pivotRelId, out var pivotPath))
                    continue;

                var pivotEntry = archive.GetEntry(pivotPath);
                if (pivotEntry is null)
                    continue;

                var pivotXml = LoadXml(pivotEntry);
                if (TryReadPivotTable(pivotXml, pivotPath, out var pivotTable))
                {
                    if (!result.TryGetValue(sheetName, out var sheetTables))
                    {
                        sheetTables = [];
                        result[sheetName] = sheetTables;
                    }

                    sheetTables.Add(pivotTable);
                }
            }
        }

        return result;
    }

    private static Dictionary<string, string> LoadRelationshipTargets(
        ZipArchive archive,
        string relsPath,
        string sourcePart,
        XNamespace packageRelNs)
    {
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var relsXml = LoadXml(relsEntry);
        return relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => ResolveRelationshipTarget(sourcePart, e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryReadPivotTable(XDocument pivotXml, string pivotPath, out PendingPivotTableModel pivotTable)
    {
        pivotTable = new PendingPivotTableModel("", 0, "", pivotPath, false, PivotSubtotalPlacement.Bottom, true, true, true, true, false, PivotReportLayout.Tabular, "PivotStyleLight16", true, true, false, false, [], [], [], [], [], [], [], [], []);
        var root = pivotXml.Root;
        if (root is null)
            return false;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var name = root.Attribute("name")?.Value ?? "";
        var cacheId = ReadIntAttribute(root, "cacheId") ?? 0;
        var targetReference = root.Element(workbookNs + "location")?.Attribute("ref")?.Value ?? "";
        if (string.IsNullOrWhiteSpace(name) || cacheId <= 0 || string.IsNullOrWhiteSpace(targetReference))
            return false;

        var calculatedFields = ReadPivotCalculatedFields(root.Element(workbookNs + "calculatedFields"), workbookNs);
        var styleInfo = root.Element(workbookNs + "pivotTableStyleInfo");
        pivotTable = new PendingPivotTableModel(
            name,
            cacheId,
            targetReference,
            pivotPath,
            ReadBoolAttribute(root.Element(workbookNs + "pivotFields")?.Elements(workbookNs + "pivotField").FirstOrDefault(), "defaultSubtotal"),
            ReadBoolAttribute(root.Element(workbookNs + "pivotFields")?.Elements(workbookNs + "pivotField").FirstOrDefault(), "subtotalTop")
                ? PivotSubtotalPlacement.Top
                : PivotSubtotalPlacement.Bottom,
            ReadBoolAttribute(root, "showGrandTotals", defaultValue: true),
            ReadBoolAttribute(root, "showRowGrandTotals", ReadBoolAttribute(root, "showGrandTotals", defaultValue: true)),
            ReadBoolAttribute(root, "showColumnGrandTotals", ReadBoolAttribute(root, "showGrandTotals", defaultValue: true)),
            ReadBoolAttribute(root, "repeatItemLabels", defaultValue: true),
            ReadBoolAttribute(root, "blankLineAfterItems"),
            ReadPivotReportLayout(root.Attribute("reportLayout")?.Value),
            styleInfo?.Attribute("name")?.Value ?? "PivotStyleLight16",
            ReadBoolAttribute(styleInfo, "showRowHeaders", defaultValue: true),
            ReadBoolAttribute(styleInfo, "showColHeaders", defaultValue: true),
            ReadBoolAttribute(styleInfo, "showRowStripes"),
            ReadBoolAttribute(styleInfo, "showColStripes"),
            ReadPivotFieldIndexes(root.Element(workbookNs + "rowFields"), workbookNs),
            ReadPivotFieldIndexes(root.Element(workbookNs + "colFields"), workbookNs),
            ReadPivotPageFields(root.Element(workbookNs + "pageFields"), workbookNs),
            ReadPivotDataFields(root.Element(workbookNs + "dataFields"), workbookNs, calculatedFields),
            calculatedFields,
            ReadPivotCalculatedItems(root.Element(workbookNs + "calculatedItems"), workbookNs),
            ReadPivotValueFilters(root.Element(workbookNs + "valueFilters"), workbookNs),
            ReadPivotLabelFilters(root.Element(workbookNs + "labelFilters"), workbookNs),
            ReadPivotSorts(root.Element(workbookNs + "pivotSorts"), workbookNs));
        return true;
    }

    private static List<PivotTableStyleModel> LoadPivotTableStyleMetadata(Stream xlsxStream)
    {
        var result = new List<PivotTableStyleModel>();
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var stylesEntry = archive.GetEntry("xl/styles.xml");
            if (stylesEntry is null)
                return result;

            var stylesXml = LoadXml(stylesEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var styleElement in stylesXml.Root?
                         .Element(workbookNs + "tableStyles")?
                         .Elements(workbookNs + "tableStyle") ?? [])
            {
                var name = styleElement.Attribute("name")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var appliesToPivotTables = ReadBoolAttribute(styleElement, "pivot");
                if (!appliesToPivotTables)
                    continue;

                var model = new PivotTableStyleModel
                {
                    Name = name,
                    AppliesToPivotTables = true,
                    AppliesToTables = ReadBoolAttribute(styleElement, "table")
                };
                foreach (var element in styleElement.Elements(workbookNs + "tableStyleElement"))
                {
                    var type = element.Attribute("type")?.Value;
                    if (string.IsNullOrWhiteSpace(type))
                        continue;

                    model.Elements.Add(new PivotTableStyleElementModel(
                        type,
                        ReadIntAttribute(element, "dxfId"),
                        ReadIntAttribute(element, "size")));
                }

                result.Add(model);
            }
        }
        catch
        {
            return result;
        }

        return result;
    }

    private static List<PivotFieldModel> ReadPivotFieldIndexes(XElement? fieldsElement, XNamespace workbookNs)
    {
        if (fieldsElement is null)
            return [];

        return fieldsElement
            .Elements(workbookNs + "field")
            .Select(field =>
            {
                var index = ReadIntAttribute(field, "x");
                return index.HasValue
                    ? new PivotFieldModel(
                        index.Value,
                        field.Attribute("name")?.Value,
                        ReadCsvAttribute(field.Attribute("selectedItems")?.Value),
                        Grouping: ReadPivotFieldGrouping(field.Attribute("groupBy")?.Value),
                        GroupStart: ReadDoubleAttribute(field, "groupStart"),
                        GroupEnd: ReadDoubleAttribute(field, "groupEnd"),
                        GroupInterval: ReadDoubleAttribute(field, "groupInterval"))
                    : null;
            })
            .Where(field => field is not null)
            .Select(field => field!)
            .ToList();
    }

    private static List<PivotFieldModel> ReadPivotPageFields(XElement? fieldsElement, XNamespace workbookNs)
    {
        if (fieldsElement is null)
            return [];

        var pageFields = fieldsElement
            .Elements(workbookNs + "pageField")
            .Select(field => new PivotFieldModel(
                ReadIntAttribute(field, "fld") ?? -1,
                field.Attribute("name")?.Value,
                ReadCsvAttribute(field.Attribute("selectedItems")?.Value),
                ReadPivotFieldGrouping(field.Attribute("groupBy")?.Value),
                ReadDoubleAttribute(field, "groupStart"),
                ReadDoubleAttribute(field, "groupEnd"),
                ReadDoubleAttribute(field, "groupInterval")))
            .Where(field => field.SourceFieldIndex >= 0)
            .ToList();
        if (pageFields.Count > 0)
            return pageFields;

        return ReadPivotFieldIndexes(fieldsElement, workbookNs);
    }

    private static List<PivotDataFieldModel> ReadPivotDataFields(
        XElement? dataFieldsElement,
        XNamespace workbookNs,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields)
    {
        if (dataFieldsElement is null)
            return [];

        return dataFieldsElement
            .Elements(workbookNs + "dataField")
            .Select(field =>
            {
                var fieldIndex = ReadIntAttribute(field, "fld") ?? -1;
                var calculatedFieldName = field.Attribute("calculatedField")?.Value ??
                    calculatedFields.FirstOrDefault(calculated => string.Equals(calculated.Name, field.Attribute("name")?.Value, StringComparison.OrdinalIgnoreCase))?.Name;
                return new PivotDataFieldModel(
                    calculatedFieldName is null ? fieldIndex : -1,
                    field.Attribute("name")?.Value ?? "",
                    field.Attribute("subtotal")?.Value ?? "sum",
                    ReadIntAttribute(field, "numFmtId"),
                    calculatedFieldName,
                    ReadPivotShowValuesAs(field.Attribute("showValuesAs")?.Value),
                    ReadIntAttribute(field, "baseField"),
                    field.Attribute("baseItem")?.Value);
            })
            .Where(field => field.SourceFieldIndex >= 0 || field.CalculatedFieldName is not null)
            .ToList();
    }

    private static List<PivotValueFilterModel> ReadPivotValueFilters(XElement? valueFiltersElement, XNamespace workbookNs)
    {
        if (valueFiltersElement is null)
            return [];

        return valueFiltersElement
            .Elements(workbookNs + "valueFilter")
            .Select(filter => new PivotValueFilterModel(
                ReadIntAttribute(filter, "dataField") ?? -1,
                ReadPivotValueFilterKind(filter.Attribute("type")?.Value),
                ReadIntAttribute(filter, "count") ?? 0,
                ReadDoubleAttribute(filter, "comparisonValue"),
                ReadDoubleAttribute(filter, "comparisonValue2"),
                ReadIntAttribute(filter, "field")))
            .Where(filter => filter.DataFieldIndex >= 0 &&
                             (filter.Count > 0 ||
                              filter.ComparisonValue is not null ||
                              filter.Kind is PivotValueFilterKind.AboveAverage or PivotValueFilterKind.BelowAverage))
            .ToList();
    }

    private static List<PivotLabelFilterModel> ReadPivotLabelFilters(XElement? labelFiltersElement, XNamespace workbookNs)
    {
        if (labelFiltersElement is null)
            return [];

        return labelFiltersElement
            .Elements(workbookNs + "labelFilter")
            .Select(filter => new PivotLabelFilterModel(
                ReadIntAttribute(filter, "field") ?? -1,
                ReadPivotLabelFilterKind(filter.Attribute("type")?.Value),
                filter.Attribute("value")?.Value ?? "",
                filter.Attribute("value2")?.Value))
            .Where(filter => filter.SourceFieldIndex >= 0 && !string.IsNullOrEmpty(filter.Value))
            .ToList();
    }

    private static List<PivotSortModel> ReadPivotSorts(XElement? sortsElement, XNamespace workbookNs)
    {
        if (sortsElement is null)
            return [];

        return sortsElement
            .Elements(workbookNs + "pivotSort")
            .Select(sort => new PivotSortModel(
                string.Equals(sort.Attribute("target")?.Value, "label", StringComparison.OrdinalIgnoreCase)
                    ? PivotSortTarget.Label
                    : PivotSortTarget.Value,
                string.Equals(sort.Attribute("direction")?.Value, "descending", StringComparison.OrdinalIgnoreCase)
                    ? PivotSortDirection.Descending
                    : PivotSortDirection.Ascending,
                ReadIntAttribute(sort, "dataField") ?? 0,
                ReadIntAttribute(sort, "field") ?? 0))
            .ToList();
    }

    private static IReadOnlyList<string>? ReadCsvAttribute(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static PivotFieldGrouping ReadPivotFieldGrouping(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "years" or "year" => PivotFieldGrouping.Year,
            "quarters" or "quarter" => PivotFieldGrouping.Quarter,
            "months" or "month" => PivotFieldGrouping.Month,
            "days" or "day" => PivotFieldGrouping.Day,
            "numberrange" or "number-range" or "number" => PivotFieldGrouping.NumberRange,
            _ => PivotFieldGrouping.None
        };

    private static PivotReportLayout ReadPivotReportLayout(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "compact" or "compactform" or "compact-form" => PivotReportLayout.Compact,
            "outline" or "outlineform" or "outline-form" => PivotReportLayout.Outline,
            _ => PivotReportLayout.Tabular
        };

    private static PivotShowValuesAs ReadPivotShowValuesAs(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "percentofgrandtotal" or "percent-grand-total" => PivotShowValuesAs.PercentOfGrandTotal,
            "percentofrowtotal" or "percent-row-total" => PivotShowValuesAs.PercentOfRowTotal,
            "percentofcolumntotal" or "percentofcoltotal" or "percent-column-total" or "percent-col-total" => PivotShowValuesAs.PercentOfColumnTotal,
            "runningtotalin" or "running-total-in" => PivotShowValuesAs.RunningTotalIn,
            "differencefrom" or "difference-from" => PivotShowValuesAs.DifferenceFrom,
            "percentdifferencefrom" or "percent-difference-from" => PivotShowValuesAs.PercentDifferenceFrom,
            "ranksmallest" or "rank-smallest" => PivotShowValuesAs.RankSmallest,
            "ranklargest" or "rank-largest" => PivotShowValuesAs.RankLargest,
            "index" => PivotShowValuesAs.Index,
            "percentofparentrowtotal" or "percent-parent-row-total" => PivotShowValuesAs.PercentOfParentRowTotal,
            "percentofparentcolumntotal" or "percentofparentcoltotal" or "percent-parent-column-total" or "percent-parent-col-total" => PivotShowValuesAs.PercentOfParentColumnTotal,
            "percentofparenttotal" or "percent-parent-total" => PivotShowValuesAs.PercentOfParentTotal,
            _ => PivotShowValuesAs.None
        };

    private static PivotValueFilterKind ReadPivotValueFilterKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "bottom" => PivotValueFilterKind.Bottom,
            "greaterthan" or "greater_than" => PivotValueFilterKind.GreaterThan,
            "greaterthanorequal" or "greater_than_or_equal" => PivotValueFilterKind.GreaterThanOrEqual,
            "lessthan" or "less_than" => PivotValueFilterKind.LessThan,
            "lessthanorequal" or "less_than_or_equal" => PivotValueFilterKind.LessThanOrEqual,
            "equals" or "equal" => PivotValueFilterKind.Equals,
            "doesnotequal" or "not_equal" => PivotValueFilterKind.DoesNotEqual,
            "between" => PivotValueFilterKind.Between,
            "notbetween" or "not_between" => PivotValueFilterKind.NotBetween,
            "aboveaverage" or "above_average" => PivotValueFilterKind.AboveAverage,
            "belowaverage" or "below_average" => PivotValueFilterKind.BelowAverage,
            _ => PivotValueFilterKind.Top
        };

    private static PivotLabelFilterKind ReadPivotLabelFilterKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "doesnotequal" or "not_equal" => PivotLabelFilterKind.DoesNotEqual,
            "beginswith" or "begins_with" => PivotLabelFilterKind.BeginsWith,
            "endswith" or "ends_with" => PivotLabelFilterKind.EndsWith,
            "contains" => PivotLabelFilterKind.Contains,
            "doesnotcontain" or "does_not_contain" => PivotLabelFilterKind.DoesNotContain,
            "greaterthan" or "greater_than" => PivotLabelFilterKind.GreaterThan,
            "greaterthanorequal" or "greater_than_or_equal" => PivotLabelFilterKind.GreaterThanOrEqual,
            "lessthan" or "less_than" => PivotLabelFilterKind.LessThan,
            "lessthanorequal" or "less_than_or_equal" => PivotLabelFilterKind.LessThanOrEqual,
            "between" => PivotLabelFilterKind.Between,
            _ => PivotLabelFilterKind.Equals
        };

    private static List<PivotCalculatedFieldModel> ReadPivotCalculatedFields(XElement? calculatedFieldsElement, XNamespace workbookNs)
    {
        if (calculatedFieldsElement is null)
            return [];

        return calculatedFieldsElement
            .Elements(workbookNs + "calculatedField")
            .Select(field => new PivotCalculatedFieldModel(
                field.Attribute("name")?.Value ?? "",
                field.Attribute("formula")?.Value ?? ""))
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .ToList();
    }

    private static List<PivotCalculatedItemModel> ReadPivotCalculatedItems(XElement? calculatedItemsElement, XNamespace workbookNs)
    {
        if (calculatedItemsElement is null)
            return [];

        return calculatedItemsElement
            .Elements(workbookNs + "calculatedItem")
            .Select(item => new PivotCalculatedItemModel(
                ReadIntAttribute(item, "field") ?? -1,
                item.Attribute("name")?.Value ?? "",
                item.Attribute("formula")?.Value ?? ""))
            .Where(item => item.SourceFieldIndex >= 0 && !string.IsNullOrWhiteSpace(item.Name))
            .ToList();
    }

    private static PivotTableModel ToPivotTableModel(PendingPivotTableModel pending, SheetId sheetId)
    {
        var pivotTable = new PivotTableModel
        {
            Name = pending.Name,
            CacheId = pending.CacheId,
            TargetRange = GridRange.Parse(pending.TargetReference, sheetId),
            PackagePart = pending.PackagePart,
            ShowSubtotals = pending.ShowSubtotals,
            SubtotalPlacement = pending.SubtotalPlacement,
            ShowRowGrandTotals = pending.ShowRowGrandTotals,
            ShowColumnGrandTotals = pending.ShowColumnGrandTotals,
            RepeatItemLabels = pending.RepeatItemLabels,
            BlankLineAfterItems = pending.BlankLineAfterItems,
            ReportLayout = pending.ReportLayout,
            StyleName = string.IsNullOrWhiteSpace(pending.StyleName) ? "PivotStyleLight16" : pending.StyleName,
            ShowRowHeaders = pending.ShowRowHeaders,
            ShowColumnHeaders = pending.ShowColumnHeaders,
            ShowRowStripes = pending.ShowRowStripes,
            ShowColumnStripes = pending.ShowColumnStripes
        };

        pivotTable.RowFields.AddRange(pending.RowFields);
        pivotTable.ColumnFields.AddRange(pending.ColumnFields);
        pivotTable.PageFields.AddRange(pending.PageFields);
        pivotTable.DataFields.AddRange(pending.DataFields);
        pivotTable.CalculatedFields.AddRange(pending.CalculatedFields);
        pivotTable.CalculatedItems.AddRange(pending.CalculatedItems);
        pivotTable.ValueFilters.AddRange(pending.ValueFilters);
        pivotTable.LabelFilters.AddRange(pending.LabelFilters);
        pivotTable.Sorts.AddRange(pending.Sorts);
        return pivotTable;
    }

    private static void ResolvePivotChartCacheBindings(Workbook workbook, Sheet chartSheet)
    {
        if (chartSheet.Charts.Count == 0)
            return;

        foreach (var chart in chartSheet.Charts)
        {
            if (!chart.IsPivotChart ||
                chart.PivotCacheId is not null ||
                string.IsNullOrWhiteSpace(chart.PivotTableName))
            {
                continue;
            }

            if (TryFindPivotTableForChart(workbook, chartSheet, chart, out var pivotTable))
                chart.PivotCacheId = pivotTable.CacheId;
        }
    }

    private static bool TryFindPivotTableForChart(
        Workbook workbook,
        Sheet chartSheet,
        ChartModel chart,
        out PivotTableModel pivotTable)
    {
        if (!string.IsNullOrWhiteSpace(chart.PivotSourceSheetName))
        {
            foreach (var sheet in workbook.Sheets)
            {
                if (!string.Equals(sheet.Name, chart.PivotSourceSheetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
                    string.Equals(pivot.Name, chart.PivotTableName, StringComparison.OrdinalIgnoreCase))!;
                return pivotTable is not null;
            }
        }

        pivotTable = chartSheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, chart.PivotTableName, StringComparison.OrdinalIgnoreCase))!;
        return pivotTable is not null;
    }

    private static int? ReadIntAttribute(XElement element, string attributeName) =>
        int.TryParse(element.Attribute(attributeName)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static double? ReadDoubleAttribute(XElement element, string attributeName) =>
        double.TryParse(element.Attribute(attributeName)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool ReadBoolAttribute(XElement? element, string attributeName, bool defaultValue = false)
    {
        var value = element?.Attribute(attributeName)?.Value;
        if (value is null)
            return defaultValue;
        return value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static MemoryStream CreateClosedXmlLoadPackage(MemoryStream sourcePackage)
    {
        var sanitized = new MemoryStream();
        var sourceBytes = sourcePackage.ToArray();
        sanitized.Write(sourceBytes, 0, sourceBytes.Length);
        sanitized.Position = 0;
        using (var archive = new ZipArchive(sanitized, ZipArchiveMode.Update, leaveOpen: true))
        {
            RemoveWorkbookPivotCacheReferences(archive);
            RemoveWorksheetPivotTableReferences(archive);
            RemovePivotRelationships(archive);
            RemovePivotPackageParts(archive);
            RemoveUnsupportedConditionalFormattingBlocks(archive);
        }

        sanitized.Position = 0;
        return sanitized;
    }

    private static void RemoveWorkbookPivotCacheReferences(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "pivotCaches").Remove();
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void RemoveWorksheetPivotTableReferences(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var worksheetEntry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var worksheetXml = LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var pivotReferences = root.Elements(workbookNs + "pivotTableDefinition").ToList();
            if (pivotReferences.Count == 0)
                continue;

            pivotReferences.Remove();
            ReplacePackageXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static void RemovePivotRelationships(ZipArchive archive)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        foreach (var relsEntry in archive.Entries
                     .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var relsXml = LoadXml(relsEntry);
            var root = relsXml.Root;
            if (root is null)
                continue;

            var pivotRelationships = root
                .Elements(packageRelNs + "Relationship")
                .Where(relationship =>
                {
                    var type = relationship.Attribute("Type")?.Value ?? "";
                    return type.EndsWith("/pivotCacheDefinition", StringComparison.OrdinalIgnoreCase) ||
                           type.EndsWith("/pivotTable", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            if (pivotRelationships.Count == 0)
                continue;

            pivotRelationships.Remove();
            ReplacePackageXml(archive, relsEntry.FullName, relsXml);
        }
    }

    private static void RemovePivotPackageParts(ZipArchive archive)
    {
        foreach (var entry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
                         entry.FullName.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            entry.Delete();
        }

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = LoadXml(contentTypesEntry);
        var root = contentTypesXml.Root;
        if (root is null)
            return;

        var pivotOverrides = root
            .Elements(contentTypeNs + "Override")
            .Where(element =>
            {
                var partName = element.Attribute("PartName")?.Value ?? "";
                return partName.StartsWith("/xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
                       partName.StartsWith("/xl/pivotTables/", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        if (pivotOverrides.Count == 0)
            return;

        pivotOverrides.Remove();
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static void RemoveUnsupportedConditionalFormattingBlocks(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var worksheetEntry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var worksheetXml = LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var unsupportedBlocks = root
                .Elements(worksheetNs + "conditionalFormatting")
                .Where(block => ConditionalFormattingHasUnsupportedRule(block, worksheetNs))
                .ToList();
            if (unsupportedBlocks.Count == 0)
                continue;

            unsupportedBlocks.Remove();
            ReplacePackageXml(archive, worksheetEntry.FullName, worksheetXml);
        }
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

    private static WorkbookCalculationProperties LoadWorkbookCalculationProperties(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return WorkbookCalculationProperties.Default;

            var workbookXml = LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var calcPr = workbookXml.Root?.Element(workbookNs + "calcPr");
            if (calcPr is null)
                return WorkbookCalculationProperties.Default;

            var mode = string.Equals(calcPr.Attribute("calcMode")?.Value, "manual", StringComparison.OrdinalIgnoreCase)
                ? WorkbookCalculationMode.Manual
                : string.Equals(calcPr.Attribute("calcMode")?.Value, "auto", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(calcPr.Attribute("calcMode")?.Value, "autoNoTable", StringComparison.OrdinalIgnoreCase)
                    ? WorkbookCalculationMode.Automatic
                    : (WorkbookCalculationMode?)null;

            return new WorkbookCalculationProperties(
                mode,
                ReadBoolAttribute(calcPr, "fullCalcOnLoad"),
                ReadBoolAttribute(calcPr, "forceFullCalc"),
                ReadBoolAttribute(calcPr, "iterate"),
                ReadIntAttribute(calcPr, "iterateCount"),
                ReadDoubleAttribute(calcPr, "iterateDelta"));
        }
        catch
        {
            return WorkbookCalculationProperties.Default;
        }
    }

    private sealed record WorkbookCalculationProperties(
        WorkbookCalculationMode? Mode,
        bool FullCalculationOnLoad,
        bool ForceFullCalculation,
        bool IterativeCalculation,
        int? MaxIterations,
        double? MaxChange)
    {
        public static WorkbookCalculationProperties Default { get; } = new(null, false, false, false, null, null);
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

    private static bool TryReadRgbColor(XElement? element, out RgbColor color)
    {
        color = default;
        var rgb = element?.Attribute("rgb")?.Value;
        if (string.IsNullOrWhiteSpace(rgb))
            return false;
        var normalized = rgb.Trim().TrimStart('#');
        if (normalized.Length == 8)
            normalized = normalized[2..];
        if (!TryParseHexColor(normalized, out var cellColor))
            return false;
        color = RgbColor.FromCellColor(cellColor);
        return true;
    }

    private static bool TryReadCellColor(XElement? element, out CellColor color)
    {
        color = default;
        var rgb = element?.Attribute("rgb")?.Value;
        if (string.IsNullOrWhiteSpace(rgb))
            return false;

        var normalized = rgb.Trim().TrimStart('#');
        if (normalized.Length == 8)
            normalized = normalized[2..];
        return TryParseHexColor(normalized, out color);
    }

    private static CfThresholdType FromCfvoType(string? type) =>
        type?.ToLowerInvariant() switch
        {
            "max" => CfThresholdType.Max,
            "num" => CfThresholdType.Number,
            "percent" => CfThresholdType.Percent,
            "percentile" => CfThresholdType.Percentile,
            "formula" => CfThresholdType.Formula,
            _ => CfThresholdType.Min
        };

    private static string ToCfvoType(CfThresholdType type) =>
        type switch
        {
            CfThresholdType.Max => "max",
            CfThresholdType.Number => "num",
            CfThresholdType.Percent => "percent",
            CfThresholdType.Percentile => "percentile",
            CfThresholdType.Formula => "formula",
            _ => "min"
        };

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

        if (sourceDirectory.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase))
        {
            return $"../tables/{targetPath["xl/tables/".Length..]}";
        }

        if (sourceDirectory.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase))
        {
            return $"../pivotTables/{targetPath["xl/pivotTables/".Length..]}";
        }

        if (sourceDirectory.Equals("xl/pivotTables", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase))
        {
            return $"../pivotCache/{targetPath["xl/pivotCache/".Length..]}";
        }

        if (sourceDirectory.Equals("xl/slicers", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/slicerCaches/", StringComparison.OrdinalIgnoreCase))
        {
            return $"../slicerCaches/{targetPath["xl/slicerCaches/".Length..]}";
        }

        if (sourceDirectory.Equals("xl/timelines", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/timelineCaches/", StringComparison.OrdinalIgnoreCase))
        {
            return $"../timelineCaches/{targetPath["xl/timelineCaches/".Length..]}";
        }

        if (sourceDirectory.Equals("xl/drawings", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase))
        {
            return $"../charts/{targetPath["xl/charts/".Length..]}";
        }

        if (sourceDirectory.Equals("xl/drawings", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase))
        {
            return $"../media/{targetPath["xl/media/".Length..]}";
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

    private static void EnsureSpecificContentType(ZipArchive archive, string partName, string contentType)
    {
        const string contentTypesPath = "[Content_Types].xml";
        var entry = archive.GetEntry(contentTypesPath);
        if (entry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var xml = LoadXml(entry);
        var root = xml.Root;
        if (root is null)
            return;

        var normalizedPartName = partName.StartsWith('/') ? partName : $"/{partName}";
        root.Elements(contentTypeNs + "Override")
            .Where(element => string.Equals(element.Attribute("PartName")?.Value, normalizedPartName, StringComparison.OrdinalIgnoreCase))
            .Remove();
        root.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", normalizedPartName),
            new XAttribute("ContentType", contentType)));

        ReplacePackageXml(archive, contentTypesPath, xml);
    }

    private static SheetXmlLayout ReadHiddenSheetLayout(
        ZipArchive archive,
        string worksheetPath,
        ZipArchiveEntry worksheetEntry,
        IReadOnlyList<CellStyle> differentialStyles)
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
        var allowEditRanges = ReadAllowEditRanges(worksheetXml, worksheetNs);

        var sheetView = worksheetXml.Root?
            .Element(worksheetNs + "sheetViews")?
            .Elements(worksheetNs + "sheetView")
            .FirstOrDefault();
        var pane = sheetView?.Element(worksheetNs + "pane");
        var viewTopLeft = ParseOptionalCellReference(sheetView?.Attribute("topLeftCell")?.Value);
        var activeCell = ParseOptionalCellReference(
            sheetView?
                .Elements(worksheetNs + "selection")
                .Select(selection => selection.Attribute("activeCell")?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        var background = ReadWorksheetBackground(archive, worksheetPath, worksheetXml);
        var chartParts = ReadWorksheetChartParts(archive, worksheetPath, worksheetXml);
        var pictureParts = ReadWorksheetPictureParts(archive, worksheetPath, worksheetXml);
        var (textBoxParts, shapeParts) = ReadWorksheetShapeParts(archive, worksheetPath, worksheetXml);
        var sparklines = ReadWorksheetSparklines(worksheetXml);
        var advancedConditionalFormats = ReadAdvancedConditionalFormats(worksheetXml, worksheetNs, differentialStyles);
        var codeName = worksheetXml.Root?
            .Element(worksheetNs + "sheetPr")?
            .Attribute("codeName")?
            .Value;

        return new SheetXmlLayout(
            hiddenRows,
            hiddenCols,
            isProtected,
            passwordHash,
            allowEditRanges,
            ParseWorksheetViewMode(sheetView?.Attribute("view")?.Value),
            !IsFalse(sheetView?.Attribute("showGridLines")?.Value),
            !IsFalse(sheetView?.Attribute("showRowColHeaders")?.Value),
            !IsFalse(sheetView?.Attribute("showRuler")?.Value),
            ParseZoomPercent(sheetView?.Attribute("zoomScale")?.Value),
            IsTruthy(sheetView?.Attribute("showFormulas")?.Value),
            pane?.Attribute("state")?.Value,
            ParsePaneSplit(pane?.Attribute("ySplit")?.Value),
            ParsePaneSplit(pane?.Attribute("xSplit")?.Value),
            viewTopLeft?.Row,
            viewTopLeft?.Col,
            activeCell?.Row,
            activeCell?.Col,
            background,
            rowOutlineLevels,
            colOutlineLevels,
            groupHiddenRows,
            groupHiddenCols,
            chartParts,
            pictureParts,
            textBoxParts,
            shapeParts,
            sparklines,
            advancedConditionalFormats,
            codeName);
    }

    private static IReadOnlyList<GridRange> ReadAllowEditRanges(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var ranges = new List<GridRange>();
        var tempSheet = SheetId.New();
        foreach (var protectedRange in worksheetXml.Root?
                     .Element(worksheetNs + "protectedRanges")?
                     .Elements(worksheetNs + "protectedRange") ?? [])
        {
            var sqref = protectedRange.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            foreach (var token in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    ranges.Add(token.Contains(':', StringComparison.Ordinal)
                        ? GridRange.Parse(token, tempSheet)
                        : new GridRange(CellAddress.Parse(token, tempSheet), CellAddress.Parse(token, tempSheet)));
                }
                catch
                {
                    // Skip malformed allow-edit references.
                }
            }
        }

        return ranges;
    }

    private static IReadOnlyList<ConditionalFormat> ReadAdvancedConditionalFormats(
        XDocument worksheetXml,
        XNamespace worksheetNs,
        IReadOnlyList<CellStyle> differentialStyles)
    {
        var result = new List<ConditionalFormat>();
        var tempSheet = SheetId.New();
        foreach (var conditionalFormatting in worksheetXml.Root?.Elements(worksheetNs + "conditionalFormatting") ?? [])
        {
            var sqref = conditionalFormatting.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            GridRange appliesTo;
            try
            {
                var firstRef = sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).First();
                appliesTo = firstRef.Contains(':', StringComparison.Ordinal)
                    ? GridRange.Parse(firstRef, tempSheet)
                    : new GridRange(CellAddress.Parse(firstRef, tempSheet), CellAddress.Parse(firstRef, tempSheet));
            }
            catch
            {
                continue;
            }

            foreach (var rule in conditionalFormatting.Elements(worksheetNs + "cfRule"))
            {
                var type = rule.Attribute("type")?.Value;
                var priority = ReadIntAttribute(rule, "priority") ?? 1;
                var formatIfTrue = ReadIntAttribute(rule, "dxfId") is { } dxfId &&
                    dxfId >= 0 &&
                    dxfId < differentialStyles.Count
                    ? differentialStyles[dxfId].Clone()
                    : null;
                if (string.Equals(type, "colorScale", StringComparison.OrdinalIgnoreCase) &&
                    rule.Element(worksheetNs + "colorScale") is { } colorScale)
                {
                    var format = ReadColorScaleConditionalFormat(colorScale, appliesTo, priority, worksheetNs);
                    format.FormatIfTrue = formatIfTrue;
                    result.Add(format);
                }
                else if (string.Equals(type, "dataBar", StringComparison.OrdinalIgnoreCase) &&
                         rule.Element(worksheetNs + "dataBar") is { } dataBar)
                {
                    var format = ReadDataBarConditionalFormat(dataBar, appliesTo, priority, worksheetNs);
                    format.FormatIfTrue = formatIfTrue;
                    result.Add(format);
                }
                else if (string.Equals(type, "iconSet", StringComparison.OrdinalIgnoreCase) &&
                         rule.Element(worksheetNs + "iconSet") is { } iconSet)
                {
                    result.Add(new ConditionalFormat
                    {
                        AppliesTo = appliesTo,
                        Priority = priority,
                        RuleType = CfRuleType.IconSet,
                        IconSetStyle = iconSet.Attribute("iconSet")?.Value,
                        IconSetShowValue = !IsFalse(iconSet.Attribute("showValue")?.Value),
                        IconSetReverse = IsTruthy(iconSet.Attribute("reverse")?.Value),
                        FormatIfTrue = formatIfTrue
                    });
                }
                else if (TryMapLongTailConditionalFormatRule(type, out var mappedType))
                {
                    result.Add(new ConditionalFormat
                    {
                        AppliesTo = appliesTo,
                        Priority = priority,
                        RuleType = mappedType,
                        AboveAverage = mappedType == CfRuleType.Top10
                            ? !IsTruthy(rule.Attribute("bottom")?.Value)
                            : !IsFalse(rule.Attribute("aboveAverage")?.Value),
                        TopBottomRank = ReadIntAttribute(rule, "rank") ?? 10,
                        TopBottomPercent = IsTruthy(rule.Attribute("percent")?.Value),
                        TextRuleText = rule.Attribute("text")?.Value,
                        DateOccurringPeriod = rule.Attribute("timePeriod")?.Value,
                        StopIfTrue = IsTruthy(rule.Attribute("stopIfTrue")?.Value),
                        FormulaText = rule.Element(worksheetNs + "formula")?.Value,
                        FormatIfTrue = formatIfTrue
                    });
                }
            }
        }

        return result;
    }

    private static bool TryMapLongTailConditionalFormatRule(string? type, out CfRuleType ruleType)
    {
        ruleType = type switch
        {
            "aboveAverage" => CfRuleType.AboveAverage,
            "top10" => CfRuleType.Top10,
            "uniqueValues" => CfRuleType.UniqueValues,
            "duplicateValues" => CfRuleType.DuplicateValues,
            "containsText" => CfRuleType.ContainsText,
            "notContainsText" => CfRuleType.NotContainsText,
            "beginsWith" => CfRuleType.BeginsWith,
            "endsWith" => CfRuleType.EndsWith,
            "timePeriod" => CfRuleType.DateOccurring,
            "containsBlanks" => CfRuleType.Blanks,
            "notContainsBlanks" => CfRuleType.NoBlanks,
            "containsErrors" => CfRuleType.Errors,
            "notContainsErrors" => CfRuleType.NoErrors,
            _ => default
        };
        return type is "aboveAverage" or "top10" or "uniqueValues" or "duplicateValues" or
            "containsText" or "notContainsText" or "beginsWith" or "endsWith" or "timePeriod" or
            "containsBlanks" or "notContainsBlanks" or "containsErrors" or "notContainsErrors";
    }

    private static bool ConditionalFormattingHasUnsupportedRule(XElement block, XNamespace worksheetNs) =>
        block
            .Elements(worksheetNs + "cfRule")
            .Any(rule => !IsSupportedConditionalFormatRuleType(rule.Attribute("type")?.Value));

    private static bool IsSupportedConditionalFormatRuleType(string? type) =>
        string.Equals(type, "cellIs", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "expression", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "colorScale", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "dataBar", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "iconSet", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "aboveAverage", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "top10", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "uniqueValues", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "duplicateValues", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "containsText", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "notContainsText", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "beginsWith", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "endsWith", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "timePeriod", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "containsBlanks", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "notContainsBlanks", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "containsErrors", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "notContainsErrors", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<SparklineModel> ReadWorksheetSparklines(XDocument worksheetXml)
    {
        var result = new List<SparklineModel>();
        var tempSheet = SheetId.New();
        foreach (var group in worksheetXml.Descendants().Where(element =>
                     string.Equals(element.Name.LocalName, "sparklineGroup", StringComparison.OrdinalIgnoreCase)))
        {
            var kind = group.Attribute("type")?.Value switch
            {
                "column" => SparklineKind.Column,
                "stacked" or "winLoss" => SparklineKind.WinLoss,
                _ => SparklineKind.Line
            };

            foreach (var sparkline in group.Descendants().Where(element =>
                         string.Equals(element.Name.LocalName, "sparkline", StringComparison.OrdinalIgnoreCase)))
            {
                var formula = sparkline.Elements().FirstOrDefault(element =>
                    string.Equals(element.Name.LocalName, "f", StringComparison.OrdinalIgnoreCase))?.Value;
                var location = sparkline.Elements().FirstOrDefault(element =>
                    string.Equals(element.Name.LocalName, "sqref", StringComparison.OrdinalIgnoreCase))?.Value;
                if (string.IsNullOrWhiteSpace(formula) || string.IsNullOrWhiteSpace(location))
                    continue;

                var bang = formula.LastIndexOf('!');
                var rangeText = bang >= 0 ? formula[(bang + 1)..] : formula;
                rangeText = rangeText.Replace("$", "", StringComparison.Ordinal);
                location = location.Replace("$", "", StringComparison.Ordinal);
                try
                {
                    result.Add(new SparklineModel
                    {
                        DataRange = GridRange.Parse(rangeText, tempSheet),
                        Location = CellAddress.Parse(location, tempSheet),
                        Kind = kind
                    });
                }
                catch
                {
                    // Skip malformed sparkline references.
                }
            }
        }

        return result;
    }

    private static ConditionalFormat ReadColorScaleConditionalFormat(
        XElement colorScale,
        GridRange appliesTo,
        int priority,
        XNamespace worksheetNs)
    {
        var thresholds = colorScale.Elements(worksheetNs + "cfvo").ToList();
        var colors = colorScale.Elements(worksheetNs + "color").ToList();
        var format = new ConditionalFormat
        {
            AppliesTo = appliesTo,
            Priority = priority,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = thresholds.Count >= 3 && colors.Count >= 3
        };

        ApplyThreshold(thresholds.ElementAtOrDefault(0), value =>
        {
            format.MinThresholdType = value.Type;
            format.MinThresholdValue = value.Value;
        });
        ApplyThreshold(thresholds.ElementAtOrDefault(1), value =>
        {
            if (format.UseThreeColorScale)
            {
                format.MidThresholdType = value.Type;
                format.MidThresholdValue = value.Value;
            }
            else
            {
                format.MaxThresholdType = value.Type;
                format.MaxThresholdValue = value.Value;
            }
        });
        if (format.UseThreeColorScale)
        {
            ApplyThreshold(thresholds.ElementAtOrDefault(2), value =>
            {
                format.MaxThresholdType = value.Type;
                format.MaxThresholdValue = value.Value;
            });
        }

        if (TryReadRgbColor(colors.ElementAtOrDefault(0), out var minColor))
            format.MinColor = minColor;
        if (format.UseThreeColorScale && TryReadRgbColor(colors.ElementAtOrDefault(1), out var midColor))
            format.MidColor = midColor;
        if (TryReadRgbColor(colors.ElementAtOrDefault(format.UseThreeColorScale ? 2 : 1), out var maxColor))
            format.MaxColor = maxColor;

        return format;
    }

    private static ConditionalFormat ReadDataBarConditionalFormat(
        XElement dataBar,
        GridRange appliesTo,
        int priority,
        XNamespace worksheetNs)
    {
        var thresholds = dataBar.Elements(worksheetNs + "cfvo").ToList();
        var format = new ConditionalFormat
        {
            AppliesTo = appliesTo,
            Priority = priority,
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = !IsFalse(dataBar.Attribute("showValue")?.Value),
            DataBarMinLength = ReadIntAttribute(dataBar, "minLength"),
            DataBarMaxLength = ReadIntAttribute(dataBar, "maxLength")
        };
        ApplyThreshold(thresholds.ElementAtOrDefault(0), value =>
        {
            format.DataBarMinThresholdType = value.Type;
            format.DataBarMinThresholdValue = value.Value;
        });
        ApplyThreshold(thresholds.ElementAtOrDefault(1), value =>
        {
            format.DataBarMaxThresholdType = value.Type;
            format.DataBarMaxThresholdValue = value.Value;
        });
        if (TryReadRgbColor(dataBar.Element(worksheetNs + "color"), out var color))
            format.DataBarColor = color;
        return format;
    }

    private static void ApplyThreshold(XElement? element, Action<(CfThresholdType Type, string? Value)> apply)
    {
        if (element is null)
            return;
        apply((FromCfvoType(element.Attribute("type")?.Value), element.Attribute("val")?.Value));
    }

    private static ConditionalFormat RemapConditionalFormat(ConditionalFormat source, SheetId sheetId) =>
        new()
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, source.AppliesTo.Start.Row, source.AppliesTo.Start.Col),
                new CellAddress(sheetId, source.AppliesTo.End.Row, source.AppliesTo.End.Col)),
            Priority = source.Priority,
            RuleType = source.RuleType,
            Operator = source.Operator,
            Value1 = source.Value1,
            Value2 = source.Value2,
            FormatIfTrue = source.FormatIfTrue?.Clone(),
            MinColor = source.MinColor,
            MidColor = source.MidColor,
            MaxColor = source.MaxColor,
            UseThreeColorScale = source.UseThreeColorScale,
            MinThresholdType = source.MinThresholdType,
            MinThresholdValue = source.MinThresholdValue,
            MidThresholdType = source.MidThresholdType,
            MidThresholdValue = source.MidThresholdValue,
            MaxThresholdType = source.MaxThresholdType,
            MaxThresholdValue = source.MaxThresholdValue,
            DataBarColor = source.DataBarColor,
            DataBarMinThresholdType = source.DataBarMinThresholdType,
            DataBarMinThresholdValue = source.DataBarMinThresholdValue,
            DataBarMaxThresholdType = source.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = source.DataBarMaxThresholdValue,
            DataBarShowValue = source.DataBarShowValue,
            DataBarMinLength = source.DataBarMinLength,
            DataBarMaxLength = source.DataBarMaxLength,
            AboveAverage = source.AboveAverage,
            FormulaText = source.FormulaText,
            IconSetStyle = source.IconSetStyle,
            IconSetShowValue = source.IconSetShowValue,
            IconSetReverse = source.IconSetReverse,
            TopBottomRank = source.TopBottomRank,
            TopBottomPercent = source.TopBottomPercent,
            TextRuleText = source.TextRuleText,
            DateOccurringPeriod = source.DateOccurringPeriod,
            StopIfTrue = source.StopIfTrue
        };

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

    private static CellAddress? ParseOptionalCellReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        return CellAddress.TryParse(reference.Split(':')[0], SheetId.New(), out var address)
            ? address
            : null;
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

    private static IReadOnlyList<XlsxPicturePackagePart> ReadWorksheetPictureParts(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        var pictures = new List<XlsxPicturePackagePart>();
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        var drawingRelId = worksheetXml.Root?
            .Element(worksheetNs + "drawing")?
            .Attribute(relNs + "id")?
            .Value;
        if (string.IsNullOrWhiteSpace(drawingRelId))
            return pictures;

        var worksheetRelsEntry = archive.GetEntry(GetWorksheetRelsPath(worksheetPath));
        if (worksheetRelsEntry is null)
            return pictures;

        var worksheetRelsXml = LoadXml(worksheetRelsEntry);
        var drawingTarget = worksheetRelsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, drawingRelId, StringComparison.Ordinal))?
            .Attribute("Target")?
            .Value;
        if (string.IsNullOrWhiteSpace(drawingTarget))
            return pictures;

        var drawingPath = ResolveRelationshipTarget(worksheetPath, drawingTarget);
        var drawingEntry = archive.GetEntry(drawingPath);
        if (drawingEntry is null)
            return pictures;

        var drawingRelsEntry = archive.GetEntry(GetWorksheetRelsPath(drawingPath));
        if (drawingRelsEntry is null)
            return pictures;

        var drawingXml = LoadXml(drawingEntry);
        var drawingRelsXml = LoadXml(drawingRelsEntry);
        foreach (var pictureElement in drawingXml.Descendants(spreadsheetDrawingNs + "pic"))
        {
            var imageRelId = pictureElement
                .Descendants(drawingNs + "blip")
                .Select(element => element.Attribute(relNs + "embed")?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(imageRelId))
                continue;

            var imageTarget = drawingRelsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, imageRelId, StringComparison.Ordinal))?
                .Attribute("Target")?
                .Value;
            if (string.IsNullOrWhiteSpace(imageTarget))
                continue;

            var imagePath = ResolveRelationshipTarget(drawingPath, imageTarget);
            var imageEntry = archive.GetEntry(imagePath);
            if (imageEntry is null)
                continue;

            using var imageStream = imageEntry.Open();
            using var ms = new MemoryStream();
            imageStream.CopyTo(ms);

            var anchor = pictureElement
                .Ancestors(spreadsheetDrawingNs + "twoCellAnchor")
                .Select(TryReadTwoCellAnchor)
                .FirstOrDefault(candidate => candidate is not null)
                ?? pictureElement
                    .Ancestors(spreadsheetDrawingNs + "oneCellAnchor")
                    .Select(TryReadOneCellAnchor)
                    .FirstOrDefault(candidate => candidate is not null)
                ?? pictureElement
                    .Ancestors(spreadsheetDrawingNs + "absoluteAnchor")
                    .Select(TryReadAbsoluteAnchor)
                    .FirstOrDefault(candidate => candidate is not null);
            var altText = pictureElement
                .Descendants(spreadsheetDrawingNs + "cNvPr")
                .Select(element => element.Attribute("descr")?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var sourceRectangle = pictureElement
                .Element(spreadsheetDrawingNs + "blipFill")?
                .Element(drawingNs + "srcRect");

            pictures.Add(new XlsxPicturePackagePart(
                ms.ToArray(),
                GetContentTypeFromPath(imagePath),
                altText,
                anchor,
                ReadSourceRectangleRatio(sourceRectangle, "l"),
                ReadSourceRectangleRatio(sourceRectangle, "t"),
                ReadSourceRectangleRatio(sourceRectangle, "r"),
                ReadSourceRectangleRatio(sourceRectangle, "b")));
        }

        return pictures;
    }

    private static double ReadSourceRectangleRatio(XElement? sourceRectangle, string attributeName)
    {
        if (!double.TryParse(
                sourceRectangle?.Attribute(attributeName)?.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return 0;
        }

        return Math.Clamp(value / 100000d, 0, 1);
    }

    private static (IReadOnlyList<XlsxTextBoxPackagePart> TextBoxes, IReadOnlyList<XlsxShapePackagePart> Shapes) ReadWorksheetShapeParts(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        var textBoxes = new List<XlsxTextBoxPackagePart>();
        var shapes = new List<XlsxShapePackagePart>();
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        var drawingXml = ReadWorksheetDrawingXml(archive, worksheetPath, worksheetXml);
        if (drawingXml is null)
            return (textBoxes, shapes);

        foreach (var shapeElement in drawingXml.Descendants(spreadsheetDrawingNs + "sp"))
        {
            var anchor = shapeElement
                .Ancestors(spreadsheetDrawingNs + "twoCellAnchor")
                .Select(TryReadTwoCellAnchor)
                .FirstOrDefault(candidate => candidate is not null)
                ?? shapeElement
                    .Ancestors(spreadsheetDrawingNs + "oneCellAnchor")
                    .Select(TryReadOneCellAnchor)
                    .FirstOrDefault(candidate => candidate is not null)
                ?? shapeElement
                    .Ancestors(spreadsheetDrawingNs + "absoluteAnchor")
                    .Select(TryReadAbsoluteAnchor)
                    .FirstOrDefault(candidate => candidate is not null);
            var altText = shapeElement
                .Descendants(spreadsheetDrawingNs + "cNvPr")
                .Select(element => element.Attribute("descr")?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var spPr = shapeElement.Element(spreadsheetDrawingNs + "spPr");
            var rotation = ReadDrawingRotation(spPr?.Element(drawingNs + "xfrm"));
            var gradientFill = ReadDrawingGradientFillColors(spPr?.Element(drawingNs + "gradFill"), drawingNs);
            var fillColor = gradientFill.StartColor ?? ReadDrawingSolidFillColor(spPr?.Element(drawingNs + "solidFill"), drawingNs);
            var outlineColor = ReadDrawingSolidFillColor(spPr?.Element(drawingNs + "ln")?.Element(drawingNs + "solidFill"), drawingNs);
            var hasShadowEffect = spPr?
                .Element(drawingNs + "effectLst")?
                .Element(drawingNs + "outerShdw") is not null;
            var text = string.Concat(shapeElement
                .Element(spreadsheetDrawingNs + "txBody")?
                .Descendants(drawingNs + "t")
                .Select(t => t.Value) ?? []);

            if (!string.IsNullOrEmpty(text))
            {
                textBoxes.Add(new XlsxTextBoxPackagePart(text, altText, anchor, rotation, fillColor, outlineColor));
                continue;
            }

            var preset = spPr?
                .Element(drawingNs + "prstGeom")?
                .Attribute("prst")?
                .Value;
            if (ToDrawingShapeKind(preset) is { } kind)
                shapes.Add(new XlsxShapePackagePart(
                    kind,
                    altText,
                    anchor,
                    rotation,
                    fillColor,
                    outlineColor,
                    gradientFill.EndColor,
                    hasShadowEffect));
        }

        return (textBoxes, shapes);
    }

    private static XDocument? ReadWorksheetDrawingXml(ZipArchive archive, string worksheetPath, XDocument worksheetXml)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var drawingRelId = worksheetXml.Root?
            .Element(worksheetNs + "drawing")?
            .Attribute(relNs + "id")?
            .Value;
        if (string.IsNullOrWhiteSpace(drawingRelId))
            return null;

        var worksheetRelsEntry = archive.GetEntry(GetWorksheetRelsPath(worksheetPath));
        if (worksheetRelsEntry is null)
            return null;

        var worksheetRelsXml = LoadXml(worksheetRelsEntry);
        var drawingTarget = worksheetRelsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, drawingRelId, StringComparison.Ordinal))?
            .Attribute("Target")?
            .Value;
        if (string.IsNullOrWhiteSpace(drawingTarget))
            return null;

        var drawingEntry = archive.GetEntry(ResolveRelationshipTarget(worksheetPath, drawingTarget));
        return drawingEntry is null ? null : LoadXml(drawingEntry);
    }

    private static double ReadDrawingRotation(XElement? transform)
    {
        if (!double.TryParse(transform?.Attribute("rot")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rotation))
            return 0;
        var degrees = rotation / 60000d;
        degrees %= 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static CellColor? ReadDrawingSolidFillColor(XElement? solidFill, XNamespace drawingNs)
    {
        var value = solidFill?
            .Element(drawingNs + "srgbClr")?
            .Attribute("val")?
            .Value;
        if (string.IsNullOrWhiteSpace(value) || value.Length != 6)
            return null;
        return byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
               byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
               byte.TryParse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            ? new CellColor(r, g, b)
            : null;
    }

    private static (CellColor? StartColor, CellColor? EndColor) ReadDrawingGradientFillColors(
        XElement? gradientFill,
        XNamespace drawingNs)
    {
        var colors = gradientFill?
            .Element(drawingNs + "gsLst")?
            .Elements(drawingNs + "gs")
            .Select(gs => new
            {
                Position = int.TryParse(gs.Attribute("pos")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pos)
                    ? pos
                    : 0,
                Color = ReadDrawingSolidFillColor(gs, drawingNs)
            })
            .Where(item => item.Color is not null)
            .OrderBy(item => item.Position)
            .Select(item => item.Color)
            .ToList();

        return colors is { Count: >= 2 }
            ? (colors[0], colors[^1])
            : (colors?.FirstOrDefault(), null);
    }

    private static DrawingShapeKind? ToDrawingShapeKind(string? preset) =>
        preset switch
        {
            "rect" or "roundRect" => DrawingShapeKind.Rectangle,
            "ellipse" => DrawingShapeKind.Ellipse,
            "line" => DrawingShapeKind.Line,
            _ => null
        };

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

    private static void ApplyPictureAnchor(PictureModel picture, XlsxDrawingAnchor? anchor, Sheet sheet)
    {
        if (anchor is null)
            return;

        var width = anchor.Width ?? (
            SumColumnPixels(sheet, anchor.FromColumnZeroBased + 1, anchor.ToColumnZeroBased!.Value - anchor.FromColumnZeroBased)
            + anchor.ToColumnOffset!.Value
            - anchor.FromColumnOffset);
        var height = anchor.Height ?? (
            SumRowPixels(sheet, anchor.FromRowZeroBased + 1, anchor.ToRowZeroBased!.Value - anchor.FromRowZeroBased)
            + anchor.ToRowOffset!.Value
            - anchor.FromRowOffset);
        if (width > 0)
            picture.Width = width;
        if (height > 0)
            picture.Height = height;
    }

    private static void ApplyTextBoxAnchor(TextBoxModel textBox, XlsxDrawingAnchor? anchor, Sheet sheet)
    {
        if (anchor is null)
            return;

        var (width, height) = GetDrawingAnchorSize(anchor, sheet);
        if (width > 0)
            textBox.Width = width;
        if (height > 0)
            textBox.Height = height;
    }

    private static void ApplyDrawingShapeAnchor(DrawingShapeModel shape, XlsxDrawingAnchor? anchor, Sheet sheet)
    {
        if (anchor is null)
            return;

        var (width, height) = GetDrawingAnchorSize(anchor, sheet);
        if (width > 0)
            shape.Width = width;
        if (height > 0)
            shape.Height = height;
    }

    private static (double Width, double Height) GetDrawingAnchorSize(XlsxDrawingAnchor anchor, Sheet sheet)
    {
        var width = anchor.Width ?? (
            SumColumnPixels(sheet, anchor.FromColumnZeroBased + 1, anchor.ToColumnZeroBased!.Value - anchor.FromColumnZeroBased)
            + anchor.ToColumnOffset!.Value
            - anchor.FromColumnOffset);
        var height = anchor.Height ?? (
            SumRowPixels(sheet, anchor.FromRowZeroBased + 1, anchor.ToRowZeroBased!.Value - anchor.FromRowZeroBased)
            + anchor.ToRowOffset!.Value
            - anchor.FromRowOffset);
        return (width, height);
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
            xlSheet.Visibility = sheet.IsVeryHidden
                ? XLWorksheetVisibility.VeryHidden
                : sheet.IsHidden ? XLWorksheetVisibility.Hidden : XLWorksheetVisibility.Visible;
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

        packageStream.Position = 0;
        SaveWorkbookCalculationProperties(packageStream, workbook);

        if (workbook.Sheets.Any(sheet => sheet.AllowEditRanges.Count > 0))
        {
            packageStream.Position = 0;
            SaveAllowEditRanges(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.ConditionalFormats.Any(IsAdvancedConditionalFormat)))
        {
            packageStream.Position = 0;
            SaveAdvancedConditionalFormats(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.Sparklines.Count > 0))
        {
            packageStream.Position = 0;
            SaveWorksheetSparklines(packageStream, workbook);
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
                sheet.ViewTopRow.HasValue ||
                sheet.ViewLeftCol.HasValue ||
                sheet.ActiveRow.HasValue ||
                sheet.ActiveCol.HasValue ||
                (sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
                 (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue))))
        {
            packageStream.Position = 0;
            SaveSplitPaneSheetViews(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => !string.IsNullOrWhiteSpace(sheet.CodeName)))
        {
            packageStream.Position = 0;
            SaveWorksheetCodeNames(packageStream, workbook);
        }

        packageStream.Position = 0;
        SaveWorkbookTheme(packageStream, workbook.Theme);

        if (workbook.Sheets.Any(sheet => sheet.Charts.Any(IsSupportedXlsxChart)))
        {
            packageStream.Position = 0;
            SaveWorksheetCharts(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet =>
                sheet.Pictures.Any(IsSupportedXlsxPicture) ||
                sheet.TextBoxes.Any(IsSupportedXlsxTextBox) ||
                sheet.DrawingShapes.Any(IsSupportedXlsxDrawingShape)))
        {
            packageStream.Position = 0;
            SaveWorksheetDrawingObjects(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.StructuredTables.Count > 0))
        {
            packageStream.Position = 0;
            SaveStructuredTables(packageStream, workbook);
        }

        if (workbook.PivotTableStyles.Count > 0)
        {
            packageStream.Position = 0;
            SavePivotTableStyles(packageStream, workbook);
        }

        if (!SourcePackages.TryGetValue(workbook, out _) &&
            workbook.PivotCaches.Count > 0 &&
            workbook.Sheets.Any(sheet => sheet.PivotTables.Count > 0))
        {
            packageStream.Position = 0;
            SavePivotTables(packageStream, workbook);
        }

        if (!SourcePackages.TryGetValue(workbook, out _) &&
            (workbook.Slicers.Count > 0 || workbook.Timelines.Count > 0))
        {
            packageStream.Position = 0;
            SaveSlicerTimelines(packageStream, workbook);
        }

        packageStream.Position = 0;
        PreserveSourcePackageParts(workbook, packageStream);

        packageStream.Position = 0;
        packageStream.CopyTo(stream);
    }

    private static void PreserveSourcePackageParts(Workbook workbook, MemoryStream generatedPackage)
    {
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return;

        using var sourceStream = new MemoryStream(sourcePackage.Bytes, writable: false);
        using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);
        using var generatedArchive = new ZipArchive(generatedPackage, ZipArchiveMode.Update, leaveOpen: true);
        var generatedEntriesBeforeMerge = generatedArchive.Entries
            .Select(entry => NormalizeZipPath(entry.FullName.Replace('\\', '/')))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceEntry in sourceArchive.Entries)
        {
            if (IsPackageMetadataEntry(sourceEntry.FullName))
                continue;
            if (generatedArchive.GetEntry(sourceEntry.FullName) is not null)
                continue;

            CopyZipEntry(sourceEntry, generatedArchive);
        }

        MergeContentTypes(sourceArchive, generatedArchive);
        MergeRelationshipParts(sourceArchive, generatedArchive, generatedEntriesBeforeMerge);
        PreserveDocumentProperties(sourceArchive, generatedArchive);
        PreserveWorkbookMetadataBlocks(sourceArchive, generatedArchive);
        PreserveStylesheetMetadata(sourceArchive, generatedArchive);
        PreservePivotXmlReferences(sourceArchive, generatedArchive);
        PreserveStructuredTableXmlReferences(sourceArchive, generatedArchive);
        PreserveExternalLinkReferences(sourceArchive, generatedArchive);
        MergeWorksheetDrawingParts(sourceArchive, generatedArchive);
        PreserveWorksheetDrawingReferences(sourceArchive, generatedArchive);
        PreserveWorksheetPrinterSettingsReferences(sourceArchive, generatedArchive);
        PreserveWorksheetMetadataBlocks(sourceArchive, generatedArchive);
        PreserveUnsupportedConditionalFormatting(sourceArchive, generatedArchive);
    }

    private static void PreserveDocumentProperties(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        PreserveDocumentPropertyElements(
            sourceArchive,
            targetArchive,
            "docProps/core.xml",
            [
                XName.Get("subject", "http://purl.org/dc/elements/1.1/"),
                XName.Get("keywords", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                XName.Get("category", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                XName.Get("contentStatus", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                XName.Get("language", "http://purl.org/dc/elements/1.1/"),
                XName.Get("version", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties")
            ]);

        PreserveDocumentPropertyElements(
            sourceArchive,
            targetArchive,
            "docProps/app.xml",
            [
                XName.Get("Application", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                XName.Get("Company", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                XName.Get("Manager", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                XName.Get("PresentationFormat", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                XName.Get("Template", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties")
            ]);
    }

    private static void PreserveDocumentPropertyElements(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string partName,
        IReadOnlyCollection<XName> stableElementNames)
    {
        var sourceEntry = sourceArchive.GetEntry(partName);
        var targetEntry = targetArchive.GetEntry(partName);
        if (sourceEntry is null)
            return;

        if (targetEntry is null)
        {
            CopyZipEntry(sourceEntry, targetArchive);
            return;
        }

        var sourceXml = LoadXml(sourceEntry);
        var targetXml = LoadXml(targetEntry);
        var sourceRoot = sourceXml.Root;
        var targetRoot = targetXml.Root;
        if (sourceRoot is null || targetRoot is null)
            return;

        var changed = false;
        foreach (var stableElementName in stableElementNames)
        {
            var sourceElement = sourceRoot.Element(stableElementName);
            if (sourceElement is null)
                continue;

            var targetElement = targetRoot.Element(stableElementName);
            if (targetElement is null)
            {
                targetRoot.Add(new XElement(sourceElement));
                changed = true;
                continue;
            }

            if (XNode.DeepEquals(targetElement, sourceElement))
                continue;

            targetElement.ReplaceWith(new XElement(sourceElement));
            changed = true;
        }

        if (changed)
            ReplacePackageXml(targetArchive, partName, targetXml);
    }

    private static void PreserveStylesheetMetadata(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var sourceStylesEntry = sourceArchive.GetEntry("xl/styles.xml");
        var targetStylesEntry = targetArchive.GetEntry("xl/styles.xml");
        if (sourceStylesEntry is null || targetStylesEntry is null)
            return;

        var sourceStylesXml = LoadXml(sourceStylesEntry);
        var targetStylesXml = LoadXml(targetStylesEntry);
        var targetRoot = targetStylesXml.Root;
        if (targetRoot is null)
            return;

        var changed = false;
        if (MergeStylesheetColors(sourceStylesXml.Root?.Element(workbookNs + "colors"), targetRoot, workbookNs))
            changed = true;
        if (MergeStylesheetTableStyles(sourceStylesXml.Root?.Element(workbookNs + "tableStyles"), targetRoot, workbookNs))
            changed = true;
        if (MergeExtensionList(sourceStylesXml.Root?.Element(workbookNs + "extLst"), targetRoot, workbookNs))
            changed = true;

        if (changed)
            ReplacePackageXml(targetArchive, "xl/styles.xml", targetStylesXml);
    }

    private static bool MergeStylesheetColors(XElement? sourceColors, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceColors is null)
            return false;

        var targetColors = targetRoot.Element(workbookNs + "colors");
        if (targetColors is null)
        {
            targetRoot.Add(new XElement(sourceColors));
            return true;
        }

        return MergeElementNativeAttributesAndChildren(sourceColors, targetColors);
    }

    private static bool MergeStylesheetTableStyles(XElement? sourceTableStyles, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceTableStyles is null)
            return false;

        var targetTableStyles = targetRoot.Element(workbookNs + "tableStyles");
        if (targetTableStyles is null)
        {
            targetRoot.Add(new XElement(sourceTableStyles));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceTableStyles.Attributes())
        {
            if (targetTableStyles.Attribute(attribute.Name)?.Value == attribute.Value)
                continue;

            targetTableStyles.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var targetStylesByName = targetTableStyles
            .Elements(workbookNs + "tableStyle")
            .Select(element => (Name: element.Attribute("name")?.Value, Element: element))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Name))
            .ToDictionary(pair => pair.Name!, pair => pair.Element, StringComparer.OrdinalIgnoreCase);
        foreach (var sourceStyle in sourceTableStyles.Elements(workbookNs + "tableStyle"))
        {
            var name = sourceStyle.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name) || !targetStylesByName.TryGetValue(name, out var targetStyle))
            {
                targetTableStyles.Add(new XElement(sourceStyle));
                if (!string.IsNullOrWhiteSpace(name))
                    targetStylesByName[name] = targetTableStyles.Elements(workbookNs + "tableStyle").Last();
                changed = true;
                continue;
            }

            if (MergeElementNativeAttributesAndChildren(sourceStyle, targetStyle))
                changed = true;
        }

        targetTableStyles.SetAttributeValue(
            "count",
            targetTableStyles.Elements(workbookNs + "tableStyle").Count().ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    private static void SavePivotTableStyles(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        if (stylesEntry is null)
            return;

        var stylesXml = LoadXml(stylesEntry);
        var targetRoot = stylesXml.Root;
        if (targetRoot is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var tableStyles = targetRoot.Element(workbookNs + "tableStyles");
        if (tableStyles is null)
        {
            tableStyles = new XElement(workbookNs + "tableStyles");
            targetRoot.Add(tableStyles);
        }

        var existingStylesByName = tableStyles
            .Elements(workbookNs + "tableStyle")
            .Select(element => (Name: element.Attribute("name")?.Value, Element: element))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Name))
            .ToDictionary(pair => pair.Name!, pair => pair.Element, StringComparer.OrdinalIgnoreCase);

        foreach (var style in workbook.PivotTableStyles.Where(style => !string.IsNullOrWhiteSpace(style.Name)))
        {
            var styleXml = ToPivotTableStyleXml(style, workbookNs);
            if (existingStylesByName.TryGetValue(style.Name, out var existingStyle))
                existingStyle.ReplaceWith(styleXml);
            else
                tableStyles.Add(styleXml);
        }

        tableStyles.SetAttributeValue(
            "count",
            tableStyles.Elements(workbookNs + "tableStyle").Count().ToString(CultureInfo.InvariantCulture));
        ReplacePackageXml(archive, "xl/styles.xml", stylesXml);
    }

    private static XElement ToPivotTableStyleXml(PivotTableStyleModel style, XNamespace workbookNs) =>
        new(
            workbookNs + "tableStyle",
            new XAttribute("name", style.Name),
            new XAttribute("pivot", style.AppliesToPivotTables ? "1" : "0"),
            new XAttribute("table", style.AppliesToTables ? "1" : "0"),
            new XAttribute("count", style.Elements.Count.ToString(CultureInfo.InvariantCulture)),
            style.Elements
                .Where(element => !string.IsNullOrWhiteSpace(element.Type))
                .Select(element => new XElement(
                    workbookNs + "tableStyleElement",
                    new XAttribute("type", element.Type),
                    element.DifferentialFormatId is { } dxfId ? new XAttribute("dxfId", dxfId.ToString(CultureInfo.InvariantCulture)) : null,
                    element.Size is { } size ? new XAttribute("size", size.ToString(CultureInfo.InvariantCulture)) : null)));

    private static void SaveSlicerTimelines(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace slicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";
        XNamespace freexcelNs = "https://freexcel.local/xlsx/slicerTimelineState";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var slicerIndex = 1;
        foreach (var slicer in workbook.Slicers)
        {
            var slicerPath = string.IsNullOrWhiteSpace(slicer.PackagePart)
                ? $"xl/slicers/slicer{slicerIndex}.xml"
                : slicer.PackagePart.TrimStart('/').Replace('\\', '/');
            var cachePath = $"xl/slicerCaches/slicerCache{slicerIndex}.xml";

            ReplacePackageXml(archive, slicerPath, new XDocument(
                new XElement(slicerNs + "slicer",
                    new XAttribute("name", slicer.Name),
                    new XAttribute("cache", string.IsNullOrWhiteSpace(slicer.CacheName) ? $"Slicer_{slicerIndex}" : slicer.CacheName),
                    OptionalAttribute("caption", slicer.Caption),
                    OptionalAttribute("style", slicer.StyleName))));
            ReplacePackageXml(archive, cachePath, new XDocument(
                new XElement(slicerNs + "slicerCacheDefinition",
                    new XAttribute("name", string.IsNullOrWhiteSpace(slicer.CacheName) ? $"Slicer_{slicerIndex}" : slicer.CacheName),
                    OptionalAttribute("sourceName", slicer.SourceFieldName),
                    new XElement(slicerNs + "pivotTables",
                        new XElement(slicerNs + "pivotTable", OptionalAttribute("name", slicer.SourcePivotTableName))),
                    new XElement(freexcelNs + "selectedItems",
                        slicer.SelectedItems.Select(item =>
                            new XElement(freexcelNs + "selectedItem", new XAttribute("value", item)))))));
            ReplacePackageXml(archive, GetWorksheetRelsPath(slicerPath), new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdSlicerCache"),
                        new XAttribute("Type", "http://schemas.microsoft.com/office/2007/relationships/slicerCache"),
                        new XAttribute("Target", GetRelativeTarget(slicerPath, cachePath))))));
            EnsureSpecificContentType(archive, $"/{slicerPath}", "application/vnd.ms-excel.slicer+xml");
            EnsureSpecificContentType(archive, $"/{cachePath}", "application/vnd.ms-excel.slicerCache+xml");
            slicerIndex++;
        }

        var timelineIndex = 1;
        foreach (var timeline in workbook.Timelines)
        {
            var timelinePath = string.IsNullOrWhiteSpace(timeline.PackagePart)
                ? $"xl/timelines/timeline{timelineIndex}.xml"
                : timeline.PackagePart.TrimStart('/').Replace('\\', '/');
            var cachePath = $"xl/timelineCaches/timelineCache{timelineIndex}.xml";

            ReplacePackageXml(archive, timelinePath, new XDocument(
                new XElement(slicerNs + "timeline",
                    new XAttribute("name", timeline.Name),
                    new XAttribute("cache", string.IsNullOrWhiteSpace(timeline.CacheName) ? $"Timeline_{timelineIndex}" : timeline.CacheName),
                    OptionalAttribute("caption", timeline.Caption),
                    OptionalAttribute("style", timeline.StyleName))));
            ReplacePackageXml(archive, cachePath, new XDocument(
                new XElement(slicerNs + "timelineCacheDefinition",
                    new XAttribute("name", string.IsNullOrWhiteSpace(timeline.CacheName) ? $"Timeline_{timelineIndex}" : timeline.CacheName),
                    OptionalAttribute("sourceName", timeline.SourceFieldName),
                    OptionalAttribute("startDate", timeline.StartDate),
                    OptionalAttribute("endDate", timeline.EndDate),
                    OptionalAttribute("selectedStartDate", timeline.SelectedStartDate),
                    OptionalAttribute("selectedEndDate", timeline.SelectedEndDate),
                    new XElement(slicerNs + "pivotTables",
                        new XElement(slicerNs + "pivotTable", OptionalAttribute("name", timeline.SourcePivotTableName))))));
            ReplacePackageXml(archive, GetWorksheetRelsPath(timelinePath), new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdTimelineCache"),
                        new XAttribute("Type", "http://schemas.microsoft.com/office/2011/relationships/timelineCache"),
                        new XAttribute("Target", GetRelativeTarget(timelinePath, cachePath))))));
            EnsureSpecificContentType(archive, $"/{timelinePath}", "application/vnd.ms-excel.timeline+xml");
            EnsureSpecificContentType(archive, $"/{cachePath}", "application/vnd.ms-excel.timelineCache+xml");
            timelineIndex++;
        }

        static XAttribute? OptionalAttribute(string name, string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value);
    }

    private static bool IsPackageMetadataEntry(string entryName) =>
        string.Equals(entryName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase) ||
        entryName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    private static void CopyZipEntry(ZipArchiveEntry sourceEntry, ZipArchive targetArchive)
    {
        var targetEntry = targetArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
        targetEntry.LastWriteTime = sourceEntry.LastWriteTime;
        using var sourceStream = sourceEntry.Open();
        using var targetStream = targetEntry.Open();
        sourceStream.CopyTo(targetStream);
    }

    private static void MergeContentTypes(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        var sourceEntry = sourceArchive.GetEntry("[Content_Types].xml");
        var targetEntry = targetArchive.GetEntry("[Content_Types].xml");
        if (sourceEntry is null || targetEntry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var sourceXml = LoadXml(sourceEntry);
        var targetXml = LoadXml(targetEntry);
        var targetRoot = targetXml.Root;
        var sourceRoot = sourceXml.Root;
        if (targetRoot is null || sourceRoot is null)
            return;

        var existingDefaults = targetRoot
            .Elements(contentTypeNs + "Default")
            .Select(element => element.Attribute("Extension")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceDefault in sourceRoot.Elements(contentTypeNs + "Default"))
        {
            var extension = sourceDefault.Attribute("Extension")?.Value;
            if (!string.IsNullOrWhiteSpace(extension) && existingDefaults.Add(extension))
                targetRoot.Add(new XElement(sourceDefault));
        }

        var existingOverrides = targetRoot
            .Elements(contentTypeNs + "Override")
            .Select(element => element.Attribute("PartName")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceOverride in sourceRoot.Elements(contentTypeNs + "Override"))
        {
            var partName = sourceOverride.Attribute("PartName")?.Value;
            if (!string.IsNullOrWhiteSpace(partName) && existingOverrides.Add(partName))
                targetRoot.Add(new XElement(sourceOverride));
        }

        ReplacePackageXml(targetArchive, "[Content_Types].xml", targetXml);
    }

    private static void MergeRelationshipParts(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        IReadOnlySet<string> generatedEntriesBeforeMerge)
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        foreach (var sourceEntry in sourceArchive.Entries.Where(entry =>
                     entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var targetEntry = targetArchive.GetEntry(sourceEntry.FullName);
            if (targetEntry is null)
            {
                CopyZipEntry(sourceEntry, targetArchive);
                continue;
            }

            var sourceXml = LoadXml(sourceEntry);
            var targetXml = LoadXml(targetEntry);
            var sourceRoot = sourceXml.Root;
            var targetRoot = targetXml.Root;
            if (sourceRoot is null || targetRoot is null)
                continue;

            var existingRelationships = targetRoot
                .Elements(relationshipNs + "Relationship")
                .Select(RelationshipSignature)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingIds = targetRoot
                .Elements(relationshipNs + "Relationship")
                .Select(element => element.Attribute("Id")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var sourceRelationship in sourceRoot.Elements(relationshipNs + "Relationship"))
            {
                if (!ShouldPreserveRelationship(sourceEntry.FullName, sourceRelationship, targetArchive, generatedEntriesBeforeMerge))
                    continue;

                if (!existingRelationships.Add(RelationshipSignature(sourceRelationship)))
                    continue;

                var copy = new XElement(sourceRelationship);
                var id = copy.Attribute("Id")?.Value;
                if (!string.IsNullOrWhiteSpace(id) && existingIds.Contains(id))
                    copy.SetAttributeValue("Id", NextRelationshipId(targetXml, relationshipNs));
                targetRoot.Add(copy);
                var copiedId = copy.Attribute("Id")?.Value;
                if (!string.IsNullOrWhiteSpace(copiedId))
                    existingIds.Add(copiedId);
            }

            ReplacePackageXml(targetArchive, sourceEntry.FullName, targetXml);
        }
    }

    private static bool ShouldPreserveRelationship(
        string relationshipPartPath,
        XElement relationship,
        ZipArchive targetArchive,
        IReadOnlySet<string> generatedEntriesBeforeMerge)
    {
        var targetMode = relationship.Attribute("TargetMode")?.Value;
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
            return false;

        var relationshipType = relationship.Attribute("Type")?.Value;
        if (string.Equals(
                relationshipType,
                "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        var targetPart = ResolveRelationshipTarget(RelationshipPartToSourcePart(relationshipPartPath), target);
        return !string.IsNullOrWhiteSpace(targetPart) &&
               !generatedEntriesBeforeMerge.Contains(targetPart) &&
               targetArchive.GetEntry(targetPart) is not null;
    }

    private static string RelationshipSignature(XElement relationship) =>
        string.Join("|",
            relationship.Attribute("Type")?.Value ?? "",
            relationship.Attribute("Target")?.Value ?? "",
            relationship.Attribute("TargetMode")?.Value ?? "");

    private static string RelationshipPartToSourcePart(string relationshipPartPath)
    {
        var normalized = NormalizeZipPath(relationshipPartPath.Replace('\\', '/'));
        if (string.Equals(normalized, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return "";

        const string relsSegment = "/_rels/";
        var relsIndex = normalized.IndexOf(relsSegment, StringComparison.OrdinalIgnoreCase);
        if (relsIndex < 0 || !normalized.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return normalized;

        var directory = normalized[..relsIndex];
        var fileName = normalized[(relsIndex + relsSegment.Length)..^".rels".Length];
        return string.IsNullOrEmpty(directory) ? fileName : $"{directory}/{fileName}";
    }

    private static void PreservePivotXmlReferences(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        PreserveWorkbookPivotCaches(sourceArchive, targetArchive, workbookNs);
        PreserveWorksheetPivotTableDefinitions(sourceArchive, targetArchive, workbookNs, relNs, packageRelNs);
    }

    private static void PreserveWorkbookPivotCaches(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XNamespace workbookNs)
    {
        var sourceEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var targetEntry = targetArchive.GetEntry("xl/workbook.xml");
        if (sourceEntry is null || targetEntry is null)
            return;

        var sourceXml = LoadXml(sourceEntry);
        var sourcePivotCaches = sourceXml.Root?.Element(workbookNs + "pivotCaches");
        if (sourcePivotCaches is null)
            return;

        var targetXml = LoadXml(targetEntry);
        var targetRoot = targetXml.Root;
        if (targetRoot is null || targetRoot.Element(workbookNs + "pivotCaches") is not null)
            return;

        var sheetsElement = targetRoot.Element(workbookNs + "sheets");
        if (sheetsElement is not null)
            sheetsElement.AddBeforeSelf(new XElement(sourcePivotCaches));
        else
            targetRoot.Add(new XElement(sourcePivotCaches));

        ReplacePackageXml(targetArchive, "xl/workbook.xml", targetXml);
    }

    private static void PreserveWorksheetPivotTableDefinitions(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var sourceWorkbookRelsEntry = sourceArchive.GetEntry("xl/_rels/workbook.xml.rels");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (sourceWorkbookEntry is null || sourceWorkbookRelsEntry is null ||
            targetWorkbookEntry is null || targetWorkbookRelsEntry is null)
        {
            return;
        }

        var sourceWorkbookXml = LoadXml(sourceWorkbookEntry);
        var sourceWorkbookRels = LoadRelationshipTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookXml = LoadXml(targetWorkbookEntry);
        var targetWorkbookRels = LoadRelationshipTargets(
            targetArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);

        var sourceSheets = GetWorkbookSheetPaths(sourceWorkbookXml, sourceWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var targetSheets = GetWorkbookSheetPaths(targetWorkbookXml, targetWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetEntry is null || targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = LoadXml(sourceWorksheetEntry);
            var sourcePivotDefinitions = sourceWorksheetXml.Root?
                .Elements(workbookNs + "pivotTableDefinition")
                .ToList() ?? [];
            if (sourcePivotDefinitions.Count == 0)
                continue;

            var targetWorksheetXml = LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null || targetRoot.Elements(workbookNs + "pivotTableDefinition").Any())
                continue;

            foreach (var pivotDefinition in sourcePivotDefinitions)
                targetRoot.Add(new XElement(pivotDefinition));

            ReplacePackageXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    private static void PreserveStructuredTableXmlReferences(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var sourceWorkbookRelsEntry = sourceArchive.GetEntry("xl/_rels/workbook.xml.rels");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (sourceWorkbookEntry is null || sourceWorkbookRelsEntry is null ||
            targetWorkbookEntry is null || targetWorkbookRelsEntry is null)
        {
            return;
        }

        var sourceWorkbookXml = LoadXml(sourceWorkbookEntry);
        var sourceWorkbookRels = LoadRelationshipTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookXml = LoadXml(targetWorkbookEntry);
        var targetWorkbookRels = LoadRelationshipTargets(
            targetArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);

        var sourceSheets = GetWorkbookSheetPaths(sourceWorkbookXml, sourceWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var targetSheets = GetWorkbookSheetPaths(targetWorkbookXml, targetWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetEntry is null || targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = LoadXml(sourceWorksheetEntry);
            var sourceTableParts = sourceWorksheetXml.Root?
                .Element(workbookNs + "tableParts")?
                .Elements(workbookNs + "tablePart")
                .ToList() ?? [];
            if (sourceTableParts.Count == 0)
                continue;

            var sourceWorksheetRels = LoadRelationshipTargets(
                sourceArchive,
                GetWorksheetRelsPath(sourceWorksheetPath),
                sourceWorksheetPath,
                packageRelNs);
            var targetWorksheetRelsPath = GetWorksheetRelsPath(targetWorksheetPath);
            var targetWorksheetRelsEntry = targetArchive.GetEntry(targetWorksheetRelsPath);
            var targetWorksheetRelsXml = targetWorksheetRelsEntry is not null
                ? LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));

            var preservedTableParts = new List<XElement>();
            foreach (var sourceTablePart in sourceTableParts)
            {
                var sourceRelId = sourceTablePart.Attribute(relNs + "id")?.Value;
                if (string.IsNullOrWhiteSpace(sourceRelId) ||
                    !sourceWorksheetRels.TryGetValue(sourceRelId, out var tablePath))
                {
                    continue;
                }

                var targetRelId = EnsureRelationshipForPackagePart(
                    targetWorksheetRelsXml,
                    packageRelNs,
                    targetWorksheetPath,
                    tablePath,
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table");
                preservedTableParts.Add(new XElement(workbookNs + "tablePart", new XAttribute(relNs + "id", targetRelId)));
            }

            if (preservedTableParts.Count == 0)
                continue;

            ReplacePackageXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            var targetWorksheetXml = LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            targetRoot.Elements(workbookNs + "tableParts").Remove();
            targetRoot.Add(new XElement(
                workbookNs + "tableParts",
                new XAttribute("count", preservedTableParts.Count.ToString(CultureInfo.InvariantCulture)),
                preservedTableParts));
            ReplacePackageXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    private static string EnsureRelationshipForPackagePart(
        XDocument relsXml,
        XNamespace packageRelNs,
        string sourcePart,
        string targetPart,
        string relationshipType)
    {
        var root = relsXml.Root;
        if (root is null)
        {
            root = new XElement(packageRelNs + "Relationships");
            relsXml.Add(root);
        }

        foreach (var relationship in root.Elements(packageRelNs + "Relationship"))
        {
            var type = relationship.Attribute("Type")?.Value;
            var target = relationship.Attribute("Target")?.Value;
            if (!string.Equals(type, relationshipType, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var resolvedTarget = ResolveRelationshipTarget(sourcePart, target);
            if (string.Equals(resolvedTarget, targetPart, StringComparison.OrdinalIgnoreCase))
                return relationship.Attribute("Id")?.Value ?? "";
        }

        var id = NextRelationshipId(relsXml, packageRelNs);
        root.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", relationshipType),
            new XAttribute("Target", GetRelativeTarget(sourcePart, targetPart))));
        return id;
    }

    private static void PreserveExternalLinkReferences(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (sourceWorkbookEntry is null || targetWorkbookEntry is null || targetWorkbookRelsEntry is null)
            return;

        var sourceWorkbookXml = LoadXml(sourceWorkbookEntry);
        var sourceExternalReferences = sourceWorkbookXml.Root?
            .Element(workbookNs + "externalReferences")?
            .Elements(workbookNs + "externalReference")
            .ToList()
            ?? [];
        if (sourceExternalReferences.Count == 0)
            return;

        var sourceWorkbookRels = LoadRelationshipTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookXml = LoadXml(targetWorkbookEntry);
        var targetWorkbookRelsXml = LoadXml(targetWorkbookRelsEntry);
        var targetRoot = targetWorkbookXml.Root;
        if (targetRoot is null)
            return;

        var targetExternalReferences = targetRoot.Element(workbookNs + "externalReferences");
        if (targetExternalReferences is null)
        {
            targetExternalReferences = new XElement(workbookNs + "externalReferences");
            targetRoot.Add(targetExternalReferences);
        }

        foreach (var sourceReference in sourceExternalReferences)
        {
            var sourceRelId = sourceReference.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceRelId) ||
                !sourceWorkbookRels.TryGetValue(sourceRelId, out var externalLinkPath))
            {
                continue;
            }

            var targetRelId = EnsureRelationshipForPackagePart(
                targetWorkbookRelsXml,
                packageRelNs,
                "xl/workbook.xml",
                externalLinkPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink");
            if (!targetExternalReferences
                    .Elements(workbookNs + "externalReference")
                    .Any(reference => string.Equals(reference.Attribute(relNs + "id")?.Value, targetRelId, StringComparison.OrdinalIgnoreCase)))
            {
                targetExternalReferences.Add(new XElement(
                    workbookNs + "externalReference",
                    new XAttribute(relNs + "id", targetRelId)));
            }
        }

        if (!targetExternalReferences.HasElements)
            targetExternalReferences.Remove();

        ReplacePackageXml(targetArchive, "xl/workbook.xml", targetWorkbookXml);
        ReplacePackageXml(targetArchive, "xl/_rels/workbook.xml.rels", targetWorkbookRelsXml);
    }

    private static void PreserveWorksheetDrawingReferences(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var sourceWorkbookRelsEntry = sourceArchive.GetEntry("xl/_rels/workbook.xml.rels");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (sourceWorkbookEntry is null || sourceWorkbookRelsEntry is null ||
            targetWorkbookEntry is null || targetWorkbookRelsEntry is null)
        {
            return;
        }

        var sourceWorkbookXml = LoadXml(sourceWorkbookEntry);
        var sourceWorkbookRels = LoadRelationshipTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookXml = LoadXml(targetWorkbookEntry);
        var targetWorkbookRels = LoadRelationshipTargets(
            targetArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);

        var sourceSheets = GetWorkbookSheetPaths(sourceWorkbookXml, sourceWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var targetSheets = GetWorkbookSheetPaths(targetWorkbookXml, targetWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetEntry is null || targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = LoadXml(sourceWorksheetEntry);
            var sourceDrawing = sourceWorksheetXml.Root?.Element(workbookNs + "drawing");
            var sourceRelId = sourceDrawing?.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceRelId))
                continue;

            var sourceWorksheetRels = LoadRelationshipTargets(
                sourceArchive,
                GetWorksheetRelsPath(sourceWorksheetPath),
                sourceWorksheetPath,
                packageRelNs);
            if (!sourceWorksheetRels.TryGetValue(sourceRelId, out var drawingPath))
                continue;

            var targetWorksheetRelsPath = GetWorksheetRelsPath(targetWorksheetPath);
            var targetWorksheetRelsEntry = targetArchive.GetEntry(targetWorksheetRelsPath);
            var targetWorksheetRelsXml = targetWorksheetRelsEntry is not null
                ? LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            var targetRelId = EnsureRelationshipForPackagePart(
                targetWorksheetRelsXml,
                packageRelNs,
                targetWorksheetPath,
                drawingPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing");
            ReplacePackageXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            var targetWorksheetXml = LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null || targetRoot.Element(workbookNs + "drawing") is not null)
                continue;

            targetRoot.Add(new XElement(workbookNs + "drawing", new XAttribute(relNs + "id", targetRelId)));
            ReplacePackageXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    private static void PreserveWorksheetPrinterSettingsReferences(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var sourceWorkbookRelsEntry = sourceArchive.GetEntry("xl/_rels/workbook.xml.rels");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (sourceWorkbookEntry is null || sourceWorkbookRelsEntry is null ||
            targetWorkbookEntry is null || targetWorkbookRelsEntry is null)
        {
            return;
        }

        var sourceWorkbookXml = LoadXml(sourceWorkbookEntry);
        var targetWorkbookXml = LoadXml(targetWorkbookEntry);
        var sourceWorkbookRels = LoadRelationshipTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookRels = LoadRelationshipTargets(
            targetArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);

        var sourceSheets = GetWorkbookSheetPaths(sourceWorkbookXml, sourceWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var targetSheets = GetWorkbookSheetPaths(targetWorkbookXml, targetWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetEntry is null || targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = LoadXml(sourceWorksheetEntry);
            var sourcePageSetup = sourceWorksheetXml.Root?.Element(workbookNs + "pageSetup");
            var sourceRelId = sourcePageSetup?.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceRelId))
                continue;

            var sourceWorksheetRels = LoadRelationshipTargets(
                sourceArchive,
                GetWorksheetRelsPath(sourceWorksheetPath),
                sourceWorksheetPath,
                packageRelNs);
            if (!sourceWorksheetRels.TryGetValue(sourceRelId, out var printerSettingsPath) ||
                !printerSettingsPath.StartsWith("xl/printerSettings/", StringComparison.OrdinalIgnoreCase) ||
                targetArchive.GetEntry(printerSettingsPath) is null)
            {
                continue;
            }

            var targetWorksheetRelsPath = GetWorksheetRelsPath(targetWorksheetPath);
            var targetWorksheetRelsXml = targetArchive.GetEntry(targetWorksheetRelsPath) is { } targetWorksheetRelsEntry
                ? LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            var targetRelId = EnsureRelationshipForPackagePart(
                targetWorksheetRelsXml,
                packageRelNs,
                targetWorksheetPath,
                printerSettingsPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings");
            ReplacePackageXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            var targetWorksheetXml = LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            var targetPageSetup = targetRoot.Element(workbookNs + "pageSetup");
            if (targetPageSetup is null)
            {
                targetPageSetup = new XElement(workbookNs + "pageSetup");
                targetRoot.Add(targetPageSetup);
            }

            targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
            targetPageSetup.SetAttributeValue(relNs + "id", targetRelId);
            ReplacePackageXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    private static void PreserveWorkbookMetadataBlocks(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        if (sourceWorkbookEntry is null || targetWorkbookEntry is null)
            return;

        var sourceWorkbookXml = LoadXml(sourceWorkbookEntry);
        var sourceExtensionList = sourceWorkbookXml.Root?.Element(workbookNs + "extLst");
        var sourceFileVersion = sourceWorkbookXml.Root?.Element(workbookNs + "fileVersion");
        var sourceFileSharing = sourceWorkbookXml.Root?.Element(workbookNs + "fileSharing");
        var sourceFileRecoveryProperties = sourceWorkbookXml.Root?.Element(workbookNs + "fileRecoveryPr");
        var sourceSmartTagProperties = sourceWorkbookXml.Root?.Element(workbookNs + "smartTagPr");
        var sourceSmartTagTypes = sourceWorkbookXml.Root?.Element(workbookNs + "smartTagTypes");
        var sourceFunctionGroups = sourceWorkbookXml.Root?.Element(workbookNs + "functionGroups");
        var sourceDefinedNames = sourceWorkbookXml.Root?.Element(workbookNs + "definedNames");
        var sourceBookViews = sourceWorkbookXml.Root?.Element(workbookNs + "bookViews");
        var sourceCustomWorkbookViews = sourceWorkbookXml.Root?.Element(workbookNs + "customWorkbookViews");
        var sourceWorkbookProperties = sourceWorkbookXml.Root?.Element(workbookNs + "workbookPr");
        var sourceWorkbookProtection = sourceWorkbookXml.Root?.Element(workbookNs + "workbookProtection");
        var sourceCalculationProperties = sourceWorkbookXml.Root?.Element(workbookNs + "calcPr");
        if (sourceExtensionList is null &&
            sourceFileVersion is null &&
            sourceFileSharing is null &&
            sourceFileRecoveryProperties is null &&
            sourceSmartTagProperties is null &&
            sourceSmartTagTypes is null &&
            sourceFunctionGroups is null &&
            sourceDefinedNames is null &&
            sourceBookViews is null &&
            sourceCustomWorkbookViews is null &&
            sourceWorkbookProperties is null &&
            sourceWorkbookProtection is null &&
            sourceCalculationProperties is null)
        {
            return;
        }

        var targetWorkbookXml = LoadXml(targetWorkbookEntry);
        var targetRoot = targetWorkbookXml.Root;
        if (targetRoot is null)
            return;

        var changed = false;
        if (MergeWorkbookChildBlock(sourceFileVersion, targetRoot, workbookNs + "fileVersion"))
            changed = true;
        if (MergeWorkbookChildBlock(sourceFileSharing, targetRoot, workbookNs + "fileSharing"))
            changed = true;
        if (MergeWorkbookChildBlock(sourceFileRecoveryProperties, targetRoot, workbookNs + "fileRecoveryPr"))
            changed = true;
        if (MergeWorkbookChildBlock(sourceSmartTagProperties, targetRoot, workbookNs + "smartTagPr"))
            changed = true;
        if (MergeWorkbookChildBlock(sourceSmartTagTypes, targetRoot, workbookNs + "smartTagTypes"))
            changed = true;
        if (MergeWorkbookChildBlock(sourceFunctionGroups, targetRoot, workbookNs + "functionGroups"))
            changed = true;
        if (MergeWorkbookProperties(sourceWorkbookProperties, targetRoot, workbookNs))
            changed = true;
        if (MergeWorkbookProtection(sourceWorkbookProtection, targetRoot, workbookNs))
            changed = true;
        if (MergeWorkbookCalculationProperties(sourceCalculationProperties, targetRoot, workbookNs))
            changed = true;
        if (MergeWorkbookViews(sourceBookViews, targetRoot, workbookNs))
            changed = true;
        if (MergeWorkbookChildBlock(sourceCustomWorkbookViews, targetRoot, workbookNs + "customWorkbookViews"))
            changed = true;
        if (MergeWorkbookDefinedNames(sourceDefinedNames, targetRoot, workbookNs))
            changed = true;
        if (MergeExtensionList(sourceExtensionList, targetRoot, workbookNs))
            changed = true;

        if (changed)
            ReplacePackageXml(targetArchive, "xl/workbook.xml", targetWorkbookXml);
    }

    private static bool MergeWorkbookChildBlock(XElement? sourceBlock, XElement targetRoot, XName blockName)
    {
        if (sourceBlock is null || targetRoot.Element(blockName) is not null)
            return false;

        targetRoot.Add(new XElement(sourceBlock));
        return true;
    }

    private static bool MergeWorkbookProtection(XElement? sourceWorkbookProtection, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceWorkbookProtection is null)
            return false;

        var targetWorkbookProtection = targetRoot.Element(workbookNs + "workbookProtection");
        if (targetWorkbookProtection is null)
        {
            targetRoot.AddFirst(new XElement(sourceWorkbookProtection));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceWorkbookProtection.Attributes())
        {
            if (targetWorkbookProtection.Attribute(attribute.Name) is not null)
                continue;

            targetWorkbookProtection.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var existingChildNames = targetWorkbookProtection
            .Elements()
            .Select(element => element.Name)
            .ToHashSet();
        foreach (var sourceChild in sourceWorkbookProtection.Elements())
        {
            if (existingChildNames.Contains(sourceChild.Name))
                continue;

            targetWorkbookProtection.Add(new XElement(sourceChild));
            existingChildNames.Add(sourceChild.Name);
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorkbookCalculationProperties(XElement? sourceCalculationProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceCalculationProperties is null)
            return false;

        var targetCalculationProperties = targetRoot.Element(workbookNs + "calcPr");
        if (targetCalculationProperties is null)
        {
            targetRoot.Add(new XElement(sourceCalculationProperties));
            return true;
        }

        string[] modeledAttributes =
        [
            "calcMode",
            "fullCalcOnLoad",
            "forceFullCalc",
            "iterate",
            "iterateCount",
            "iterateDelta"
        ];
        var modeledAttributeNames = modeledAttributes
            .Select(name => XName.Get(name))
            .ToHashSet();

        var changed = false;
        foreach (var attribute in sourceCalculationProperties.Attributes())
        {
            if (modeledAttributeNames.Contains(attribute.Name))
                continue;

            if (targetCalculationProperties.Attribute(attribute.Name)?.Value == attribute.Value)
                continue;

            targetCalculationProperties.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorkbookProperties(XElement? sourceWorkbookProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceWorkbookProperties is null)
            return false;

        var targetWorkbookProperties = targetRoot.Element(workbookNs + "workbookPr");
        if (targetWorkbookProperties is null)
        {
            targetRoot.AddFirst(new XElement(sourceWorkbookProperties));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceWorkbookProperties.Attributes())
        {
            if (targetWorkbookProperties.Attribute(attribute.Name) is not null)
                continue;

            targetWorkbookProperties.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var existingChildNames = targetWorkbookProperties
            .Elements()
            .Select(element => element.Name)
            .ToHashSet();
        foreach (var sourceChild in sourceWorkbookProperties.Elements())
        {
            if (existingChildNames.Contains(sourceChild.Name))
                continue;

            targetWorkbookProperties.Add(new XElement(sourceChild));
            existingChildNames.Add(sourceChild.Name);
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorkbookViews(XElement? sourceBookViews, XElement targetRoot, XNamespace workbookNs)
    {
        var sourceViews = sourceBookViews?
            .Elements(workbookNs + "workbookView")
            .ToList()
            ?? [];
        if (sourceViews.Count == 0)
            return false;

        var targetBookViews = targetRoot.Element(workbookNs + "bookViews");
        if (targetBookViews is null)
        {
            targetRoot.AddFirst(new XElement(sourceBookViews!));
            return true;
        }

        var targetViews = targetBookViews
            .Elements(workbookNs + "workbookView")
            .ToList();
        var existingRawViews = targetViews
            .Select(view => view.ToString(System.Xml.Linq.SaveOptions.DisableFormatting))
            .ToHashSet(StringComparer.Ordinal);

        var changed = false;
        var mergedTargetViewKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceView in sourceViews)
        {
            var raw = sourceView.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
            if (existingRawViews.Contains(raw))
                continue;

            var sourceViewKey = WorkbookViewIdentityKey(sourceView);
            var targetView = IsPrimaryWorkbookView(sourceView) && !mergedTargetViewKeys.Contains(sourceViewKey)
                ? targetViews.FirstOrDefault(view => string.Equals(
                    WorkbookViewIdentityKey(view),
                    sourceViewKey,
                    StringComparison.OrdinalIgnoreCase))
                : null;
            if (targetView is not null)
            {
                if (MergeElementNativeAttributesAndChildren(sourceView, targetView))
                    changed = true;
                mergedTargetViewKeys.Add(sourceViewKey);
                continue;
            }

            targetBookViews.Add(new XElement(sourceView));
            targetViews.Add(targetBookViews.Elements(workbookNs + "workbookView").Last());
            existingRawViews.Add(raw);
            changed = true;
        }

        return changed;

        static string WorkbookViewIdentityKey(XElement view)
        {
            var firstSheet = view.Attribute("firstSheet")?.Value ?? string.Empty;
            var activeTab = view.Attribute("activeTab")?.Value ?? string.Empty;
            return $"{firstSheet}\u001f{activeTab}";
        }

        static bool IsPrimaryWorkbookView(XElement view)
        {
            var visibility = view.Attribute("visibility")?.Value;
            return string.IsNullOrWhiteSpace(visibility) ||
                   string.Equals(visibility, "visible", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool MergeWorkbookDefinedNames(XElement? sourceDefinedNames, XElement targetRoot, XNamespace workbookNs)
    {
        var sourceNames = sourceDefinedNames?
            .Elements(workbookNs + "definedName")
            .ToList()
            ?? [];
        if (sourceNames.Count == 0)
            return false;

        var targetDefinedNames = targetRoot.Element(workbookNs + "definedNames");
        if (targetDefinedNames is null)
        {
            targetRoot.Add(new XElement(sourceDefinedNames!));
            return true;
        }

        var existingKeys = targetDefinedNames
            .Elements(workbookNs + "definedName")
            .Select(DefinedNameKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceName in sourceNames)
        {
            var key = DefinedNameKey(sourceName);
            if (existingKeys.Contains(key))
                continue;

            targetDefinedNames.Add(new XElement(sourceName));
            existingKeys.Add(key);
            changed = true;
        }

        return changed;

        static string DefinedNameKey(XElement element)
        {
            var name = element.Attribute("name")?.Value ?? string.Empty;
            var localSheetId = element.Attribute("localSheetId")?.Value ?? string.Empty;
            return $"{name}\u001f{localSheetId}";
        }
    }

    private static void PreserveWorksheetMetadataBlocks(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XName[] retainedChildNames =
        [
            workbookNs + "customSheetViews",
            workbookNs + "scenarios",
            workbookNs + "ignoredErrors",
            workbookNs + "cellWatches",
            workbookNs + "sheetCalcPr",
            workbookNs + "phoneticPr",
            workbookNs + "sortState",
            workbookNs + "dataConsolidate",
            workbookNs + "legacyDrawingHF",
            workbookNs + "customProperties",
            workbookNs + "smartTags",
            workbookNs + "autoFilter",
            workbookNs + "protectedRanges",
            workbookNs + "rowBreaks",
            workbookNs + "colBreaks"
        ];

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var sourceWorkbookRelsEntry = sourceArchive.GetEntry("xl/_rels/workbook.xml.rels");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (sourceWorkbookEntry is null || sourceWorkbookRelsEntry is null ||
            targetWorkbookEntry is null || targetWorkbookRelsEntry is null)
        {
            return;
        }

        var sourceWorkbookXml = LoadXml(sourceWorkbookEntry);
        var targetWorkbookXml = LoadXml(targetWorkbookEntry);
        var sourceWorkbookRels = LoadRelationshipTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookRels = LoadRelationshipTargets(
            targetArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);

        var sourceSheets = GetWorkbookSheetPaths(sourceWorkbookXml, sourceWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var targetSheets = GetWorkbookSheetPaths(targetWorkbookXml, targetWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetEntry is null || targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = LoadXml(sourceWorksheetEntry);
            var sourceBlocks = retainedChildNames
                .Select(name => sourceWorksheetXml.Root?.Element(name))
                .Where(element => element is not null)
                .Cast<XElement>()
                .ToList();
            var sourceSheetProperties = sourceWorksheetXml.Root?.Element(workbookNs + "sheetPr");
            var sourceSheetFormatProperties = sourceWorksheetXml.Root?.Element(workbookNs + "sheetFormatPr");
            var sourcePrintOptions = sourceWorksheetXml.Root?.Element(workbookNs + "printOptions");
            var sourcePageSetup = sourceWorksheetXml.Root?.Element(workbookNs + "pageSetup");
            var sourceColumns = sourceWorksheetXml.Root?.Element(workbookNs + "cols");
            var sourceSheetData = sourceWorksheetXml.Root?.Element(workbookNs + "sheetData");
            var sourceSheetProtection = sourceWorksheetXml.Root?.Element(workbookNs + "sheetProtection");
            var sourceSheetViews = sourceWorksheetXml.Root?.Element(workbookNs + "sheetViews");
            var sourceExtensionList = sourceWorksheetXml.Root?.Element(workbookNs + "extLst");
            if (sourceBlocks.Count == 0 &&
                sourceSheetProperties is null &&
                sourceSheetFormatProperties is null &&
                sourcePrintOptions is null &&
                sourcePageSetup is null &&
                sourceColumns is null &&
                sourceSheetData is null &&
                sourceSheetProtection is null &&
                sourceSheetViews is null &&
                sourceExtensionList is null)
            {
                continue;
            }

            var targetWorksheetXml = LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            var changed = false;
            if (MergeWorksheetSheetProperties(sourceSheetProperties, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetSheetFormatProperties(sourceSheetFormatProperties, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetElementAttributes(sourcePrintOptions, targetRoot, workbookNs + "printOptions"))
                changed = true;
            if (MergeWorksheetElementAttributes(sourcePageSetup, targetRoot, workbookNs + "pageSetup"))
                changed = true;
            if (MergeWorksheetColumnAttributes(sourceColumns, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetRowAttributes(sourceSheetData, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetCellAttributes(sourceSheetData, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetSheetProtection(sourceSheetProtection, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetSheetViews(sourceSheetViews, targetRoot, workbookNs))
                changed = true;
            foreach (var sourceBlock in sourceBlocks)
            {
                if (sourceBlock.Name == workbookNs + "protectedRanges" &&
                    MergeWorksheetProtectedRanges(sourceBlock, targetRoot, workbookNs))
                {
                    changed = true;
                    continue;
                }
                if ((sourceBlock.Name == workbookNs + "rowBreaks" || sourceBlock.Name == workbookNs + "colBreaks") &&
                    MergeWorksheetBreaks(sourceBlock, targetRoot, workbookNs))
                {
                    changed = true;
                    continue;
                }

                if (targetRoot.Element(sourceBlock.Name) is not null)
                    continue;

                targetRoot.Add(new XElement(sourceBlock));
                changed = true;
            }

            if (MergeExtensionList(sourceExtensionList, targetRoot, workbookNs))
                changed = true;

            if (changed)
                ReplacePackageXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    private static bool MergeWorksheetColumnAttributes(XElement? sourceColumns, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceColumns is null)
            return false;

        var targetColumns = targetRoot.Element(workbookNs + "cols");
        if (targetColumns is null)
            return false;

        var targetColumnsByRange = targetColumns
            .Elements(workbookNs + "col")
            .Where(column => !string.IsNullOrWhiteSpace(column.Attribute("min")?.Value) &&
                             !string.IsNullOrWhiteSpace(column.Attribute("max")?.Value))
            .ToDictionary(ColumnRangeKey, StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceColumn in sourceColumns.Elements(workbookNs + "col"))
        {
            var key = ColumnRangeKey(sourceColumn);
            if (string.IsNullOrWhiteSpace(key) ||
                !targetColumnsByRange.TryGetValue(key, out var targetColumn))
            {
                continue;
            }

            foreach (var attribute in sourceColumn.Attributes())
            {
                if (targetColumn.Attribute(attribute.Name) is not null)
                    continue;

                targetColumn.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }
        }

        return changed;

        static string ColumnRangeKey(XElement column)
        {
            var min = column.Attribute("min")?.Value;
            var max = column.Attribute("max")?.Value;
            return string.IsNullOrWhiteSpace(min) || string.IsNullOrWhiteSpace(max)
                ? string.Empty
                : $"{min}:{max}";
        }
    }

    private static bool MergeWorksheetRowAttributes(XElement? sourceSheetData, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetData is null)
            return false;

        var targetSheetData = targetRoot.Element(workbookNs + "sheetData");
        if (targetSheetData is null)
            return false;

        var targetRowsByNumber = targetSheetData
            .Elements(workbookNs + "row")
            .Where(row => !string.IsNullOrWhiteSpace(row.Attribute("r")?.Value))
            .ToDictionary(
                row => row.Attribute("r")!.Value,
                StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceRow in sourceSheetData.Elements(workbookNs + "row"))
        {
            var rowNumber = sourceRow.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(rowNumber) ||
                !targetRowsByNumber.TryGetValue(rowNumber, out var targetRow))
            {
                continue;
            }

            foreach (var attribute in sourceRow.Attributes())
            {
                if (targetRow.Attribute(attribute.Name) is not null)
                    continue;

                targetRow.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }
        }

        return changed;
    }

    private static bool MergeWorksheetCellAttributes(XElement? sourceSheetData, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetData is null)
            return false;

        var targetSheetData = targetRoot.Element(workbookNs + "sheetData");
        if (targetSheetData is null)
            return false;

        var targetCellsByAddress = targetSheetData
            .Descendants(workbookNs + "c")
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Attribute("r")?.Value))
            .ToDictionary(
                cell => cell.Attribute("r")!.Value,
                StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceCell in sourceSheetData.Descendants(workbookNs + "c"))
        {
            var address = sourceCell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(address) ||
                !targetCellsByAddress.TryGetValue(address, out var targetCell))
            {
                continue;
            }

            foreach (var attribute in sourceCell.Attributes())
            {
                if (targetCell.Attribute(attribute.Name) is not null)
                    continue;

                targetCell.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }
        }

        return changed;
    }

    private static bool MergeWorksheetElementAttributes(XElement? sourceElement, XElement targetRoot, XName elementName)
    {
        if (sourceElement is null)
            return false;

        var targetElement = targetRoot.Element(elementName);
        if (targetElement is null)
        {
            targetRoot.Add(new XElement(sourceElement));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (targetElement.Attribute(attribute.Name) is not null)
                continue;

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetBreaks(XElement sourceBreaks, XElement targetRoot, XNamespace workbookNs)
    {
        var targetBreaks = targetRoot.Element(sourceBreaks.Name);
        if (targetBreaks is null)
        {
            targetRoot.Add(new XElement(sourceBreaks));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceBreaks.Attributes())
        {
            if (targetBreaks.Attribute(attribute.Name) is not null)
                continue;

            targetBreaks.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var targetBreaksById = targetBreaks
            .Elements(workbookNs + "brk")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("id")?.Value))
            .ToDictionary(
                element => element.Attribute("id")!.Value,
                StringComparer.OrdinalIgnoreCase);

        foreach (var sourceBreak in sourceBreaks.Elements(workbookNs + "brk"))
        {
            var id = sourceBreak.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id) || !targetBreaksById.TryGetValue(id, out var targetBreak))
            {
                targetBreaks.Add(new XElement(sourceBreak));
                if (!string.IsNullOrWhiteSpace(id))
                    targetBreaksById[id] = targetBreaks.Elements(workbookNs + "brk").Last();
                changed = true;
                continue;
            }

            foreach (var attribute in sourceBreak.Attributes())
            {
                if (targetBreak.Attribute(attribute.Name) is not null)
                    continue;

                targetBreak.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }
        }

        return changed;
    }

    private static bool MergeWorksheetSheetFormatProperties(XElement? sourceSheetFormatProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetFormatProperties is null)
            return false;

        var targetSheetFormatProperties = targetRoot.Element(workbookNs + "sheetFormatPr");
        if (targetSheetFormatProperties is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetFormatProperties));
            return true;
        }

        string[] nativeOnlyAttributes =
        [
            "baseColWidth",
            "zeroHeight",
            "thickTop",
            "thickBottom",
            "outlineLevelRow",
            "outlineLevelCol"
        ];
        var nativeOnlyAttributeNames = nativeOnlyAttributes
            .Select(name => XName.Get(name))
            .ToHashSet();

        var changed = false;
        foreach (var attribute in sourceSheetFormatProperties.Attributes())
        {
            if (targetSheetFormatProperties.Attribute(attribute.Name) is not null &&
                !nativeOnlyAttributeNames.Contains(attribute.Name))
            {
                continue;
            }

            if (targetSheetFormatProperties.Attribute(attribute.Name)?.Value == attribute.Value)
                continue;

            targetSheetFormatProperties.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetSheetViews(XElement? sourceSheetViews, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetViews is null)
            return false;

        var sourceViews = sourceSheetViews.Elements(workbookNs + "sheetView").ToList();
        if (sourceViews.Count == 0)
            return false;

        var targetSheetViews = targetRoot.Element(workbookNs + "sheetViews");
        if (targetSheetViews is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetViews));
            return true;
        }

        var existingViewIds = targetSheetViews
            .Elements(workbookNs + "sheetView")
            .Select(element => element.Attribute("workbookViewId")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceView in sourceViews)
        {
            var viewId = sourceView.Attribute("workbookViewId")?.Value;
            var targetView = !string.IsNullOrWhiteSpace(viewId)
                ? targetSheetViews
                    .Elements(workbookNs + "sheetView")
                    .FirstOrDefault(element => string.Equals(
                        element.Attribute("workbookViewId")?.Value,
                        viewId,
                        StringComparison.OrdinalIgnoreCase))
                : null;
            if (targetView is not null)
            {
                if (MergeElementNativeAttributesAndChildren(sourceView, targetView))
                    changed = true;
                continue;
            }

            targetSheetViews.Add(new XElement(sourceView));
            if (!string.IsNullOrWhiteSpace(viewId))
                existingViewIds.Add(viewId);
            changed = true;
        }

        return changed;
    }

    private static bool MergeElementNativeAttributesAndChildren(XElement sourceElement, XElement targetElement)
    {
        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (targetElement.Attribute(attribute.Name) is not null)
                continue;

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var existingChildrenByKey = targetElement
            .Elements()
            .GroupBy(ElementIdentityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var sourceChild in sourceElement.Elements())
        {
            var key = ElementIdentityKey(sourceChild);
            if (existingChildrenByKey.TryGetValue(key, out var targetChild))
            {
                if (MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetElement.Add(new XElement(sourceChild));
            existingChildrenByKey[key] = targetElement.Elements().Last();
            changed = true;
        }

        return changed;
    }

    private static string ElementIdentityKey(XElement element)
    {
        var address = element.Attribute("pane")?.Value
            ?? element.Attribute("sqref")?.Value
            ?? element.Attribute("ref")?.Value
            ?? element.Attribute("r")?.Value
            ?? element.Attribute("activeCell")?.Value
            ?? element.Attribute("name")?.Value
            ?? element.Attribute("id")?.Value
            ?? element.Attribute("uid")?.Value
            ?? element.Attribute("uri")?.Value
            ?? string.Empty;
        return $"{element.Name}\u001f{address}";
    }

    private static bool MergeWorksheetProtectedRanges(XElement sourceProtectedRanges, XElement targetRoot, XNamespace workbookNs)
    {
        var targetProtectedRanges = targetRoot.Element(workbookNs + "protectedRanges");
        if (targetProtectedRanges is null)
        {
            targetRoot.Add(new XElement(sourceProtectedRanges));
            return true;
        }

        var changed = false;
        var targetBySqref = targetProtectedRanges
            .Elements(workbookNs + "protectedRange")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("sqref")?.Value))
            .ToDictionary(
                element => element.Attribute("sqref")!.Value,
                element => element,
                StringComparer.OrdinalIgnoreCase);

        foreach (var sourceRange in sourceProtectedRanges.Elements(workbookNs + "protectedRange"))
        {
            var sqref = sourceRange.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref) || !targetBySqref.TryGetValue(sqref, out var targetRange))
            {
                targetProtectedRanges.Add(new XElement(sourceRange));
                changed = true;
                continue;
            }

            foreach (var sourceAttribute in sourceRange.Attributes())
            {
                if (sourceAttribute.Name == "sqref")
                    continue;

                if (targetRange.Attribute(sourceAttribute.Name)?.Value == sourceAttribute.Value)
                    continue;

                targetRange.SetAttributeValue(sourceAttribute.Name, sourceAttribute.Value);
                changed = true;
            }

            var existingChildNames = targetRange
                .Elements()
                .Select(element => element.Name)
                .ToHashSet();
            foreach (var sourceChild in sourceRange.Elements())
            {
                if (existingChildNames.Contains(sourceChild.Name))
                    continue;

                targetRange.Add(new XElement(sourceChild));
                existingChildNames.Add(sourceChild.Name);
                changed = true;
            }
        }

        return changed;
    }

    private static bool MergeWorksheetSheetProtection(XElement? sourceSheetProtection, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetProtection is null)
            return false;

        var targetSheetProtection = targetRoot.Element(workbookNs + "sheetProtection");
        if (targetSheetProtection is null)
        {
            targetRoot.Add(new XElement(sourceSheetProtection));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceSheetProtection.Attributes())
        {
            if (targetSheetProtection.Attribute(attribute.Name) is not null)
                continue;

            targetSheetProtection.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var existingChildNames = targetSheetProtection
            .Elements()
            .Select(element => element.Name)
            .ToHashSet();
        foreach (var sourceChild in sourceSheetProtection.Elements())
        {
            if (existingChildNames.Contains(sourceChild.Name))
                continue;

            targetSheetProtection.Add(new XElement(sourceChild));
            existingChildNames.Add(sourceChild.Name);
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetSheetProperties(XElement? sourceSheetProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetProperties is null)
            return false;

        var targetSheetProperties = targetRoot.Element(workbookNs + "sheetPr");
        if (targetSheetProperties is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetProperties));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceSheetProperties.Attributes())
        {
            if (targetSheetProperties.Attribute(attribute.Name) is not null)
                continue;

            targetSheetProperties.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var existingChildNames = targetSheetProperties
            .Elements()
            .Select(element => element.Name)
            .ToHashSet();
        foreach (var sourceChild in sourceSheetProperties.Elements())
        {
            if (existingChildNames.Contains(sourceChild.Name))
                continue;

            targetSheetProperties.Add(new XElement(sourceChild));
            existingChildNames.Add(sourceChild.Name);
            changed = true;
        }

        return changed;
    }

    private static bool MergeExtensionList(XElement? sourceExtensionList, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceExtensionList is null)
            return false;

        var sourceExtensions = sourceExtensionList
            .Elements(workbookNs + "ext")
            .ToList();
        if (sourceExtensions.Count == 0)
            return false;

        var targetExtensionList = targetRoot.Element(workbookNs + "extLst");
        if (targetExtensionList is null)
        {
            targetRoot.Add(new XElement(sourceExtensionList));
            return true;
        }

        var existingUris = targetExtensionList
            .Elements(workbookNs + "ext")
            .Select(extension => extension.Attribute("uri")?.Value)
            .Where(uri => !string.IsNullOrWhiteSpace(uri))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceExtension in sourceExtensions)
        {
            var uri = sourceExtension.Attribute("uri")?.Value;
            if (!string.IsNullOrWhiteSpace(uri) && existingUris.Contains(uri))
                continue;

            targetExtensionList.Add(new XElement(sourceExtension));
            if (!string.IsNullOrWhiteSpace(uri))
                existingUris.Add(uri);
            changed = true;
        }

        return changed;
    }

    private static void MergeWorksheetDrawingParts(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var sourceWorkbookRelsEntry = sourceArchive.GetEntry("xl/_rels/workbook.xml.rels");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (sourceWorkbookEntry is null || sourceWorkbookRelsEntry is null ||
            targetWorkbookEntry is null || targetWorkbookRelsEntry is null)
        {
            return;
        }

        var sourceWorkbookXml = LoadXml(sourceWorkbookEntry);
        var sourceWorkbookRels = LoadRelationshipTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookXml = LoadXml(targetWorkbookEntry);
        var targetWorkbookRels = LoadRelationshipTargets(
            targetArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);

        var sourceSheets = GetWorkbookSheetPaths(sourceWorkbookXml, sourceWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var targetSheets = GetWorkbookSheetPaths(targetWorkbookXml, targetWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceDrawingPath = GetWorksheetDrawingPath(sourceArchive, sourceWorksheetPath, workbookNs, relNs, packageRelNs);
            var targetDrawingPath = GetWorksheetDrawingPath(targetArchive, targetWorksheetPath, workbookNs, relNs, packageRelNs);
            if (string.IsNullOrWhiteSpace(sourceDrawingPath) || string.IsNullOrWhiteSpace(targetDrawingPath))
                continue;

            MergeDrawingPart(sourceArchive, targetArchive, sourceDrawingPath, targetDrawingPath, relNs, packageRelNs);
        }
    }

    private static string? GetWorksheetDrawingPath(
        ZipArchive archive,
        string worksheetPath,
        XNamespace worksheetNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return null;

        var worksheetXml = LoadXml(worksheetEntry);
        var drawingRelId = worksheetXml.Root?
            .Element(worksheetNs + "drawing")?
            .Attribute(relNs + "id")?
            .Value;
        if (string.IsNullOrWhiteSpace(drawingRelId))
            return null;

        var worksheetRels = LoadRelationshipTargets(
            archive,
            GetWorksheetRelsPath(worksheetPath),
            worksheetPath,
            packageRelNs);
        return worksheetRels.TryGetValue(drawingRelId, out var drawingPath)
            ? drawingPath
            : null;
    }

    private static void MergeDrawingPart(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceDrawingPath,
        string targetDrawingPath,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var sourceDrawingEntry = sourceArchive.GetEntry(sourceDrawingPath);
        var targetDrawingEntry = targetArchive.GetEntry(targetDrawingPath);
        if (sourceDrawingEntry is null || targetDrawingEntry is null)
            return;

        var sourceDrawingXml = LoadXml(sourceDrawingEntry);
        var targetDrawingXml = LoadXml(targetDrawingEntry);
        if (sourceDrawingXml.Root is null || targetDrawingXml.Root is null)
            return;

        var relIdMap = MergeDrawingRelationships(
            sourceArchive,
            targetArchive,
            sourceDrawingPath,
            targetDrawingPath,
            packageRelNs);
        var existingAnchorKeys = targetDrawingXml.Root.Elements()
            .Select(GetDrawingAnchorIdentity)
            .ToHashSet(StringComparer.Ordinal);

        var changed = false;
        foreach (var sourceAnchor in sourceDrawingXml.Root.Elements())
        {
            var anchorCopy = new XElement(sourceAnchor);
            RemapRelationshipReferences(anchorCopy, relNs, relIdMap);
            if (!existingAnchorKeys.Add(GetDrawingAnchorIdentity(anchorCopy)))
                continue;

            targetDrawingXml.Root.Add(anchorCopy);
            changed = true;
        }

        if (changed)
        {
            EnsureUniqueDrawingObjectIds(targetDrawingXml.Root);
            ReplacePackageXml(targetArchive, targetDrawingPath, targetDrawingXml);
        }
    }

    private static Dictionary<string, string> MergeDrawingRelationships(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceDrawingPath,
        string targetDrawingPath,
        XNamespace packageRelNs)
    {
        var relIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceRelsPath = GetWorksheetRelsPath(sourceDrawingPath);
        var sourceRelsEntry = sourceArchive.GetEntry(sourceRelsPath);
        if (sourceRelsEntry is null)
            return relIdMap;

        var targetRelsPath = GetWorksheetRelsPath(targetDrawingPath);
        var sourceRelsXml = LoadXml(sourceRelsEntry);
        var targetRelsXml = targetArchive.GetEntry(targetRelsPath) is { } targetRelsEntry
            ? LoadXml(targetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        if (sourceRelsXml.Root is null || targetRelsXml.Root is null)
            return relIdMap;

        var targetRelationships = targetRelsXml.Root.Elements(packageRelNs + "Relationship").ToList();
        var usedIds = targetRelationships
            .Select(rel => rel.Attribute("Id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var sourceRelationship in sourceRelsXml.Root.Elements(packageRelNs + "Relationship"))
        {
            var sourceId = sourceRelationship.Attribute("Id")?.Value;
            var type = sourceRelationship.Attribute("Type")?.Value;
            var target = sourceRelationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(sourceId) ||
                string.IsNullOrWhiteSpace(type) ||
                string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var targetMode = sourceRelationship.Attribute("TargetMode")?.Value;
            var resolvedTarget = string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase)
                ? target
                : ResolveRelationshipTarget(sourceDrawingPath, target);
            var targetRelationship = targetRelationships.FirstOrDefault(rel =>
                string.Equals(rel.Attribute("Type")?.Value, type, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("TargetMode")?.Value, targetMode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase)
                        ? rel.Attribute("Target")?.Value
                        : ResolveRelationshipTarget(targetDrawingPath, rel.Attribute("Target")?.Value ?? ""),
                    resolvedTarget,
                    StringComparison.OrdinalIgnoreCase));
            if (targetRelationship is not null)
            {
                relIdMap[sourceId] = targetRelationship.Attribute("Id")!.Value;
                continue;
            }

            var targetId = sourceId;
            if (usedIds.Contains(targetId))
                targetId = NextPreservedRelationshipId(usedIds);
            usedIds.Add(targetId);
            relIdMap[sourceId] = targetId;

            targetRelsXml.Root.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", targetId),
                new XAttribute("Type", type),
                new XAttribute("Target", string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase)
                    ? target
                    : GetRelativeTarget(targetDrawingPath, resolvedTarget)),
                string.IsNullOrWhiteSpace(targetMode) ? null : new XAttribute("TargetMode", targetMode)));
            changed = true;
        }

        if (changed)
            ReplacePackageXml(targetArchive, targetRelsPath, targetRelsXml);

        return relIdMap;
    }

    private static string NextPreservedRelationshipId(HashSet<string> usedIds)
    {
        var index = 1;
        while (usedIds.Contains($"rIdPreserved{index}"))
            index++;

        return $"rIdPreserved{index}";
    }

    private static void RemapRelationshipReferences(
        XElement element,
        XNamespace relNs,
        IReadOnlyDictionary<string, string> relIdMap)
    {
        if (relIdMap.Count == 0)
            return;

        foreach (var attribute in element.DescendantsAndSelf().Attributes().Where(attribute => attribute.Name.Namespace == relNs))
        {
            if (relIdMap.TryGetValue(attribute.Value, out var replacementId))
                attribute.Value = replacementId;
        }
    }

    private static string GetDrawingAnchorIdentity(XElement anchor)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var objectName = anchor
            .Descendants(spreadsheetDrawingNs + "cNvPr")
            .Select(element => element.Attribute("name")?.Value)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        return string.IsNullOrWhiteSpace(objectName)
            ? anchor.ToString(System.Xml.Linq.SaveOptions.DisableFormatting)
            : $"{anchor.Name.LocalName}:{objectName}";
    }

    private static void EnsureUniqueDrawingObjectIds(XElement drawingRoot)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var objectProperties = drawingRoot
            .Descendants(spreadsheetDrawingNs + "cNvPr")
            .ToList();
        var usedIds = new HashSet<int>();
        var nextId = objectProperties
            .Select(element => int.TryParse(element.Attribute("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        foreach (var objectProperty in objectProperties)
        {
            if (int.TryParse(objectProperty.Attribute("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                id > 0 &&
                usedIds.Add(id))
            {
                continue;
            }

            while (!usedIds.Add(nextId))
                nextId++;
            objectProperty.SetAttributeValue("id", nextId.ToString(CultureInfo.InvariantCulture));
            nextId++;
        }
    }

    private static void PreserveUnsupportedConditionalFormatting(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var sourceWorksheetEntry in sourceArchive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var targetWorksheetEntry = targetArchive.GetEntry(sourceWorksheetEntry.FullName);
            if (targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = LoadXml(sourceWorksheetEntry);
            var unsupportedBlocks = sourceWorksheetXml.Root?
                .Elements(worksheetNs + "conditionalFormatting")
                .Where(block => ConditionalFormattingHasUnsupportedRule(block, worksheetNs))
                .ToList()
                ?? [];
            if (unsupportedBlocks.Count == 0)
                continue;

            var targetWorksheetXml = LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            var existing = targetRoot
                .Elements(worksheetNs + "conditionalFormatting")
                .Select(element => element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var block in unsupportedBlocks)
            {
                var raw = block.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
                if (!existing.Contains(raw))
                    targetRoot.Add(new XElement(block));
            }

            ReplacePackageXml(targetArchive, sourceWorksheetEntry.FullName, targetWorksheetXml);
        }
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

    private static void SaveWorksheetCodeNames(Stream xlsxStream, Workbook workbook)
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

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                string.IsNullOrWhiteSpace(sheet.CodeName) ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var sheetPr = root.Element(workbookNs + "sheetPr");
            if (sheetPr is null)
            {
                sheetPr = new XElement(workbookNs + "sheetPr");
                root.AddFirst(sheetPr);
            }

            sheetPr.SetAttributeValue("codeName", sheet.CodeName);
            ReplacePackageXml(archive, worksheetPath, worksheetXml);
        }
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

    private static void SaveWorkbookCalculationProperties(Stream xlsxStream, Workbook workbook)
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

        var calcPr = root.Element(workbookNs + "calcPr");
        if (calcPr is null)
        {
            calcPr = new XElement(workbookNs + "calcPr");
            root.Add(calcPr);
        }

        calcPr.SetAttributeValue("calcMode", workbook.CalculationMode == WorkbookCalculationMode.Manual ? "manual" : "auto");
        SetBooleanAttribute(calcPr, "fullCalcOnLoad", workbook.FullCalculationOnLoad);
        SetBooleanAttribute(calcPr, "forceFullCalc", workbook.ForceFullCalculation);
        SetBooleanAttribute(calcPr, "iterate", workbook.IterativeCalculation);
        calcPr.SetAttributeValue(
            "iterateCount",
            workbook.MaxCalculationIterations is { } maxIterations ? maxIterations.ToString(CultureInfo.InvariantCulture) : null);
        calcPr.SetAttributeValue(
            "iterateDelta",
            workbook.MaxCalculationChange is { } maxChange ? maxChange.ToString(CultureInfo.InvariantCulture) : null);

        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

        static void SetBooleanAttribute(XElement element, string name, bool value) =>
            element.SetAttributeValue(name, value ? "1" : null);
    }

    private static void SaveAllowEditRanges(Stream xlsxStream, Workbook workbook)
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

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                sheet.AllowEditRanges.Count == 0 ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            root.Elements(workbookNs + "protectedRanges").Remove();
            var protectedRanges = new XElement(workbookNs + "protectedRanges",
                sheet.AllowEditRanges.Select((range, index) =>
                    new XElement(workbookNs + "protectedRange",
                        new XAttribute("name", $"FreexcelAllowEditRange{index + 1}"),
                        new XAttribute("sqref", range.ToString()))));

            var sheetProtection = root.Element(workbookNs + "sheetProtection");
            if (sheetProtection is not null)
                sheetProtection.AddAfterSelf(protectedRanges);
            else
                root.Add(protectedRanges);

            ReplacePackageXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static void SaveStructuredTables(Stream xlsxStream, Workbook workbook)
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
        var tablePartIndex = 1;

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                sheet.StructuredTables.Count == 0 ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = LoadXml(worksheetEntry);
            var worksheetRoot = worksheetXml.Root;
            if (worksheetRoot is null)
                continue;

            var worksheetRelsPath = GetWorksheetRelsPath(worksheetPath);
            var worksheetRelsEntry = archive.GetEntry(worksheetRelsPath);
            var worksheetRelsXml = worksheetRelsEntry is not null
                ? LoadXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));

            var tableParts = new List<XElement>();
            foreach (var table in sheet.StructuredTables)
            {
                var tablePath = string.IsNullOrWhiteSpace(table.PackagePart)
                    ? $"xl/tables/table{tablePartIndex}.xml"
                    : table.PackagePart.TrimStart('/').Replace('\\', '/');
                if (!tablePath.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase))
                    tablePath = $"xl/tables/table{tablePartIndex}.xml";

                tablePartIndex++;
                ReplacePackageXml(archive, tablePath, ToStructuredTableXml(table, tablePath));
                EnsureSpecificContentType(
                    archive,
                    $"/{tablePath}",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml");
                var tableRelId = EnsureRelationshipForPackagePart(
                    worksheetRelsXml,
                    packageRelNs,
                    worksheetPath,
                    tablePath,
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table");
                tableParts.Add(new XElement(workbookNs + "tablePart", new XAttribute(relNs + "id", tableRelId)));
            }

            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);
            worksheetRoot.Elements(workbookNs + "tableParts").Remove();
            worksheetRoot.Add(new XElement(
                workbookNs + "tableParts",
                new XAttribute("count", tableParts.Count.ToString(CultureInfo.InvariantCulture)),
                tableParts));
            ReplacePackageXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static XDocument ToStructuredTableXml(StructuredTableModel table, string tablePath)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var columns = table.Columns.Count > 0
            ? table.Columns.ToList()
            : Enumerable.Range(1, (int)(table.Range.End.Col - table.Range.Start.Col + 1))
                .Select(index => new StructuredTableColumnModel(index, $"Column{index}"))
                .ToList();

        var root = new XElement(
            workbookNs + "table",
            new XAttribute("id", table.Id > 0 ? table.Id : ExtractTrailingNumber(tablePath)),
            new XAttribute("name", string.IsNullOrWhiteSpace(table.Name) ? $"Table{ExtractTrailingNumber(tablePath)}" : table.Name),
            new XAttribute("displayName", string.IsNullOrWhiteSpace(table.DisplayName) ? table.Name : table.DisplayName),
            new XAttribute("ref", table.Range.ToString()),
            new XAttribute("totalsRowShown", table.TotalsRowShown ? "1" : "0"));
        if (table.HasAutoFilter)
            root.Add(ToStructuredTableAutoFilterXml(table, workbookNs));
        root.Add(new XElement(
            workbookNs + "tableColumns",
            new XAttribute("count", columns.Count.ToString(CultureInfo.InvariantCulture)),
            columns.Select(column => new XElement(
                workbookNs + "tableColumn",
                new XAttribute("id", column.Id),
                new XAttribute("name", column.Name),
                string.IsNullOrWhiteSpace(column.TotalsRowLabel) ? null : new XAttribute("totalsRowLabel", column.TotalsRowLabel),
                string.IsNullOrWhiteSpace(column.TotalsRowFunction) ? null : new XAttribute("totalsRowFunction", column.TotalsRowFunction)))));
        if (!string.IsNullOrWhiteSpace(table.StyleName))
        {
            root.Add(new XElement(
                workbookNs + "tableStyleInfo",
                new XAttribute("name", table.StyleName),
                new XAttribute("showFirstColumn", table.ShowFirstColumn ? "1" : "0"),
                new XAttribute("showLastColumn", table.ShowLastColumn ? "1" : "0"),
                new XAttribute("showRowStripes", table.ShowRowStripes ? "1" : "0"),
                new XAttribute("showColumnStripes", table.ShowColumnStripes ? "1" : "0")));
        }

        return new XDocument(root);
    }

    private static XElement ToStructuredTableAutoFilterXml(StructuredTableModel table, XNamespace workbookNs) =>
        new(
            workbookNs + "autoFilter",
            new XAttribute("ref", table.Range.ToString()),
            table.FilterColumns.Select(filterColumn => new XElement(
                workbookNs + "filterColumn",
                new XAttribute("colId", filterColumn.ColumnId.ToString(CultureInfo.InvariantCulture)),
                new XElement(
                    workbookNs + "filters",
                    filterColumn.IncludeBlank ? new XAttribute("blank", "1") : null,
                    filterColumn.Values.Select(value => new XElement(workbookNs + "filter", new XAttribute("val", value)))))));

    private static int ExtractTrailingNumber(string text)
    {
        var digits = new string(text.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 1;
    }

    private static void SaveAdvancedConditionalFormats(Stream xlsxStream, Workbook workbook)
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
        var dxfIds = SaveDifferentialStyles(archive, workbook, workbookNs);

        var relTargets = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            var advancedRules = sheet.ConditionalFormats.Where(IsAdvancedConditionalFormat).ToList();
            if (advancedRules.Count == 0)
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            foreach (var cf in advancedRules)
                root.Add(ToAdvancedConditionalFormattingXml(cf, workbookNs, dxfIds));

            ReplacePackageXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static IReadOnlyDictionary<Guid, int> SaveDifferentialStyles(
        ZipArchive archive,
        Workbook workbook,
        XNamespace workbookNs)
    {
        var rules = workbook.Sheets
            .SelectMany(sheet => sheet.ConditionalFormats)
            .Where(cf => IsAdvancedConditionalFormat(cf) && cf.FormatIfTrue is not null)
            .ToList();
        if (rules.Count == 0)
            return new Dictionary<Guid, int>();

        var stylesEntry = archive.GetEntry("xl/styles.xml");
        var stylesXml = stylesEntry is not null
            ? LoadXml(stylesEntry)
            : new XDocument(new XElement(workbookNs + "styleSheet"));
        var root = stylesXml.Root;
        if (root is null)
            return new Dictionary<Guid, int>();

        var dxfs = root.Element(workbookNs + "dxfs");
        if (dxfs is null)
        {
            dxfs = new XElement(workbookNs + "dxfs");
            root.Add(dxfs);
        }

        var result = new Dictionary<Guid, int>();
        var nextIndex = dxfs.Elements(workbookNs + "dxf").Count();
        foreach (var rule in rules)
        {
            if (rule.FormatIfTrue is null)
                continue;

            result[rule.Id] = nextIndex++;
            dxfs.Add(ToDifferentialStyleXml(rule.FormatIfTrue, workbookNs, nextIndex));
        }

        dxfs.SetAttributeValue("count", dxfs.Elements(workbookNs + "dxf").Count().ToString(CultureInfo.InvariantCulture));
        ReplacePackageXml(archive, "xl/styles.xml", stylesXml);
        return result;
    }

    private static XElement ToDifferentialStyleXml(CellStyle style, XNamespace workbookNs, int numberFormatId)
    {
        var def = CellStyle.Default;
        return new XElement(
            workbookNs + "dxf",
            style.NumberFormat != def.NumberFormat
                ? new XElement(
                    workbookNs + "numFmt",
                    new XAttribute("numFmtId", (164 + numberFormatId).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("formatCode", style.NumberFormat))
                : null,
            HasDifferentialFont(style)
                ? new XElement(
                    workbookNs + "font",
                    style.Bold != def.Bold ? new XElement(workbookNs + "b") : null,
                    style.Italic != def.Italic ? new XElement(workbookNs + "i") : null,
                    style.Underline != def.Underline ? new XElement(workbookNs + "u") : null,
                    style.Strikethrough != def.Strikethrough ? new XElement(workbookNs + "strike") : null,
                    style.Superscript != def.Superscript
                        ? new XElement(workbookNs + "vertAlign", new XAttribute("val", "superscript"))
                        : style.Subscript != def.Subscript
                            ? new XElement(workbookNs + "vertAlign", new XAttribute("val", "subscript"))
                            : null,
                    style.FontColor != def.FontColor ? new XElement(workbookNs + "color", new XAttribute("rgb", ToArgb(style.FontColor))) : null,
                    style.FontSize != def.FontSize && IsSupportedFontSize(style.FontSize)
                        ? new XElement(workbookNs + "sz", new XAttribute("val", style.FontSize.ToString(CultureInfo.InvariantCulture)))
                        : null,
                    style.FontName != def.FontName ? new XElement(workbookNs + "name", new XAttribute("val", style.FontName)) : null)
                : null,
            style.FillColor is { } fill
                ? new XElement(
                    workbookNs + "fill",
                    new XElement(
                        workbookNs + "patternFill",
                        new XAttribute("patternType", "solid"),
                        new XElement(workbookNs + "fgColor", new XAttribute("rgb", ToArgb(fill))),
                        new XElement(workbookNs + "bgColor", new XAttribute("indexed", "64"))))
                : null,
            HasDifferentialBorder(style)
                ? new XElement(
                    workbookNs + "border",
                    ToDifferentialBorderXml("left", style.BorderLeft, workbookNs),
                    ToDifferentialBorderXml("right", style.BorderRight, workbookNs),
                    ToDifferentialBorderXml("top", style.BorderTop, workbookNs),
                    ToDifferentialBorderXml("bottom", style.BorderBottom, workbookNs))
                : null);
    }

    private static bool HasDifferentialFont(CellStyle style)
    {
        var def = CellStyle.Default;
        return style.Bold != def.Bold ||
            style.Italic != def.Italic ||
            style.Underline != def.Underline ||
            style.Strikethrough != def.Strikethrough ||
            style.Superscript != def.Superscript ||
            style.Subscript != def.Subscript ||
            style.FontColor != def.FontColor ||
            style.FontSize != def.FontSize ||
            style.FontName != def.FontName;
    }

    private static bool HasDifferentialBorder(CellStyle style) =>
        style.BorderLeft.Style != BorderStyle.None ||
        style.BorderRight.Style != BorderStyle.None ||
        style.BorderTop.Style != BorderStyle.None ||
        style.BorderBottom.Style != BorderStyle.None;

    private static XElement ToDifferentialBorderXml(string edgeName, CellBorder border, XNamespace workbookNs)
    {
        var element = new XElement(workbookNs + edgeName);
        if (border.Style != BorderStyle.None)
        {
            element.SetAttributeValue("style", ToDifferentialBorderStyle(border.Style));
            element.Add(new XElement(workbookNs + "color", new XAttribute("rgb", ToArgb(border.Color))));
        }

        return element;
    }

    private static string ToDifferentialBorderStyle(BorderStyle style) =>
        style switch
        {
            BorderStyle.Thin => "thin",
            BorderStyle.Medium => "medium",
            BorderStyle.Thick => "thick",
            BorderStyle.Dashed => "dashed",
            BorderStyle.Dotted => "dotted",
            BorderStyle.Double => "double",
            _ => "none"
        };

    private static void SaveWorksheetSparklines(Stream xlsxStream, Workbook workbook)
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
        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        XNamespace xmNs = "http://schemas.microsoft.com/office/excel/2006/main";

        var relTargets = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                sheet.Sparklines.Count == 0 ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            root.Elements(workbookNs + "extLst").Remove();
            root.SetAttributeValue(XNamespace.Xmlns + "x14", x14Ns.NamespaceName);
            root.SetAttributeValue(XNamespace.Xmlns + "xm", xmNs.NamespaceName);
            root.Add(new XElement(
                workbookNs + "extLst",
                new XElement(
                    workbookNs + "ext",
                    new XAttribute("uri", "{05C60535-1F16-4fd2-B633-F4F36F0B64E0}"),
                    new XElement(
                        x14Ns + "sparklineGroups",
                        sheet.Sparklines
                            .Where(sparkline =>
                                sparkline.DataRange.Start.Sheet == sheet.Id &&
                                sparkline.DataRange.End.Sheet == sheet.Id &&
                                sparkline.Location.Sheet == sheet.Id &&
                                Enum.IsDefined(sparkline.Kind))
                            .GroupBy(sparkline => sparkline.Kind)
                            .Select(group => ToSparklineGroupXml(sheet, group.Key, group, x14Ns, xmNs))))));
            ReplacePackageXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static void SavePivotTables(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = LoadXml(workbookEntry);
        var workbookRelsXml = LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var cachePartById = new Dictionary<int, string>();
        var pivotCacheElements = new List<XElement>();
        var cacheIndex = 1;
        foreach (var cache in workbook.PivotCaches.OrderBy(cache => cache.CacheId))
        {
            if (cache.CacheId <= 0)
                continue;

            var cachePath = $"xl/pivotCache/pivotCacheDefinition{cacheIndex++}.xml";
            ReplacePackageXml(archive, cachePath, ToPivotCacheDefinitionXml(cache, workbookNs, relNs));
            ReplacePackageXml(archive, GetWorksheetRelsPath(cachePath), ToPivotCacheDefinitionRelsXml(packageRelNs));
            EnsureSpecificContentType(archive, $"/{cachePath}", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml");

            var cacheRelId = EnsureRelationshipForPackagePart(
                workbookRelsXml,
                packageRelNs,
                "xl/workbook.xml",
                cachePath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition");
            pivotCacheElements.Add(new XElement(
                workbookNs + "pivotCache",
                new XAttribute("cacheId", cache.CacheId.ToString(CultureInfo.InvariantCulture)),
                new XAttribute(relNs + "id", cacheRelId)));
            cachePartById[cache.CacheId] = cachePath;
        }

        var workbookRoot = workbookXml.Root;
        if (workbookRoot is not null && pivotCacheElements.Count > 0)
        {
            workbookRoot.Elements(workbookNs + "pivotCaches").Remove();
            var sheetsElement = workbookRoot.Element(workbookNs + "sheets");
            var pivotCachesElement = new XElement(
                workbookNs + "pivotCaches",
                new XAttribute("count", pivotCacheElements.Count.ToString(CultureInfo.InvariantCulture)),
                pivotCacheElements);
            if (sheetsElement is not null)
                sheetsElement.AddBeforeSelf(pivotCachesElement);
            else
                workbookRoot.Add(pivotCachesElement);
        }

        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        ReplacePackageXml(archive, "xl/_rels/workbook.xml.rels", workbookRelsXml);

        var relTargets = workbookRelsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        var pivotIndex = 1;
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                sheet.PivotTables.Count == 0 ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            WriteWorksheetPivotTables(archive, worksheetPath, sheet, cachePartById, ref pivotIndex, workbookNs, relNs, packageRelNs);
        }
    }

    private static void WriteWorksheetPivotTables(
        ZipArchive archive,
        string worksheetPath,
        Sheet sheet,
        IReadOnlyDictionary<int, string> cachePartById,
        ref int pivotIndex,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadXml(worksheetEntry);
        var worksheetRelsPath = GetWorksheetRelsPath(worksheetPath);
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));

        var references = new List<XElement>();
        foreach (var pivot in sheet.PivotTables)
        {
            if (!cachePartById.TryGetValue(pivot.CacheId, out var cachePath))
                continue;

            var pivotPath = $"xl/pivotTables/pivotTable{pivotIndex++}.xml";
            var cacheRelId = "rIdPivotCache";
            ReplacePackageXml(archive, pivotPath, ToPivotTableDefinitionXml(pivot, workbookNs, cacheRelId));
            ReplacePackageXml(archive, GetWorksheetRelsPath(pivotPath), new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", cacheRelId),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition"),
                        new XAttribute("Target", GetRelativeTarget(pivotPath, cachePath))))));
            EnsureSpecificContentType(archive, $"/{pivotPath}", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml");

            var pivotRelId = EnsureRelationshipForPackagePart(
                worksheetRelsXml,
                packageRelNs,
                worksheetPath,
                pivotPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable");
            references.Add(new XElement(workbookNs + "pivotTableDefinition", new XAttribute(relNs + "id", pivotRelId)));
        }

        if (references.Count == 0)
            return;

        worksheetXml.Root?.Elements(workbookNs + "pivotTableDefinition").Remove();
        worksheetXml.Root?.Add(references);
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);
    }

    private static XDocument ToPivotCacheDefinitionXml(PivotCacheModel cache, XNamespace workbookNs, XNamespace relNs)
    {
        var cacheSource = new XElement(
            workbookNs + "cacheSource",
            new XAttribute("type", ToPivotCacheSourceTypeText(cache.SourceType)));
        if (cache.ConnectionId is { } connectionId)
            cacheSource.SetAttributeValue("connectionId", connectionId.ToString(CultureInfo.InvariantCulture));
        if (cache.SourceType is PivotCacheSourceType.WorksheetRange or PivotCacheSourceType.Table or PivotCacheSourceType.Unknown)
        {
            var source = new XElement(workbookNs + "worksheetSource");
            if (!string.IsNullOrWhiteSpace(cache.SourceTableName))
                source.SetAttributeValue("name", cache.SourceTableName);
            if (!string.IsNullOrWhiteSpace(cache.SourceSheetName))
                source.SetAttributeValue("sheet", cache.SourceSheetName);
            if (!string.IsNullOrWhiteSpace(cache.SourceReference))
                source.SetAttributeValue("ref", cache.SourceReference);
            cacheSource.Add(source);
        }

        return new XDocument(new XElement(
            workbookNs + "pivotCacheDefinition",
            new XAttribute(XNamespace.Xmlns + "r", relNs),
            new XAttribute("refreshOnLoad", cache.RefreshOnLoad ? "1" : "0"),
            new XAttribute("saveData", cache.SaveData ? "1" : "0"),
            new XAttribute("enableRefresh", cache.EnableRefresh ? "1" : "0"),
            cache.IsOlap ? new XAttribute("olap", "1") : null,
            cache.RefreshedVersion is { } refreshedVersion ? new XAttribute("refreshedVersion", refreshedVersion.ToString(CultureInfo.InvariantCulture)) : null,
            !string.IsNullOrWhiteSpace(cache.RefreshedBy) ? new XAttribute("refreshedBy", cache.RefreshedBy) : null,
            new XAttribute("recordCount", "0"),
            cacheSource,
            new XElement(
                workbookNs + "cacheFields",
                new XAttribute("count", cache.Fields.Count.ToString(CultureInfo.InvariantCulture)),
                cache.Fields.Select(field => new XElement(
                    workbookNs + "cacheField",
                    new XAttribute("name", string.IsNullOrWhiteSpace(field.Name) ? "Field" : field.Name),
                    field.NumberFormatId is { } numFmtId ? new XAttribute("numFmtId", numFmtId.ToString(CultureInfo.InvariantCulture)) : null,
                    ToPivotCacheSharedItemsXml(field, workbookNs))))));
    }

    private static string ToPivotCacheSourceTypeText(PivotCacheSourceType sourceType) =>
        sourceType switch
        {
            PivotCacheSourceType.External => "external",
            PivotCacheSourceType.Consolidation => "consolidation",
            PivotCacheSourceType.Scenario => "scenario",
            _ => "worksheet"
        };

    private static XElement ToPivotCacheSharedItemsXml(PivotCacheFieldModel field, XNamespace workbookNs) =>
        new(
            workbookNs + "sharedItems",
            field.SharedItemCount is { } count ? new XAttribute("count", count.ToString(CultureInfo.InvariantCulture)) : null,
            field.ContainsBlank ? new XAttribute("containsBlank", "1") : null,
            field.ContainsString ? new XAttribute("containsString", "1") : null,
            field.ContainsNumber ? new XAttribute("containsNumber", "1") : null,
            field.ContainsDate ? new XAttribute("containsDate", "1") : null,
            field.ContainsMixedTypes ? new XAttribute("containsMixedTypes", "1") : null,
            field.ContainsSemiMixedTypes ? new XAttribute("containsSemiMixedTypes", "1") : null,
            field.ContainsNonDate ? new XAttribute("containsNonDate", "1") : null,
            field.ContainsInteger ? new XAttribute("containsInteger", "1") : null,
            field.ContainsLongText ? new XAttribute("longText", "1") : null,
            field.MinValue is { } minValue ? new XAttribute("minValue", minValue.ToString(CultureInfo.InvariantCulture)) : null,
            field.MaxValue is { } maxValue ? new XAttribute("maxValue", maxValue.ToString(CultureInfo.InvariantCulture)) : null,
            !string.IsNullOrWhiteSpace(field.MinDate) ? new XAttribute("minDate", field.MinDate) : null,
            !string.IsNullOrWhiteSpace(field.MaxDate) ? new XAttribute("maxDate", field.MaxDate) : null);

    private static XDocument ToPivotCacheDefinitionRelsXml(XNamespace packageRelNs) =>
        new(new XElement(packageRelNs + "Relationships"));

    private static XDocument ToPivotTableDefinitionXml(PivotTableModel pivot, XNamespace workbookNs, string cacheRelId) =>
        new(new XElement(
            workbookNs + "pivotTableDefinition",
            new XAttribute("name", string.IsNullOrWhiteSpace(pivot.Name) ? "PivotTable" : pivot.Name),
            new XAttribute("cacheId", pivot.CacheId.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("dataOnRows", "1"),
            new XAttribute("applyNumberFormats", "1"),
            new XAttribute("applyBorderFormats", "1"),
            new XAttribute("applyFontFormats", "1"),
            new XAttribute("applyPatternFormats", "1"),
            new XAttribute("updatedVersion", "8"),
            new XAttribute("minRefreshableVersion", "3"),
            new XAttribute("showGrandTotals", pivot.ShowGrandTotals ? "1" : "0"),
            new XAttribute("showRowGrandTotals", pivot.ShowRowGrandTotals ? "1" : "0"),
            new XAttribute("showColumnGrandTotals", pivot.ShowColumnGrandTotals ? "1" : "0"),
            new XAttribute("repeatItemLabels", pivot.RepeatItemLabels ? "1" : "0"),
            new XAttribute("blankLineAfterItems", pivot.BlankLineAfterItems ? "1" : "0"),
            new XAttribute("reportLayout", ToPivotReportLayoutText(pivot.ReportLayout)),
            new XElement(
                workbookNs + "location",
                new XAttribute("ref", pivot.TargetRange.ToString()),
                new XAttribute("firstDataCol", "1"),
                new XAttribute("firstDataRow", "1"),
                new XAttribute("firstHeaderRow", "1")),
            ToPivotFieldsXml(pivot, workbookNs),
            ToPivotFieldCollectionXml("rowFields", pivot.RowFields, workbookNs),
            ToPivotFieldCollectionXml("colFields", pivot.ColumnFields, workbookNs),
            ToPivotPageFieldsXml(pivot.PageFields, workbookNs),
            ToPivotDataFieldsXml(pivot.DataFields, workbookNs),
            ToPivotCalculatedFieldsXml(pivot.CalculatedFields, workbookNs),
            ToPivotCalculatedItemsXml(pivot.CalculatedItems, workbookNs),
            ToPivotValueFiltersXml(pivot.ValueFilters, workbookNs),
            ToPivotLabelFiltersXml(pivot.LabelFilters, workbookNs),
            ToPivotSortsXml(pivot.Sorts, workbookNs),
            new XElement(workbookNs + "pivotTableStyleInfo",
                new XAttribute("name", string.IsNullOrWhiteSpace(pivot.StyleName) ? "PivotStyleLight16" : pivot.StyleName),
                new XAttribute("showRowHeaders", pivot.ShowRowHeaders ? "1" : "0"),
                new XAttribute("showColHeaders", pivot.ShowColumnHeaders ? "1" : "0"),
                new XAttribute("showRowStripes", pivot.ShowRowStripes ? "1" : "0"),
                new XAttribute("showColStripes", pivot.ShowColumnStripes ? "1" : "0"),
                new XAttribute("showLastColumn", "1"))));

    private static XElement ToPivotFieldsXml(PivotTableModel pivot, XNamespace workbookNs)
    {
        var maxFieldIndex = pivot.RowFields
            .Concat(pivot.ColumnFields)
            .Concat(pivot.PageFields)
            .Select(field => field.SourceFieldIndex)
            .Concat(pivot.DataFields.Select(field => field.SourceFieldIndex))
            .DefaultIfEmpty(-1)
            .Max();

        return new XElement(
            workbookNs + "pivotFields",
            new XAttribute("count", Math.Max(0, maxFieldIndex + 1).ToString(CultureInfo.InvariantCulture)),
            Enumerable.Range(0, Math.Max(0, maxFieldIndex + 1)).Select(index => new XElement(
                workbookNs + "pivotField",
                pivot.RowFields.Any(field => field.SourceFieldIndex == index) ? new XAttribute("axis", "axisRow") : null,
                pivot.ColumnFields.Any(field => field.SourceFieldIndex == index) ? new XAttribute("axis", "axisCol") : null,
                pivot.PageFields.Any(field => field.SourceFieldIndex == index) ? new XAttribute("axis", "axisPage") : null,
                pivot.ShowSubtotals ? new XAttribute("defaultSubtotal", "1") : null,
                pivot.ShowSubtotals && pivot.SubtotalPlacement == PivotSubtotalPlacement.Top ? new XAttribute("subtotalTop", "1") : null,
                new XAttribute("showAll", "0"),
                new XElement(workbookNs + "items",
                    new XAttribute("count", "1"),
                    new XElement(workbookNs + "item", new XAttribute("t", "default"))))));
    }

    private static XElement? ToPivotFieldCollectionXml(string elementName, IReadOnlyList<PivotFieldModel> fields, XNamespace workbookNs) =>
        fields.Count == 0
            ? null
            : new XElement(
                workbookNs + elementName,
                new XAttribute("count", fields.Count.ToString(CultureInfo.InvariantCulture)),
                fields.Select(field => new XElement(
                    workbookNs + "field",
                    new XAttribute("x", field.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    string.IsNullOrWhiteSpace(field.SelectedItem) ? null : new XAttribute("name", field.SelectedItem),
                    field.SelectedItems is null || field.SelectedItems.Count == 0 ? null : new XAttribute("selectedItems", string.Join(",", field.SelectedItems)),
                    field.Grouping == PivotFieldGrouping.None ? null : new XAttribute("groupBy", ToPivotFieldGroupingText(field.Grouping)),
                    field.GroupStart is null ? null : new XAttribute("groupStart", FormatInvariant(field.GroupStart.Value)),
                    field.GroupEnd is null ? null : new XAttribute("groupEnd", FormatInvariant(field.GroupEnd.Value)),
                    field.GroupInterval is null ? null : new XAttribute("groupInterval", FormatInvariant(field.GroupInterval.Value)))));

    private static XElement? ToPivotPageFieldsXml(IReadOnlyList<PivotFieldModel> fields, XNamespace workbookNs) =>
        fields.Count == 0
            ? null
            : new XElement(
                workbookNs + "pageFields",
                new XAttribute("count", fields.Count.ToString(CultureInfo.InvariantCulture)),
                fields.Select(field => new XElement(
                    workbookNs + "pageField",
                    new XAttribute("fld", field.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    string.IsNullOrWhiteSpace(field.SelectedItem) ? null : new XAttribute("name", field.SelectedItem),
                    field.SelectedItems is null || field.SelectedItems.Count == 0 ? null : new XAttribute("selectedItems", string.Join(",", field.SelectedItems)),
                    field.Grouping == PivotFieldGrouping.None ? null : new XAttribute("groupBy", ToPivotFieldGroupingText(field.Grouping)),
                    field.GroupStart is null ? null : new XAttribute("groupStart", FormatInvariant(field.GroupStart.Value)),
                    field.GroupEnd is null ? null : new XAttribute("groupEnd", FormatInvariant(field.GroupEnd.Value)),
                    field.GroupInterval is null ? null : new XAttribute("groupInterval", FormatInvariant(field.GroupInterval.Value)))));

    private static XElement? ToPivotDataFieldsXml(IReadOnlyList<PivotDataFieldModel> fields, XNamespace workbookNs) =>
        fields.Count == 0
            ? null
            : new XElement(
                workbookNs + "dataFields",
                new XAttribute("count", fields.Count.ToString(CultureInfo.InvariantCulture)),
                fields.Select(field => new XElement(
                    workbookNs + "dataField",
                    new XAttribute("name", string.IsNullOrWhiteSpace(field.Name) ? "Values" : field.Name),
                    new XAttribute("fld", field.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("subtotal", string.IsNullOrWhiteSpace(field.SummaryFunction) ? "sum" : field.SummaryFunction),
                    string.IsNullOrWhiteSpace(field.CalculatedFieldName) ? null : new XAttribute("calculatedField", field.CalculatedFieldName),
                    field.ShowValuesAs == PivotShowValuesAs.None ? null : new XAttribute("showValuesAs", ToPivotShowValuesAsText(field.ShowValuesAs)),
                    field.BaseFieldIndex is { } baseField ? new XAttribute("baseField", baseField.ToString(CultureInfo.InvariantCulture)) : null,
                    string.IsNullOrWhiteSpace(field.BaseItem) ? null : new XAttribute("baseItem", field.BaseItem),
                    field.NumberFormatId is { } numFmtId ? new XAttribute("numFmtId", numFmtId.ToString(CultureInfo.InvariantCulture)) : null)));

    private static XElement? ToPivotCalculatedFieldsXml(IReadOnlyList<PivotCalculatedFieldModel> fields, XNamespace workbookNs) =>
        fields.Count == 0
            ? null
            : new XElement(
                workbookNs + "calculatedFields",
                new XAttribute("count", fields.Count.ToString(CultureInfo.InvariantCulture)),
                fields.Select((field, index) => new XElement(
                    workbookNs + "calculatedField",
                    new XAttribute("name", field.Name),
                    new XAttribute("fld", index.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("formula", field.Formula))));

    private static XElement? ToPivotCalculatedItemsXml(IReadOnlyList<PivotCalculatedItemModel> items, XNamespace workbookNs) =>
        items.Count == 0
            ? null
            : new XElement(
                workbookNs + "calculatedItems",
                new XAttribute("count", items.Count.ToString(CultureInfo.InvariantCulture)),
                items.Select(item => new XElement(
                    workbookNs + "calculatedItem",
                    new XAttribute("field", item.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("name", item.Name),
                    new XAttribute("formula", item.Formula))));

    private static XElement? ToPivotValueFiltersXml(IReadOnlyList<PivotValueFilterModel> filters, XNamespace workbookNs) =>
        filters.Count == 0
            ? null
            : new XElement(
                workbookNs + "valueFilters",
                new XAttribute("count", filters.Count.ToString(CultureInfo.InvariantCulture)),
                filters.Select(filter => new XElement(
                    workbookNs + "valueFilter",
                    new XAttribute("dataField", filter.DataFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("type", ToPivotValueFilterKindText(filter.Kind)),
                    new XAttribute("count", filter.Count.ToString(CultureInfo.InvariantCulture)),
                    filter.SourceFieldIndex is null ? null : new XAttribute("field", filter.SourceFieldIndex.Value.ToString(CultureInfo.InvariantCulture)),
                    filter.ComparisonValue is null ? null : new XAttribute("comparisonValue", FormatInvariant(filter.ComparisonValue.Value)),
                    filter.ComparisonValue2 is null ? null : new XAttribute("comparisonValue2", FormatInvariant(filter.ComparisonValue2.Value)))));

    private static XElement? ToPivotLabelFiltersXml(IReadOnlyList<PivotLabelFilterModel> filters, XNamespace workbookNs) =>
        filters.Count == 0
            ? null
            : new XElement(
                workbookNs + "labelFilters",
                new XAttribute("count", filters.Count.ToString(CultureInfo.InvariantCulture)),
                filters.Select(filter => new XElement(
                    workbookNs + "labelFilter",
                    new XAttribute("field", filter.SourceFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("type", ToPivotLabelFilterKindText(filter.Kind)),
                    new XAttribute("value", filter.Value),
                    string.IsNullOrWhiteSpace(filter.Value2) ? null : new XAttribute("value2", filter.Value2))));

    private static XElement? ToPivotSortsXml(IReadOnlyList<PivotSortModel> sorts, XNamespace workbookNs) =>
        sorts.Count == 0
            ? null
            : new XElement(
                workbookNs + "pivotSorts",
                new XAttribute("count", sorts.Count.ToString(CultureInfo.InvariantCulture)),
                sorts.Select(sort => new XElement(
                    workbookNs + "pivotSort",
                    new XAttribute("target", sort.Target == PivotSortTarget.Label ? "label" : "value"),
                    new XAttribute("direction", sort.Direction == PivotSortDirection.Descending ? "descending" : "ascending"),
                    new XAttribute("dataField", sort.DataFieldIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("field", sort.FieldIndex.ToString(CultureInfo.InvariantCulture)))));

    private static string ToPivotFieldGroupingText(PivotFieldGrouping grouping) =>
        grouping switch
        {
            PivotFieldGrouping.Year => "years",
            PivotFieldGrouping.Quarter => "quarters",
            PivotFieldGrouping.Month => "months",
            PivotFieldGrouping.Day => "days",
            PivotFieldGrouping.NumberRange => "numberRange",
            _ => "none"
        };

    private static string ToPivotReportLayoutText(PivotReportLayout layout) =>
        layout switch
        {
            PivotReportLayout.Compact => "compact",
            PivotReportLayout.Outline => "outline",
            _ => "tabular"
        };

    private static string ToPivotShowValuesAsText(PivotShowValuesAs showValuesAs) =>
        showValuesAs switch
        {
            PivotShowValuesAs.PercentOfGrandTotal => "percentOfGrandTotal",
            PivotShowValuesAs.PercentOfRowTotal => "percentOfRowTotal",
            PivotShowValuesAs.PercentOfColumnTotal => "percentOfColumnTotal",
            PivotShowValuesAs.RunningTotalIn => "runningTotalIn",
            PivotShowValuesAs.DifferenceFrom => "differenceFrom",
            PivotShowValuesAs.PercentDifferenceFrom => "percentDifferenceFrom",
            PivotShowValuesAs.RankSmallest => "rankSmallest",
            PivotShowValuesAs.RankLargest => "rankLargest",
            PivotShowValuesAs.Index => "index",
            PivotShowValuesAs.PercentOfParentRowTotal => "percentOfParentRowTotal",
            PivotShowValuesAs.PercentOfParentColumnTotal => "percentOfParentColumnTotal",
            PivotShowValuesAs.PercentOfParentTotal => "percentOfParentTotal",
            _ => "none"
        };

    private static string ToPivotValueFilterKindText(PivotValueFilterKind kind) =>
        kind switch
        {
            PivotValueFilterKind.Bottom => "bottom",
            PivotValueFilterKind.GreaterThan => "greaterThan",
            PivotValueFilterKind.GreaterThanOrEqual => "greaterThanOrEqual",
            PivotValueFilterKind.LessThan => "lessThan",
            PivotValueFilterKind.LessThanOrEqual => "lessThanOrEqual",
            PivotValueFilterKind.Equals => "equals",
            PivotValueFilterKind.DoesNotEqual => "doesNotEqual",
            PivotValueFilterKind.Between => "between",
            PivotValueFilterKind.NotBetween => "notBetween",
            PivotValueFilterKind.AboveAverage => "aboveAverage",
            PivotValueFilterKind.BelowAverage => "belowAverage",
            _ => "top"
        };

    private static string ToPivotLabelFilterKindText(PivotLabelFilterKind kind) =>
        kind switch
        {
            PivotLabelFilterKind.DoesNotEqual => "doesNotEqual",
            PivotLabelFilterKind.BeginsWith => "beginsWith",
            PivotLabelFilterKind.EndsWith => "endsWith",
            PivotLabelFilterKind.Contains => "contains",
            PivotLabelFilterKind.DoesNotContain => "doesNotContain",
            PivotLabelFilterKind.GreaterThan => "greaterThan",
            PivotLabelFilterKind.GreaterThanOrEqual => "greaterThanOrEqual",
            PivotLabelFilterKind.LessThan => "lessThan",
            PivotLabelFilterKind.LessThanOrEqual => "lessThanOrEqual",
            PivotLabelFilterKind.Between => "between",
            _ => "equals"
        };

    private static string FormatInvariant(double value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    private static XElement ToSparklineGroupXml(
        Sheet sheet,
        SparklineKind kind,
        IEnumerable<SparklineModel> sparklines,
        XNamespace x14Ns,
        XNamespace xmNs) =>
        new(x14Ns + "sparklineGroup",
            new XAttribute("type", ToSparklineType(kind)),
            new XElement(
                x14Ns + "sparklines",
                sparklines.Select(sparkline => new XElement(
                    x14Ns + "sparkline",
                    new XElement(xmNs + "f", $"{QuoteSheetName(sheet.Name)}!{sparkline.DataRange}"),
                    new XElement(xmNs + "sqref", sparkline.Location.ToA1())))));

    private static string ToSparklineType(SparklineKind kind) =>
        kind switch
        {
            SparklineKind.Column => "column",
            SparklineKind.WinLoss => "stacked",
            _ => "line"
        };

    private static string QuoteSheetName(string sheetName) =>
        sheetName.Any(ch => char.IsWhiteSpace(ch) || ch == '\'')
            ? $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'"
            : sheetName;

    private static XElement ToAdvancedConditionalFormattingXml(
        ConditionalFormat cf,
        XNamespace worksheetNs,
        IReadOnlyDictionary<Guid, int> differentialStyleIds) =>
        new(worksheetNs + "conditionalFormatting",
            new XAttribute("sqref", cf.AppliesTo.ToString()),
            ToAdvancedCfRuleXml(cf, worksheetNs, differentialStyleIds));

    private static XElement ToAdvancedCfRuleXml(
        ConditionalFormat cf,
        XNamespace worksheetNs,
        IReadOnlyDictionary<Guid, int> differentialStyleIds)
    {
        var rule = new XElement(
            worksheetNs + "cfRule",
            new XAttribute("type", ToAdvancedCfRuleType(cf.RuleType)),
            new XAttribute("priority", cf.Priority));
        if (differentialStyleIds.TryGetValue(cf.Id, out var dxfId))
            rule.SetAttributeValue("dxfId", dxfId.ToString(CultureInfo.InvariantCulture));
        if (cf.StopIfTrue)
            rule.SetAttributeValue("stopIfTrue", "1");
        switch (cf.RuleType)
        {
            case CfRuleType.ColorScale:
                rule.Add(new XElement(
                    worksheetNs + "colorScale",
                    ToCfvoXml(worksheetNs, cf.MinThresholdType, cf.MinThresholdValue),
                    cf.UseThreeColorScale ? ToCfvoXml(worksheetNs, cf.MidThresholdType, cf.MidThresholdValue) : null,
                    ToCfvoXml(worksheetNs, cf.MaxThresholdType, cf.MaxThresholdValue),
                    ToColorXml(worksheetNs, cf.MinColor),
                    cf.UseThreeColorScale ? ToColorXml(worksheetNs, cf.MidColor) : null,
                    ToColorXml(worksheetNs, cf.MaxColor)));
                break;
            case CfRuleType.DataBar:
                var dataBar = new XElement(
                    worksheetNs + "dataBar",
                    new XAttribute("showValue", cf.DataBarShowValue ? "1" : "0"),
                    ToCfvoXml(worksheetNs, cf.DataBarMinThresholdType, cf.DataBarMinThresholdValue),
                    ToCfvoXml(worksheetNs, cf.DataBarMaxThresholdType, cf.DataBarMaxThresholdValue),
                    ToColorXml(worksheetNs, cf.DataBarColor));
                if (cf.DataBarMinLength.HasValue)
                    dataBar.SetAttributeValue("minLength", cf.DataBarMinLength.Value.ToString(CultureInfo.InvariantCulture));
                if (cf.DataBarMaxLength.HasValue)
                    dataBar.SetAttributeValue("maxLength", cf.DataBarMaxLength.Value.ToString(CultureInfo.InvariantCulture));
                rule.Add(dataBar);
                break;
            case CfRuleType.IconSet:
                rule.Add(new XElement(
                    worksheetNs + "iconSet",
                    new XAttribute("iconSet", string.IsNullOrWhiteSpace(cf.IconSetStyle) ? "3TrafficLights1" : cf.IconSetStyle),
                    new XAttribute("showValue", cf.IconSetShowValue ? "1" : "0"),
                    new XAttribute("reverse", cf.IconSetReverse ? "1" : "0"),
                    ToCfvoXml(worksheetNs, CfThresholdType.Percent, "0"),
                    ToCfvoXml(worksheetNs, CfThresholdType.Percent, "33"),
                    ToCfvoXml(worksheetNs, CfThresholdType.Percent, "67")));
                break;
            case CfRuleType.AboveAverage:
                rule.SetAttributeValue("aboveAverage", cf.AboveAverage ? "1" : "0");
                break;
            case CfRuleType.Top10:
                rule.SetAttributeValue("rank", Math.Clamp(cf.TopBottomRank, 1, 1000).ToString(CultureInfo.InvariantCulture));
                rule.SetAttributeValue("bottom", cf.AboveAverage ? "0" : "1");
                rule.SetAttributeValue("percent", cf.TopBottomPercent ? "1" : "0");
                break;
            case CfRuleType.ContainsText:
            case CfRuleType.NotContainsText:
            case CfRuleType.BeginsWith:
            case CfRuleType.EndsWith:
                if (!string.IsNullOrWhiteSpace(cf.TextRuleText))
                    rule.SetAttributeValue("text", cf.TextRuleText);
                if (!string.IsNullOrWhiteSpace(cf.FormulaText))
                    rule.Add(new XElement(worksheetNs + "formula", cf.FormulaText));
                break;
            case CfRuleType.DateOccurring:
                rule.SetAttributeValue("timePeriod", string.IsNullOrWhiteSpace(cf.DateOccurringPeriod) ? "today" : cf.DateOccurringPeriod);
                if (!string.IsNullOrWhiteSpace(cf.FormulaText))
                    rule.Add(new XElement(worksheetNs + "formula", cf.FormulaText));
                break;
            case CfRuleType.Blanks:
            case CfRuleType.NoBlanks:
            case CfRuleType.Errors:
            case CfRuleType.NoErrors:
            case CfRuleType.UniqueValues:
            case CfRuleType.DuplicateValues:
                if (!string.IsNullOrWhiteSpace(cf.FormulaText))
                    rule.Add(new XElement(worksheetNs + "formula", cf.FormulaText));
                break;
        }

        return rule;
    }

    private static XElement ToCfvoXml(XNamespace worksheetNs, CfThresholdType type, string? value)
    {
        var element = new XElement(worksheetNs + "cfvo", new XAttribute("type", ToCfvoType(type)));
        if (!string.IsNullOrWhiteSpace(value))
            element.SetAttributeValue("val", value);
        return element;
    }

    private static XElement ToColorXml(XNamespace worksheetNs, RgbColor color) =>
        new(worksheetNs + "color", new XAttribute("rgb", $"FF{color.R:X2}{color.G:X2}{color.B:X2}"));

    private static string ToArgb(CellColor color) =>
        $"FF{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool IsAdvancedConditionalFormat(ConditionalFormat cf) =>
        cf.RuleType is CfRuleType.ColorScale or CfRuleType.DataBar or CfRuleType.IconSet or
            CfRuleType.AboveAverage or CfRuleType.Top10 or
            CfRuleType.UniqueValues or CfRuleType.DuplicateValues or
            CfRuleType.ContainsText or CfRuleType.NotContainsText or CfRuleType.BeginsWith or CfRuleType.EndsWith or
            CfRuleType.DateOccurring or
            CfRuleType.Blanks or CfRuleType.NoBlanks or CfRuleType.Errors or CfRuleType.NoErrors;

    private static string ToAdvancedCfRuleType(CfRuleType type) =>
        type switch
        {
            CfRuleType.ColorScale => "colorScale",
            CfRuleType.DataBar => "dataBar",
            CfRuleType.IconSet => "iconSet",
            CfRuleType.AboveAverage => "aboveAverage",
            CfRuleType.Top10 => "top10",
            CfRuleType.UniqueValues => "uniqueValues",
            CfRuleType.DuplicateValues => "duplicateValues",
            CfRuleType.ContainsText => "containsText",
            CfRuleType.NotContainsText => "notContainsText",
            CfRuleType.BeginsWith => "beginsWith",
            CfRuleType.EndsWith => "endsWith",
            CfRuleType.DateOccurring => "timePeriod",
            CfRuleType.Blanks => "containsBlanks",
            CfRuleType.NoBlanks => "notContainsBlanks",
            CfRuleType.Errors => "containsErrors",
            CfRuleType.NoErrors => "notContainsErrors",
            _ => throw new InvalidOperationException("Conditional format is not an advanced rule.")
        };

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

    private static void SaveWorksheetDrawingObjects(Stream xlsxStream, Workbook workbook)
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
        var pictureIndex = 1;
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            var pictures = sheet.Pictures.Where(IsSupportedXlsxPicture).ToList();
            var textBoxes = sheet.TextBoxes.Where(IsSupportedXlsxTextBox).ToList();
            var shapes = sheet.DrawingShapes.Where(IsSupportedXlsxDrawingShape).ToList();
            if (pictures.Count == 0 && textBoxes.Count == 0 && shapes.Count == 0)
                continue;

            WriteWorksheetDrawingObjects(archive, worksheetPath, pictures, textBoxes, shapes, drawingIndex++, ref pictureIndex);
        }
    }

    private static void WriteWorksheetDrawingObjects(
        ZipArchive archive,
        string worksheetPath,
        IReadOnlyList<PictureModel> pictures,
        IReadOnlyList<TextBoxModel> textBoxes,
        IReadOnlyList<DrawingShapeModel> shapes,
        int drawingIndex,
        ref int pictureIndex)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        var drawingPath = $"xl/drawings/drawing{drawingIndex}.xml";
        var drawingRelsPath = GetWorksheetRelsPath(drawingPath);
        archive.GetEntry(drawingPath)?.Delete();
        archive.GetEntry(drawingRelsPath)?.Delete();

        var drawingRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        var anchors = new List<XElement>();
        foreach (var picture in pictures)
        {
            var currentPictureIndex = pictureIndex++;
            var contentType = string.IsNullOrWhiteSpace(picture.ContentType) ? "image/png" : picture.ContentType;
            var extension = GetExtensionFromContentType(contentType).TrimStart('.');
            var mediaPath = $"xl/media/freexcelPicture{currentPictureIndex}.{extension}";
            archive.GetEntry(mediaPath)?.Delete();
            var mediaEntry = archive.CreateEntry(mediaPath);
            using (var mediaStream = mediaEntry.Open())
                mediaStream.Write(picture.ImageBytes!);
            EnsureContentType(archive, extension, contentType);

            var imageRelId = $"rIdFreexcelPicture{currentPictureIndex}";
            drawingRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", imageRelId),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", GetRelativeTarget(drawingPath, mediaPath))));
            anchors.Add(ToOneCellPictureAnchor(
                picture,
                currentPictureIndex,
                imageRelId,
                spreadsheetDrawingNs,
                drawingNs,
                relNs));
        }
        var shapeIndex = 1;
        foreach (var textBox in textBoxes)
        {
            anchors.Add(ToOneCellTextBoxAnchor(
                textBox,
                shapeIndex++,
                spreadsheetDrawingNs,
                drawingNs));
        }
        foreach (var shape in shapes)
        {
            anchors.Add(ToOneCellDrawingShapeAnchor(
                shape,
                shapeIndex++,
                spreadsheetDrawingNs,
                drawingNs));
        }

        ReplacePackageXml(archive, drawingPath, new XDocument(
            new XElement(spreadsheetDrawingNs + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute(XNamespace.Xmlns + "r", relNs),
                anchors)));
        ReplacePackageXml(archive, drawingRelsPath, drawingRelsXml);
        EnsureSpecificContentType(archive, $"/{drawingPath}", "application/vnd.openxmlformats-officedocument.drawing+xml");

        var relsPath = GetWorksheetRelsPath(worksheetPath);
        var worksheetRelsXml = archive.GetEntry(relsPath) is { } relsEntry
            ? LoadXml(relsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        var drawingRelId = EnsureRelationshipForPackagePart(
            worksheetRelsXml,
            packageRelNs,
            worksheetPath,
            drawingPath,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing");
        ReplacePackageXml(archive, relsPath, worksheetRelsXml);

        var worksheetXml = LoadXml(worksheetEntry);
        var root = worksheetXml.Root;
        if (root is null)
            return;

        root.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
        root.Elements(worksheetNs + "drawing").Remove();
        root.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", drawingRelId)));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static XElement ToOneCellPictureAnchor(
        PictureModel picture,
        int pictureIndex,
        string imageRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace relNs) =>
        new(spreadsheetDrawingNs + "oneCellAnchor",
            new XElement(spreadsheetDrawingNs + "from",
                new XElement(spreadsheetDrawingNs + "col", Math.Max(0, (long)picture.Anchor.Col - 1).ToString(CultureInfo.InvariantCulture)),
                new XElement(spreadsheetDrawingNs + "colOff", "0"),
                new XElement(spreadsheetDrawingNs + "row", Math.Max(0, (long)picture.Anchor.Row - 1).ToString(CultureInfo.InvariantCulture)),
                new XElement(spreadsheetDrawingNs + "rowOff", "0")),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", PixelsToEmus(picture.Width)),
                new XAttribute("cy", PixelsToEmus(picture.Height))),
            new XElement(spreadsheetDrawingNs + "pic",
                new XElement(spreadsheetDrawingNs + "nvPicPr",
                    new XElement(spreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", pictureIndex + 1),
                        new XAttribute("name", $"Picture {pictureIndex}"),
                        string.IsNullOrWhiteSpace(picture.AltText) ? null : new XAttribute("descr", picture.AltText)),
                    new XElement(spreadsheetDrawingNs + "cNvPicPr")),
                new XElement(spreadsheetDrawingNs + "blipFill",
                    new XElement(drawingNs + "blip", new XAttribute(relNs + "embed", imageRelId)),
                    HasPictureCrop(picture)
                        ? new XElement(drawingNs + "srcRect",
                            new XAttribute("l", ToSourceRectanglePercent(picture.CropLeft)),
                            new XAttribute("t", ToSourceRectanglePercent(picture.CropTop)),
                            new XAttribute("r", ToSourceRectanglePercent(picture.CropRight)),
                            new XAttribute("b", ToSourceRectanglePercent(picture.CropBottom)))
                        : null,
                    new XElement(drawingNs + "stretch", new XElement(drawingNs + "fillRect"))),
                new XElement(spreadsheetDrawingNs + "spPr",
                    new XElement(drawingNs + "xfrm"),
                    new XElement(drawingNs + "prstGeom",
                        new XAttribute("prst", "rect"),
                        new XElement(drawingNs + "avLst")))),
            new XElement(spreadsheetDrawingNs + "clientData"));

    private static bool HasPictureCrop(PictureModel picture) =>
        picture.CropLeft > 0 ||
        picture.CropTop > 0 ||
        picture.CropRight > 0 ||
        picture.CropBottom > 0;

    private static string ToSourceRectanglePercent(double ratio) =>
        ((int)Math.Round(Math.Clamp(ratio, 0, 1) * 100000d)).ToString(CultureInfo.InvariantCulture);

    private static XElement ToOneCellTextBoxAnchor(
        TextBoxModel textBox,
        int shapeIndex,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs) =>
        new(spreadsheetDrawingNs + "oneCellAnchor",
            ToDrawingAnchorFrom(textBox.Anchor, spreadsheetDrawingNs),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", PixelsToEmus(textBox.Width)),
                new XAttribute("cy", PixelsToEmus(textBox.Height))),
            new XElement(spreadsheetDrawingNs + "sp",
                new XElement(spreadsheetDrawingNs + "nvSpPr",
                    new XElement(spreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", shapeIndex + 100),
                        new XAttribute("name", $"TextBox {shapeIndex}"),
                        string.IsNullOrWhiteSpace(textBox.AltText) ? null : new XAttribute("descr", textBox.AltText)),
                    new XElement(spreadsheetDrawingNs + "cNvSpPr", new XAttribute("txBox", "1"))),
                ToShapePropertiesForDrawingObject(
                    "rect",
                    textBox.RotationDegrees,
                    textBox.FillThemeColor,
                    textBox.FillColor,
                    textBox.OutlineThemeColor,
                    textBox.OutlineColor,
                    spreadsheetDrawingNs,
                    drawingNs),
                new XElement(spreadsheetDrawingNs + "txBody",
                    new XElement(drawingNs + "bodyPr"),
                    new XElement(drawingNs + "lstStyle"),
                    new XElement(drawingNs + "p",
                        new XElement(drawingNs + "r",
                            new XElement(drawingNs + "t", textBox.Text))))),
            new XElement(spreadsheetDrawingNs + "clientData"));

    private static XElement ToOneCellDrawingShapeAnchor(
        DrawingShapeModel shape,
        int shapeIndex,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs) =>
        new(spreadsheetDrawingNs + "oneCellAnchor",
            ToDrawingAnchorFrom(shape.Anchor, spreadsheetDrawingNs),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", PixelsToEmus(shape.Width)),
                new XAttribute("cy", PixelsToEmus(shape.Height))),
            new XElement(spreadsheetDrawingNs + "sp",
                new XElement(spreadsheetDrawingNs + "nvSpPr",
                    new XElement(spreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", shapeIndex + 200),
                        new XAttribute("name", $"Shape {shapeIndex}"),
                        string.IsNullOrWhiteSpace(shape.AltText) ? null : new XAttribute("descr", shape.AltText)),
                    new XElement(spreadsheetDrawingNs + "cNvSpPr")),
                ToShapePropertiesForDrawingObject(
                    ToDrawingPreset(shape.Kind),
                    shape.RotationDegrees,
                    shape.FillThemeColor,
                    shape.FillColor,
                    shape.OutlineThemeColor,
                    shape.OutlineColor,
                    spreadsheetDrawingNs,
                    drawingNs,
                    shape.GradientFillEndColor,
                    shape.HasShadowEffect)),
            new XElement(spreadsheetDrawingNs + "clientData"));

    private static XElement ToDrawingAnchorFrom(CellAddress anchor, XNamespace spreadsheetDrawingNs) =>
        new(spreadsheetDrawingNs + "from",
            new XElement(spreadsheetDrawingNs + "col", Math.Max(0, (long)anchor.Col - 1).ToString(CultureInfo.InvariantCulture)),
            new XElement(spreadsheetDrawingNs + "colOff", "0"),
            new XElement(spreadsheetDrawingNs + "row", Math.Max(0, (long)anchor.Row - 1).ToString(CultureInfo.InvariantCulture)),
            new XElement(spreadsheetDrawingNs + "rowOff", "0"));

    private static XElement ToShapePropertiesForDrawingObject(
        string preset,
        double rotationDegrees,
        WorkbookThemeColorReference? fillThemeColor,
        CellColor? fillColor,
        WorkbookThemeColorReference? outlineThemeColor,
        CellColor? outlineColor,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        CellColor? gradientFillEndColor = null,
        bool hasShadowEffect = false)
    {
        var rotation = NormalizeRotation(rotationDegrees);
        return new XElement(spreadsheetDrawingNs + "spPr",
            new XElement(drawingNs + "xfrm",
                rotation == 0 ? null : new XAttribute("rot", (long)Math.Round(rotation * 60000))),
            new XElement(drawingNs + "prstGeom",
                new XAttribute("prst", preset),
                new XElement(drawingNs + "avLst")),
            gradientFillEndColor is { } gradientEndColor && fillColor is { } gradientStartColor
                ? ToGradientFill(gradientStartColor, gradientEndColor, drawingNs)
                : ToSolidFill(fillThemeColor, fillColor, drawingNs),
            ToLineProperties(outlineThemeColor, outlineColor, drawingNs),
            hasShadowEffect ? ToOuterShadowEffect(drawingNs) : null);
    }

    private static XElement ToGradientFill(CellColor startColor, CellColor endColor, XNamespace drawingNs) =>
        new(drawingNs + "gradFill",
            new XElement(drawingNs + "gsLst",
                new XElement(drawingNs + "gs",
                    new XAttribute("pos", "0"),
                    ToRgbColorElement(startColor, drawingNs)),
                new XElement(drawingNs + "gs",
                    new XAttribute("pos", "100000"),
                    ToRgbColorElement(endColor, drawingNs))),
            new XElement(drawingNs + "lin",
                new XAttribute("ang", "5400000"),
                new XAttribute("scaled", "1")));

    private static XElement ToOuterShadowEffect(XNamespace drawingNs) =>
        new(drawingNs + "effectLst",
            new XElement(drawingNs + "outerShdw",
                new XAttribute("blurRad", "40000"),
                new XAttribute("dist", "20000"),
                new XAttribute("dir", "5400000"),
                ToRgbColorElement(new CellColor(128, 128, 128), drawingNs)));

    private static XElement ToRgbColorElement(CellColor color, XNamespace drawingNs) =>
        new(drawingNs + "srgbClr", new XAttribute("val", FormatThemeColor(color)));

    private static XElement? ToLineProperties(
        WorkbookThemeColorReference? outlineThemeColor,
        CellColor? outlineColor,
        XNamespace drawingNs)
    {
        var fill = ToSolidFill(outlineThemeColor, outlineColor, drawingNs);
        return fill is null ? null : new XElement(drawingNs + "ln", fill);
    }

    private static string ToDrawingPreset(DrawingShapeKind kind) =>
        kind switch
        {
            DrawingShapeKind.Ellipse => "ellipse",
            DrawingShapeKind.Line => "line",
            _ => "rect"
        };

    private static double NormalizeRotation(double rotationDegrees)
    {
        if (!double.IsFinite(rotationDegrees))
            return 0;
        var normalized = rotationDegrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static bool IsSupportedXlsxPicture(PictureModel picture) =>
        picture.Kind == PictureKind.Image &&
        picture.ImageBytes is { Length: > 0 } &&
        double.IsFinite(picture.Width) &&
        double.IsFinite(picture.Height) &&
        picture.Width > 0 &&
        picture.Height > 0;

    private static bool IsSupportedXlsxTextBox(TextBoxModel textBox) =>
        double.IsFinite(textBox.Width) &&
        double.IsFinite(textBox.Height) &&
        textBox.Width > 0 &&
        textBox.Height > 0;

    private static bool IsSupportedXlsxDrawingShape(DrawingShapeModel shape) =>
        Enum.IsDefined(shape.Kind) &&
        double.IsFinite(shape.Width) &&
        double.IsFinite(shape.Height) &&
        shape.Width > 0 &&
        shape.Height > 0;

    private static XDocument ToChartXml(ChartModel chart, Sheet sheet)
    {
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var plotCharts = ToPlotChartXml(chart, sheet, chartNs, drawingNs).ToList();

        return new XDocument(
            new XElement(chartNs + "chartSpace",
                new XAttribute(XNamespace.Xmlns + "c", chartNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                chart.ChartStyleId is { } styleId ? new XElement(chartNs + "style", new XAttribute("val", styleId.ToString(CultureInfo.InvariantCulture))) : null,
                chart.RoundedCorners ? new XElement(chartNs + "roundedCorners", new XAttribute("val", "1")) : null,
                ToChartAreaShapeProperties(chart, chartNs, drawingNs),
                ToPivotSourceXml(chart, sheet, chartNs),
                new XElement(chartNs + "chart",
                    string.IsNullOrWhiteSpace(chart.Title)
                        ? null
                        : ToChartTitleXml(chart, chartNs, drawingNs),
                    chart.AutoTitleDeleted ? new XElement(chartNs + "autoTitleDeleted", new XAttribute("val", "1")) : null,
                    new XElement(chartNs + "plotArea",
                        plotCharts,
                        ShouldWriteChartAxes(chart.Type)
                            ? ToChartAxesXml(chart, chartNs, drawingNs)
                            : null,
                        ToPlotAreaShapeProperties(chart, chartNs, drawingNs)),
                    ToLegendXml(chart, chartNs, drawingNs),
                    chart.ShowDataInHiddenRowsAndColumns ? new XElement(chartNs + "plotVisOnly", new XAttribute("val", "0")) : null,
                    ToBlankDisplayXml(chart, chartNs),
                    chart.ShowDataLabelsOverMaximum ? new XElement(chartNs + "showDLblsOverMax", new XAttribute("val", "1")) : null)));
    }

    private static XElement? ToBlankDisplayXml(ChartModel chart, XNamespace chartNs) =>
        chart.BlankDisplayMode == ChartBlankDisplayMode.Gap
            ? null
            : new XElement(chartNs + "dispBlanksAs",
                new XAttribute("val", chart.BlankDisplayMode == ChartBlankDisplayMode.Span ? "span" : "zero"));

    private static XElement? ToPivotSourceXml(ChartModel chart, Sheet sheet, XNamespace chartNs)
    {
        if (!chart.IsPivotChart || string.IsNullOrWhiteSpace(chart.PivotTableName))
            return null;

        var sourceSheetName = string.IsNullOrWhiteSpace(chart.PivotSourceSheetName)
            ? sheet.Name
            : chart.PivotSourceSheetName!;
        return new XElement(chartNs + "pivotSource",
            new XElement(chartNs + "name", $"{QuoteSheetName(sourceSheetName)}!{chart.PivotTableName}"),
            new XElement(chartNs + "fmtId", new XAttribute("val", "0")));
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
            ChartType.Radar => new XElement(chartNs + "radarChart",
                new XElement(chartNs + "radarStyle", new XAttribute("val", "marker")),
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries, forceLineShapeProperties: true)),
            ChartType.Stock => new XElement(chartNs + "stockChart",
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries, forceLineShapeProperties: true)),
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
                or ChartType.Doughnut
                or ChartType.Radar
                or ChartType.Stock);

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
                sheet.ViewTopRow.HasValue ||
                sheet.ViewLeftCol.HasValue ||
                sheet.ActiveRow.HasValue ||
                sheet.ActiveCol.HasValue ||
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
        sheetView.SetAttributeValue("topLeftCell", ToOptionalA1(sheet.ViewTopRow, sheet.ViewLeftCol));
        if (ToOptionalA1(sheet.ActiveRow, sheet.ActiveCol) is { } activeCell)
        {
            sheetView.Elements(worksheetNs + "selection").Remove();
            sheetView.Add(new XElement(
                worksheetNs + "selection",
                new XAttribute("activeCell", activeCell),
                new XAttribute("sqref", activeCell)));
        }

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

    private static string? ToOptionalA1(uint? row, uint? col)
    {
        return row is > 0 and <= CellAddress.MaxRow &&
               col is > 0 and <= CellAddress.MaxCol
            ? $"{CellAddress.NumberToColumnName(col.Value)}{row.Value}"
            : null;
    }

    private static ScalarValue MapValue(IXLCell xlCell)
    {
        if (xlCell.Value.IsDateTime)
        {
            try { return DateTimeValue.FromDateTime(xlCell.GetDateTime()); }
            catch (ArgumentException)
            {
                return TryGetUnifiedNumber(xlCell.Value, out var serial)
                    ? new NumberValue(serial)
                    : ErrorValue.Num;
            }
        }

        return MapValue(xlCell.Value);
    }

    private static string NormalizeExcelFormulaText(string formulaText)
    {
        return formulaText
            .Replace("_xlfn.", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_xlws.", "", StringComparison.OrdinalIgnoreCase);
    }

    private static ScalarValue MapValue(XLCellValue xlValue)
    {
        if (xlValue.IsBlank) return BlankValue.Instance;
        if (xlValue.IsNumber) return new NumberValue(xlValue.GetNumber());
        if (xlValue.IsText) return new TextValue(xlValue.GetText());
        if (xlValue.IsBoolean) return new BoolValue(xlValue.GetBoolean());
        if (xlValue.IsDateTime)
        {
            try { return DateTimeValue.FromDateTime(xlValue.GetDateTime()); }
            catch (ArgumentException)
            {
                try { return new NumberValue(xlValue.GetNumber()); }
                catch { return ErrorValue.Num; }
            }
        }
        if (xlValue.IsError) return MapErrorValue(xlValue.GetError());
        return new TextValue(xlValue.ToString());
    }

    private static bool TryGetUnifiedNumber(XLCellValue value, out double number)
    {
        number = 0;
        if (!value.IsUnifiedNumber || XlCellValueNumberField is null)
            return false;

        if (XlCellValueNumberField.GetValue(value) is double raw)
        {
            number = raw;
            return true;
        }

        return false;
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
                XLAlignmentHorizontalValues.Justify => HorizontalAlignment.Justify,
                XLAlignmentHorizontalValues.Distributed => HorizontalAlignment.Distributed,
                _ => HorizontalAlignment.General,
            },
            VerticalAlignment = xlStyle.Alignment.Vertical switch
            {
                XLAlignmentVerticalValues.Top => VerticalAlignment.Top,
                XLAlignmentVerticalValues.Center => VerticalAlignment.Center,
                XLAlignmentVerticalValues.Bottom => VerticalAlignment.Bottom,
                XLAlignmentVerticalValues.Justify => VerticalAlignment.Justify,
                XLAlignmentVerticalValues.Distributed => VerticalAlignment.Distributed,
                _ => VerticalAlignment.Bottom,
            },
            WrapText = xlStyle.Alignment.WrapText,
            ShrinkToFit = xlStyle.Alignment.ShrinkToFit,
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
                HorizontalAlignment.Justify => XLAlignmentHorizontalValues.Justify,
                HorizontalAlignment.Distributed => XLAlignmentHorizontalValues.Distributed,
                _ => XLAlignmentHorizontalValues.General,
            };

        if (style.VerticalAlignment != def.VerticalAlignment)
            xlCell.Style.Alignment.Vertical = style.VerticalAlignment switch
            {
                VerticalAlignment.Top => XLAlignmentVerticalValues.Top,
                VerticalAlignment.Center => XLAlignmentVerticalValues.Center,
                VerticalAlignment.Justify => XLAlignmentVerticalValues.Justify,
                VerticalAlignment.Distributed => XLAlignmentVerticalValues.Distributed,
                _ => XLAlignmentVerticalValues.Bottom,
            };

        if (style.WrapText != def.WrapText)
            xlCell.Style.Alignment.WrapText = style.WrapText;

        if (style.ShrinkToFit != def.ShrinkToFit)
            xlCell.Style.Alignment.ShrinkToFit = style.ShrinkToFit;

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

    private sealed record XlsxSourcePackage(byte[] Bytes);

    private sealed record PivotPackageMetadata(
        IReadOnlyList<PivotCacheModel> PivotCaches,
        IReadOnlyDictionary<string, List<PendingPivotTableModel>> PivotTablesBySheetName)
    {
        public static PivotPackageMetadata Empty { get; } = new(
            [],
            new Dictionary<string, List<PendingPivotTableModel>>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed record SlicerTimelinePackageMetadata(
        IReadOnlyList<SlicerModel> Slicers,
        IReadOnlyList<TimelineModel> Timelines);

    private sealed record SlicerCacheMetadata(
        string Name,
        string? SourceFieldName,
        string? PivotTableName,
        IReadOnlyList<string> SelectedItems);

    private sealed record TimelineCacheMetadata(
        string Name,
        string? SourceFieldName,
        string? PivotTableName,
        string? StartDate,
        string? EndDate,
        string? SelectedStartDate,
        string? SelectedEndDate);

    private sealed record PendingPivotTableModel(
        string Name,
        int CacheId,
        string TargetReference,
        string PackagePart,
        bool ShowSubtotals,
        PivotSubtotalPlacement SubtotalPlacement,
        bool ShowGrandTotals,
        bool ShowRowGrandTotals,
        bool ShowColumnGrandTotals,
        bool RepeatItemLabels,
        bool BlankLineAfterItems,
        PivotReportLayout ReportLayout,
        string StyleName,
        bool ShowRowHeaders,
        bool ShowColumnHeaders,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotDataFieldModel> DataFields,
        IReadOnlyList<PivotCalculatedFieldModel> CalculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> CalculatedItems,
        IReadOnlyList<PivotValueFilterModel> ValueFilters,
        IReadOnlyList<PivotLabelFilterModel> LabelFilters,
        IReadOnlyList<PivotSortModel> Sorts);

    private sealed record StructuredTablePackageMetadata(
        IReadOnlyDictionary<string, List<PendingStructuredTableModel>> TablesBySheetName)
    {
        public static StructuredTablePackageMetadata Empty { get; } = new(
            new Dictionary<string, List<PendingStructuredTableModel>>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed record PendingStructuredTableModel(
        int Id,
        string Name,
        string DisplayName,
        string RangeReference,
        bool HasAutoFilter,
        bool TotalsRowShown,
        string? StyleName,
        bool ShowFirstColumn,
        bool ShowLastColumn,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        string PackagePart,
        IReadOnlyList<StructuredTableColumnModel> Columns,
        IReadOnlyList<StructuredTableFilterColumnModel> FilterColumns);
}
