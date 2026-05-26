namespace Freexcel.Core.Model;

public sealed partial class Sheet
{
    /// <summary>
    /// Creates a deep copy of this sheet with a new <paramref name="newId"/> and <paramref name="newName"/>.
    /// All model-layer properties are copied, including the previously missed fields:
    /// <c>BackgroundImage</c>, <c>RowOutlineLevels</c>, <c>ColOutlineLevels</c>,
    /// <c>GroupHiddenRows</c>, and <c>GroupHiddenCols</c>.
    /// Drawing collections (Charts, TextBoxes, DrawingShapes, Pictures, Sparklines) are intentionally
    /// left empty; the caller (e.g. <c>DuplicateSheetCommand</c>) is responsible for copying those.
    /// </summary>
    public Sheet Clone(SheetId newId, string newName)
    {
        var copy = new Sheet(newId, newName)
        {
            DefaultColumnWidth            = DefaultColumnWidth,
            DefaultRowHeight              = DefaultRowHeight,
            FrozenRows                    = FrozenRows,
            FrozenCols                    = FrozenCols,
            SplitRow                      = SplitRow,
            SplitColumn                   = SplitColumn,
            ViewTopRow                    = ViewTopRow,
            ViewLeftCol                   = ViewLeftCol,
            ActiveRow                     = ActiveRow,
            ActiveCol                     = ActiveCol,
            ShowGridlines                 = ShowGridlines,
            ShowHeadings                  = ShowHeadings,
            ShowRulers                    = ShowRulers,
            ZoomPercent                   = ZoomPercent,
            ShowFormulas                  = ShowFormulas,
            FullCalculationOnLoad         = FullCalculationOnLoad,
            PhoneticProperties            = PhoneticProperties,
            PrintArea                     = PrintArea.HasValue ? RemapRange(PrintArea.Value, newId) : null,
            AutoFilter                    = CloneAutoFilter(AutoFilter),
            SmartTags                     = SmartTags,
            DataConsolidation             = DataConsolidation,
            SortState                     = SortState,
            SingleXmlCells                = CloneSingleXmlCells(SingleXmlCells),
            AdditionalViews               = AdditionalViews,
            PrimaryViewMetadata           = PrimaryViewMetadata is null
                ? null
                : new WorksheetPrimaryViewMetadataModel
                {
                    NativeAttributes = new Dictionary<string, string>(PrimaryViewMetadata.NativeAttributes, StringComparer.Ordinal),
                    NativeChildXmls = [.. PrimaryViewMetadata.NativeChildXmls]
                },
            PageOrientation               = PageOrientation,
            PaperSize                     = PaperSize,
            PageMargins                   = PageMargins,
            HeaderMargin                  = HeaderMargin,
            FooterMargin                  = FooterMargin,
            PrintGridlines                = PrintGridlines,
            PrintHeadings                 = PrintHeadings,
            ScaleToFit                    = ScaleToFit,
            FitToPage                     = FitToPage,
            AutoPageBreaks                = AutoPageBreaks,
            PrintTitleRows                = PrintTitleRows,
            PrintTitleColumns             = PrintTitleColumns,
            PageHeader                    = PageHeader,
            PageHeaderPictures            = PageHeaderPictures.DeepClone(),
            PageFooter                    = PageFooter,
            PageFooterPictures            = PageFooterPictures.DeepClone(),
            FirstPageHeader               = FirstPageHeader,
            FirstPageHeaderPictures       = FirstPageHeaderPictures.DeepClone(),
            FirstPageFooter               = FirstPageFooter,
            FirstPageFooterPictures       = FirstPageFooterPictures.DeepClone(),
            EvenPageHeader                = EvenPageHeader,
            EvenPageHeaderPictures        = EvenPageHeaderPictures.DeepClone(),
            EvenPageFooter                = EvenPageFooter,
            EvenPageFooterPictures        = EvenPageFooterPictures.DeepClone(),
            DifferentFirstPageHeaderFooter = DifferentFirstPageHeaderFooter,
            DifferentOddEvenHeaderFooter  = DifferentOddEvenHeaderFooter,
            HeaderFooterScaleWithDocument = HeaderFooterScaleWithDocument,
            HeaderFooterAlignWithMargins  = HeaderFooterAlignWithMargins,
            CenterHorizontallyOnPage      = CenterHorizontallyOnPage,
            CenterVerticallyOnPage        = CenterVerticallyOnPage,
            PageOrder                     = PageOrder,
            FirstPageNumber               = FirstPageNumber,
            UsePrinterDefaults            = UsePrinterDefaults,
            PrintCopies                   = PrintCopies,
            PrintBlackAndWhite            = PrintBlackAndWhite,
            PrintDraftQuality             = PrintDraftQuality,
            PrintQualityDpi               = PrintQualityDpi,
            PrintQualityVerticalDpi       = PrintQualityVerticalDpi,
            PageMarginsMetadata           = PageMarginsMetadata is null
                ? null
                : new WorksheetPageMarginsMetadataModel
                {
                    NativeAttributes = new Dictionary<string, string>(PageMarginsMetadata.NativeAttributes, StringComparer.Ordinal),
                    NativeChildXmls = [.. PageMarginsMetadata.NativeChildXmls]
                },
            PrintErrorValue               = PrintErrorValue,
            PrintComments                 = PrintComments,
            PrintOptionsMetadata          = PrintOptionsMetadata is null
                ? null
                : new WorksheetPrintOptionsMetadataModel
                {
                    NativeAttributes = new Dictionary<string, string>(PrintOptionsMetadata.NativeAttributes, StringComparer.Ordinal),
                    NativeChildXmls = [.. PrintOptionsMetadata.NativeChildXmls]
                },
            HeaderFooterMetadata          = HeaderFooterMetadata is null
                ? null
                : new WorksheetHeaderFooterMetadataModel
                {
                    NativeAttributes = new Dictionary<string, string>(HeaderFooterMetadata.NativeAttributes, StringComparer.Ordinal),
                    NativeChildXmls = [.. HeaderFooterMetadata.NativeChildXmls]
                },
            PageSetupMetadata             = PageSetupMetadata is null
                ? null
                : new WorksheetPageSetupMetadataModel
                {
                    NativeAttributes = new Dictionary<string, string>(PageSetupMetadata.NativeAttributes, StringComparer.Ordinal),
                    NativeChildXmls = [.. PageSetupMetadata.NativeChildXmls]
                },
            ViewMode                      = ViewMode,
            IsHidden                      = false,
            IsVeryHidden                  = IsVeryHidden,
            CodeName                      = CodeName,
            TabColor                      = TabColor,
            OutlineSummaryBelow           = OutlineSummaryBelow,
            OutlineSummaryRight           = OutlineSummaryRight,
            ShowOutlineSymbols            = ShowOutlineSymbols,
            ApplyOutlineStyles            = ApplyOutlineStyles,
            SheetFormatMetadata           = SheetFormatMetadata is null
                ? null
                : new WorksheetSheetFormatMetadataModel
                {
                    NativeAttributes = new Dictionary<string, string>(SheetFormatMetadata.NativeAttributes, StringComparer.Ordinal),
                    NativeChildXmls = [.. SheetFormatMetadata.NativeChildXmls]
                },
            DimensionMetadata             = DimensionMetadata is null
                ? null
                : new WorksheetDimensionMetadataModel
                {
                    NativeAttributes = new Dictionary<string, string>(DimensionMetadata.NativeAttributes, StringComparer.Ordinal)
                },
            SheetPropertiesMetadata       = SheetPropertiesMetadata is null
                ? null
                : new WorksheetSheetPropertiesMetadataModel
                {
                    NativeAttributes = new Dictionary<string, string>(SheetPropertiesMetadata.NativeAttributes, StringComparer.Ordinal),
                    NativeChildXmls = [.. SheetPropertiesMetadata.NativeChildXmls]
                },
            IsProtected                   = IsProtected,
            ProtectionPassword            = ProtectionPassword,
            ProtectionMetadata            = ProtectionMetadata is null
                ? null
                : new WorksheetProtectionMetadataModel
                {
                    NativeAttributes = new Dictionary<string, string>(ProtectionMetadata.NativeAttributes, StringComparer.Ordinal),
                    NativeChildXmls = [.. ProtectionMetadata.NativeChildXmls]
                },
            // Previously missed fields:
            BackgroundImage               = BackgroundImage,
            RowPageBreaksMetadata         = ClonePageBreaksMetadata(RowPageBreaksMetadata),
            ColumnPageBreaksMetadata      = ClonePageBreaksMetadata(ColumnPageBreaksMetadata),
        };

        CopyLayoutCollectionsTo(copy);
        CopyCellContentTo(copy, newId);

        // Comments and hyperlinks
        foreach (var (address, comment) in Comments)
            copy.Comments[RemapAddress(address, newId)] = comment;
        foreach (var (address, comment) in ThreadedComments)
            copy.ThreadedComments[RemapAddress(address, newId)] = comment;
        foreach (var (address, hyperlink) in Hyperlinks)
            copy.Hyperlinks[RemapAddress(address, newId)] = hyperlink;
        foreach (var (address, metadata) in HyperlinkMetadata)
            copy.HyperlinkMetadata[RemapAddress(address, newId)] = metadata;

        // Allow-edit ranges (protection)
        copy.ProtectionPermissions.Clear();
        foreach (var permission in ProtectionPermissions)
            copy.ProtectionPermissions.Add(permission);
        foreach (var range in AllowEditRanges)
            copy.AllowEditRanges.Add(RemapRange(range, newId));
        foreach (var property in CustomProperties)
            copy.CustomProperties.Add(property);

        foreach (var pt in PivotTables)
            copy.PivotTables.Add(ClonePivotTable(pt, newId));

        foreach (var table in StructuredTables)
            copy.StructuredTables.Add(CloneStructuredTable(table, newId));

        foreach (var cf in ConditionalFormats)
            copy.ConditionalFormats.Add(CloneConditionalFormat(cf, newId));

        foreach (var dv in DataValidations)
            copy.DataValidations.Add(CloneDataValidation(dv, newId));

        // Note: Charts, TextBoxes, DrawingShapes, Pictures, and Sparklines are intentionally
        // left empty here. The caller must copy those drawing collections separately.

        return copy;
    }

