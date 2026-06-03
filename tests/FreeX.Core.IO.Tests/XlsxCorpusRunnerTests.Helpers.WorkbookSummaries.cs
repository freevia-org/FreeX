using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace FreeX.Core.IO.Tests;

public partial class XlsxCorpusRunnerTests
{
    private static WorkbookSummary CaptureSummary(Workbook workbook) =>
        new(
            workbook.SheetCount,
            workbook.NamedRanges
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => CaptureNamedRangeSummary(workbook, pair.Key, pair.Value))
                .ToArray(),
            workbook.NamedRanges.Count,
            workbook.IsStructureProtected,
            ToLegacyPasswordHash(workbook.StructureProtectionPassword),
            workbook.PivotCaches.Select(CapturePivotCacheSummary).ToArray(),
            workbook.PivotCaches.Count,
            workbook.PivotCaches.Sum(cache => cache.Fields.Count),
            workbook.PivotTableStyles
                .OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CapturePivotTableStyleSummary)
                .ToArray(),
            workbook.PivotTableStyles.Count,
            workbook.PivotTableStyles.Sum(style => style.Elements.Count),
            CapturePivotNumberFormatCatalogSummary(workbook),
            workbook.CustomViews
                .OrderBy(view => view.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CaptureCustomViewSummary)
                .ToArray(),
            workbook.CustomViews.Count,
            CaptureWorkbookMetadataSummary(workbook),
            CaptureWorkbookCalculationSummary(workbook),
            CaptureWorkbookThemeSummary(workbook.Theme),
            workbook.Sheets.Select(sheet => CaptureSheetSummary(workbook, sheet)).ToArray());

    private static WorkbookMetadataSummary CaptureWorkbookMetadataSummary(Workbook workbook) =>
        new(
            workbook.Slicers
                .OrderBy(slicer => slicer.PackagePart, StringComparer.OrdinalIgnoreCase)
                .Select(slicer => new SlicerSummary(
                    slicer.Name,
                    slicer.Caption ?? "",
                    slicer.CacheName,
                    slicer.SourcePivotTableName ?? "",
                    slicer.SourceFieldName ?? "",
                    slicer.StyleName ?? "",
                    slicer.SelectedItems.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                    slicer.PackagePart))
                .ToArray(),
            workbook.Timelines
                .OrderBy(timeline => timeline.PackagePart, StringComparer.OrdinalIgnoreCase)
                .Select(timeline => new TimelineSummary(
                    timeline.Name,
                    timeline.Caption ?? "",
                    timeline.CacheName,
                    timeline.SourcePivotTableName ?? "",
                    timeline.SourceFieldName ?? "",
                    timeline.StyleName ?? "",
                    timeline.StartDate ?? "",
                    timeline.EndDate ?? "",
                    timeline.SelectedStartDate ?? "",
                    timeline.SelectedEndDate ?? "",
                    timeline.PackagePart))
                .ToArray(),
            workbook.ExternalLinks
                .OrderBy(link => link.PackagePart, StringComparer.OrdinalIgnoreCase)
                .Select(link => new ExternalLinkSummary(
                    link.PackagePart,
                    link.TargetUri ?? "",
                    link.TargetMode ?? ""))
                .ToArray(),
            workbook.WatchedCells
                .Select(address => new WatchedCellSummary(
                    workbook.GetSheet(address.Sheet)?.Name ?? "",
                    address.Row,
                    address.Col))
                .OrderBy(cell => cell.SheetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(cell => cell.Row)
                .ThenBy(cell => cell.Column)
                .ToArray(),
            workbook.Scenarios
                .OrderBy(scenario => scenario.Name, StringComparer.OrdinalIgnoreCase)
                .Select(scenario => new ScenarioSummary(
                    scenario.Name,
                    scenario.ChangingCells
                        .Select(change => new ScenarioCellSummary(
                            workbook.GetSheet(change.Address.Sheet)?.Name ?? "",
                            change.Address.Row,
                            change.Address.Col,
                            CaptureScalarValueSummary(change.Value)))
                        .OrderBy(cell => cell.SheetName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(cell => cell.Row)
                        .ThenBy(cell => cell.Column)
                        .ToArray()))
                .ToArray());

    private static WorkbookCalculationSummary CaptureWorkbookCalculationSummary(Workbook workbook) =>
        new(
            workbook.CalculationMode,
            workbook.FullCalculationOnLoad,
            workbook.ForceFullCalculation,
            workbook.IterativeCalculation,
            workbook.MaxCalculationIterations,
            workbook.MaxCalculationChange);

    private static IReadOnlyList<NumberFormatCatalogSummary> CapturePivotNumberFormatCatalogSummary(Workbook workbook)
    {
        var referencedIds = workbook.PivotCaches
            .SelectMany(cache => cache.Fields)
            .Select(field => field.NumberFormatId)
            .Concat(workbook.Sheets
                .SelectMany(sheet => sheet.PivotTables)
                .SelectMany(pivot => pivot.DataFields)
                .Select(field => field.NumberFormatId))
            .Where(id => id is >= 164)
            .Select(id => id!.Value)
            .ToHashSet();

        return workbook.NumberFormatCatalog
            .Where(pair => referencedIds.Contains(pair.Key))
            .OrderBy(pair => pair.Key)
            .Select(pair => new NumberFormatCatalogSummary(pair.Key, pair.Value))
            .ToArray();
    }

    private static WorkbookThemeSummary CaptureWorkbookThemeSummary(WorkbookTheme theme) =>
        new(
            theme.Name,
            theme.MajorFontName,
            theme.MinorFontName,
            theme.EffectsName,
            Enum.GetValues<WorkbookThemeColorSlot>()
                .Select(slot => new ThemeColorSummary(slot, ToColorSummary(theme.GetColor(slot))))
                .ToArray());

    private static string ToColorSummary(CellColor color) =>
        FormattableString.Invariant($"{color.R:X2}{color.G:X2}{color.B:X2}");

    private static string ToLegacyPasswordHash(string? passwordOrHash)
    {
        if (string.IsNullOrWhiteSpace(passwordOrHash))
            return "";
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

    private static SheetSummary CaptureSheetSummary(Workbook workbook, Sheet sheet) =>
        new(
            sheet.Name,
            sheet.EnumerateCells()
                .OrderBy(item => item.Address.Row)
                .ThenBy(item => item.Address.Col)
                .Select(item => CaptureCellSummary(workbook, item.Address, item.Cell))
                .ToArray(),
            sheet.CellCount,
            sheet.EnumerateCells().Count(item => item.Cell.HasFormula),
            sheet.MergedRegions.Count,
            sheet.DataValidations.Select(CaptureDataValidationSummary).ToArray(),
            sheet.DataValidations.Count,
            sheet.ConditionalFormats
                .OrderBy(format => format.AppliesTo.Start.Row)
                .ThenBy(format => format.AppliesTo.Start.Col)
                .ThenBy(format => format.AppliesTo.End.Row)
                .ThenBy(format => format.AppliesTo.End.Col)
                .ThenBy(format => format.Priority)
                .ThenBy(format => format.RuleType)
                .Select(CaptureConditionalFormatSummary)
                .ToArray(),
            sheet.ConditionalFormats.Count,
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.ColorScale),
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.DataBar),
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.IconSet),
            sheet.Comments
                .OrderBy(pair => pair.Key.Row)
                .ThenBy(pair => pair.Key.Col)
                .Select(pair => new CommentSummary(pair.Key.Row, pair.Key.Col, pair.Value))
                .ToArray(),
            sheet.Comments.Count,
            sheet.Hyperlinks
                .OrderBy(pair => pair.Key.Row)
                .ThenBy(pair => pair.Key.Col)
                .Select(pair => CaptureHyperlinkSummary(sheet, pair))
                .ToArray(),
            sheet.Hyperlinks.Count,
            sheet.Charts.Select(CaptureChartSummary).ToArray(),
            sheet.Charts.Count,
            sheet.PivotTables.Select(CapturePivotTableSummary).ToArray(),
            sheet.PivotTables.Count,
            sheet.PivotTables.Sum(pivot => pivot.RowFields.Count + pivot.ColumnFields.Count + pivot.PageFields.Count + pivot.DataFields.Count),
            sheet.StructuredTables.Select(CaptureStructuredTableSummary).ToArray(),
            sheet.StructuredTables.Count,
            sheet.StructuredTables.Sum(table => table.Columns.Count),
            sheet.Sparklines.Select(sparkline => new SparklineSummary(sparkline.Kind, ToRangeSummary(sparkline.DataRange), sparkline.Location.Row, sparkline.Location.Col)).ToArray(),
            sheet.Sparklines.Count,
            sheet.TextBoxes.Select(CaptureTextBoxSummary).ToArray(),
            sheet.TextBoxes.Count,
            sheet.DrawingShapes.Select(CaptureDrawingShapeSummary).ToArray(),
            sheet.DrawingShapes.Count,
            sheet.Pictures.Select(CapturePictureSummary).ToArray(),
            sheet.Pictures.Count,
            CaptureBackgroundImageSummary(sheet.BackgroundImage),
            sheet.BackgroundImage is not null,
            sheet.IsProtected,
            ToLegacyPasswordHash(sheet.ProtectionPassword),
            sheet.AllowEditRanges
                .OrderBy(range => range.Start.Row)
                .ThenBy(range => range.Start.Col)
                .ThenBy(range => range.End.Row)
                .ThenBy(range => range.End.Col)
                .Select(ToRangeSummary)
                .ToArray(),
            sheet.AllowEditRanges.Count,
            sheet.PrintArea.HasValue ? ToRangeSummary(sheet.PrintArea.Value) : null,
            sheet.PrintArea is not null,
            sheet.PrintTitleRows.HasValue ? ToRepeatRangeSummary(sheet.PrintTitleRows.Value) : null,
            sheet.PrintTitleRows is not null,
            sheet.PrintTitleColumns.HasValue ? ToRepeatRangeSummary(sheet.PrintTitleColumns.Value) : null,
            sheet.PrintTitleColumns is not null,
            sheet.PageOrientation,
            sheet.PaperSize,
            sheet.PageMargins,
            sheet.HeaderMargin,
            sheet.FooterMargin,
            sheet.ScaleToFit,
            sheet.PrintGridlines,
            sheet.PrintHeadings,
            CaptureHeaderFooterSummary(sheet.PageHeader),
            !sheet.PageHeader.Equals(new WorksheetHeaderFooter("", "", "")),
            CaptureHeaderFooterSummary(sheet.PageFooter),
            !sheet.PageFooter.Equals(new WorksheetHeaderFooter("", "", "")),
            sheet.DifferentFirstPageHeaderFooter ? CaptureHeaderFooterSummary(sheet.FirstPageHeader) : HeaderFooterSummary.Empty,
            sheet.DifferentFirstPageHeaderFooter ? CaptureHeaderFooterSummary(sheet.FirstPageFooter) : HeaderFooterSummary.Empty,
            sheet.DifferentOddEvenHeaderFooter ? CaptureHeaderFooterSummary(sheet.EvenPageHeader) : HeaderFooterSummary.Empty,
            sheet.DifferentOddEvenHeaderFooter ? CaptureHeaderFooterSummary(sheet.EvenPageFooter) : HeaderFooterSummary.Empty,
            sheet.DifferentFirstPageHeaderFooter,
            sheet.DifferentOddEvenHeaderFooter,
            sheet.HeaderFooterScaleWithDocument,
            sheet.HeaderFooterAlignWithMargins,
            CaptureHeaderFooterPictureSetSummary(sheet.PageHeaderPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.PageFooterPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.FirstPageHeaderPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.FirstPageFooterPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.EvenPageHeaderPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.EvenPageFooterPictures),
            sheet.CenterHorizontallyOnPage,
            sheet.CenterVerticallyOnPage,
            sheet.PageOrder,
            sheet.FirstPageNumber,
            sheet.PrintBlackAndWhite,
            sheet.PrintDraftQuality,
            sheet.PrintQualityDpi,
            sheet.PrintErrorValue,
            sheet.PrintComments,
            sheet.DefaultColumnWidth,
            sheet.DefaultRowHeight,
            sheet.ColumnWidths
                .OrderBy(pair => pair.Key)
                .Where(pair => Math.Abs(pair.Value - sheet.DefaultColumnWidth) >= 0.01)
                .Select(pair => new DimensionSummary(pair.Key, Math.Round(pair.Value, 2)))
                .ToArray(),
            sheet.RowHeights
                .OrderBy(pair => pair.Key)
                .Where(pair => Math.Abs(pair.Value - sheet.DefaultRowHeight) >= 0.01)
                .Select(pair => new DimensionSummary(pair.Key, Math.Round(pair.Value, 2)))
                .ToArray(),
            sheet.RowPageBreaks.OrderBy(row => row).ToArray(),
            sheet.RowPageBreaks.Count,
            sheet.ColumnPageBreaks.OrderBy(column => column).ToArray(),
            sheet.ColumnPageBreaks.Count,
            sheet.FrozenRows,
            sheet.FrozenCols,
            sheet.SplitRow,
            sheet.SplitColumn,
            sheet.ViewMode,
            sheet.ViewTopRow,
            sheet.ViewLeftCol,
            sheet.ActiveRow,
            sheet.ActiveCol,
            sheet.ShowGridlines,
            sheet.ShowHeadings,
            sheet.ShowRulers,
            sheet.ZoomPercent,
            sheet.ShowFormulas,
            sheet.FullCalculationOnLoad,
            CapturePhoneticSummary(sheet.PhoneticProperties),
            sheet.IsHidden,
            sheet.IsVeryHidden,
            sheet.CodeName ?? "",
            sheet.TabColor is null ? "" : ToColorSummary(sheet.TabColor.Value),
            sheet.CustomProperties
                .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .Select(property => new WorksheetCustomPropertySummary(property.Name, property.Id))
                .ToArray(),
            CaptureEffectiveHiddenRows(sheet),
            CaptureEffectiveHiddenRows(sheet).Length,
            [],
            0,
            sheet.HiddenCols.OrderBy(column => column).ToArray(),
            sheet.HiddenCols.Count,
            sheet.RowOutlineLevels
                .OrderBy(pair => pair.Key)
                .Select(pair => new OutlineLevelSummary(pair.Key, pair.Value))
                .ToArray(),
            sheet.RowOutlineLevels.Count,
            sheet.ColOutlineLevels
                .OrderBy(pair => pair.Key)
                .Select(pair => new OutlineLevelSummary(pair.Key, pair.Value))
                .ToArray(),
            sheet.ColOutlineLevels.Count,
            sheet.GroupHiddenRows.OrderBy(row => row).ToArray(),
            sheet.GroupHiddenRows.Count,
            sheet.GroupHiddenCols.OrderBy(column => column).ToArray(),
            sheet.GroupHiddenCols.Count,
            sheet.GetStyleOnlyEntries()
                .OrderBy(entry => entry.Key.Row)
                .ThenBy(entry => entry.Key.Col)
                .Select(entry => new StyleOnlyCellSummary(
                    entry.Key.Row,
                    entry.Key.Col,
                    CaptureStyleSummary(workbook.GetStyle(entry.StyleId))))
                .ToArray(),
            sheet.GetStyleOnlyEntries().Count());

    private static uint[] CaptureEffectiveHiddenRows(Sheet sheet) =>
        sheet.HiddenRows
            .Concat(sheet.FilterHiddenRows)
            .Distinct()
            .OrderBy(row => row)
            .ToArray();

    private static PhoneticSummary? CapturePhoneticSummary(WorksheetPhoneticProperties? properties) =>
        properties is null
            ? null
            : new PhoneticSummary(
                properties.FontId ?? "",
                properties.Type ?? "",
                properties.Alignment ?? "");

    private static BackgroundImageSummary? CaptureBackgroundImageSummary(WorksheetBackgroundImage? background) =>
        background is null
            ? null
            : new BackgroundImageSummary(
                background.ContentType,
                background.FileName ?? "",
                background.ImageBytes.Length);

    private static HyperlinkSummary CaptureHyperlinkSummary(Sheet sheet, KeyValuePair<CellAddress, string> pair)
    {
        sheet.HyperlinkMetadata.TryGetValue(pair.Key, out var metadata);
        metadata ??= new HyperlinkMetadata();
        return new HyperlinkSummary(
            pair.Key.Row,
            pair.Key.Col,
            pair.Value,
            metadata.LinkType,
            metadata.ScreenTip,
            metadata.Bookmark);
    }

    private static NamedRangeSummary CaptureNamedRangeSummary(Workbook workbook, string name, GridRange range)
    {
        var metadata = workbook.TryGetNamedRangeMetadata(name, out var savedMetadata)
            ? savedMetadata
            : NamedRangeMetadata.WorkbookScope;

        return new NamedRangeSummary(
            name,
            metadata.Scope,
            metadata.Comment,
            ToRangeSummary(range));
    }

    private static IReadOnlyList<FormulaCellSummary> CaptureFormulaCellSummaries(Workbook workbook) =>
        workbook.Sheets
            .SelectMany(sheet => sheet.EnumerateCells()
                .Where(item => item.Cell.HasFormula)
                .OrderBy(item => item.Address.Row)
                .ThenBy(item => item.Address.Col)
                .Select(item => new FormulaCellSummary(
                    sheet.Name,
                    item.Address.Row,
                    item.Address.Col,
                    item.Cell.FormulaText ?? "",
                    CaptureScalarValueSummary(item.Cell.Value))))
            .ToArray();

    private static CellSummary CaptureCellSummary(Workbook workbook, CellAddress address, Cell cell) =>
        new(
            address.Row,
            address.Col,
            cell.HasFormula ? new ScalarValueSummary("FormulaCachedValue", "") : CaptureScalarValueSummary(cell.Value),
            cell.FormulaText ?? "",
            cell.IgnoreFormulaError,
            CaptureStyleSummary(workbook.GetStyle(cell.StyleId)));

    private static ScalarValueSummary CaptureScalarValueSummary(ScalarValue value) =>
        value switch
        {
            BlankValue => new ScalarValueSummary("Blank", ""),
            NumberValue number => new ScalarValueSummary("Number", number.Value.ToString("R", CultureInfo.InvariantCulture)),
            BoolValue boolean => new ScalarValueSummary("Boolean", boolean.Value ? "TRUE" : "FALSE"),
            TextValue text => new ScalarValueSummary("Text", text.Value),
            DateTimeValue dateTime => new ScalarValueSummary("DateTime", dateTime.Value.ToString("R", CultureInfo.InvariantCulture)),
            ErrorValue error => new ScalarValueSummary("Error", error.Code),
            _ => new ScalarValueSummary(value.GetType().Name, value.ToString() ?? "")
        };

    private static CustomViewSummary CaptureCustomViewSummary(WorkbookCustomView view) =>
        new(
            view.Name,
            view.IncludePrintSettings,
            view.IncludeHiddenRowsColumnsAndFilterSettings,
            view.Sheets
                .OrderBy(sheet => sheet.SheetName, StringComparer.OrdinalIgnoreCase)
                .Select(sheet => new CustomViewSheetSummary(
                    sheet.SheetName,
                    sheet.ViewMode,
                    sheet.FrozenRows,
                    sheet.FrozenCols,
                    sheet.SplitRow,
                    sheet.SplitColumn,
                    sheet.ShowGridlines,
                    sheet.ShowHeadings,
                    sheet.ShowRulers,
                    sheet.ZoomPercent,
                    sheet.ShowFormulas))
                .ToArray());

}
