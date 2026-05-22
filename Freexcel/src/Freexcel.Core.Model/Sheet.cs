namespace Freexcel.Core.Model;

public sealed record ThreadedComment(string Text, string Author = "Freexcel");

public enum HyperlinkTargetKind
{
    ExistingFileOrWebPage,
    CreateNewDocument,
    PlaceInThisDocument,
    EmailAddress
}

public sealed record HyperlinkMetadata(
    HyperlinkTargetKind LinkType = HyperlinkTargetKind.ExistingFileOrWebPage,
    string ScreenTip = "",
    string Bookmark = "");

/// <summary>
/// Represents a worksheet within a workbook.
/// Storage is Dictionary-based (sparse) per the build plan — NOT sparse columnar.
/// </summary>
public sealed class Sheet
{
    private readonly Dictionary<(uint Row, uint Col), Cell> _cells = [];
    private readonly Dictionary<(uint Row, uint Col), ScalarValue> _spillValues = [];
    private readonly Dictionary<(uint Row, uint Col), (uint Rows, uint Cols)> _spillAnchors = [];
    private readonly Dictionary<(uint Row, uint Col), StyleId> _styleOnly = [];
    private Dictionary<(uint Row, uint Col), GridRange>? _mergeIndex;

    /// <summary>Unique identifier for this sheet.</summary>
    public SheetId Id { get; }

    /// <summary>Display name of the sheet (shown on tab).</summary>
    public string Name { get; set; }

    /// <summary>Column widths override (1-based column index → width in characters).</summary>
    public Dictionary<uint, double> ColumnWidths { get; } = [];

    /// <summary>Row heights override (1-based row index → height in pixels).</summary>
    public Dictionary<uint, double> RowHeights { get; } = [];

    /// <summary>Default column width in characters.</summary>
    public double DefaultColumnWidth { get; set; } = 8.43;

    /// <summary>Default row height in pixels.</summary>
    public double DefaultRowHeight { get; set; } = 20.0;

    /// <summary>Number of rows frozen at the top (0 = none).</summary>
    public uint FrozenRows { get; set; } = 0;

    /// <summary>Number of columns frozen at the left (0 = none).</summary>
    public uint FrozenCols { get; set; } = 0;

    /// <summary>First row below a split pane, or null when no horizontal split is active.</summary>
    public uint? SplitRow { get; set; }

    /// <summary>First column to the right of a split pane, or null when no vertical split is active.</summary>
    public uint? SplitColumn { get; set; }

    /// <summary>Saved top visible row from the worksheet view, when present.</summary>
    public uint? ViewTopRow { get; set; }

    /// <summary>Saved left visible column from the worksheet view, when present.</summary>
    public uint? ViewLeftCol { get; set; }

    /// <summary>Saved active cell row from the worksheet view, when present.</summary>
    public uint? ActiveRow { get; set; }

    /// <summary>Saved active cell column from the worksheet view, when present.</summary>
    public uint? ActiveCol { get; set; }

    /// <summary>Optional worksheet print area. Null means print the used range.</summary>
    public GridRange? PrintArea { get; set; }

    /// <summary>Worksheet page orientation used for print preview/export.</summary>
    public WorksheetPageOrientation PageOrientation { get; set; } = WorksheetPageOrientation.Portrait;

    /// <summary>Worksheet paper size used for print preview/export.</summary>
    public WorksheetPaperSize PaperSize { get; set; } = WorksheetPaperSize.A4;

    /// <summary>Worksheet page margins in inches.</summary>
    public WorksheetPageMargins PageMargins { get; set; } = WorksheetPageMargins.Narrow;

    /// <summary>Distance from the page top to the printed header, in inches.</summary>
    public double HeaderMargin { get; set; } = 0.3;

    /// <summary>Distance from the page bottom to the printed footer, in inches.</summary>
    public double FooterMargin { get; set; } = 0.3;

    /// <summary>Whether gridlines are printed for this worksheet.</summary>
    public bool PrintGridlines { get; set; }

    /// <summary>Whether row and column headings are printed for this worksheet.</summary>
    public bool PrintHeadings { get; set; }

    /// <summary>Worksheet print scaling settings.</summary>
    public WorksheetScaleToFit ScaleToFit { get; set; } = WorksheetScaleToFit.Default;

    /// <summary>Rows repeated at the top of every printed page.</summary>
    public WorksheetRepeatRange? PrintTitleRows { get; set; }