    private void CopyCellContentTo(Sheet copy, SheetId newId)
    {
        foreach (var (address, cell) in EnumerateCells())
            copy.SetCell(RemapAddress(address, newId), cell.Clone());

        foreach (var ((row, col), styleId) in GetStyleOnlyEntries())
            copy.SetStyleOnly(row, col, styleId);

        copy.ReplaceMergedRegions(MergedRegions.Select(r => RemapRange(r, newId)));
    }

    private static PivotTableModel ClonePivotTable(PivotTableModel pt, SheetId newId)
    {
        var clonedPt = new PivotTableModel
        {
            Name        = pt.Name,
            CacheId     = pt.CacheId,
            SourceRange = RemapRange(pt.SourceRange, newId),
            TargetRange = RemapRange(pt.TargetRange, newId),
            PackagePart = pt.PackagePart,
            CreatedVersion = pt.CreatedVersion,
            UpdatedVersion = pt.UpdatedVersion,
            MinRefreshableVersion = pt.MinRefreshableVersion,
            DataOnRows = pt.DataOnRows,
            FirstHeaderRow = pt.FirstHeaderRow,
            FirstDataRow = pt.FirstDataRow,
            FirstDataColumn = pt.FirstDataColumn,
            ShowSubtotals = pt.ShowSubtotals,
            SubtotalPlacement = pt.SubtotalPlacement,
            ShowRowGrandTotals = pt.ShowRowGrandTotals,
            ShowColumnGrandTotals = pt.ShowColumnGrandTotals,
            RepeatItemLabels = pt.RepeatItemLabels,
            BlankLineAfterItems = pt.BlankLineAfterItems,
            ReportLayout = pt.ReportLayout,
            CompactRowLabelIndent = pt.CompactRowLabelIndent,
            StyleName = pt.StyleName,
            ShowRowHeaders = pt.ShowRowHeaders,
            ShowColumnHeaders = pt.ShowColumnHeaders,
            ShowRowStripes = pt.ShowRowStripes,
            ShowColumnStripes = pt.ShowColumnStripes,
            ShowFieldHeaders = pt.ShowFieldHeaders,
            ShowContextualTooltips = pt.ShowContextualTooltips,
            ShowPropertiesInTooltips = pt.ShowPropertiesInTooltips,
            ShowClassicLayout = pt.ShowClassicLayout,
            MergeAndCenterLabels = pt.MergeAndCenterLabels,
            ShowItemsWithNoDataOnRows = pt.ShowItemsWithNoDataOnRows,
            ShowItemsWithNoDataOnColumns = pt.ShowItemsWithNoDataOnColumns,
            PageOverThenDown = pt.PageOverThenDown,
            PageWrap = pt.PageWrap,
            EmptyValueText = pt.EmptyValueText,
            ApplyNumberFormats = pt.ApplyNumberFormats,
            ApplyBorderFormats = pt.ApplyBorderFormats,
            ApplyFontFormats = pt.ApplyFontFormats,
            ApplyPatternFormats = pt.ApplyPatternFormats,
            AutofitColumnsOnUpdate = pt.AutofitColumnsOnUpdate,
            PreserveFormattingOnUpdate = pt.PreserveFormattingOnUpdate,
            ShowExpandCollapseButtons = pt.ShowExpandCollapseButtons,
            EnableDrill = pt.EnableDrill,
            AsteriskTotals = pt.AsteriskTotals,
            MultipleFieldFilters = pt.MultipleFieldFilters,
            EnableFieldDialog = pt.EnableFieldDialog,
            EnableFieldProperties = pt.EnableFieldProperties,
            EnableDataValueEditing = pt.EnableDataValueEditing,
            PrintTitles = pt.PrintTitles,
            PrintExpandCollapseButtons = pt.PrintExpandCollapseButtons,
            AltTextTitle = pt.AltTextTitle,
            AltTextDescription = pt.AltTextDescription,
            DataCaption = pt.DataCaption,
            GrandTotalCaption = pt.GrandTotalCaption,
            MissingCaption = pt.MissingCaption,
            ErrorCaption = pt.ErrorCaption
        };
        clonedPt.RowFields.AddRange(pt.RowFields);
        clonedPt.ColumnFields.AddRange(pt.ColumnFields);
        clonedPt.PageFields.AddRange(pt.PageFields);
        clonedPt.DataFields.AddRange(pt.DataFields);
        clonedPt.CalculatedFields.AddRange(pt.CalculatedFields);
        clonedPt.CalculatedItems.AddRange(pt.CalculatedItems);
        clonedPt.LabelFilters.AddRange(pt.LabelFilters);
        clonedPt.ValueFilters.AddRange(pt.ValueFilters);
        clonedPt.Sorts.AddRange(pt.Sorts);
        return clonedPt;
    }

