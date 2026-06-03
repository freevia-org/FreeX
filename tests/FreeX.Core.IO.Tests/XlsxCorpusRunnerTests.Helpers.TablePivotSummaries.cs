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
    private static PivotCacheSummary CapturePivotCacheSummary(PivotCacheModel cache) =>
        new(
            cache.CacheId,
            cache.SourceType,
            cache.SourceSheetName ?? "",
            cache.SourceReference ?? "",
            cache.SourceTableName ?? "",
            cache.ConnectionId,
            cache.IsOlap,
            cache.RefreshOnLoad,
            cache.SaveData,
            cache.EnableRefresh,
            cache.PreserveSourceSortFilter,
            cache.MissingItemsLimit,
            cache.RecordCount,
            cache.CreatedVersion,
            cache.MinRefreshableVersion,
            cache.RefreshedVersion,
            cache.RefreshedBy ?? "",
            cache.RefreshedDateIso ?? "",
            cache.Fields
                .Select(field => new PivotCacheFieldSummary(
                    field.Name,
                    field.NumberFormatId,
                    field.SharedItemCount,
                    field.ContainsBlank,
                    field.ContainsString,
                    field.ContainsNumber,
                    field.ContainsDate,
                    field.ContainsMixedTypes,
                    field.ContainsSemiMixedTypes,
                    field.ContainsNonDate,
                    field.ContainsInteger,
                    field.ContainsLongText,
                    field.MinValue,
                    field.MaxValue,
                    field.MinDate ?? "",
                    field.MaxDate ?? "",
                    field.SharedItems?.ToArray() ?? [],
                    field.Formula ?? "",
                    field.IsDatabaseField))
                .ToArray());

    private static StructuredTableSummary CaptureStructuredTableSummary(StructuredTableModel table) =>
        new(
            table.Name,
            table.DisplayName,
            table.StyleName ?? "",
            table.HasAutoFilter,
            table.TotalsRowShown,
            table.ShowFirstColumn,
            table.ShowLastColumn,
            table.ShowRowStripes,
            table.ShowColumnStripes,
            NormalizeXml(table.NativeSortStateXml),
            new ChartRangeSummary(
                table.Range.Start.Row,
                table.Range.Start.Col,
                table.Range.End.Row,
                table.Range.End.Col),
            table.Columns
                .Select(column => new StructuredTableColumnSummary(
                    column.Id,
                    column.Name,
                    column.TotalsRowLabel ?? "",
                    column.TotalsRowFunction ?? "",
                    column.CalculatedColumnFormula ?? "",
                    column.TotalsRowFormula ?? ""))
                .ToArray(),
            table.FilterColumns
                .OrderBy(filter => filter.ColumnId)
                .Select(filter => new StructuredTableFilterColumnSummary(
                    filter.ColumnId,
                    filter.Values.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    filter.IncludeBlank,
                    filter.CustomFilters
                        .Select(customFilter => new StructuredTableCustomFilterSummary(
                            customFilter.Operator ?? "",
                            customFilter.Value ?? "",
                            customFilter.NativeAttributes is null
                                ? []
                                : customFilter.NativeAttributes
                                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                    .Select(pair => new NativeAttributeSummary(pair.Key, pair.Value))
                                    .ToArray()))
                        .ToArray(),
                    filter.CustomFiltersAnd,
                    filter.CustomFiltersAndRaw ?? "",
                    filter.NativeCustomFiltersAttributes is null
                        ? []
                        : filter.NativeCustomFiltersAttributes
                            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .Select(pair => new NativeAttributeSummary(pair.Key, pair.Value))
                            .ToArray(),
                    filter.NativeFilterXmls.Select(NormalizeXml).ToArray(),
                    filter.NativeAttributes is null
                        ? []
                        : filter.NativeAttributes
                            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .Select(pair => new NativeAttributeSummary(pair.Key, pair.Value))
                            .ToArray()))
                .ToArray());

    private static PivotTableStyleSummary CapturePivotTableStyleSummary(PivotTableStyleModel style) =>
        new(
            style.Name,
            style.AppliesToPivotTables,
            style.AppliesToTables,
            style.Elements
                .OrderBy(element => element.Type, StringComparer.OrdinalIgnoreCase)
                .ThenBy(element => element.DifferentialFormatId)
                .ThenBy(element => element.Size)
                .Select(element => new PivotTableStyleElementSummary(
                    element.Type,
                    element.DifferentialFormatId,
                    element.Size))
                .ToArray());

    private static PivotTableSummary CapturePivotTableSummary(PivotTableModel pivot) =>
        new(
            pivot.Name,
            pivot.CacheId,
            ToRangeSummary(pivot.SourceRange),
            ToRangeSummary(pivot.TargetRange),
            pivot.DataOnRows,
            pivot.FirstHeaderRow,
            pivot.FirstDataRow,
            pivot.FirstDataColumn,
            pivot.ShowSubtotals,
            pivot.SubtotalPlacement,
            pivot.ShowRowGrandTotals,
            pivot.ShowColumnGrandTotals,
            pivot.RepeatItemLabels,
            pivot.BlankLineAfterItems,
            pivot.ReportLayout,
            pivot.StyleName,
            pivot.ShowRowHeaders,
            pivot.ShowColumnHeaders,
            pivot.ShowRowStripes,
            pivot.ShowColumnStripes,
            pivot.ShowFieldHeaders,
            pivot.ShowContextualTooltips,
            pivot.ShowPropertiesInTooltips,
            pivot.ShowClassicLayout,
            pivot.MergeAndCenterLabels,
            pivot.ShowItemsWithNoDataOnRows,
            pivot.ShowItemsWithNoDataOnColumns,
            pivot.PageOverThenDown,
            pivot.PageWrap,
            pivot.EmptyValueText ?? "",
            pivot.ApplyNumberFormats,
            pivot.ApplyBorderFormats,
            pivot.ApplyFontFormats,
            pivot.ApplyPatternFormats,
            pivot.AutofitColumnsOnUpdate,
            pivot.PreserveFormattingOnUpdate,
            pivot.ShowExpandCollapseButtons,
            pivot.EnableDrill,
            pivot.AsteriskTotals,
            pivot.MultipleFieldFilters,
            pivot.EnableFieldDialog,
            pivot.EnableFieldProperties,
            pivot.EnableDataValueEditing,
            pivot.PrintTitles,
            pivot.PrintExpandCollapseButtons,
            pivot.AltTextTitle ?? "",
            pivot.AltTextDescription ?? "",
            pivot.DataCaption ?? "",
            pivot.GrandTotalCaption ?? "",
            pivot.MissingCaption ?? "",
            pivot.ErrorCaption ?? "",
            pivot.RowFields.Select(CapturePivotFieldSummary).ToArray(),
            pivot.ColumnFields.Select(CapturePivotFieldSummary).ToArray(),
            pivot.PageFields.Select(CapturePivotFieldSummary).ToArray(),
            pivot.DataFields.Select(CapturePivotDataFieldSummary).ToArray(),
            pivot.CalculatedFields
                .Select(field => new PivotCalculatedFieldSummary(field.Name, field.Formula))
                .ToArray());

    private static PivotFieldSummary CapturePivotFieldSummary(PivotFieldModel field) =>
        new(
            field.SourceFieldIndex,
            field.SelectedItem ?? "",
            field.SelectedItems?.ToArray() ?? [],
            field.Grouping,
            field.GroupStart,
            field.GroupEnd,
            field.GroupInterval);

    private static PivotDataFieldSummary CapturePivotDataFieldSummary(PivotDataFieldModel field) =>
        new(
            field.SourceFieldIndex,
            field.Name,
            field.SummaryFunction,
            field.NumberFormatId,
            field.CalculatedFieldName ?? "",
            field.ShowValuesAs,
            field.BaseFieldIndex,
            field.BaseItem ?? "",
            field.NumberFormatCode ?? "");

    private static ChartRangeSummary ToRangeSummary(GridRange range) =>
        new(
            range.Start.Row,
            range.Start.Col,
            range.End.Row,
            range.End.Col);

    private static RepeatRangeSummary ToRepeatRangeSummary(WorksheetRepeatRange range) =>
        new(range.Start, range.End);

    private static HeaderFooterSummary CaptureHeaderFooterSummary(WorksheetHeaderFooter value) =>
        new(
            NormalizeHeaderFooterText(value.Left),
            NormalizeHeaderFooterText(value.Center),
            NormalizeHeaderFooterText(value.Right));

    private static HeaderFooterPictureSetSummary CaptureHeaderFooterPictureSetSummary(WorksheetHeaderFooterPictureSet value) =>
        new(
            CaptureHeaderFooterPictureSummary(value.Left),
            CaptureHeaderFooterPictureSummary(value.Center),
            CaptureHeaderFooterPictureSummary(value.Right));

    private static HeaderFooterPictureSummary? CaptureHeaderFooterPictureSummary(WorksheetHeaderFooterPicture? picture) =>
        picture is null
            ? null
            : new HeaderFooterPictureSummary(
                picture.ContentType,
                picture.FileName ?? "",
                picture.ImageBytes.Length,
                picture.Width,
                picture.Height);

    private static string NormalizeHeaderFooterText(string text) =>
        text
            .Replace("&[Page]", "&P", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Pages]", "&N", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Date]", "&D", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Time]", "&T", StringComparison.OrdinalIgnoreCase)
            .Replace("&[File]", "&F", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Tab]", "&A", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Path]", "&Z", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeXml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return "";

        try
        {
            return XElement.Parse(xml).ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return xml.Trim();
        }
    }

}