    /// <summary>Columns repeated at the left of every printed page.</summary>
    public WorksheetRepeatRange? PrintTitleColumns { get; set; }

    /// <summary>Worksheet printed page header text.</summary>
    public WorksheetHeaderFooter PageHeader { get; set; } = new("", "", "");

    /// <summary>Pictures used by the left, center, and right page header sections.</summary>
    public WorksheetHeaderFooterPictureSet PageHeaderPictures { get; set; } = WorksheetHeaderFooterPictureSet.Empty;

    /// <summary>Worksheet printed page footer text.</summary>
    public WorksheetHeaderFooter PageFooter { get; set; } = new("", "", "");

    /// <summary>Pictures used by the left, center, and right page footer sections.</summary>
    public WorksheetHeaderFooterPictureSet PageFooterPictures { get; set; } = WorksheetHeaderFooterPictureSet.Empty;

    /// <summary>Optional first-page header used when different first-page headers/footers are enabled.</summary>
    public WorksheetHeaderFooter FirstPageHeader { get; set; } = new("", "", "");

    /// <summary>Pictures used by first-page header sections.</summary>
    public WorksheetHeaderFooterPictureSet FirstPageHeaderPictures { get; set; } = WorksheetHeaderFooterPictureSet.Empty;

    /// <summary>Optional first-page footer used when different first-page headers/footers are enabled.</summary>
    public WorksheetHeaderFooter FirstPageFooter { get; set; } = new("", "", "");

    /// <summary>Pictures used by first-page footer sections.</summary>
    public WorksheetHeaderFooterPictureSet FirstPageFooterPictures { get; set; } = WorksheetHeaderFooterPictureSet.Empty;

    /// <summary>Optional even-page header used when different odd/even headers/footers are enabled.</summary>
    public WorksheetHeaderFooter EvenPageHeader { get; set; } = new("", "", "");

    /// <summary>Pictures used by even-page header sections.</summary>
    public WorksheetHeaderFooterPictureSet EvenPageHeaderPictures { get; set; } = WorksheetHeaderFooterPictureSet.Empty;

    /// <summary>Optional even-page footer used when different odd/even headers/footers are enabled.</summary>
    public WorksheetHeaderFooter EvenPageFooter { get; set; } = new("", "", "");

    /// <summary>Pictures used by even-page footer sections.</summary>
    public WorksheetHeaderFooterPictureSet EvenPageFooterPictures { get; set; } = WorksheetHeaderFooterPictureSet.Empty;

    /// <summary>Whether the first printed page uses separate header/footer text.</summary>
    public bool DifferentFirstPageHeaderFooter { get; set; }

    /// <summary>Whether even printed pages use separate header/footer text from odd pages.</summary>
    public bool DifferentOddEvenHeaderFooter { get; set; }

    /// <summary>Whether headers and footers scale with worksheet print scaling.</summary>
    public bool HeaderFooterScaleWithDocument { get; set; } = true;

    /// <summary>Whether headers and footers align with the configured page margins.</summary>
    public bool HeaderFooterAlignWithMargins { get; set; } = true;

    /// <summary>Whether the printed grid is centered horizontally within the printable page area.</summary>
    public bool CenterHorizontallyOnPage { get; set; }

    /// <summary>Whether the printed grid is centered vertically within the printable page area.</summary>
    public bool CenterVerticallyOnPage { get; set; }

    /// <summary>Order used when printing multi-page worksheets.</summary>
    public WorksheetPageOrder PageOrder { get; set; } = WorksheetPageOrder.DownThenOver;

    /// <summary>Optional first printed page number. Null means automatic numbering from 1.</summary>
    public int? FirstPageNumber { get; set; }

    /// <summary>Whether the worksheet should be printed in black and white.</summary>
    public bool PrintBlackAndWhite { get; set; }

    /// <summary>Whether the worksheet should be printed in draft quality.</summary>
    public bool PrintDraftQuality { get; set; }

    /// <summary>Optional worksheet print quality in dots per inch. Null means printer/default quality.</summary>
    public int? PrintQualityDpi { get; set; }

    /// <summary>How formula/cell error values are represented when printing.</summary>
    public WorksheetPrintErrorValue PrintErrorValue { get; set; } = WorksheetPrintErrorValue.Displayed;