    private static StructuredTableModel CloneStructuredTable(StructuredTableModel table, SheetId newId)
    {
        var clonedTable = new StructuredTableModel
        {
            Id = table.Id,
            Name = table.Name,
            DisplayName = table.DisplayName,
            Range = RemapRange(table.Range, newId),
            HasAutoFilter = table.HasAutoFilter,
            TotalsRowShown = table.TotalsRowShown,
            HeaderRowCount = table.HeaderRowCount,
            TotalsRowCount = table.TotalsRowCount,
            InsertRow = table.InsertRow,
            InsertRowShift = table.InsertRowShift,
            Published = table.Published,
            Comment = table.Comment,
            StyleName = table.StyleName,
            ShowFirstColumn = table.ShowFirstColumn,
            ShowLastColumn = table.ShowLastColumn,
            ShowRowStripes = table.ShowRowStripes,
            ShowColumnStripes = table.ShowColumnStripes,
            PackagePart = table.PackagePart,
            NativeSortStateXml = table.NativeSortStateXml,
            NativeAttributes = table.NativeAttributes is null
                ? null
                : new Dictionary<string, string>(table.NativeAttributes, StringComparer.Ordinal),
            NativeChildXmls = table.NativeChildXmls?.ToArray(),
            NativeAutoFilterAttributes = table.NativeAutoFilterAttributes is null
                ? null
                : new Dictionary<string, string>(table.NativeAutoFilterAttributes, StringComparer.Ordinal),
            NativeAutoFilterChildXmls = table.NativeAutoFilterChildXmls?.ToArray(),
            NativeStyleInfoAttributes = table.NativeStyleInfoAttributes is null
                ? null
                : new Dictionary<string, string>(table.NativeStyleInfoAttributes, StringComparer.Ordinal),
            NativeStyleInfoChildXmls = table.NativeStyleInfoChildXmls?.ToArray()
        };
        clonedTable.Columns.AddRange(table.Columns);
        clonedTable.FilterColumns.AddRange(table.FilterColumns.Select(CloneStructuredTableFilterColumn));
        return clonedTable;
    }

