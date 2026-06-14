using System.IO;
using System.Xml;
using ClosedXML.Excel;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    /// <summary>
    /// Saves a workbook to the given stream and returns any non-fatal warnings collected during
    /// the save (e.g. individual named ranges or data-validation rules that could not be serialized).
    /// The file is always written; warnings indicate partial data loss.
    /// </summary>
    public XlsxSaveResult SaveWithWarnings(Workbook workbook, Stream stream)
    {
        var warnings = new List<string>();
        SaveCore(workbook, stream, warnings);
        return warnings.Count == 0 ? XlsxSaveResult.Clean : new XlsxSaveResult(warnings.AsReadOnly());
    }

    /// <inheritdoc/>
    public void Save(Workbook workbook, Stream stream)
    {
        SaveCore(workbook, stream, warnings: null);
    }

    private void SaveCore(Workbook workbook, Stream stream, List<string>? warnings)
    {
        // Serialize with loads/other saves: the full-save path builds a ClosedXML XLWorkbook, which
        // shares process-global static state with the load path.  The cheap patch/source-copy paths
        // don't touch ClosedXML, but gating the whole method keeps the rule simple and the cost is
        // negligible (saves are user-initiated and brief on the patch path).  See ClosedXmlGate.
        lock (ClosedXmlGate)
        {
            SaveCoreUnlocked(workbook, stream, warnings);
        }
    }

    private void SaveCoreUnlocked(Workbook workbook, Stream stream, List<string>? warnings)
    {
        LastSaveDiagnostics = XlsxSaveDiagnostics.NotRun;
        string? currentModelFingerprint = null;
        if (SourcePackages.TryGetValue(workbook, out var sourcePackage) &&
            sourcePackage.Matches(workbook, out currentModelFingerprint))
        {
            sourcePackage.CopyTo(stream);
            LastSaveDiagnostics = XlsxSaveDiagnostics.SourceCopy("model_unchanged");
            return;
        }

        var patchDiagnostics = XlsxSaveDiagnostics.FullSave("patch_not_attempted");
        if (sourcePackage is not null)
        {
            bool patchSucceeded;
            try
            {
                patchSucceeded = sourcePackage.TrySavePatchedCellValues(
                    workbook,
                    stream,
                    ref currentModelFingerprint,
                    out patchDiagnostics);
            }
            catch (ArgumentException ex) when (ex.Message.Contains("invalid character", StringComparison.OrdinalIgnoreCase))
            {
                // Patch-path XML serialisation failed due to a character that cannot be represented
                // in XML (e.g. a control character that slipped through escaping).  Fall back to
                // the full ClosedXML save so the user's data is never lost.
                System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Patch-save XML error, falling back to full save: {ex.Message}");
                patchDiagnostics = XlsxSaveDiagnostics.FullSave("patch_xml_serialization_error");
                patchSucceeded = false;
            }
            catch (XmlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Patch-save XML error, falling back to full save: {ex.Message}");
                patchDiagnostics = XlsxSaveDiagnostics.FullSave("patch_xml_serialization_error");
                patchSucceeded = false;
            }

            if (patchSucceeded)
            {
                LastSaveDiagnostics = patchDiagnostics;
                return;
            }
        }

        LastSaveDiagnostics = sourcePackage is null
            ? XlsxSaveDiagnostics.FullSave("no_source_package")
            : patchDiagnostics;

        using var xlWorkbook = new XLWorkbook();
        XlsxClosedXmlCellMapper.ApplyStyle(xlWorkbook.Style, workbook.GetStyle(StyleId.Default));
        xlWorkbook.CalculateMode = workbook.CalculationMode == WorkbookCalculationMode.Manual
            ? XLCalculateMode.Manual
            : XLCalculateMode.Auto;
        var styleCache = new Dictionary<StyleId, CellStyle>(workbook.StyleCount);
        // Per-save cache: StyleId → boxed XLStyleValue captured after the first application.
        // Subsequent cells with the same StyleId are styled in one SetStyle call instead of
        // the ~15 individual ClosedXML setter calls that ApplyStyle performs.
        var xlStyleValueCache = XlCellSetStyleValueAction is not null && XlCellStyleValueAccessor is not null
            ? new Dictionary<StyleId, object>(workbook.StyleCount)
            : null;

        foreach (var sheet in workbook.Sheets)
        {
            var xlSheet = xlWorkbook.Worksheets.Add(sheet.Name);
            XlsxClosedXmlCellMapper.ApplyStyle(xlSheet.Style, workbook.GetStyle(StyleId.Default));
            xlSheet.Visibility = sheet.IsVeryHidden
                ? XLWorksheetVisibility.VeryHidden
                : sheet.IsHidden ? XLWorksheetVisibility.Hidden : XLWorksheetVisibility.Visible;
            if (sheet.TabColor is { } tabColor)
                xlSheet.TabColor = XLColor.FromArgb(tabColor.R, tabColor.G, tabColor.B);

            foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
            {
                // Skip blank cells that carry no style
                if (cell.Value is BlankValue && !cell.HasFormula && cell.StyleId == StyleId.Default)
                    continue;
                if (!IsValidWorksheetRow(row) || !IsValidWorksheetColumn(col))
                    continue;

                var xlCell = xlSheet.Cell((int)row, (int)col);

                if (cell.HasFormula)
                {
                    var formula = XlsxClosedXmlCellMapper.NormalizeFormulaText(cell.FormulaText!);
                    if (cell.ArrayMode == FormulaArrayMode.Dynamic &&
                        sheet.TryGetSpillExtent(new CellAddress(sheet.Id, row, col), out var spillRows, out var spillCols) &&
                        (long)spillRows * spillCols > 1)
                    {
                        // A dynamic array that spills is written as an array formula over its spill range, so it
                        // reloads as Dynamic (spilling) instead of being mis-detected as a legacy
                        // implicit-intersection (plain) formula.
                        xlSheet.Range((int)row, (int)col, (int)(row + spillRows - 1), (int)(col + spillCols - 1))
                            .FormulaArrayA1 = formula;
                    }
                    else
                    {
                        xlCell.FormulaA1 = formula;
                    }
                }
                else if (cell.Value is not BlankValue)
                {
                    xlCell.Value = XlsxClosedXmlCellMapper.MapValueInverse(cell.Value);
                }

                if (cell.StyleId != StyleId.Default)
                {
                    var style = GetCachedStyle(workbook, styleCache, cell.StyleId);
                    if (!style.Equals(CellStyle.Default))
                        ApplyStyleFast(xlCell, style, cell.StyleId, xlStyleValueCache);
                }
            }

            ApplyStyleOnlySeedCells(workbook, styleCache, xlStyleValueCache, xlSheet, sheet);

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

            foreach (var rowNum in sheet.FilterHiddenRows)
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
                    sheet.HyperlinkMetadata.TryGetValue(address, out var metadata);
                    var xlCell = xlSheet.Cell((int)address.Row, (int)address.Col);
                    xlCell.SetHyperlink(CreateXlsxHyperlink(target, metadata));

                    // SetHyperlink replaces the cell font with ClosedXML's Hyperlink style; restore the
                    // modelled font so explicit colours and underline choices round-trip.
                    var styledCell = sheet.GetCell(address);
                    if (styledCell is not null && styledCell.StyleId != StyleId.Default)
                    {
                        var style = GetCachedStyle(workbook, styleCache, styledCell.StyleId);
                        if (!style.Equals(CellStyle.Default))
                            XlsxClosedXmlCellMapper.ApplyHyperlinkFontOverride(xlCell, style);
                    }
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

            var pageOrientation = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PageOrientation, WorksheetPageOrientation.Portrait);
            var paperSize = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PaperSize, WorksheetPaperSize.A4);
            var pageMargins = XlsxWorksheetValueSanitizer.ValidPageMarginsOrDefault(sheet.PageMargins, WorksheetPageMargins.Narrow);
            var headerMargin = XlsxWorksheetValueSanitizer.NonNegativeFiniteOrDefault(sheet.HeaderMargin, 0.3);
            var footerMargin = XlsxWorksheetValueSanitizer.NonNegativeFiniteOrDefault(sheet.FooterMargin, 0.3);
            var scaleToFit = XlsxWorksheetValueSanitizer.ValidScaleToFitOrDefault(sheet.ScaleToFit, WorksheetScaleToFit.Default);
            var pageOrder = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PageOrder, WorksheetPageOrder.DownThenOver);
            var printErrorValue = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PrintErrorValue, WorksheetPrintErrorValue.Displayed);
            var printComments = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PrintComments, WorksheetPrintComments.None);

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
                xlSheet.PageSetup.VerticalDpi = sheet.PrintQualityVerticalDpi is { } verticalDpi && verticalDpi > 0
                    ? verticalDpi
                    : printQualityDpi;
            }
            else if (sheet.PrintQualityVerticalDpi is { } verticalDpi && verticalDpi > 0)
            {
                xlSheet.PageSetup.VerticalDpi = verticalDpi;
            }

            xlSheet.PageSetup.PrintErrorValue = XlsxWorksheetPageSetupMapper.ToPrintErrorValue(printErrorValue);
            xlSheet.PageSetup.ShowComments = XlsxWorksheetPageSetupMapper.ToPrintComments(printComments);
            xlSheet.PageSetup.DifferentFirstPageOnHF = sheet.DifferentFirstPageHeaderFooter;
            xlSheet.PageSetup.DifferentOddEvenPagesOnHF = sheet.DifferentOddEvenHeaderFooter;
            xlSheet.PageSetup.ScaleHFWithDocument = sheet.HeaderFooterScaleWithDocument;
            xlSheet.PageSetup.AlignHFWithMargins = sheet.HeaderFooterAlignWithMargins;
            XlsxWorksheetPageSetupMapper.SetHeaderFooter(
                xlSheet.PageSetup.Header,
                sheet.PageHeader,
                sheet.FirstPageHeader,
                sheet.EvenPageHeader,
                sheet.DifferentFirstPageHeaderFooter,
                sheet.DifferentOddEvenHeaderFooter);
            XlsxWorksheetPageSetupMapper.SetHeaderFooter(
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
            XlsxConditionalFormatClosedXmlMapper.Save(sheet, xlSheet);

            // Save data validation rules back to XLSX. Sheets with native validation metadata are
            // emitted in the worksheet XML post-processing pass to avoid duplicate ClosedXML work.
            if (!XlsxDataValidationNativeMetadataMapper.HasNativeMetadata(sheet))
            {
                XlsxDataValidationClosedXmlMapper.Save(sheet, xlSheet, warnings);
            }

            // Save merged regions
            foreach (var region in sheet.MergedRegions)
            {
                try
                {
                    var rangeStr = $"{CellAddress.NumberToColumnName(region.Start.Col)}{region.Start.Row}" +
                                   $":{CellAddress.NumberToColumnName(region.End.Col)}{region.End.Row}";
                    xlSheet.Range(rangeStr).Merge();
                }
                catch (Exception ex)
                {
                    var regionDesc = $"{CellAddress.NumberToColumnName(region.Start.Col)}{region.Start.Row}:{CellAddress.NumberToColumnName(region.End.Col)}{region.End.Row}";
                    System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Skipping merged-region save for sheet '{sheet.Name}' region '{regionDesc}': {ex.Message}");
                    warnings?.Add($"[merged-region] Merged region '{regionDesc}' on sheet '{sheet.Name}' could not be saved and was skipped.");
                }
            }
        }

        // Save named ranges (per-item isolation is inside the mapper)
        XlsxNamedRangeMapper.Save(workbook, xlWorkbook, warnings);

        if (CanSavePackageInPlace(stream))
        {
            stream.Position = 0;
            stream.SetLength(0);
            xlWorkbook.SaveAs(stream);
            ApplyPackagePostProcessing(
                workbook,
                stream,
                currentModelFingerprint,
                removeSourceCalcChain: patchDiagnostics.InvalidatesCalcChain);
            stream.Position = stream.Length;
            return;
        }

        using var packageStream = new MemoryStream();
        xlWorkbook.SaveAs(packageStream);
        ApplyPackagePostProcessing(
            workbook,
            packageStream,
            currentModelFingerprint,
            removeSourceCalcChain: patchDiagnostics.InvalidatesCalcChain);
        packageStream.Position = 0;
        packageStream.CopyTo(stream);
    }

    private static bool CanSavePackageInPlace(Stream stream) =>
        stream.CanRead && stream.CanWrite && stream.CanSeek;

    private static CellStyle GetCachedStyle(
        Workbook workbook,
        Dictionary<StyleId, CellStyle> styleCache,
        StyleId styleId)
    {
        if (!styleCache.TryGetValue(styleId, out var style))
        {
            style = workbook.GetStyle(styleId);
            styleCache.Add(styleId, style);
        }

        return style;
    }

    private static void ApplyStyleOnlySeedCells(
        Workbook workbook,
        Dictionary<StyleId, CellStyle> styleCache,
        Dictionary<StyleId, object>? xlStyleValueCache,
        IXLWorksheet xlSheet,
        Sheet sheet)
    {
        if (!sheet.HasStyleOnlyCells)
            return;

        foreach (var seed in XlsxStyleOnlyCellWriter.GetSeedCells(sheet))
        {
            var style = GetCachedStyle(workbook, styleCache, seed.StyleId);
            if (style.Equals(CellStyle.Default))
                continue;

            var xlCell = xlSheet.Cell((int)seed.Row, (int)seed.Col);
            ApplyStyleFast(xlCell, style, seed.StyleId, xlStyleValueCache);
        }
    }

    /// <summary>
    /// Applies <paramref name="style"/> to <paramref name="xlCell"/> using a fast path when the
    /// ClosedXML <c>XLStyleValue</c> for this <paramref name="styleId"/> has been cached from a
    /// previous application: calls <c>SetStyle(XLStyleValue, propagate: false)</c> in one
    /// operation instead of ~15 individual property setters.  Falls back to the full
    /// <see cref="XlsxClosedXmlCellMapper.ApplyStyle"/> call on the first encounter and captures
    /// the resulting <c>XLStyleValue</c> for subsequent cells.
    /// </summary>
    private static void ApplyStyleFast(
        IXLCell xlCell,
        CellStyle style,
        StyleId styleId,
        Dictionary<StyleId, object>? xlStyleValueCache)
    {
        if (xlStyleValueCache is not null)
        {
            if (xlStyleValueCache.TryGetValue(styleId, out var cachedXlStyleValue))
            {
                // Fast path: replay cached XLStyleValue in a single SetStyle call.
                XlCellSetStyleValueAction!(xlCell, cachedXlStyleValue);
                return;
            }
        }

        // Slow path (first cell for this StyleId): apply via individual setters and, if the
        // fast-path delegates are available, capture the resulting XLStyleValue for reuse.
        XlsxClosedXmlCellMapper.ApplyStyle(xlCell, style);

        if (xlStyleValueCache is not null)
        {
            var captured = XlCellStyleValueAccessor!(xlCell);
            if (captured is not null)
                xlStyleValueCache[styleId] = captured;
        }
    }
}
