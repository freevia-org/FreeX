using System.Text;
using System.Text.RegularExpressions;

namespace FreeX.Core.Model;

public sealed partial class Sheet
{
    /// <summary>
    /// Creates a deep copy of this sheet with a new <paramref name="newId"/> and <paramref name="newName"/>.
    /// All model-layer properties are copied, including the previously missed fields:
    /// <c>BackgroundImage</c>, <c>RowOutlineLevels</c>, <c>ColOutlineLevels</c>,
    /// <c>GroupHiddenRows</c>, <c>GroupHiddenCols</c>, <c>CollapsedAnchorRows</c>,
    /// <c>CollapsedAnchorCols</c>, <c>SubtotalRows</c>, <c>CommentAuthors</c>, <c>ShownComments</c>,
    /// <c>CellWatchesMetadata</c>, and <c>IgnoredErrorsMetadata</c>.
    /// Drawing collections (Charts, TextBoxes, DrawingShapes, Pictures, Sparklines) are intentionally
    /// left empty; the caller (e.g. <c>DuplicateSheetCommand</c>) is responsible for copying those.
    /// </summary>
    public Sheet Clone(SheetId newId, string newName)
    {
        var copy = new Sheet(newId, newName)
        {
            DefaultColumnWidth            = DefaultColumnWidth,
            DefaultRowHeight              = DefaultRowHeight,
            Kind                          = Kind,
            FrozenRows                    = FrozenRows,
            FrozenCols                    = FrozenCols,
            SplitRow                      = SplitRow,
            SplitColumn                   = SplitColumn,
            ViewTopRow                    = ViewTopRow,
            ViewLeftCol                   = ViewLeftCol,
            ActiveRow                     = ActiveRow,
            ActiveCol                     = ActiveCol,
            IsRightToLeft                 = IsRightToLeft,
            ShowGridlines                 = ShowGridlines,
            ShowHeadings                  = ShowHeadings,
            ShowRulers                    = ShowRulers,
            ZoomPercent                   = ZoomPercent,
            ShowFormulas                  = ShowFormulas,
            ShowZeros                     = ShowZeros,
            FullCalculationOnLoad         = FullCalculationOnLoad,
            PhoneticProperties            = PhoneticProperties,
            AutoFilter                    = CloneAutoFilter(AutoFilter),
            SmartTags                     = SmartTags,
            DataConsolidation             = DataConsolidation,
            SortState                     = SortState,
            SingleXmlCells                = CloneSingleXmlCells(SingleXmlCells),
            AdditionalViews               = AdditionalViews,
            PrimaryViewMetadata           = PrimaryViewMetadata?.Clone(),
            PageOrientation               = PageOrientation,
            PaperSize                     = PaperSize,
            PaperSizeCode                 = PaperSizeCode,
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
            PageMarginsMetadata           = PageMarginsMetadata?.Clone(),
            PrintErrorValue               = PrintErrorValue,
            PrintComments                 = PrintComments,
            LegacyPrintSize               = LegacyPrintSize,
            PrintOptionsMetadata          = PrintOptionsMetadata?.Clone(),
            HeaderFooterMetadata          = HeaderFooterMetadata?.Clone(),
            PageSetupMetadata             = PageSetupMetadata?.Clone(),
            ViewMode                      = ViewMode,
            IsHidden                      = false,
            IsVeryHidden                  = IsVeryHidden,
            CodeName                      = CodeName,
            TabColor                      = TabColor,
            TabThemeColor                 = TabThemeColor,
            OutlineSummaryBelow           = OutlineSummaryBelow,
            OutlineSummaryRight           = OutlineSummaryRight,
            ShowOutlineSymbols            = ShowOutlineSymbols,
            ApplyOutlineStyles            = ApplyOutlineStyles,
            SheetFormatMetadata           = SheetFormatMetadata?.Clone(),
            DimensionMetadata             = DimensionMetadata?.Clone(),
            SheetPropertiesMetadata       = SheetPropertiesMetadata?.Clone(),
            IsProtected                   = IsProtected,
            ProtectionPassword            = ProtectionPassword,
            ProtectionMetadata            = ProtectionMetadata?.Clone(),
            // Previously missed fields:
            BackgroundImage               = BackgroundImage,
            RowPageBreaksMetadata         = ClonePageBreaksMetadata(RowPageBreaksMetadata),
            ColumnPageBreaksMetadata      = ClonePageBreaksMetadata(ColumnPageBreaksMetadata),
            CellWatchesMetadata           = CloneCellWatchesMetadata(CellWatchesMetadata),
            IgnoredErrorsMetadata         = CloneIgnoredErrorsMetadata(IgnoredErrorsMetadata),
        };

        // Multi-area print areas: remap all areas to the new sheet id.
        if (PrintAreas.Count > 0)
            copy.SetPrintAreas(PrintAreas.Select(r => RemapRange(r, newId)));

        CopyLayoutCollectionsTo(copy);
        CopyCellContentTo(copy, newId);

        // Comments and hyperlinks
        foreach (var (address, comment) in Comments)
            copy.Comments[RemapAddress(address, newId)] = comment;
        foreach (var (address, author) in CommentAuthors)
            copy.CommentAuthors[RemapAddress(address, newId)] = author;
        foreach (var address in ShownComments)
            copy.ShownComments.Add(RemapAddress(address, newId));
        foreach (var (address, comment) in ThreadedComments)
            copy.ThreadedComments[RemapAddress(address, newId)] = comment;
        foreach (var (address, hyperlink) in Hyperlinks)
        {
            // R95: a 'Place in This Document' hyperlink target is a sheet-qualified reference
            // (e.g. "Sheet1!A1" or "'Sheet 1'!A1") stored verbatim in this string when no
            // Bookmark is set (see HyperlinkNavigationPlanner). Rebase it onto the copy exactly
            // like ConditionalFormat.FormulaText / DataValidation.Formula1-2 below, or a
            // duplicated sheet's internal link keeps jumping back to the source sheet.
            var linkType = HyperlinkMetadata.TryGetValue(address, out var metaForTarget)
                ? metaForTarget.LinkType
                : HyperlinkTargetKind.ExistingFileOrWebPage;
            copy.Hyperlinks[RemapAddress(address, newId)] = linkType == HyperlinkTargetKind.PlaceInThisDocument
                ? RewriteSameSheetQualifiedFormula(hyperlink, Name, newName)!
                : hyperlink;
        }
        foreach (var (address, metadata) in HyperlinkMetadata)
        {
            // R95: same rebase for the explicit Bookmark field, mirroring RenameSheetCommand's
            // O25/P113 rewrite of this exact field on sheet rename (SheetCommands.cs).
            copy.HyperlinkMetadata[RemapAddress(address, newId)] = metadata.LinkType == HyperlinkTargetKind.PlaceInThisDocument
                ? metadata with { Bookmark = RewriteSameSheetQualifiedFormula(metadata.Bookmark, Name, newName)! }
                : metadata;
        }
        foreach (var (address, runs) in RichTextRuns)
            copy.RichTextRuns[RemapAddress(address, newId)] = runs;
        foreach (var (address, guide) in CellPhoneticGuides)
            copy.CellPhoneticGuides[RemapAddress(address, newId)] = guide;
        // R106-io-hyperlink-range-shift: the key is the original load-time ref string (sheet-
        // independent identity) and is copied verbatim; only the live GridRange value is rebased
        // onto the new sheet id, mirroring RemapRange's use for print areas/allow-edit ranges below.
        foreach (var (originalRef, range) in RangeHyperlinks)
            copy.RangeHyperlinks[originalRef] = RemapRange(range, newId);

        // Allow-edit ranges (protection)
        copy.ProtectionPermissions.Clear();
        foreach (var permission in ProtectionPermissions)
            copy.ProtectionPermissions.Add(permission);
        foreach (var range in AllowEditRanges)
            copy.AllowEditRanges.Add(RemapRange(range, newId));
        foreach (var (range, password) in AllowEditRangePasswords)
            copy.AllowEditRangePasswords[RemapRange(range, newId)] = password;
        foreach (var property in CustomProperties)
            copy.CustomProperties.Add(property);

        foreach (var pt in PivotTables)
            copy.PivotTables.Add(ClonePivotTable(pt, Id, newId));

        foreach (var table in StructuredTables)
            copy.StructuredTables.Add(CloneStructuredTable(table, newId));

        foreach (var cf in ConditionalFormats)
            copy.ConditionalFormats.Add(CloneConditionalFormat(cf, newId, Name, newName));

        foreach (var dv in DataValidations)
            copy.DataValidations.Add(CloneDataValidation(dv, newId, Name, newName));

        // Note: Charts, TextBoxes, DrawingShapes, Pictures, and Sparklines are intentionally
        // left empty here. The caller must copy those drawing collections separately.

        return copy;
    }