    private static StructuredTableFilterColumnModel CloneStructuredTableFilterColumn(StructuredTableFilterColumnModel column) =>
        new(
            column.ColumnId,
            column.Values.ToArray(),
            column.IncludeBlank,
            column.CustomFilters.Select(CloneStructuredTableCustomFilter).ToArray(),
            column.CustomFiltersAnd,
            column.CustomFiltersAndRaw,
            column.NativeCustomFiltersAttributes is null
                ? null
                : new Dictionary<string, string>(column.NativeCustomFiltersAttributes, StringComparer.Ordinal),
            column.NativeFilterXmls.ToArray(),
            column.NativeAttributes is null
                ? null
                : new Dictionary<string, string>(column.NativeAttributes, StringComparer.Ordinal));

    private static StructuredTableCustomFilterModel CloneStructuredTableCustomFilter(StructuredTableCustomFilterModel filter) =>
        new(
            filter.Operator,
            filter.Value,
            filter.NativeAttributes is null
                ? null
                : new Dictionary<string, string>(filter.NativeAttributes, StringComparer.Ordinal));

    private static ConditionalFormat CloneConditionalFormat(ConditionalFormat cf, SheetId newId)
    {
        var clonedFormat = new ConditionalFormat
        {
            AppliesTo            = RemapRange(cf.AppliesTo, newId),
            Priority             = cf.Priority,
            RuleType             = cf.RuleType,
            Operator             = cf.Operator,
            Value1               = cf.Value1,
            Value2               = cf.Value2,
            FormatIfTrue         = cf.FormatIfTrue?.Clone(),
            MinColor             = cf.MinColor,
            MidColor             = cf.MidColor,
            MaxColor             = cf.MaxColor,
            UseThreeColorScale   = cf.UseThreeColorScale,
            MinThresholdType     = cf.MinThresholdType,
            MinThresholdValue    = cf.MinThresholdValue,
            MinThresholdGreaterThanOrEqual = cf.MinThresholdGreaterThanOrEqual,
            MidThresholdType     = cf.MidThresholdType,
            MidThresholdValue    = cf.MidThresholdValue,
            MidThresholdGreaterThanOrEqual = cf.MidThresholdGreaterThanOrEqual,
            MaxThresholdType     = cf.MaxThresholdType,
            MaxThresholdValue    = cf.MaxThresholdValue,
            MaxThresholdGreaterThanOrEqual = cf.MaxThresholdGreaterThanOrEqual,
            DataBarColor         = cf.DataBarColor,
            DataBarMinThresholdType = cf.DataBarMinThresholdType,
            DataBarMinThresholdValue = cf.DataBarMinThresholdValue,
            DataBarMaxThresholdType = cf.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = cf.DataBarMaxThresholdValue,
            DataBarShowValue     = cf.DataBarShowValue,
            DataBarMinLength     = cf.DataBarMinLength,
            DataBarMaxLength     = cf.DataBarMaxLength,
            DataBarGradient      = cf.DataBarGradient,
            DataBarBorder        = cf.DataBarBorder,
            DataBarAxisPosition  = cf.DataBarAxisPosition,
            DataBarAxisColor     = cf.DataBarAxisColor,
            DataBarNegativeFillColor = cf.DataBarNegativeFillColor,
            DataBarNegativeBorderColor = cf.DataBarNegativeBorderColor,
            AboveAverage         = cf.AboveAverage,
            FormulaText          = cf.FormulaText,
            IconSetStyle         = cf.IconSetStyle,
            IconSetShowValue     = cf.IconSetShowValue,
            IconSetReverse       = cf.IconSetReverse,
            TopBottomRank        = cf.TopBottomRank,
            TopBottomPercent     = cf.TopBottomPercent,
            TextRuleText         = cf.TextRuleText,
            DateOccurringPeriod  = cf.DateOccurringPeriod,
            StopIfTrue           = cf.StopIfTrue,
            NativeAttributes     = cf.NativeAttributes,
            NativeChildXmls      = ConditionalFormatNativeMetadata.RemoveX14IdNativeChildXmls(cf.NativeChildXmls),
            NativePayloadAttributes = cf.NativePayloadAttributes,
            NativePayloadChildXmls = cf.NativePayloadChildXmls,
            NativeContainerAttributes = cf.NativeContainerAttributes,
            NativeContainerChildXmls = cf.NativeContainerChildXmls
        };
        clonedFormat.IconSetThresholds.AddRange(cf.IconSetThresholds);
        clonedFormat.IconOverrides.AddRange(cf.IconOverrides);
        return clonedFormat;
    }