    /// <summary>How cell comments are included in printed output.</summary>
    public WorksheetPrintComments PrintComments { get; set; } = WorksheetPrintComments.None;

    /// <summary>Manual row page breaks, stored as the first row after each break.</summary>
    public SortedSet<uint> RowPageBreaks { get; } = [];

    /// <summary>Manual column page breaks, stored as the first column after each break.</summary>
    public SortedSet<uint> ColumnPageBreaks { get; } = [];

    /// <summary>Display-only tiled worksheet background image. It is not printed, matching Excel behavior.</summary>
    public WorksheetBackgroundImage? BackgroundImage { get; set; }

    /// <summary>Worksheet view mode shown in the grid.</summary>
    public WorksheetViewMode ViewMode { get; set; } = WorksheetViewMode.Normal;

    /// <summary>Whether worksheet gridlines are displayed in the editing view.</summary>
    public bool ShowGridlines { get; set; } = true;

    /// <summary>Whether row and column headings are displayed in the editing view.</summary>
    public bool ShowHeadings { get; set; } = true;

    /// <summary>Whether Page Layout rulers are displayed in the editing view.</summary>
    public bool ShowRulers { get; set; } = true;

    /// <summary>Worksheet zoom percentage for the editing view.</summary>
    public int ZoomPercent { get; set; } = 100;

    /// <summary>Whether formulas are displayed in cells instead of their calculated values.</summary>
    public bool ShowFormulas { get; set; }

    /// <summary>Whether Excel should fully recalculate this worksheet when opened.</summary>
    public bool FullCalculationOnLoad { get; set; }

    /// <summary>Worksheet-level phonetic display metadata loaded from XLSX phoneticPr.</summary>
    public WorksheetPhoneticProperties? PhoneticProperties { get; set; }

    /// <summary>True when the sheet is hidden from the worksheet tab strip.</summary>
    public bool IsHidden { get; set; }

    /// <summary>True when the sheet is Excel veryHidden and cannot be shown from the normal sheet-tab UI.</summary>
    public bool IsVeryHidden { get; set; }

    /// <summary>Optional VBA/OOXML sheet code name metadata.</summary>
    public string? CodeName { get; set; }

    /// <summary>Worksheet custom-property metadata loaded from XLSX customPr elements.</summary>
    public List<WorksheetCustomProperty> CustomProperties { get; } = [];

    /// <summary>Optional worksheet tab color.</summary>
    public CellColor? TabColor { get; set; }

    /// <summary>Charts embedded in this sheet.</summary>
    public List<ChartModel> Charts { get; } = [];

    /// <summary>PivotTable metadata loaded from XLSX packages.</summary>
    public List<PivotTableModel> PivotTables { get; } = [];

    /// <summary>Structured Excel table metadata loaded from XLSX packages.</summary>
    public List<StructuredTableModel> StructuredTables { get; } = [];

    /// <summary>Text boxes embedded in this sheet.</summary>
    public List<TextBoxModel> TextBoxes { get; } = [];

    /// <summary>Drawing shapes embedded in this sheet.</summary>
    public List<DrawingShapeModel> DrawingShapes { get; } = [];

    /// <summary>Pictures embedded in this sheet, including pasted cell-range pictures.</summary>
    public List<PictureModel> Pictures { get; } = [];

    /// <summary>Sparklines embedded in cells on this sheet.</summary>
    public List<SparklineModel> Sparklines { get; } = [];

    /// <summary>Conditional formatting rules applied to this sheet, ordered by priority.</summary>
    public List<ConditionalFormat> ConditionalFormats { get; } = [];

    /// <summary>Data validation rules applied to this sheet.</summary>
    public List<DataValidation> DataValidations { get; } = [];

    /// <summary>Set of row numbers manually hidden or imported as hidden (1-based).</summary>
    public HashSet<uint> HiddenRows { get; } = [];

    /// <summary>Set of row numbers hidden by the active filter (1-based). Empty when no filter is active.</summary>
    public HashSet<uint> FilterHiddenRows { get; } = [];

    /// <summary>Set of column numbers that are hidden (1-based).</summary>
    public HashSet<uint> HiddenCols { get; } = [];

    /// <summary>Outline level (1–8) per row. 0 = no grouping.</summary>
    public Dictionary<uint, int> RowOutlineLevels { get; } = [];

    /// <summary>Outline level (1–8) per column. 0 = no grouping.</summary>
    public Dictionary<uint, int> ColOutlineLevels { get; } = [];