    private void CopyCellContentTo(Sheet copy, SheetId newId)
    {
        foreach (var (address, cell) in EnumerateCells())
        {
            var clonedCell = cell.Clone();

            // R104: rebase an explicit same-sheet-qualified formula reference (e.g. "Sheet1!A1"
            // typed/pasted on Sheet1 itself) onto the copy, mirroring the same rebase already
            // applied to ConditionalFormat.FormulaText / DataValidation.Formula1-2 (below) and to
            // hyperlink targets/bookmarks (above) -- otherwise RecalcEngine resolves the
            // unrewritten qualifier by live sheet-name lookup and the duplicate's formula keeps
            // pointing at the ORIGINAL sheet instead of following the copy, unlike every other
            // same-sheet-qualified formula surface in this method.
            if (clonedCell.FormulaText is { } formulaText)
            {
                var rewritten = RewriteSameSheetQualifiedFormula(formulaText, Name, copy.Name);
                if (!string.Equals(rewritten, formulaText, StringComparison.Ordinal))
                {
                    var arrayMode = clonedCell.ArrayMode;
                    var legacyArrayRows = clonedCell.LegacyArrayRows;
                    var legacyArrayCols = clonedCell.LegacyArrayCols;
                    // The FormulaText setter clears CachedAst (so the rewritten text re-parses
                    // instead of reusing the stale AST built from the original text) but also
                    // resets ArrayMode/LegacyArrayRows/LegacyArrayCols to "freshly authored"
                    // defaults, which would silently discard a loaded legacy CSE array formula's
                    // extent -- restore them so this rebase doesn't change anything else about
                    // the cell.
                    clonedCell.FormulaText = rewritten;
                    clonedCell.ArrayMode = arrayMode;
                    clonedCell.LegacyArrayRows = legacyArrayRows;
                    clonedCell.LegacyArrayCols = legacyArrayCols;
                }
            }

            copy.SetCell(RemapAddress(address, newId), clonedCell);
        }

        foreach (var ((row, col), styleId) in GetStyleOnlyEntries())
            copy.SetStyleOnly(row, col, styleId);

        copy.ReplaceMergedRegions(MergedRegions.Select(r => RemapRange(r, newId)));
        CopySpillStateTo(copy);
    }

