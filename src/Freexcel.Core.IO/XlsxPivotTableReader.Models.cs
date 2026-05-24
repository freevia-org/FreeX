using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxPivotTableReader
{
    private static PivotTableModel ToPivotTableModel(PendingPivotTableModel pending, SheetId sheetId)
    {
        var pivotTable = new PivotTableModel
        {
            Name = pending.Name,
            CacheId = pending.CacheId,
            SourceRange = ParseOptionalRange(pending.SourceReference, sheetId),
            TargetRange = GridRange.Parse(pending.TargetReference, sheetId),
            PackagePart = pending.PackagePart,
            ShowSubtotals = pending.ShowSubtotals,
            SubtotalPlacement = pending.SubtotalPlacement,
            ShowRowGrandTotals = pending.ShowRowGrandTotals,
            ShowColumnGrandTotals = pending.ShowColumnGrandTotals,
            RepeatItemLabels = pending.RepeatItemLabels,
            BlankLineAfterItems = pending.BlankLineAfterItems,
            ReportLayout = pending.ReportLayout,
            CompactRowLabelIndent = pending.CompactRowLabelIndent,
            StyleName = string.IsNullOrWhiteSpace(pending.StyleName) ? "PivotStyleLight16" : pending.StyleName,
            ShowRowHeaders = pending.ShowRowHeaders,
            ShowColumnHeaders = pending.ShowColumnHeaders,
            ShowRowStripes = pending.ShowRowStripes,
            ShowColumnStripes = pending.ShowColumnStripes,
            ShowFieldHeaders = pending.ShowFieldHeaders,
            ShowContextualTooltips = pending.ShowContextualTooltips,
            ShowPropertiesInTooltips = pending.ShowPropertiesInTooltips,
            ShowClassicLayout = pending.ShowClassicLayout,
            MergeAndCenterLabels = pending.MergeAndCenterLabels,
            ShowItemsWithNoDataOnRows = pending.ShowItemsWithNoDataOnRows,
            ShowItemsWithNoDataOnColumns = pending.ShowItemsWithNoDataOnColumns,
            PageOverThenDown = pending.PageOverThenDown,
            PageWrap = pending.PageWrap,
            ShowExpandCollapseButtons = pending.ShowExpandCollapseButtons,
            EnableDrill = pending.EnableDrill,
            AsteriskTotals = pending.AsteriskTotals,
            MultipleFieldFilters = pending.MultipleFieldFilters,
            EnableFieldDialog = pending.EnableFieldDialog,
            EnableFieldProperties = pending.EnableFieldProperties,
            EnableDataValueEditing = pending.EnableDataValueEditing,
            ApplyNumberFormats = pending.ApplyNumberFormats,
            ApplyBorderFormats = pending.ApplyBorderFormats,
            ApplyFontFormats = pending.ApplyFontFormats,
            ApplyPatternFormats = pending.ApplyPatternFormats,
            AutofitColumnsOnUpdate = pending.AutofitColumnsOnUpdate,
            PreserveFormattingOnUpdate = pending.PreserveFormattingOnUpdate,
            PrintTitles = pending.PrintTitles,
            PrintExpandCollapseButtons = pending.PrintExpandCollapseButtons,
            AltTextTitle = string.IsNullOrWhiteSpace(pending.AltTextTitle) ? null : pending.AltTextTitle,
            AltTextDescription = string.IsNullOrWhiteSpace(pending.AltTextDescription) ? null : pending.AltTextDescription,
            DataCaption = string.IsNullOrWhiteSpace(pending.DataCaption) ? null : pending.DataCaption,
            GrandTotalCaption = string.IsNullOrWhiteSpace(pending.GrandTotalCaption) ? null : pending.GrandTotalCaption,
            MissingCaption = string.IsNullOrWhiteSpace(pending.MissingCaption) ? null : pending.MissingCaption,
            ErrorCaption = string.IsNullOrWhiteSpace(pending.ErrorCaption) ? null : pending.ErrorCaption
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

    private static GridRange ParseOptionalRange(string reference, SheetId sheetId)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return default;

        try
        {
            return GridRange.Parse(reference, sheetId);
        }
        catch
        {
            return default;
        }
    }

    public sealed record PivotPackageMetadata(
        IReadOnlyList<PivotCacheModel> PivotCaches,
        IReadOnlyDictionary<string, List<PendingPivotTableModel>> PivotTablesBySheetName)
    {
        public static PivotPackageMetadata Empty { get; } = new(
            [],
            new Dictionary<string, List<PendingPivotTableModel>>(StringComparer.OrdinalIgnoreCase));
    }

    public sealed record PendingPivotTableModel(
        string Name,
        int CacheId,
        string TargetReference,
        string SourceReference,
        string PackagePart,
        bool ShowSubtotals,
        PivotSubtotalPlacement SubtotalPlacement,
        bool ShowGrandTotals,
        bool ShowRowGrandTotals,
        bool ShowColumnGrandTotals,
        bool RepeatItemLabels,
        bool BlankLineAfterItems,
        PivotReportLayout ReportLayout,
        int CompactRowLabelIndent,
        string StyleName,
        bool ShowRowHeaders,
        bool ShowColumnHeaders,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        bool ShowFieldHeaders,
        bool ShowContextualTooltips,
        bool ShowPropertiesInTooltips,
        bool ShowClassicLayout,
        bool MergeAndCenterLabels,
        bool ShowItemsWithNoDataOnRows,
        bool ShowItemsWithNoDataOnColumns,
        bool PageOverThenDown,
        int PageWrap,
        bool ShowExpandCollapseButtons,
        bool EnableDrill,
        bool AsteriskTotals,
        bool MultipleFieldFilters,
        bool EnableFieldDialog,
        bool EnableFieldProperties,
        bool EnableDataValueEditing,
        bool ApplyNumberFormats,
        bool ApplyBorderFormats,
        bool ApplyFontFormats,
        bool ApplyPatternFormats,
        bool AutofitColumnsOnUpdate,
        bool PreserveFormattingOnUpdate,
        bool PrintTitles,
        bool PrintExpandCollapseButtons,
        string? AltTextTitle,
        string? AltTextDescription,
        string? DataCaption,
        string? GrandTotalCaption,
        string? MissingCaption,
        string? ErrorCaption,
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotDataFieldModel> DataFields,
        IReadOnlyList<PivotCalculatedFieldModel> CalculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> CalculatedItems,
        IReadOnlyList<PivotValueFilterModel> ValueFilters,
        IReadOnlyList<PivotLabelFilterModel> LabelFilters,
        IReadOnlyList<PivotSortModel> Sorts)
    {
        public PivotTableModel ToPivotTableModel(SheetId sheetId) =>
            XlsxPivotTableReader.ToPivotTableModel(this, sheetId);
    }
}
