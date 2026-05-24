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
            AutoFilter                    = AutoFilter,
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
            PrintErrorValue               = PrintErrorValue,
            PrintComments                 = PrintComments,
            ViewMode                      = ViewMode,
            IsHidden                      = false,
            IsVeryHidden                  = IsVeryHidden,
            CodeName                      = CodeName,
            TabColor                      = TabColor,
            OutlineSummaryBelow           = OutlineSummaryBelow,
            OutlineSummaryRight           = OutlineSummaryRight,
            ShowOutlineSymbols            = ShowOutlineSymbols,
            ApplyOutlineStyles            = ApplyOutlineStyles,
            IsProtected                   = IsProtected,
            ProtectionPassword            = ProtectionPassword,
            // Previously missed fields:
            BackgroundImage               = BackgroundImage,
        };

        // Collections: column/row dimensions
        foreach (var (col, width) in ColumnWidths)
            copy.ColumnWidths[col] = width;
        foreach (var (row, height) in RowHeights)
            copy.RowHeights[row] = height;

        // Hidden rows/cols
        foreach (var row in HiddenRows)
            copy.HiddenRows.Add(row);
        foreach (var row in FilterHiddenRows)
            copy.FilterHiddenRows.Add(row);
        foreach (var col in HiddenCols)
            copy.HiddenCols.Add(col);

        // Page breaks
        foreach (var rowBreak in RowPageBreaks)
            copy.RowPageBreaks.Add(rowBreak);
        foreach (var colBreak in ColumnPageBreaks)
            copy.ColumnPageBreaks.Add(colBreak);

        // Previously missed: outline levels and group-hidden rows/cols
        foreach (var (row, level) in RowOutlineLevels)
            copy.RowOutlineLevels[row] = level;
        foreach (var (col, level) in ColOutlineLevels)
            copy.ColOutlineLevels[col] = level;
        foreach (var row in GroupHiddenRows)
            copy.GroupHiddenRows.Add(row);
        foreach (var col in GroupHiddenCols)
            copy.GroupHiddenCols.Add(col);

        // Cells (deep copy)
        foreach (var (address, cell) in EnumerateCells())
            copy.SetCell(RemapAddress(address, newId), cell.Clone());

        // Style-only overrides for empty cells
        foreach (var ((row, col), styleId) in GetStyleOnlyEntries())
            copy.SetStyleOnly(row, col, styleId);

        // Merged regions
        copy.ReplaceMergedRegions(MergedRegions.Select(r => RemapRange(r, newId)));

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

        // Pivot tables
        foreach (var pt in PivotTables)
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
            copy.PivotTables.Add(clonedPt);
        }

        // Structured tables
        foreach (var table in StructuredTables)
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
                NativeAttributes = table.NativeAttributes,
                NativeChildXmls = table.NativeChildXmls,
                NativeAutoFilterAttributes = table.NativeAutoFilterAttributes,
                NativeAutoFilterChildXmls = table.NativeAutoFilterChildXmls,
                NativeStyleInfoAttributes = table.NativeStyleInfoAttributes,
                NativeStyleInfoChildXmls = table.NativeStyleInfoChildXmls
            };
            clonedTable.Columns.AddRange(table.Columns);
            clonedTable.FilterColumns.AddRange(table.FilterColumns);
            copy.StructuredTables.Add(clonedTable);
        }

        // Conditional formats
        foreach (var cf in ConditionalFormats)
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
                MidThresholdType     = cf.MidThresholdType,
                MidThresholdValue    = cf.MidThresholdValue,
                MaxThresholdType     = cf.MaxThresholdType,
                MaxThresholdValue    = cf.MaxThresholdValue,
                DataBarColor         = cf.DataBarColor,
                DataBarMinThresholdType = cf.DataBarMinThresholdType,
                DataBarMinThresholdValue = cf.DataBarMinThresholdValue,
                DataBarMaxThresholdType = cf.DataBarMaxThresholdType,
                DataBarMaxThresholdValue = cf.DataBarMaxThresholdValue,
                DataBarShowValue     = cf.DataBarShowValue,
                DataBarMinLength     = cf.DataBarMinLength,
                DataBarMaxLength     = cf.DataBarMaxLength,
                DataBarGradient      = cf.DataBarGradient,
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
                NativeChildXmls      = cf.NativeChildXmls,
                NativePayloadAttributes = cf.NativePayloadAttributes,
                NativePayloadChildXmls = cf.NativePayloadChildXmls,
                NativeContainerAttributes = cf.NativeContainerAttributes,
                NativeContainerChildXmls = cf.NativeContainerChildXmls
            };
            clonedFormat.IconSetThresholds.AddRange(cf.IconSetThresholds);
            copy.ConditionalFormats.Add(clonedFormat);
        }

        // Data validations
        foreach (var dv in DataValidations)
            copy.DataValidations.Add(new DataValidation
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
            });

        // Note: Charts, TextBoxes, DrawingShapes, Pictures, and Sparklines are intentionally
        // left empty here. The caller must copy those drawing collections separately.

        return copy;

        static CellAddress RemapAddress       (CellAddress a, SheetId id) => new(id, a.Row, a.Col);
        static GridRange   RemapRange         (GridRange   r, SheetId id) =>
            new(RemapAddress(r.Start, id), RemapAddress(r.End, id));
    }
}