    /// <summary>
    /// Copy dynamic-array spill state (spilled values, anchor extents, and provisional spill-cell
    /// ownership) to a clone. Keys are (row, col) within the sheet and values are immutable, so no
    /// SheetId remapping is required. Without this a duplicated sheet shows blank spill ranges and
    /// loses <see cref="Sheet.TryGetSpillExtent"/> info until a recalculation reruns.
    /// </summary>
    private void CopySpillStateTo(Sheet copy)
    {
        foreach (var (key, value) in _spillValues)
            copy._spillValues[key] = value;

        foreach (var (key, extent) in _spillAnchors)
            copy._spillAnchors[key] = extent;

        if (_provisionalSpillCells is { Count: > 0 })
        {
            copy._provisionalSpillCells ??= [];
            foreach (var (key, owner) in _provisionalSpillCells)
                copy._provisionalSpillCells[key] = owner;
        }
    }

    private static PivotTableModel ClonePivotTable(PivotTableModel pt, SheetId sourceSheetId, SheetId newId)
    {
        var clonedPt = new PivotTableModel
        {
            Name        = pt.Name,
            CacheId     = pt.CacheId,
            // Only remap SourceRange onto the copy when it actually points at the sheet being
            // duplicated -- a cross-sheet SourceRange (e.g. a pivot table on its own sheet reading
            // data from a Data sheet, Excel's normal "PivotTable on new sheet, data on the original
            // sheet" pattern) must keep pointing at the original source sheet, matching Excel's
            // Duplicate Sheet behavior (only same-sheet references travel with the copy) and
            // mirroring DuplicateSheetDrawingCloner.CloneChart's identical DataRange handling.
            SourceRange = pt.SourceRange.Start.Sheet == sourceSheetId
                ? RemapRange(pt.SourceRange, newId)
                : pt.SourceRange,
            // TargetRange/LastRenderedRange describe where the pivot itself is RENDERED, which
            // always lives on this sheet -- so these always travel with the copy.
            TargetRange = RemapRange(pt.TargetRange, newId),
            LastRenderedRange = pt.LastRenderedRange is { } lastRenderedRange ? RemapRange(lastRenderedRange, newId) : null,
            // R127B-model-pivot-clone-packagepart: deliberately NOT pt.PackagePart. PackagePart is
            // the exact package-part path (e.g. "xl/pivotTables/pivotTable1.xml") the SOURCE pivot
            // was loaded from/last saved to; copying it verbatim would give the clone and the source
            // pivot the identical part path. XlsxFileAdapter's patch-save eligibility guard
            // (TryAddPatchSafePivotPackagePaths) keys a dictionary by this exact path across every
            // pivot table on a sheet, so two distinct PivotTableModel instances sharing one path
            // throws a duplicate-key ArgumentException the first time either sheet is patch-saved.
            // Leaving it empty matches the established "brand-new pivot has no PackagePart yet"
            // convention (see AddPivotTableCommand-created pivots and
            // XlsxFileAdapter.SavePostProcessing's IsNullOrWhiteSpace(pivot.PackagePart) skips): the
            // patch-save guard gracefully treats an empty PackagePart as "needs a full regenerate"
            // rather than colliding, and the full-write path (XlsxPivotTableWriter) always mints a
            // fresh part path regardless of this field.
            PackagePart = string.Empty,
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

    /// <summary>
    /// R17-table-listobject-3: re-numbers and renames the structured table at <paramref name="index"/>
    /// on this sheet in place, preserving every other piece of table metadata (columns, filters,
    /// native XML, etc.). Used by <c>DuplicateSheetCommand</c> to give a cloned sheet's tables a
    /// workbook-unique identity — <see cref="Clone"/> otherwise copies <see cref="StructuredTables"/>
    /// verbatim, leaving the duplicate sharing the same table Id/Name as the source.
    /// </summary>
    public void ReidentifyStructuredTable(int index, int newTableId, string newName)
    {
        var table = StructuredTables[index];
        StructuredTables[index] = CloneStructuredTable(table, table.Range.Start.Sheet, newTableId, newName);
    }

    private static StructuredTableModel CloneStructuredTable(
        StructuredTableModel table,
        SheetId newId,
        int? overrideTableId = null,
        string? overrideName = null)
    {
        var clonedTable = new StructuredTableModel
        {
            Id = overrideTableId ?? table.Id,
            Name = overrideName ?? table.Name,
            DisplayName = overrideName ?? table.DisplayName,
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
            // R128-model-table-clone-packagepart: deliberately NOT table.PackagePart, mirroring the
            // R127B fix applied to PivotTableModel above (and PivotCacheModel/SlicerModel/TimelineModel
            // in DuplicateSheetCommand.cs / DuplicateSheetDrawingCloner.cs). PackagePart is the exact
            // package-part path (e.g. "xl/tables/table1.xml") the SOURCE table was loaded from/last
            // saved to; copying it verbatim would give the clone and the source table the identical
            // part path, so XlsxStructuredTableWriter.Save's preserved-path branch (the "else" at
            // line ~106, which has no collision guard because a preserved path is assumed unique)
            // writes both tables to the same zip entry and whichever is processed second silently
            // clobbers the other's saved <table> XML on the very next full save -- even though both
            // sheets keep their own worksheet relationship (and so both still resolve) pointing at
            // that one shared, now-wrong physical part. Leaving it empty makes
            // XlsxStructuredTableWriter mint a fresh "xl/tables/tableN.xml" for the clone instead.
            PackagePart = string.Empty,
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

    private static ConditionalFormat CloneConditionalFormat(ConditionalFormat cf, SheetId newId, string sourceSheetName, string newSheetName)
    {
        IReadOnlyList<GridRange>? remappedAdditional = cf.AdditionalRanges is null
            ? null
            : cf.AdditionalRanges.Select(r => RemapRange(r, newId)).ToList();

        var clonedFormat = new ConditionalFormat
        {
            AppliesTo            = RemapRange(cf.AppliesTo, newId),
            AdditionalRanges     = remappedAdditional,
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
            EqualAverage         = cf.EqualAverage,
            StdDevCount          = cf.StdDevCount,
            FormulaText          = RewriteSameSheetQualifiedFormula(cf.FormulaText, sourceSheetName, newSheetName),
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

    private static DataValidation CloneDataValidation(DataValidation dv, SheetId newId, string sourceSheetName, string newSheetName)
    {
        var clone = dv.CloneWithNewIdentity(
            RemapRange(dv.AppliesTo, newId),
            dv.AdditionalRanges.Select(range => RemapRange(range, newId)));
        clone.Formula1 = RewriteSameSheetQualifiedFormula(dv.Formula1, sourceSheetName, newSheetName);
        clone.Formula2 = RewriteSameSheetQualifiedFormula(dv.Formula2, sourceSheetName, newSheetName);
        return clone;
    }

    /// <summary>
    /// R26-sheet-lifecycle-deep-2 / R27-meta-4: rebases an explicit same-sheet-qualified reference
    /// (e.g. <c>Sheet1!A1</c> or <c>'Sheet 1'!A1</c>) embedded in a Conditional Format / Data
    /// Validation formula so a duplicated sheet's rule follows the COPY instead of continuing
    /// to point at the source sheet by name — matching Excel's Move-or-Copy behavior and
    /// mirroring how <c>DuplicateSheetDrawingCloner</c> already rebases the analogous verbatim
    /// chart-range text. <see cref="ConditionalFormat.FormulaText"/> / <see cref="DataValidation.Formula1"/> /
    /// <see cref="DataValidation.Formula2"/> are stored as raw strings (Core.Model has no formula
    /// parser), so this is a targeted text substitution rather than a full AST rewrite: only
    /// literal occurrences of the SOURCE sheet's own name used as a reference qualifier are
    /// replaced. Qualifiers naming any OTHER sheet, and unqualified references (which already
    /// implicitly follow the copy since they mean "this sheet"), are left untouched. The
    /// substitution is also skipped inside double-quoted text literals (Excel's <c>""</c>-escaped
    /// string syntax) so a sheet name that merely occurs as ordinary quoted text — e.g.
    /// <c>=EXACT(A1,"Data!")</c> — is never mistaken for a reference qualifier and rewritten.
    /// </summary>
    private static string? RewriteSameSheetQualifiedFormula(string? formula, string sourceSheetName, string newSheetName)
    {
        if (string.IsNullOrEmpty(formula) || string.Equals(sourceSheetName, newSheetName, StringComparison.Ordinal))
            return formula;

        var newQualifier = SheetNameFormatter.QuoteIfNeeded(newSheetName) + "!";

        // Already-quoted source qualifier, e.g. 'Sheet 1'!
        var quotedOldQualifier = "'" + sourceSheetName.Replace("'", "''") + "'!";

        // Bare (unquoted) source qualifier, e.g. Sheet1! — guarded so it can't match a fragment
        // of a longer identifier/qualifier (e.g. a source name of "Sheet1" must not match inside
        // "OtherSheet1!") or re-touch the quoted form already handled above. This ALSO already
        // correctly rebases the source name when it is the END endpoint of a bare 3-D sheet span
        // (e.g. "Other:Sheet1!") since the ':' immediately before the name isn't excluded by the
        // lookbehind — only the START endpoint of a span (e.g. "Sheet1:Other!", where the source
        // name is followed by ':' rather than '!') needs the dedicated handling below.
        var pattern = "(?<![A-Za-z0-9_.'])" + Regex.Escape(sourceSheetName) + "!";

        // R106: 3-D sheet-span endpoint handling (e.g. "=SUM(Sheet1:Sheet3!A1)" authored on
        // Sheet1 itself). A span qualifier is either fully bare ("Sheet1:Sheet3!") when NEITHER
        // endpoint name needs quoting, or wholly quoted as a single token ("'Sheet1:Last
        // Sheet'!") when EITHER does — Excel never quotes just one endpoint of a span, see
        // FormulaSerializer.WriteRangeRef/WriteSheetSpanName — so all four combinations (source as
        // span start/end, crossed with bare/quoted span text) are matched below and the WHOLE
        // qualifier is rebuilt with BuildSpanQualifier (fresh quoting decision based on the final
        // start/end names), rather than text-substituting just the source name's fragment in
        // place. The latter would produce a malformed mixed qualifier like "Other:'New Name'!"
        // whenever the new copy's name needs quoting but the original span didn't. This mirrors
        // FormulaRewriter.RewriteRange's AST-based 3-D span endpoint rebase for RenameSheetOp
        // (FormulaRewriter.cs:310-336), which already treats a same-sheet span endpoint the same
        // as a simple same-sheet qualifier for this class of sheet-identity-changing operation.
        var escapedSource = sourceSheetName.Replace("'", "''", StringComparison.Ordinal);
        var bareSpanStart = new Regex(
            "(?<![A-Za-z0-9_.'])" + Regex.Escape(sourceSheetName) + @":(?<other>[A-Za-z0-9_.]+)!",
            RegexOptions.IgnoreCase);
        var bareSpanEnd = new Regex(
            @"(?<![A-Za-z0-9_.'])(?<other>[A-Za-z0-9_.]+):" + Regex.Escape(sourceSheetName) + "!",
            RegexOptions.IgnoreCase);
        var quotedSpanStart = new Regex(
            "'" + Regex.Escape(escapedSource) + @":(?<other>(?:[^']|'')*)'!",
            RegexOptions.IgnoreCase);
        var quotedSpanEnd = new Regex(
            @"'(?<other>(?:[^']|'')*):" + Regex.Escape(escapedSource) + "'!",
            RegexOptions.IgnoreCase);

        return ReplaceOutsideStringLiterals(formula, segment =>
        {
            // Span forms are handled first (on the untouched segment) so the later plain-quoted /
            // bare single-qualifier substitutions below never get a chance to partially rewrite
            // just one side of an already-handled span.
            var rewritten = quotedSpanStart.Replace(segment, m =>
                BuildSpanQualifier(newSheetName, UnescapeQuotedSheetName(m.Groups["other"].Value)) + "!");
            rewritten = quotedSpanEnd.Replace(rewritten, m =>
                BuildSpanQualifier(UnescapeQuotedSheetName(m.Groups["other"].Value), newSheetName) + "!");
            rewritten = bareSpanStart.Replace(rewritten, m =>
                BuildSpanQualifier(newSheetName, m.Groups["other"].Value) + "!");
            rewritten = bareSpanEnd.Replace(rewritten, m =>
                BuildSpanQualifier(m.Groups["other"].Value, newSheetName) + "!");

            rewritten = rewritten.Replace(quotedOldQualifier, newQualifier, StringComparison.OrdinalIgnoreCase);
            return Regex.Replace(rewritten, pattern, _ => newQualifier, RegexOptions.IgnoreCase);
        });
    }

    private static string UnescapeQuotedSheetName(string escaped) =>
        escaped.Replace("''", "'", StringComparison.Ordinal);

    /// <summary>
    /// Builds a 3-D sheet-span qualifier ("Start:End" or, if either name needs quoting, the
    /// whole-span-quoted "'Start:End'" form), mirroring <c>FormulaSerializer.WriteSheetSpanName</c>
    /// so a rebased span's text stays in the same canonical shape the formula engine itself emits.
    /// </summary>
    private static string BuildSpanQualifier(string startName, string endName)
    {
        if (!SheetNameFormatter.NeedsQuoting(startName) && !SheetNameFormatter.NeedsQuoting(endName))
            return startName + ":" + endName;

        return "'" + startName.Replace("'", "''", StringComparison.Ordinal) + ":" +
               endName.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// Applies <paramref name="transform"/> to every part of <paramref name="formula"/> that falls
    /// OUTSIDE an Excel double-quoted text literal (<c>"..."</c>, with <c>""</c> as an escaped quote
    /// character inside the literal), leaving the contents of any string literal byte-for-byte
    /// untouched. This is what keeps <see cref="RewriteSameSheetQualifiedFormula"/> from reaching
    /// into ordinary quoted text that merely happens to contain a matching substring.
    /// </summary>
    private static string ReplaceOutsideStringLiterals(string formula, Func<string, string> transform)
    {
        var result = new StringBuilder(formula.Length);
        var i = 0;
        while (i < formula.Length)
        {
            if (formula[i] == '"')
            {
                var literalStart = i;
                i++;
                while (i < formula.Length)
                {
                    if (formula[i] == '"')
                    {
                        if (i + 1 < formula.Length && formula[i + 1] == '"')
                        {
                            i += 2; // "" escaped quote character inside the literal
                            continue;
                        }
                        i++; // closing quote
                        break;
                    }
                    i++;
                }
                result.Append(formula, literalStart, i - literalStart);
                continue;
            }

            var segmentStart = i;
            while (i < formula.Length && formula[i] != '"')
                i++;
            result.Append(transform(formula[segmentStart..i]));
        }

        return result.ToString();
    }

    private void CopyLayoutCollectionsTo(Sheet copy)
    {
        foreach (var (col, width) in ColumnWidths)
            copy.ColumnWidths[col] = width;
        foreach (var (row, height) in RowHeights)
            copy.RowHeights[row] = height;
        foreach (var (col, styleId) in ColumnStyles)
            copy.ColumnStyles[col] = styleId;
        foreach (var (row, styleId) in RowStyles)
            copy.RowStyles[row] = styleId;

        foreach (var row in HiddenRows)
            copy.HiddenRows.Add(row);
        foreach (var row in FilterHiddenRows)
            copy.FilterHiddenRows.Add(row);
        foreach (var (col, allowedValues) in ActiveValueFilterColumns)
            copy.ActiveValueFilterColumns[col] = [.. allowedValues];
        foreach (var row in ValueFilterHiddenRows)
            copy.ValueFilterHiddenRows.Add(row);
        foreach (var (col, ownedRows) in ColumnFilterOwnedRows)
            copy.ColumnFilterOwnedRows[col] = [.. ownedRows];
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
        foreach (var row in CollapsedAnchorRows)
            copy.CollapsedAnchorRows.Add(row);
        foreach (var col in CollapsedAnchorCols)
            copy.CollapsedAnchorCols.Add(col);

        foreach (var row in SubtotalRows)
            copy.SubtotalRows.Add(row);
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

    private static WorksheetCellWatchesMetadataModel? CloneCellWatchesMetadata(WorksheetCellWatchesMetadataModel? metadata)
    {
        if (metadata is null)
            return null;

        return new WorksheetCellWatchesMetadataModel
        {
            NativeAttributes = new Dictionary<string, string>(metadata.NativeAttributes, StringComparer.Ordinal),
            WatchNativeAttributes = metadata.WatchNativeAttributes.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(pair.Value, StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static WorksheetIgnoredErrorsMetadataModel? CloneIgnoredErrorsMetadata(WorksheetIgnoredErrorsMetadataModel? metadata)
    {
        if (metadata is null)
            return null;

        return new WorksheetIgnoredErrorsMetadataModel
        {
            NativeAttributes = new Dictionary<string, string>(metadata.NativeAttributes, StringComparer.Ordinal),
            ErrorNativeAttributes = metadata.ErrorNativeAttributes.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(pair.Value, StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase)
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
