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
                XlsxNamedRangeMapper.SaveToPackage(workbook, stream, warnings);
                sourcePackage.RestoreWorkbookDefinedNames(stream);
                LastSaveDiagnostics = patchDiagnostics;
                return;
            }
        }

        LastSaveDiagnostics = sourcePackage is null
            ? XlsxSaveDiagnostics.FullSave("no_source_package")
            : patchDiagnostics;

        using var xlWorkbook = new XLWorkbook();
        // F4: put ClosedXML into the workbook's date system so it writes the correct on-disk serial
        // for date cells (1904-relative when date1904="1"). Without this, ClosedXML always emits a
        // 1900-epoch serial while post-processing stamps date1904="1", so a reload (ClosedXML honors
        // date1904 on read) shifts every date by the 1462-day epoch difference. MapValueInverse hands
        // ClosedXML a true calendar DateTime, so ClosedXML now serializes it 1904-consistently.
        xlWorkbook.Use1904DateSystem = workbook.Uses1904DateSystem;
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
                else if (cell.Value is TextValue &&
                         sheet.RichTextRuns.TryGetValue(new CellAddress(sheet.Id, row, col), out var richRuns) &&
                         richRuns is { Count: > 0 })
                {
                    // Full-save rich-text path: write runs via ClosedXML's IXLRichText API so
                    // that per-run formatting (bold, subscript, color, …) is preserved in the
                    // shared-string table.  The patch-save path is mutually exclusive (it exits
                    // early at line 80-84 before this ClosedXML block is ever reached).
                    ApplyRichTextRuns(xlCell, richRuns);
                }
                else if (cell.Value is not BlankValue)
                {
                    xlCell.Value = XlsxClosedXmlCellMapper.MapValueInverse(cell.Value, workbook.Uses1904DateSystem);
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
                    var xlComment = xlSheet.Cell((int)address.Row, (int)address.Col)
                        .CreateComment();
                    // GAP 1: preserve the note author stored in CommentAuthors; fall back to
                    // empty string so ClosedXML doesn't silently default to the OS username.
                    if (sheet.CommentAuthors.TryGetValue(address, out var author) &&
                        !string.IsNullOrEmpty(author))
                    {
                        xlComment.Author = author;
                    }
                    else
                    {
                        xlComment.Author = string.Empty;
                    }
                    xlComment.AddText(commentText);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Skipping comment save for sheet '{sheet.Name}' cell '{address}': {ex.Message}");
                    warnings?.Add($"[comment] Comment at '{sheet.Name}!{address}' could not be saved and was skipped.");
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
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Skipping hyperlink save for sheet '{sheet.Name}' cell '{address}' target '{target}': {ex.Message}");
                    warnings?.Add($"[hyperlink] Hyperlink at '{sheet.Name}!{address}' to '{target}' could not be saved and was skipped.");
                }
            }

            var frozenRows = ValidFrozenRowsOrZero(sheet.FrozenRows);
            var frozenCols = ValidFrozenColumnsOrZero(sheet.FrozenCols);
            if (frozenRows > 0 || frozenCols > 0)
                xlSheet.SheetView.Freeze((int)frozenRows, (int)frozenCols);

            if (sheet.PrintAreas.Count > 0)
            {
                xlSheet.PageSetup.PrintAreas.Clear();
                foreach (var printArea in sheet.PrintAreas)
                {
                    xlSheet.PageSetup.PrintAreas.Add(
                        (int)printArea.Start.Row,
                        (int)printArea.Start.Col,
                        (int)printArea.End.Row,
                        (int)printArea.End.Col);
                }
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
            // Emit the raw OOXML paper-size code to preserve non-Letter/A4/Legal sizes.
            // When PaperSizeCode is a known OOXML code (Letter/A4/Legal/A3/etc.), the enum
            // is authoritative so dialog changes always take effect.  When PaperSizeCode is
            // an exotic/unknown code not in the enum map, preserve it verbatim.
            var paperSizeCode = sheet.PaperSizeCode > 0
                                && !PaperSizeCodes.TryGetEnum(sheet.PaperSizeCode, out _)
                ? sheet.PaperSizeCode                   // exotic code: preserve as-is
                : PaperSizeCodes.GetCode(paperSize);    // known code: derive from enum
            xlSheet.PageSetup.PaperSize = (XLPaperSize)paperSizeCode;
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
            sourcePackage?.RestoreWorkbookDefinedNames(stream);
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
        sourcePackage?.RestoreWorkbookDefinedNames(packageStream);
        packageStream.Position = 0;
        packageStream.CopyTo(stream);
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);
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

    /// <summary>
    /// Writes per-run rich-text formatting on <paramref name="xlCell"/> via ClosedXML's
    /// <see cref="IXLRichText"/> API.  Used by the full-save (ClosedXML) path only — the
    /// patch-save path exits early before this code is reached, so there is no double-apply risk.
    /// </summary>
    /// <remarks>
    /// ClosedXML serialises rich-text cells as shared strings (<c>t="s"</c>).  On reload
    /// <see cref="XlsxRichRunLoader"/> reads both inline-string and shared-string sources, so the
    /// round-trip is fully transparent to the rest of the stack.
    ///
    /// Limitation: ClosedXML materialises default font properties (Calibri 11pt black) into every
    /// run even when the model run has <c>null</c> (inherit-from-cell).  After a full-save the
    /// reloaded run will carry explicit FontName/FontSize/FontColor values instead of <c>null</c>.
    /// Theme and indexed colors ARE round-tripped faithfully via
    /// <c>XLColor.FromTheme</c>/<c>XLColor.FromIndex</c>.
    /// </remarks>
    private static void ApplyRichTextRuns(
        IXLCell xlCell,
        IReadOnlyList<CellTextRun> runs)
    {
        var richText = xlCell.CreateRichText();

        foreach (var run in runs)
        {
            var xlRun = richText.AddText(run.Text);

            // Only set properties that are explicitly specified in the model run (non-null).
            // Null means "inherit from cell style" — ClosedXML will use its workbook default
            // when unset, which is the closest approximation available.
            if (run.Bold is { } bold)
                xlRun.Bold = bold;

            if (run.Italic is { } italic)
                xlRun.Italic = italic;

            if (run.Underline is { } underline)
                xlRun.Underline = underline
                    ? XLFontUnderlineValues.Single
                    : XLFontUnderlineValues.None;

            if (run.Strikethrough is { } strike)
                xlRun.Strikethrough = strike;

            if (run.FontName is { } fontName)
                xlRun.FontName = fontName;

            if (run.FontSize is { } fontSize)
                xlRun.FontSize = fontSize;

            if (run.FontColor is { } runColor)
                xlRun.FontColor = MapRunColorToXLColor(runColor);

            xlRun.VerticalAlignment = run.VertAlign switch
            {
                CellTextRunVertAlign.Superscript => XLFontVerticalTextAlignmentValues.Superscript,
                CellTextRunVertAlign.Subscript   => XLFontVerticalTextAlignmentValues.Subscript,
                _                                => XLFontVerticalTextAlignmentValues.Baseline,
            };
        }
    }

    /// <summary>
    /// Converts a <see cref="CellRunColor"/> to an <see cref="XLColor"/>, preserving theme
    /// and indexed references so they survive the round-trip without being flattened to RGB.
    /// </summary>
    /// <remarks>
    /// ClosedXML cannot express <c>&lt;color auto="1"/&gt;</c>.  For <see cref="CellRunColorKind.Auto"/>,
    /// we intentionally use <c>FromArgb(0,0,0,0)</c> (fully-transparent black, <c>rgb="00000000"</c>)
    /// as a sentinel value — it is an impossible color in real OOXML (alpha=0 is never written by Excel)
    /// so a post-processing pass in <see cref="ApplyPackagePostProcessing"/> can safely replace every
    /// <c>rgb="00000000"</c> in the shared-strings part with <c>auto="1"</c>, restoring the correct
    /// round-trip semantics without ambiguity.
    /// </remarks>
    private static XLColor MapRunColorToXLColor(CellRunColor color) => color.Kind switch
    {
        CellRunColorKind.Theme =>
            color.Tint is { } tint && Math.Abs(tint) > 0.000001
                ? XLColor.FromTheme((XLThemeColor)color.ThemeIndex, tint)
                : XLColor.FromTheme((XLThemeColor)color.ThemeIndex),
        CellRunColorKind.Indexed =>
            XLColor.FromIndex(color.IndexedIndex),
        CellRunColorKind.Auto =>
            // BX1 sentinel: transparent black is never emitted by Excel for a real color,
            // so the post-processing pass can safely rewrite it to <color auto="1"/>.
            XLColor.FromArgb(0, 0, 0, 0),
        _ => // Rgb
            XLColor.FromArgb(255, color.Rgb.R, color.Rgb.G, color.Rgb.B),
    };
}