    private static DataValidation CloneDataValidation(DataValidation dv, SheetId newId)
    {
        var clone = new DataValidation
        {
            AppliesTo         = RemapRange(dv.AppliesTo, newId),
            Type              = dv.Type,
            Operator          = dv.Operator,
            Formula1          = dv.Formula1,
            Formula2          = dv.Formula2,
            AllowBlank        = dv.AllowBlank,
            ShowDropdown      = dv.ShowDropdown,
            AlertStyle        = dv.AlertStyle,
            ShowInputMessage  = dv.ShowInputMessage,
            ShowErrorMessage  = dv.ShowErrorMessage,
            ErrorTitle        = dv.ErrorTitle,
            ErrorMessage      = dv.ErrorMessage,
            PromptTitle       = dv.PromptTitle,
            PromptMessage     = dv.PromptMessage,
            NativeAttributes  = dv.NativeAttributes,
            NativeChildXmls   = dv.NativeChildXmls,
            NativeContainerAttributes = dv.NativeContainerAttributes,
            NativeContainerChildXmls = dv.NativeContainerChildXmls
        };
        clone.AdditionalRanges.AddRange(dv.AdditionalRanges.Select(range => RemapRange(range, newId)));
        return clone;
    }

    private void CopyLayoutCollectionsTo(Sheet copy)
    {
        foreach (var (col, width) in ColumnWidths)
            copy.ColumnWidths[col] = width;
        foreach (var (row, height) in RowHeights)
            copy.RowHeights[row] = height;

        foreach (var row in HiddenRows)
            copy.HiddenRows.Add(row);
        foreach (var row in FilterHiddenRows)
            copy.FilterHiddenRows.Add(row);
        foreach (var col in HiddenCols)
            copy.HiddenCols.Add(col);

        foreach (var rowBreak in RowPageBreaks)
            copy.RowPageBreaks.Add(rowBreak);
        foreach (var colBreak in ColumnPageBreaks)
            copy.ColumnPageBreaks.Add(colBreak);

        foreach (var (row, level) in RowOutlineLevels)
            copy.RowOutlineLevels[row] = level;
        foreach (var (col, level) in ColOutlineLevels)
            copy.ColOutlineLevels[col] = level;
        foreach (var row in GroupHiddenRows)
            copy.GroupHiddenRows.Add(row);
        foreach (var col in GroupHiddenCols)
            copy.GroupHiddenCols.Add(col);
    }