    /// <summary>Rows currently collapsed by a group expand/collapse operation.</summary>
    public HashSet<uint> GroupHiddenRows { get; } = [];

    /// <summary>Columns currently collapsed by a group expand/collapse operation.</summary>
    public HashSet<uint> GroupHiddenCols { get; } = [];

    /// <summary>True if the row is hidden by any mechanism (filter, manual, or group collapse).</summary>
    public bool IsRowEffectivelyHidden(uint row) =>
        HiddenRows.Contains(row) || FilterHiddenRows.Contains(row) || GroupHiddenRows.Contains(row);

    /// <summary>True if the column is hidden by any mechanism.</summary>
    public bool IsColEffectivelyHidden(uint col) =>
        HiddenCols.Contains(col) || GroupHiddenCols.Contains(col);

    private readonly List<GridRange> _mergedRegions = [];

    /// <summary>Merged cell regions on this sheet. Each region's top-left cell holds the display value.</summary>
    public IReadOnlyList<GridRange> MergedRegions => _mergedRegions;

    /// <summary>Add a merged region and invalidate the merge index.</summary>
    public void AddMergedRegion(GridRange region) { _mergedRegions.Add(region); _mergeIndex = null; }

    /// <summary>Remove a merged region and invalidate the merge index.</summary>
    public bool RemoveMergedRegion(GridRange region) { var removed = _mergedRegions.Remove(region); if (removed) _mergeIndex = null; return removed; }

    /// <summary>Replace the entire merged-regions list and invalidate the merge index.</summary>
    public void ReplaceMergedRegions(IEnumerable<GridRange> regions)
    {
        // Materialize before clearing to guard against callers passing a lazy LINQ query
        // over MergedRegions itself (would otherwise enumerate an already-emptied list).
        var list = regions is List<GridRange> l ? l : regions.ToList();
        _mergedRegions.Clear();
        _mergedRegions.AddRange(list);
        _mergeIndex = null;
    }

    /// <summary>Cell comments keyed by address.</summary>
    public Dictionary<CellAddress, string> Comments { get; } = [];

    /// <summary>Threaded cell comments keyed by address.</summary>
    public Dictionary<CellAddress, ThreadedComment> ThreadedComments { get; } = [];

    /// <summary>Cell hyperlinks keyed by address. Value is the target URL/location.</summary>
    public Dictionary<CellAddress, string> Hyperlinks { get; } = [];

    /// <summary>Excel hyperlink metadata keyed by address.</summary>
    public Dictionary<CellAddress, HyperlinkMetadata> HyperlinkMetadata { get; } = [];

    /// <summary>True when the sheet is protected against edits.</summary>
    public bool IsProtected { get; set; }

    /// <summary>Password hash for sheet protection. Null means no password required.</summary>
    public string? ProtectionPassword { get; set; }

    /// <summary>Ranges that remain editable while the sheet is protected.</summary>
    public List<GridRange> AllowEditRanges { get; } = [];

    private void EnsureMergeIndex()
    {
        if (_mergeIndex is not null) return;
        _mergeIndex = new Dictionary<(uint, uint), GridRange>(_mergedRegions.Count * 4);
        foreach (var region in _mergedRegions)
            for (var r = region.Start.Row; r <= region.End.Row; r++)
                for (var c = region.Start.Col; c <= region.End.Col; c++)
                    _mergeIndex[(r, c)] = region;
    }

    /// <summary>Returns the merged region that contains <paramref name="addr"/>, or null if not merged.</summary>
    public GridRange? GetMergeRegion(CellAddress addr)
    {
        EnsureMergeIndex();
        return _mergeIndex!.TryGetValue((addr.Row, addr.Col), out var r) ? r : null;
    }

    /// <summary>True if <paramref name="addr"/> is inside any merged region.</summary>
    public bool IsMerged(CellAddress addr) => GetMergeRegion(addr) is not null;

    /// <summary>Returns the style-only override for an empty cell, or null if none exists.</summary>
    public StyleId? GetStyleOnly(uint row, uint col)
        => _styleOnly.TryGetValue((row, col), out var s) ? s : null;

    /// <summary>Sets a style-only override for an empty cell.</summary>
    public void SetStyleOnly(uint row, uint col, StyleId styleId)
        => _styleOnly[(row, col)] = styleId;

