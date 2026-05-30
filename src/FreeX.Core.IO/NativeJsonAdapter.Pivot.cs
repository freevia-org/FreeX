using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static void LoadPivotCaches(Workbook workbook, IReadOnlyList<PivotCacheDto>? cacheDtos)
    {
        var seenCacheIds = new HashSet<int>();
        foreach (var dto in cacheDtos ?? [])
        {
            if (dto is null || dto.CacheId <= 0 || !seenCacheIds.Add(dto.CacheId))
                continue;

            var cache = new PivotCacheModel
            {
                CacheId = dto.CacheId,
                SourceType = ValidEnumOrDefault(dto.SourceType, PivotCacheSourceType.Unknown),
                SourceSheetName = TrimToNull(dto.SourceSheetName),
                SourceReference = TrimToNull(dto.SourceReference),
                SourceTableName = TrimToNull(dto.SourceTableName),
                ConnectionId = dto.ConnectionId,
                IsOlap = dto.IsOlap,
                PackagePart = dto.PackagePart ?? "",
                RefreshOnLoad = dto.RefreshOnLoad,
                SaveData = dto.SaveData,
                EnableRefresh = dto.EnableRefresh,
                PreserveSourceSortFilter = dto.PreserveSourceSortFilter,
                MissingItemsLimit = dto.MissingItemsLimit,
                RecordCount = dto.RecordCount,
                CreatedVersion = dto.CreatedVersion,
                MinRefreshableVersion = dto.MinRefreshableVersion,
                RefreshedVersion = dto.RefreshedVersion,
                RefreshedBy = TrimToNull(dto.RefreshedBy),
                RefreshedDateIso = TrimToNull(dto.RefreshedDateIso)
            };

            foreach (var field in (dto.Fields ?? []).Select(ToPivotCacheField).OfType<PivotCacheFieldModel>())
                cache.Fields.Add(field);
            workbook.PivotCaches.Add(cache);
        }
    }

    private static void LoadPivotTables(
        Workbook workbook,
        IReadOnlyDictionary<string, Sheet> loadedSheetsBySourceName,
        IReadOnlyList<(Sheet Sheet, SheetDto Dto)> sheetDtos)
    {
        foreach (var (sheet, sheetDto) in sheetDtos)
        foreach (var dto in sheetDto.PivotTables ?? [])
        {
            if (TryLoadPivotTable(workbook, loadedSheetsBySourceName, sheet, dto) is { } pivotTable)
                sheet.PivotTables.Add(pivotTable);
        }
    }

    private static PivotCacheFieldModel? ToPivotCacheField(PivotCacheFieldDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Name))
            return null;

        var sharedItems = dto.SharedItems?
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();

        return new PivotCacheFieldModel(
            dto.Name,
            dto.NumberFormatId,
            dto.SharedItemCount,
            dto.ContainsBlank,
            dto.ContainsString,
            dto.ContainsNumber,
            dto.ContainsDate,
            dto.ContainsMixedTypes,
            dto.ContainsSemiMixedTypes,
            dto.ContainsNonDate,
            dto.ContainsInteger,
            dto.ContainsLongText,
            dto.MinValue,
            dto.MaxValue,
            TrimToNull(dto.MinDate),
            TrimToNull(dto.MaxDate),
            sharedItems);
    }

    private static PivotTableModel? TryLoadPivotTable(
        Workbook workbook,
        IReadOnlyDictionary<string, Sheet> loadedSheetsBySourceName,
        Sheet targetSheet,
        PivotTableDto? dto)
    {
        if (dto is null ||
            string.IsNullOrWhiteSpace(dto.Name) ||
            string.IsNullOrWhiteSpace(dto.TargetRange))
        {
            return null;
        }

        var cache = workbook.PivotCaches.FirstOrDefault(cache => cache.CacheId == dto.CacheId);
        var sourceSheetName = TrimToNull(dto.SourceSheetName) ?? cache?.SourceSheetName ?? targetSheet.Name;
        var sourceSheet = ResolveLoadedSheet(workbook, loadedSheetsBySourceName, sourceSheetName);
        var sourceReference = TrimToNull(dto.SourceRange) ?? cache?.SourceReference;
        if (sourceSheet is null || string.IsNullOrWhiteSpace(sourceReference))
            return null;

        try
        {
            var sourceRange = GridRange.Parse(sourceReference, sourceSheet.Id);
            var targetRange = GridRange.Parse(dto.TargetRange, targetSheet.Id);
            if (!IsValidRangeOnSheet(sourceRange, sourceSheet.Id) ||
                !IsValidRangeOnSheet(targetRange, targetSheet.Id))
            {
                return null;
            }

            var pivotTable = new PivotTableModel
            {
                Name = dto.Name.Trim(),
                CacheId = dto.CacheId,
                SourceRange = sourceRange,
                TargetRange = targetRange,
                PackagePart = dto.PackagePart ?? "",
                CreatedVersion = dto.CreatedVersion,
                UpdatedVersion = dto.UpdatedVersion,
                MinRefreshableVersion = dto.MinRefreshableVersion,
                DataOnRows = dto.DataOnRows,
                FirstHeaderRow = Math.Max(0, dto.FirstHeaderRow),
                FirstDataRow = Math.Max(0, dto.FirstDataRow),
                FirstDataColumn = Math.Max(0, dto.FirstDataColumn),
                ShowSubtotals = dto.ShowSubtotals,
                SubtotalPlacement = ValidEnumOrDefault(dto.SubtotalPlacement, PivotSubtotalPlacement.Bottom),
                ShowRowGrandTotals = dto.ShowRowGrandTotals,
                ShowColumnGrandTotals = dto.ShowColumnGrandTotals,
                RepeatItemLabels = dto.RepeatItemLabels,
                BlankLineAfterItems = dto.BlankLineAfterItems,
                ReportLayout = ValidEnumOrDefault(dto.ReportLayout, PivotReportLayout.Tabular),
                CompactRowLabelIndent = Math.Clamp(dto.CompactRowLabelIndent, 0, 15),
                StyleName = string.IsNullOrWhiteSpace(dto.StyleName) ? "PivotStyleLight16" : dto.StyleName,
                ShowRowHeaders = dto.ShowRowHeaders,
                ShowColumnHeaders = dto.ShowColumnHeaders,
                ShowRowStripes = dto.ShowRowStripes,
                ShowColumnStripes = dto.ShowColumnStripes,
                ShowFieldHeaders = dto.ShowFieldHeaders,
                ShowContextualTooltips = dto.ShowContextualTooltips,
                ShowPropertiesInTooltips = dto.ShowPropertiesInTooltips,
                ShowClassicLayout = dto.ShowClassicLayout,
                MergeAndCenterLabels = dto.MergeAndCenterLabels,
                ShowItemsWithNoDataOnRows = dto.ShowItemsWithNoDataOnRows,
                ShowItemsWithNoDataOnColumns = dto.ShowItemsWithNoDataOnColumns,
                PageOverThenDown = dto.PageOverThenDown,
                PageWrap = Math.Clamp(dto.PageWrap, 0, 255),
                EmptyValueText = TrimToNull(dto.EmptyValueText),
                ApplyNumberFormats = dto.ApplyNumberFormats,
                ApplyBorderFormats = dto.ApplyBorderFormats,
                ApplyFontFormats = dto.ApplyFontFormats,
                ApplyPatternFormats = dto.ApplyPatternFormats,
                AutofitColumnsOnUpdate = dto.AutofitColumnsOnUpdate,
                PreserveFormattingOnUpdate = dto.PreserveFormattingOnUpdate,
                ShowExpandCollapseButtons = dto.ShowExpandCollapseButtons,
                EnableDrill = dto.EnableDrill,
                AsteriskTotals = dto.AsteriskTotals,
                MultipleFieldFilters = dto.MultipleFieldFilters,
                EnableFieldDialog = dto.EnableFieldDialog,
                EnableFieldProperties = dto.EnableFieldProperties,
                EnableDataValueEditing = dto.EnableDataValueEditing,
                PrintTitles = dto.PrintTitles,
                PrintExpandCollapseButtons = dto.PrintExpandCollapseButtons,
                AltTextTitle = TrimToNull(dto.AltTextTitle),
                AltTextDescription = TrimToNull(dto.AltTextDescription),
                DataCaption = TrimToNull(dto.DataCaption),
                GrandTotalCaption = TrimToNull(dto.GrandTotalCaption),
                MissingCaption = TrimToNull(dto.MissingCaption),
                ErrorCaption = TrimToNull(dto.ErrorCaption)
            };

            pivotTable.RowFields.AddRange((dto.RowFields ?? []).Select(ToPivotField).OfType<PivotFieldModel>());
            pivotTable.ColumnFields.AddRange((dto.ColumnFields ?? []).Select(ToPivotField).OfType<PivotFieldModel>());
            pivotTable.PageFields.AddRange((dto.PageFields ?? []).Select(ToPivotField).OfType<PivotFieldModel>());
            pivotTable.DataFields.AddRange((dto.DataFields ?? []).Select(ToPivotDataField).OfType<PivotDataFieldModel>());
            pivotTable.CalculatedFields.AddRange((dto.CalculatedFields ?? []).Select(ToPivotCalculatedField).OfType<PivotCalculatedFieldModel>());
            pivotTable.CalculatedItems.AddRange((dto.CalculatedItems ?? []).Select(ToPivotCalculatedItem).OfType<PivotCalculatedItemModel>());
            pivotTable.LabelFilters.AddRange((dto.LabelFilters ?? []).Select(ToPivotLabelFilter).OfType<PivotLabelFilterModel>());
            pivotTable.ValueFilters.AddRange((dto.ValueFilters ?? []).Select(ToPivotValueFilter).OfType<PivotValueFilterModel>());
            pivotTable.Sorts.AddRange((dto.Sorts ?? []).Select(ToPivotSort).OfType<PivotSortModel>());
            return pivotTable;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static PivotFieldModel? ToPivotField(PivotFieldDto? dto)
    {
        if (dto is null || dto.SourceFieldIndex < 0)
            return null;

        var selectedItems = dto.SelectedItems?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToList();

        return new PivotFieldModel(
            dto.SourceFieldIndex,
            TrimToNull(dto.SelectedItem),
            selectedItems,
            ValidEnumOrDefault(dto.Grouping, PivotFieldGrouping.None),
            dto.GroupStart,
            dto.GroupEnd,
            dto.GroupInterval,
            dto.ShowAll,
            dto.IncludeNewItemsInFilter,
            dto.MultipleItemSelectionAllowed,
            dto.DragToRow,
            dto.DragToColumn,
            dto.DragToPage,
            dto.DragToData,
            dto.ShowDropDowns);
    }

    private static PivotDataFieldModel? ToPivotDataField(PivotDataFieldDto? dto)
    {
        if (dto is null || (dto.SourceFieldIndex < 0 && string.IsNullOrWhiteSpace(dto.CalculatedFieldName)))
            return null;

        var name = string.IsNullOrWhiteSpace(dto.Name) ? "Values" : dto.Name.Trim();
        var summaryFunction = string.IsNullOrWhiteSpace(dto.SummaryFunction) ? "sum" : dto.SummaryFunction.Trim();
        return new PivotDataFieldModel(
            dto.SourceFieldIndex,
            name,
            summaryFunction,
            dto.NumberFormatId,
            TrimToNull(dto.CalculatedFieldName),
            ValidEnumOrDefault(dto.ShowValuesAs, PivotShowValuesAs.None),
            dto.BaseFieldIndex,
            TrimToNull(dto.BaseItem),
            TrimToNull(dto.NumberFormatCode));
    }

    private static PivotCalculatedFieldModel? ToPivotCalculatedField(PivotCalculatedFieldDto? dto) =>
        dto is null || string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Formula)
            ? null
            : new PivotCalculatedFieldModel(dto.Name.Trim(), dto.Formula.Trim());

    private static PivotCalculatedItemModel? ToPivotCalculatedItem(PivotCalculatedItemDto? dto) =>
        dto is null || dto.SourceFieldIndex < 0 || string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Formula)
            ? null
            : new PivotCalculatedItemModel(dto.SourceFieldIndex, dto.Name.Trim(), dto.Formula.Trim());

    private static PivotLabelFilterModel? ToPivotLabelFilter(PivotLabelFilterDto? dto) =>
        dto is null || dto.SourceFieldIndex < 0
            ? null
            : new PivotLabelFilterModel(
                dto.SourceFieldIndex,
                ValidEnumOrDefault(dto.Kind, PivotLabelFilterKind.Equals),
                dto.Value ?? "",
                dto.Value2);

    private static PivotValueFilterModel? ToPivotValueFilter(PivotValueFilterDto? dto) =>
        dto is null || dto.DataFieldIndex < 0
            ? null
            : new PivotValueFilterModel(
                dto.DataFieldIndex,
                ValidEnumOrDefault(dto.Kind, PivotValueFilterKind.Top),
                Math.Max(0, dto.Count),
                dto.ComparisonValue,
                dto.ComparisonValue2,
                dto.SourceFieldIndex is >= 0 ? dto.SourceFieldIndex : null);

    private static PivotSortModel? ToPivotSort(PivotSortDto? dto) =>
        dto is null || dto.FieldIndex < 0 || dto.DataFieldIndex < 0
            ? null
            : new PivotSortModel(
                ValidEnumOrDefault(dto.Target, PivotSortTarget.Label),
                ValidEnumOrDefault(dto.Direction, PivotSortDirection.Ascending),
                dto.DataFieldIndex,
                dto.FieldIndex);

    private static PivotCacheDto ToPivotCacheDto(PivotCacheModel cache) =>
        new()
        {
            CacheId = cache.CacheId,
            SourceType = cache.SourceType,
            SourceSheetName = cache.SourceSheetName,
            SourceReference = cache.SourceReference,
            SourceTableName = cache.SourceTableName,
            ConnectionId = cache.ConnectionId,
            IsOlap = cache.IsOlap,
            PackagePart = cache.PackagePart,
            RefreshOnLoad = cache.RefreshOnLoad,
            SaveData = cache.SaveData,
            EnableRefresh = cache.EnableRefresh,
            PreserveSourceSortFilter = cache.PreserveSourceSortFilter,
            MissingItemsLimit = cache.MissingItemsLimit,
            RecordCount = cache.RecordCount,
            CreatedVersion = cache.CreatedVersion,
            MinRefreshableVersion = cache.MinRefreshableVersion,
            RefreshedVersion = cache.RefreshedVersion,
            RefreshedBy = cache.RefreshedBy,
            RefreshedDateIso = cache.RefreshedDateIso,
            Fields = cache.Fields.Select(FromPivotCacheField).ToList()
        };

    private static PivotCacheFieldDto FromPivotCacheField(PivotCacheFieldModel field) =>
        new()
        {
            Name = field.Name,
            NumberFormatId = field.NumberFormatId,
            SharedItemCount = field.SharedItemCount,
            ContainsBlank = field.ContainsBlank,
            ContainsString = field.ContainsString,
            ContainsNumber = field.ContainsNumber,
            ContainsDate = field.ContainsDate,
            ContainsMixedTypes = field.ContainsMixedTypes,
            ContainsSemiMixedTypes = field.ContainsSemiMixedTypes,
            ContainsNonDate = field.ContainsNonDate,
            ContainsInteger = field.ContainsInteger,
            ContainsLongText = field.ContainsLongText,
            MinValue = field.MinValue,
            MaxValue = field.MaxValue,
            MinDate = field.MinDate,
            MaxDate = field.MaxDate,
            SharedItems = field.SharedItems?.ToList()
        };

    private static PivotTableDto? ToPivotTableDto(Workbook workbook, Sheet sheet, PivotTableModel pivot)
    {
        var sourceSheet = workbook.GetSheet(pivot.SourceRange.Start.Sheet);
        if (sourceSheet is null ||
            !IsValidRangeOnSheet(pivot.SourceRange, sourceSheet.Id) ||
            !IsValidRangeOnSheet(pivot.TargetRange, sheet.Id))
        {
            return null;
        }

        return new PivotTableDto
        {
            Name = pivot.Name,
            CacheId = pivot.CacheId,
            SourceSheetName = sourceSheet.Name,
            SourceRange = pivot.SourceRange.ToString(),
            TargetRange = pivot.TargetRange.ToString(),
            PackagePart = pivot.PackagePart,
            CreatedVersion = pivot.CreatedVersion,
            UpdatedVersion = pivot.UpdatedVersion,
            MinRefreshableVersion = pivot.MinRefreshableVersion,
            DataOnRows = pivot.DataOnRows,
            FirstHeaderRow = pivot.FirstHeaderRow,
            FirstDataRow = pivot.FirstDataRow,
            FirstDataColumn = pivot.FirstDataColumn,
            ShowSubtotals = pivot.ShowSubtotals,
            SubtotalPlacement = pivot.SubtotalPlacement,
            ShowRowGrandTotals = pivot.ShowRowGrandTotals,
            ShowColumnGrandTotals = pivot.ShowColumnGrandTotals,
            RepeatItemLabels = pivot.RepeatItemLabels,
            BlankLineAfterItems = pivot.BlankLineAfterItems,
            ReportLayout = pivot.ReportLayout,
            CompactRowLabelIndent = pivot.CompactRowLabelIndent,
            StyleName = pivot.StyleName,
            ShowRowHeaders = pivot.ShowRowHeaders,
            ShowColumnHeaders = pivot.ShowColumnHeaders,
            ShowRowStripes = pivot.ShowRowStripes,
            ShowColumnStripes = pivot.ShowColumnStripes,
            ShowFieldHeaders = pivot.ShowFieldHeaders,
            ShowContextualTooltips = pivot.ShowContextualTooltips,
            ShowPropertiesInTooltips = pivot.ShowPropertiesInTooltips,
            ShowClassicLayout = pivot.ShowClassicLayout,
            MergeAndCenterLabels = pivot.MergeAndCenterLabels,
            ShowItemsWithNoDataOnRows = pivot.ShowItemsWithNoDataOnRows,
            ShowItemsWithNoDataOnColumns = pivot.ShowItemsWithNoDataOnColumns,
            PageOverThenDown = pivot.PageOverThenDown,
            PageWrap = pivot.PageWrap,
            EmptyValueText = pivot.EmptyValueText,
            ApplyNumberFormats = pivot.ApplyNumberFormats,
            ApplyBorderFormats = pivot.ApplyBorderFormats,
            ApplyFontFormats = pivot.ApplyFontFormats,
            ApplyPatternFormats = pivot.ApplyPatternFormats,
            AutofitColumnsOnUpdate = pivot.AutofitColumnsOnUpdate,
            PreserveFormattingOnUpdate = pivot.PreserveFormattingOnUpdate,
            ShowExpandCollapseButtons = pivot.ShowExpandCollapseButtons,
            EnableDrill = pivot.EnableDrill,
            AsteriskTotals = pivot.AsteriskTotals,
            MultipleFieldFilters = pivot.MultipleFieldFilters,
            EnableFieldDialog = pivot.EnableFieldDialog,
            EnableFieldProperties = pivot.EnableFieldProperties,
            EnableDataValueEditing = pivot.EnableDataValueEditing,
            PrintTitles = pivot.PrintTitles,
            PrintExpandCollapseButtons = pivot.PrintExpandCollapseButtons,
            AltTextTitle = pivot.AltTextTitle,
            AltTextDescription = pivot.AltTextDescription,
            DataCaption = pivot.DataCaption,
            GrandTotalCaption = pivot.GrandTotalCaption,
            MissingCaption = pivot.MissingCaption,
            ErrorCaption = pivot.ErrorCaption,
            RowFields = pivot.RowFields.Select(FromPivotField).ToList(),
            ColumnFields = pivot.ColumnFields.Select(FromPivotField).ToList(),
            PageFields = pivot.PageFields.Select(FromPivotField).ToList(),
            DataFields = pivot.DataFields.Select(FromPivotDataField).ToList(),
            CalculatedFields = pivot.CalculatedFields.Select(FromPivotCalculatedField).ToList(),
            CalculatedItems = pivot.CalculatedItems.Select(FromPivotCalculatedItem).ToList(),
            LabelFilters = pivot.LabelFilters.Select(FromPivotLabelFilter).ToList(),
            ValueFilters = pivot.ValueFilters.Select(FromPivotValueFilter).ToList(),
            Sorts = pivot.Sorts.Select(FromPivotSort).ToList()
        };
    }

    private static PivotFieldDto FromPivotField(PivotFieldModel field) =>
        new()
        {
            SourceFieldIndex = field.SourceFieldIndex,
            SelectedItem = field.SelectedItem,
            SelectedItems = field.SelectedItems?.ToList(),
            Grouping = field.Grouping,
            GroupStart = field.GroupStart,
            GroupEnd = field.GroupEnd,
            GroupInterval = field.GroupInterval,
            ShowAll = field.ShowAll,
            IncludeNewItemsInFilter = field.IncludeNewItemsInFilter,
            MultipleItemSelectionAllowed = field.MultipleItemSelectionAllowed,
            DragToRow = field.DragToRow,
            DragToColumn = field.DragToColumn,
            DragToPage = field.DragToPage,
            DragToData = field.DragToData,
            ShowDropDowns = field.ShowDropDowns
        };

    private static PivotDataFieldDto FromPivotDataField(PivotDataFieldModel field) =>
        new()
        {
            SourceFieldIndex = field.SourceFieldIndex,
            Name = field.Name,
            SummaryFunction = field.SummaryFunction,
            NumberFormatId = field.NumberFormatId,
            CalculatedFieldName = field.CalculatedFieldName,
            ShowValuesAs = field.ShowValuesAs,
            BaseFieldIndex = field.BaseFieldIndex,
            BaseItem = field.BaseItem,
            NumberFormatCode = field.NumberFormatCode
        };

    private static PivotCalculatedFieldDto FromPivotCalculatedField(PivotCalculatedFieldModel field) =>
        new() { Name = field.Name, Formula = field.Formula };

    private static PivotCalculatedItemDto FromPivotCalculatedItem(PivotCalculatedItemModel item) =>
        new() { SourceFieldIndex = item.SourceFieldIndex, Name = item.Name, Formula = item.Formula };

    private static PivotLabelFilterDto FromPivotLabelFilter(PivotLabelFilterModel filter) =>
        new()
        {
            SourceFieldIndex = filter.SourceFieldIndex,
            Kind = filter.Kind,
            Value = filter.Value,
            Value2 = filter.Value2
        };

    private static PivotValueFilterDto FromPivotValueFilter(PivotValueFilterModel filter) =>
        new()
        {
            DataFieldIndex = filter.DataFieldIndex,
            Kind = filter.Kind,
            Count = filter.Count,
            ComparisonValue = filter.ComparisonValue,
            ComparisonValue2 = filter.ComparisonValue2,
            SourceFieldIndex = filter.SourceFieldIndex
        };

    private static PivotSortDto FromPivotSort(PivotSortModel sort) =>
        new()
        {
            Target = sort.Target,
            Direction = sort.Direction,
            DataFieldIndex = sort.DataFieldIndex,
            FieldIndex = sort.FieldIndex
        };

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static T ValidEnumOrDefault<T>(T value, T defaultValue)
        where T : struct, Enum =>
        Enum.IsDefined(typeof(T), value) ? value : defaultValue;
}