    private static WorksheetPageBreaksMetadataModel? ClonePageBreaksMetadata(WorksheetPageBreaksMetadataModel? metadata)
    {
        if (metadata is null)
            return null;

        return new WorksheetPageBreaksMetadataModel
        {
            NativeAttributes = new Dictionary<string, string>(metadata.NativeAttributes, StringComparer.Ordinal),
            BreakNativeAttributes = metadata.BreakNativeAttributes.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(pair.Value, StringComparer.Ordinal))
        };
    }

    private static WorksheetSingleXmlCellsModel? CloneSingleXmlCells(WorksheetSingleXmlCellsModel? model)
    {
        if (model is null)
            return null;

        return new WorksheetSingleXmlCellsModel
        {
            NativeAttributes = new Dictionary<string, string>(model.NativeAttributes, StringComparer.Ordinal),
            Cells = model.Cells.Select(cell => new WorksheetSingleXmlCellModel
            {
                Id = cell.Id,
                Reference = cell.Reference,
                XmlCellPropertyId = cell.XmlCellPropertyId,
                NativeAttributes = new Dictionary<string, string>(cell.NativeAttributes, StringComparer.Ordinal)
            }).ToList()
        };
    }

    private static CellAddress RemapAddress(CellAddress address, SheetId id) =>
        new(id, address.Row, address.Col);

    private static GridRange RemapRange(GridRange range, SheetId id) =>
        new(RemapAddress(range.Start, id), RemapAddress(range.End, id));
}