    /// <summary>Removes the style-only override for an empty cell.</summary>
    public void ClearStyleOnly(uint row, uint col)
        => _styleOnly.Remove((row, col));

    /// <summary>Enumerates all style-only entries (for empty cells that have been styled).</summary>
    public IEnumerable<((uint Row, uint Col) Key, StyleId StyleId)> GetStyleOnlyEntries()
        => _styleOnly.Select(kv => (kv.Key, kv.Value));

    public Sheet(SheetId id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>Get the cell at the given address, or null if no cell exists there.</summary>
    public Cell? GetCell(uint row, uint col)
    {
        return _cells.GetValueOrDefault((row, col));
    }

    /// <summary>Get the cell at the given address, or null if no cell exists there.</summary>
    public Cell? GetCell(CellAddress address)
    {
        return _cells.GetValueOrDefault((address.Row, address.Col));
    }

    /// <summary>
    /// Set a cell value at the given address. Creates the cell if it doesn't exist.
    /// </summary>
    public void SetCell(CellAddress address, ScalarValue value)
    {
        ClearSpillRange(address);
        if (_cells.TryGetValue((address.Row, address.Col), out var existing))
        {
            existing.Value = value;
            existing.FormulaText = null;
            existing.IgnoreFormulaError = false;
        }
        else
        {
            _cells[(address.Row, address.Col)] = Cell.FromValue(value);
        }
        _styleOnly.Remove((address.Row, address.Col));
    }

    /// <summary>
    /// Set a cell with a formula at the given address.
    /// The value should be computed separately by the calc engine.
    /// </summary>
    public void SetFormula(CellAddress address, string formulaText)
    {
        ClearSpillRange(address);
        if (_cells.TryGetValue((address.Row, address.Col), out var existing))
        {
            existing.FormulaText = formulaText;
            existing.IgnoreFormulaError = false;
        }
        else
        {
            _cells[(address.Row, address.Col)] = Cell.FromFormula(formulaText);
        }
        _styleOnly.Remove((address.Row, address.Col));
    }

    /// <summary>Set a cell directly.</summary>
    public void SetCell(CellAddress address, Cell cell)
    {
        ClearSpillRange(address);
        _cells[(address.Row, address.Col)] = cell;
        _styleOnly.Remove((address.Row, address.Col));
    }

    /// <summary>Remove a cell (clear its contents).</summary>
    public void ClearCell(uint row, uint col)
    {
        ClearSpillRange(new CellAddress(Id, row, col));
        _cells.Remove((row, col));
    }

    /// <summary>Remove a cell at the given address.</summary>
    public void ClearCell(CellAddress address)
    {
        ClearSpillRange(address);
        _cells.Remove((address.Row, address.Col));
    }

    /// <summary>
    /// Returns true if any non-anchor cell in the proposed spill range is occupied by user data
    /// or by a spill value from a different anchor.
    /// </summary>
    public bool IsSpillBlocked(CellAddress anchor, int rows, int cols)
    {
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (r == 0 && c == 0) continue;
                long targetRow = (long)anchor.Row + r;
                long targetCol = (long)anchor.Col + c;
                if (targetRow > CellAddress.MaxRow || targetCol > CellAddress.MaxCol) return true;
                var key = ((uint)targetRow, (uint)targetCol);
                if (_cells.ContainsKey(key)) return true;
                if (_spillValues.ContainsKey(key)) return true;
            }
        return false;
    }

