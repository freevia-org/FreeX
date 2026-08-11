using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

public sealed partial class PivotApplicationSession
{
    public IReadOnlyList<string> ReadSourceHeaders(PivotApplicationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return PivotSourceContext.ReadHeaders(_workbook, target.PivotTable, target.Sheet);
    }

    public IReadOnlyList<string> ReadSourceItems(PivotApplicationTarget target, int sourceFieldIndex)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (sourceFieldIndex < 0)
            return [];

        return PivotSourceContext.ReadItems(
            _workbook,
            target.Sheet,
            target.PivotTable,
            sourceFieldIndex);
    }

    public PivotApplicationPlan PlanFieldFilters(
        PivotApplicationTarget target,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotLabelFilterModel> labelFilters,
        IReadOnlyList<PivotValueFilterModel> valueFilters,
        IReadOnlyList<PivotSortModel>? sorts = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        return PlanMutation(
            target,
            new ConfigurePivotTableFieldFiltersCommand(
                target.Sheet.Id,
                target.PivotTable.Name,
                rowFields,
                columnFields,
                pageFields,
                labelFilters,
                valueFilters,
                sorts ?? target.PivotTable.Sorts.ToList()));
    }

    public PivotApplicationPlan PlanFieldItemSelection(
        PivotApplicationTarget target,
        PivotHeaderArea area,
        int sourceFieldIndex,
        IReadOnlyList<string>? selectedItems)
    {
        ArgumentNullException.ThrowIfNull(target);
        var pivot = target.PivotTable;
        var selection = PivotUiPlanner
            .CreateFieldSelectionState(pivot, area, sourceFieldIndex)
            .WithSelectedItems(selectedItems);
        return PlanFieldFilters(
            target,
            selection.RowFields,
            selection.ColumnFields,
            selection.PageFields,
            pivot.LabelFilters.ToList(),
            pivot.ValueFilters.ToList());
    }

    public PivotApplicationPlan PlanClearFieldFilters(
        PivotApplicationTarget target,
        PivotHeaderArea area,
        int sourceFieldIndex)
    {
        ArgumentNullException.ThrowIfNull(target);
        var pivot = target.PivotTable;
        var selection = PivotUiPlanner
            .CreateFieldSelectionState(pivot, area, sourceFieldIndex)
            .WithSelectedItems(null);
        return PlanFieldFilters(
            target,
            selection.RowFields,
            selection.ColumnFields,
            selection.PageFields,
            pivot.LabelFilters.Where(filter => filter.SourceFieldIndex != sourceFieldIndex).ToList(),
            pivot.ValueFilters.Where(filter =>
                !PivotFilterOwnership.BelongsToSourceField(filter, sourceFieldIndex)).ToList());
    }

    public PivotApplicationPlan PlanReplaceLabelFilter(
        PivotApplicationTarget target,
        int sourceFieldIndex,
        PivotLabelFilterModel? filter)
    {
        ArgumentNullException.ThrowIfNull(target);
        var pivot = target.PivotTable;
        return PlanFieldFilters(
            target,
            pivot.RowFields.ToList(),
            pivot.ColumnFields.ToList(),
            pivot.PageFields.ToList(),
            PivotFieldFilterPlanner.ReplaceFieldLabelFilter(
                pivot.LabelFilters,
                sourceFieldIndex,
                filter),
            pivot.ValueFilters.ToList());
    }

    public PivotApplicationPlan PlanReplaceValueFilter(
        PivotApplicationTarget target,
        int sourceFieldIndex,
        PivotValueFilterModel? filter)
    {
        ArgumentNullException.ThrowIfNull(target);
        var pivot = target.PivotTable;
        return PlanFieldFilters(
            target,
            pivot.RowFields.ToList(),
            pivot.ColumnFields.ToList(),
            pivot.PageFields.ToList(),
            pivot.LabelFilters.ToList(),
            PivotFieldFilterPlanner.ReplaceFieldValueFilter(
                pivot.ValueFilters,
                sourceFieldIndex,
                filter));
    }

    public PivotApplicationPlan PlanRemoveValueFilter(
        PivotApplicationTarget target,
        int sourceFieldIndex)
    {
        ArgumentNullException.ThrowIfNull(target);
        var pivot = target.PivotTable;
        return PlanFieldFilters(
            target,
            pivot.RowFields.ToList(),
            pivot.ColumnFields.ToList(),
            pivot.PageFields.ToList(),
            pivot.LabelFilters.ToList(),
            pivot.ValueFilters.Where(filter =>
                !PivotFilterOwnership.BelongsToSourceField(filter, sourceFieldIndex)).ToList());
    }

    public PivotApplicationPlan PlanFieldView(
        PivotApplicationTarget target,
        IReadOnlyList<PivotLabelFilterModel> labelFilters,
        IReadOnlyList<PivotValueFilterModel> valueFilters,
        IReadOnlyList<PivotSortModel> sorts)
    {
        ArgumentNullException.ThrowIfNull(target);
        return PlanMutation(
            target,
            new ConfigurePivotTableViewCommand(
                target.Sheet.Id,
                target.PivotTable.Name,
                labelFilters,
                valueFilters,
                sorts));
    }

    public PivotApplicationPlan PlanFieldSort(
        PivotApplicationTarget target,
        PivotSortModel sort)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sort);
        var pivot = target.PivotTable;
        return PlanFieldView(
            target,
            pivot.LabelFilters.ToList(),
            pivot.ValueFilters.ToList(),
            PivotSortPlanner.ReplaceFieldSort(pivot.Sorts, sort));
    }

    public PivotApplicationPlan PlanCalculatedConfiguration(
        PivotApplicationTarget target,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> calculatedItems)
    {
        ArgumentNullException.ThrowIfNull(target);
        return PlanMutation(
            target,
            new ConfigurePivotTableCalculatedItemsCommand(
                target.Sheet.Id,
                target.PivotTable.Name,
                rowFields,
                columnFields,
                pageFields,
                calculatedFields,
                calculatedItems));
    }

    public PivotApplicationPlan PlanDesignOptions(
        PivotApplicationTarget target,
        PivotDesignOptionsValues values,
        bool? showExpandCollapseButtons = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(values);
        return PlanMutation(
            target,
            new ConfigurePivotTableOptionsCommand(
                target.Sheet.Id,
                target.PivotTable.Name,
                values.ShowRowGrandTotals,
                values.ShowColumnGrandTotals,
                values.ShowSubtotals,
                values.SubtotalPlacement,
                values.RepeatItemLabels,
                values.BlankLineAfterItems,
                values.StyleName,
                values.ShowRowHeaders,
                values.ShowColumnHeaders,
                values.ShowRowStripes,
                values.ShowColumnStripes,
                values.ReportLayout,
                showFieldHeaders: values.ShowFieldHeaders,
                showExpandCollapseButtons: showExpandCollapseButtons));
    }

    public PivotApplicationPlan PlanDialogOptions(
        PivotApplicationTarget target,
        PivotOptionsDialogValues values)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(values);
        return PlanMutation(
            target,
            new ConfigurePivotTableOptionsCommand(
                target.Sheet.Id,
                target.PivotTable.Name,
                values.ShowRowGrandTotals,
                values.ShowColumnGrandTotals,
                values.ShowSubtotals,
                values.SubtotalPlacement,
                values.RepeatItemLabels,
                values.BlankLineAfterItems,
                values.StyleName,
                values.ShowRowHeaders,
                values.ShowColumnHeaders,
                values.ShowRowStripes,
                values.ShowColumnStripes,
                values.ReportLayout,
                emptyValueText: values.EmptyValueText,
                updateEmptyValueText: true,
                refreshOnOpen: values.RefreshOnOpen,
                saveSourceData: values.SaveSourceData,
                enableRefresh: values.EnableRefresh,
                preserveSourceSortFilter: values.PreserveSourceSortFilter,
                missingItemsLimit: values.MissingItemsLimit,
                updateMissingItemsLimit: true,
                printTitles: values.PrintTitles,
                printExpandCollapseButtons: values.PrintExpandCollapseButtons,
                altTextTitle: values.AltTextTitle,
                altTextDescription: values.AltTextDescription,
                compactRowLabelIndent: values.CompactRowLabelIndent,
                updateAltText: true,
                showExpandCollapseButtons: values.ShowExpandCollapseButtons,
                autofitColumnsOnUpdate: values.AutofitColumnsOnUpdate,
                preserveFormattingOnUpdate: values.PreserveFormattingOnUpdate,
                showFieldHeaders: values.ShowFieldHeaders,
                showContextualTooltips: values.ShowContextualTooltips,
                showPropertiesInTooltips: values.ShowPropertiesInTooltips,
                showClassicLayout: values.ShowClassicLayout,
                mergeAndCenterLabels: values.MergeAndCenterLabels,
                showItemsWithNoDataOnRows: values.ShowItemsWithNoDataOnRows,
                showItemsWithNoDataOnColumns: values.ShowItemsWithNoDataOnColumns,
                pageOverThenDown: values.PageOverThenDown,
                pageWrap: values.PageWrap,
                errorCaption: values.ErrorValueText,
                updateErrorCaption: true,
                enableDrill: values.EnableDrill));
    }
}
