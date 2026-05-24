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
public sealed partial class Sheet
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

    /// <summary>Actions that remain available while the sheet is protected.</summary>
    public List<SheetProtectionPermission> ProtectionPermissions { get; } =
    [
        SheetProtectionPermission.SelectLockedCells,
        SheetProtectionPermission.SelectUnlockedCells
    ];

    /// <summary>Ranges that remain editable while the sheet is protected.</summary>
    public List<GridRange> AllowEditRanges { get; } = [];

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

}

public sealed record WorksheetCustomProperty(string Name, int Id);

public sealed record WorksheetPhoneticProperties(string? FontId, string? Type, string? Alignment);

public enum WorksheetViewMode
{
    Normal,
    PageBreakPreview,
    PageLayout
}