    /// <summary>
    /// Write the spill range for a dynamic-array anchor cell.
    /// Clears any previous spill from this anchor first.
    /// Does NOT check for blockage — call IsSpillBlocked first.
    /// </summary>
    public void SetSpillRange(CellAddress anchor, RangeValue rv)
    {
        ClearSpillRange(anchor);
        int rows = rv.RowCount, cols = rv.ColCount;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (r == 0 && c == 0) continue;
                _spillValues[(anchor.Row + (uint)r, anchor.Col + (uint)c)] = rv.Cells[r, c];
            }
        _spillAnchors[(anchor.Row, anchor.Col)] = ((uint)rows, (uint)cols);
    }

    /// <summary>Remove all spill values written by the given anchor cell's formula.</summary>
    public void ClearSpillRange(CellAddress anchor)
    {
        if (!_spillAnchors.TryGetValue((anchor.Row, anchor.Col), out var extent)) return;
        for (uint r = 0; r < extent.Rows; r++)
            for (uint c = 0; c < extent.Cols; c++)
            {
                if (r == 0 && c == 0) continue;
                _spillValues.Remove((anchor.Row + r, anchor.Col + c));
            }
        _spillAnchors.Remove((anchor.Row, anchor.Col));
    }

    /// <summary>Get the value at a cell address, returning BlankValue if no cell exists.</summary>
    public ScalarValue GetValue(uint row, uint col)
    {
        if (_cells.TryGetValue((row, col), out var cell)) return cell.Value;
        if (_spillValues.TryGetValue((row, col), out var spill)) return spill;
        return BlankValue.Instance;
    }

    /// <summary>Get the value at a cell address, returning BlankValue if no cell exists.</summary>
    public ScalarValue GetValue(CellAddress address)
    {
        return GetValue(address.Row, address.Col);
    }

    /// <summary>Get all non-empty cell positions.</summary>
    public IReadOnlyCollection<(uint Row, uint Col)> GetOccupiedCells()
    {
        return _cells.Keys;
    }

    /// <summary>Get all cells as address-cell pairs.</summary>
    public IEnumerable<(CellAddress Address, Cell Cell)> EnumerateCells()
    {
        foreach (var ((row, col), cell) in _cells)
        {
            yield return (new CellAddress(Id, row, col), cell);
        }
    }

    /// <summary>Total number of non-empty cells.</summary>
    public int CellCount => _cells.Count;

    /// <summary>Get all non-empty cells as a dictionary keyed by CellAddress.</summary>
    public Dictionary<CellAddress, Cell> GetUsedCells()
    {
        var result = new Dictionary<CellAddress, Cell>(_cells.Count);
        foreach (var ((row, col), cell) in _cells)
            result[new CellAddress(Id, row, col)] = cell;
        return result;
    }

    /// <summary>
    /// Get the bounding range of all non-empty cells, or null if the sheet is empty.
    /// </summary>
    public GridRange? GetUsedRange()
    {
        if (_cells.Count == 0)
            return null;

        uint minRow = uint.MaxValue, maxRow = 0, minCol = uint.MaxValue, maxCol = 0;
        foreach (var (row, col) in _cells.Keys)
        {
            if (row < minRow) minRow = row;
            if (row > maxRow) maxRow = row;
            if (col < minCol) minCol = col;
            if (col > maxCol) maxCol = col;
        }

        return new GridRange(
            new CellAddress(Id, minRow, minCol),
            new CellAddress(Id, maxRow, maxCol));
    }

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
            PageOrientation               = PageOrientation,
            PaperSize                     = PaperSize,
            PageMargins                   = PageMargins,
            HeaderMargin                  = HeaderMargin,
            FooterMargin                  = FooterMargin,
            PrintGridlines                = PrintGridlines,
            PrintHeadings                 = PrintHeadings,
            ScaleToFit                    = ScaleToFit,
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
            PrintBlackAndWhite            = PrintBlackAndWhite,
            PrintDraftQuality             = PrintDraftQuality,
            PrintQualityDpi               = PrintQualityDpi,
            PrintErrorValue               = PrintErrorValue,
            PrintComments                 = PrintComments,
            ViewMode                      = ViewMode,
            IsHidden                      = false,
            IsVeryHidden                  = IsVeryHidden,
            CodeName                      = CodeName,
            TabColor                      = TabColor,
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
                ShowSubtotals = pt.ShowSubtotals,
                SubtotalPlacement = pt.SubtotalPlacement,
                ShowRowGrandTotals = pt.ShowRowGrandTotals,
                ShowColumnGrandTotals = pt.ShowColumnGrandTotals,
                RepeatItemLabels = pt.RepeatItemLabels,
                BlankLineAfterItems = pt.BlankLineAfterItems,
                ReportLayout = pt.ReportLayout,
                StyleName = pt.StyleName,
                ShowRowHeaders = pt.ShowRowHeaders,
                ShowColumnHeaders = pt.ShowColumnHeaders,
                ShowRowStripes = pt.ShowRowStripes,
                ShowColumnStripes = pt.ShowColumnStripes,
                EmptyValueText = pt.EmptyValueText
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

public sealed record WorksheetCustomProperty(string Name, int Id);

public sealed record WorksheetPhoneticProperties(string? FontId, string? Type, string? Alignment);

public enum WorksheetViewMode
{
    Normal,
    PageBreakPreview,
    PageLayout
}
