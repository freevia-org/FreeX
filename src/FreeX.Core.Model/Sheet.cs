namespace FreeX.Core.Model;

public sealed record CommentReply(string Text, string Author = "FreeX")
{
    public DateTimeOffset? CreatedAtUtc { get; init; }
    public DateTimeOffset? ModifiedAtUtc { get; init; }
}

public sealed record ThreadedComment(string Text, string Author = "FreeX")
{
    public IReadOnlyList<CommentReply> Replies { get; init; } = [];
    public bool IsResolved { get; init; } = false;
    public DateTimeOffset? CreatedAtUtc { get; init; }
    public DateTimeOffset? ModifiedAtUtc { get; init; }
}

/// <summary>
/// Distinguishes a normal worksheet (cell grid) from non-standard sheet tabs such as chartsheets
/// and legacy dialog sheets. Macro sheets remain unsupported.
/// </summary>
public enum SheetKind
{
    /// <summary>A normal worksheet backed by a cell grid.</summary>
    Worksheet,

    /// <summary>A full-page chart-only sheet; its chart is stored in <see cref="Sheet.Charts"/>.</summary>
    Chartsheet,

    /// <summary>A legacy Excel dialog sheet. FreeX preserves the kind but does not model dialog controls.</summary>
    DialogSheet
}

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
    // Maps a position to the anchor address of the array formula whose cached spill value was
    // loaded into _cells from the XLSX. These cells are displayable (in _cells) but should not
    // block the owning anchor's SetSpillRange during recalculation.
    private Dictionary<(uint Row, uint Col), (uint AnchorRow, uint AnchorCol)>? _provisionalSpillCells;
    private readonly Dictionary<(uint Row, uint Col), StyleId> _styleOnly = [];
    private List<StyleOnlyRun>? _styleOnlyRuns;
    private HashSet<(uint Row, uint Col)>? _styleOnlyRunTombstones;
    private int _styleOnlyRunCellCount;
    private int _styleOnlyOverlayNewCellCount;
    private readonly HashSet<(uint Row, uint Col)> _formulaCells = [];
    private MergeRegionIndex? _mergeIndex;
    private GridRange? _usedRangeCache;
    private bool _usedRangeCacheDirty = true;
    private int _contentVersion;

    /// <summary>Unique identifier for this sheet.</summary>
    public SheetId Id { get; }

    /// <summary>
    /// The kind of sheet. <see cref="SheetKind.Worksheet"/> is a normal cell-grid sheet;
    /// <see cref="SheetKind.Chartsheet"/> is a full-page chart-only sheet (no cell grid), and
    /// <see cref="SheetKind.DialogSheet"/> preserves legacy Excel dialog-sheet identity.
    /// </summary>
    public SheetKind Kind { get; set; } = SheetKind.Worksheet;

    /// <summary>True when this sheet is a full-page chartsheet rather than a worksheet grid.</summary>
    public bool IsChartsheet => Kind == SheetKind.Chartsheet;

    /// <summary>True when this sheet originated as a legacy Excel dialog sheet.</summary>
    public bool IsDialogSheet => Kind == SheetKind.DialogSheet;

    /// <summary>
    /// The full-page chart for a chartsheet, or <see langword="null"/> when this is not a
    /// chartsheet or the chart could not be loaded. Returns the first entry of <see cref="Charts"/>.
    /// </summary>
    public ChartModel? ChartsheetChart => IsChartsheet ? (Charts.Count > 0 ? Charts[0] : null) : null;

    /// <summary>
    /// Monotonically increasing counter bumped whenever cell content changes (SetCell, ClearCell,
    /// SetFormula, SetSpillRange, ClearSpillRange). Used by caches that depend on cell values.
    /// </summary>
    public int ContentVersion => _contentVersion;

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

    /// <summary>Native Excel sheetFormatPr metadata not yet modeled as editable fields.</summary>
    public NativeXmlPreserveBag? SheetFormatMetadata { get; set; }

    /// <summary>Native Excel dimension metadata not yet modeled as editable fields.</summary>
    public NativeXmlPreserveBag? DimensionMetadata { get; set; }

    /// <summary>Native Excel sheetPr metadata not yet modeled as editable fields.</summary>
    public NativeXmlPreserveBag? SheetPropertiesMetadata { get; set; }

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

    /// <summary>
    /// Optional worksheet print areas (Excel supports comma-separated ranges on the
    /// <c>_xlnm.Print_Area</c> defined name). Each area prints starting on its own page.
    /// Null / empty means print the used range.
    /// </summary>
    /// <remarks>
    /// Use <see cref="PrintArea"/> for single-area access (first element or null).
    /// Setting <see cref="PrintArea"/> replaces the entire list with that single range (or clears).
    /// </remarks>
    private List<GridRange>? _printAreas;

    /// <summary>
    /// All configured print areas for this sheet (may be multiple for multi-area print ranges).
    /// Empty list is equivalent to null (print the used range).
    /// </summary>
    public IReadOnlyList<GridRange> PrintAreas => _printAreas ?? (IReadOnlyList<GridRange>)[];

    /// <summary>
    /// Sets the print areas, replacing any previously configured areas.
    /// Pass an empty collection to clear the print area (revert to used range).
    /// </summary>
    public void SetPrintAreas(IEnumerable<GridRange> areas)
    {
        var list = areas.ToList();
        if (list.Count == 0)
        {
            _printAreas = null;
        }
        else
        {
            _printAreas = list;
        }
    }

    /// <summary>
    /// Convenience accessor: the first (or only) print area, or null if none is set.
    /// Setting this replaces all print areas with the single specified range, or clears if null.
    /// </summary>
    public GridRange? PrintArea
    {
        get => _printAreas is { Count: > 0 } areas ? areas[0] : null;
        set
        {
            if (value is null)
                _printAreas = null;
            else
                _printAreas = [value.Value];
        }
    }

    /// <summary>Worksheet-level Excel AutoFilter metadata loaded from XLSX.</summary>
    public WorksheetAutoFilterModel? AutoFilter { get; set; }

    /// <summary>Worksheet-level Excel smart-tag metadata loaded from XLSX.</summary>
    public WorksheetSmartTagsModel? SmartTags { get; set; }

    /// <summary>Worksheet-level Excel data-consolidation metadata loaded from XLSX.</summary>
    public WorksheetDataConsolidationModel? DataConsolidation { get; set; }

    /// <summary>Worksheet-level Excel sort-state metadata loaded from XLSX.</summary>
    public WorksheetSortStateModel? SortState { get; set; }

    /// <summary>Worksheet XML-map single-cell mapping metadata loaded from XLSX.</summary>
    public WorksheetSingleXmlCellsModel? SingleXmlCells { get; set; }

    /// <summary>Native Excel cellWatches metadata not yet modeled as editable fields (per-watch attributes preserved separately).</summary>
    public WorksheetCellWatchesMetadataModel? CellWatchesMetadata { get; set; }

    /// <summary>Native Excel ignoredErrors metadata not yet modeled as editable fields (per-error attributes preserved separately).</summary>
    public WorksheetIgnoredErrorsMetadataModel? IgnoredErrorsMetadata { get; set; }

    /// <summary>Non-primary Excel worksheet view metadata loaded from XLSX sheetViews.</summary>
    public WorksheetAdditionalViewsModel? AdditionalViews { get; set; }

    /// <summary>Native Excel primary sheetView metadata not yet modeled as editable fields.</summary>
    public NativeXmlPreserveBag? PrimaryViewMetadata { get; set; }

    /// <summary>Worksheet page orientation used for print preview/export.</summary>
    public WorksheetPageOrientation PageOrientation { get; set; } = WorksheetPageOrientation.Portrait;

    /// <summary>Worksheet paper size used for print preview/export.</summary>
    public WorksheetPaperSize PaperSize { get; set; } = WorksheetPaperSize.A4;

    /// <summary>
    /// Raw OOXML <c>pageSetup/@paperSize</c> integer code (1=Letter, 9=A4, 5=Legal, 8=A3, …).
    /// Preserved so arbitrary paper sizes round-trip through XLSX without coercion.
    /// Defaults to 9 (A4) matching <see cref="PaperSize"/> default.
    /// On load this is set from the raw XML attribute; on save it is emitted verbatim.
    /// The <see cref="PaperSize"/> enum is kept for the dialog (common sizes only) and is
    /// kept in sync with this code via <see cref="PaperSizeCodes"/>.
    /// </summary>
    public int PaperSizeCode { get; set; } = PaperSizeCodes.DefaultCode;

    /// <summary>Worksheet page margins in inches.</summary>
    public WorksheetPageMargins PageMargins { get; set; } = WorksheetPageMargins.Narrow;

    /// <summary>Distance from the page top to the printed header, in inches.</summary>
    public double HeaderMargin { get; set; } = 0.3;

    /// <summary>Distance from the page bottom to the printed footer, in inches.</summary>
    public double FooterMargin { get; set; } = 0.3;

    /// <summary>Native Excel pageMargins metadata not yet modeled as editable fields.</summary>
    public NativeXmlPreserveBag? PageMarginsMetadata { get; set; }

    /// <summary>Whether gridlines are printed for this worksheet.</summary>
    public bool PrintGridlines { get; set; }

    /// <summary>Whether row and column headings are printed for this worksheet.</summary>
    public bool PrintHeadings { get; set; }

    /// <summary>Native Excel printOptions metadata not yet modeled as editable fields.</summary>
    public NativeXmlPreserveBag? PrintOptionsMetadata { get; set; }

    /// <summary>Worksheet print scaling settings.</summary>
    public WorksheetScaleToFit ScaleToFit { get; set; } = WorksheetScaleToFit.Default;

    /// <summary>Optional Excel fit-to-page sheet property. Null lets Excel infer from scaling settings.</summary>
    public bool? FitToPage { get; set; }

    /// <summary>Optional Excel automatic page-break flag stored in sheet properties.</summary>
    public bool? AutoPageBreaks { get; set; }

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

    /// <summary>Native Excel headerFooter metadata not yet modeled as editable fields.</summary>
    public NativeXmlPreserveBag? HeaderFooterMetadata { get; set; }

    /// <summary>Whether the printed grid is centered horizontally within the printable page area.</summary>
    public bool CenterHorizontallyOnPage { get; set; }

    /// <summary>Whether the printed grid is centered vertically within the printable page area.</summary>
    public bool CenterVerticallyOnPage { get; set; }

    /// <summary>Order used when printing multi-page worksheets.</summary>
    public WorksheetPageOrder PageOrder { get; set; } = WorksheetPageOrder.DownThenOver;

    /// <summary>Optional first printed page number. Null means automatic numbering from 1.</summary>
    public int? FirstPageNumber { get; set; }

    /// <summary>Whether Excel should use printer defaults for worksheet page setup.</summary>
    public bool? UsePrinterDefaults { get; set; }

    /// <summary>Optional number of copies for worksheet print settings.</summary>
    public int? PrintCopies { get; set; }

    /// <summary>Whether the worksheet should be printed in black and white.</summary>
    public bool PrintBlackAndWhite { get; set; }

    /// <summary>Whether the worksheet should be printed in draft quality.</summary>
    public bool PrintDraftQuality { get; set; }

    /// <summary>Optional worksheet print quality in dots per inch. Null means printer/default quality.</summary>
    public int? PrintQualityDpi { get; set; }

    /// <summary>Optional vertical worksheet print quality in dots per inch when it differs from horizontal DPI.</summary>
    public int? PrintQualityVerticalDpi { get; set; }

    /// <summary>How formula/cell error values are represented when printing.</summary>
    public WorksheetPrintErrorValue PrintErrorValue { get; set; } = WorksheetPrintErrorValue.Displayed;

    /// <summary>How cell comments are included in printed output.</summary>
    public WorksheetPrintComments PrintComments { get; set; } = WorksheetPrintComments.None;

    /// <summary>Legacy BIFF sheet print-size metadata, when present in an imported XLS workbook.</summary>
    public int? LegacyPrintSize { get; set; }

    /// <summary>Native Excel pageSetup metadata not yet modeled as editable fields.</summary>
    public NativeXmlPreserveBag? PageSetupMetadata { get; set; }

    /// <summary>Manual row page breaks, stored as the first row after each break.</summary>
    public SortedSet<uint> RowPageBreaks { get; } = [];

    /// <summary>Native Excel rowBreaks metadata not yet modeled as editable fields.</summary>
    public WorksheetPageBreaksMetadataModel? RowPageBreaksMetadata { get; set; }

    /// <summary>Manual column page breaks, stored as the first column after each break.</summary>
    public SortedSet<uint> ColumnPageBreaks { get; } = [];

    /// <summary>Native Excel colBreaks metadata not yet modeled as editable fields.</summary>
    public WorksheetPageBreaksMetadataModel? ColumnPageBreaksMetadata { get; set; }

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

    /// <summary>Whether zero values are displayed in cells.</summary>
    public bool ShowZeros { get; set; } = true;

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

    /// <summary>Back-to-front z-order for supported drawing objects: shapes, pictures, and text boxes.</summary>
    public List<DrawingObjectZOrderEntry> DrawingObjectZOrder { get; } = [];

    /// <summary>Sparklines embedded in cells on this sheet.</summary>
    public List<SparklineModel> Sparklines { get; } = [];

    /// <summary>Legacy Excel form controls (checkboxes, option buttons, spinners, scroll bars, etc.) loaded from XLSX.</summary>
    public List<FormControlModel> FormControls { get; } = [];

    /// <summary>Conditional formatting rules applied to this sheet, ordered by priority.</summary>
    public ConditionalFormatCollection ConditionalFormats { get; } = [];

    /// <summary>Data validation rules applied to this sheet.</summary>
    public DataValidationCollection DataValidations { get; } = [];

    /// <summary>Set of row numbers manually hidden or imported as hidden (1-based).</summary>
    public HashSet<uint> HiddenRows { get; } = [];

    /// <summary>Set of row numbers hidden by the active filter (1-based). Empty when no filter is active.</summary>
    public HashSet<uint> FilterHiddenRows { get; } = [];

    /// <summary>
    /// Runtime (non-serialized) per-column value-filter state, keyed by absolute 1-based column index.
    /// Each entry is the set of allowed cell-text values for that column's active AutoFilter criteria.
    /// Excel ANDs AutoFilter criteria across columns: a row is hidden if it fails ANY active column's
    /// filter. <see cref="FilterHiddenRows"/> is kept as the recomputed union of every column's
    /// exclusions (see FreeX.Core.Commands.FilterCommand, finding F8) so applying/clearing one column's
    /// filter never disturbs another column's hidden rows. This is separate from the heavyweight
    /// XLSX-serialization AutoFilter model — it exists purely to drive that recompute.
    /// </summary>
    public Dictionary<uint, IReadOnlyList<string>> ActiveValueFilterColumns { get; } = [];

    /// <summary>Set of column numbers that are hidden (1-based).</summary>
    public HashSet<uint> HiddenCols { get; } = [];

    /// <summary>Outline level (1–8) per row. 0 = no grouping.</summary>
    public Dictionary<uint, int> RowOutlineLevels { get; } = [];

    /// <summary>Outline level (1–8) per column. 0 = no grouping.</summary>
    public Dictionary<uint, int> ColOutlineLevels { get; } = [];

    /// <summary>Whether row outline summary rows appear below detail rows. Null means Excel default.</summary>
    public bool? OutlineSummaryBelow { get; set; }

    /// <summary>Whether column outline summary columns appear to the right of detail columns. Null means Excel default.</summary>
    public bool? OutlineSummaryRight { get; set; }

    /// <summary>Whether outline symbols are displayed for grouped rows and columns. Null means Excel default.</summary>
    public bool? ShowOutlineSymbols { get; set; }

    /// <summary>Whether outline styles should be applied automatically. Null means Excel default.</summary>
    public bool? ApplyOutlineStyles { get; set; }

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

    /// <summary>
    /// Legacy comment authors keyed by address. Populated during load when the comments XML has an
    /// <c>authors</c> list; absent entries default to an empty-string author on write. This companion
    /// dictionary avoids changing the Comments value type and its 60+ call sites.
    /// </summary>
    public Dictionary<CellAddress, string> CommentAuthors { get; } = [];

    /// <summary>Threaded cell comments keyed by address.</summary>
    public Dictionary<CellAddress, ThreadedComment> ThreadedComments { get; } = [];

    /// <summary>
    /// Addresses of legacy notes (Comments) whose comment box is pinned open ("Show Comment"
    /// in Excel's VML <c>&lt;x:Visible/&gt;</c> sense). Only addresses that also appear in
    /// <see cref="Comments"/> are meaningful; the set is kept tidy by the commands but is not
    /// validated on every access for performance.
    /// </summary>
    public HashSet<CellAddress> ShownComments { get; } = [];

    /// <summary>Cell hyperlinks keyed by address. Value is the target URL/location.</summary>
    public Dictionary<CellAddress, string> Hyperlinks { get; } = [];

    /// <summary>Excel hyperlink metadata keyed by address.</summary>
    public Dictionary<CellAddress, HyperlinkMetadata> HyperlinkMetadata { get; } = [];

    /// <summary>
    /// Per-cell rich-text run sequences, keyed by cell address.
    /// Only populated when a text cell has more than one run <em>or</em> a run deviates from the
    /// cell's <see cref="CellStyle"/>.  The plain-text value in <c>Cell.Value</c> (a
    /// <see cref="ScalarValue"/> <c>TextValue</c>) always remains the authoritative string for
    /// formulas, search, and number-format — this map is a parallel decoration layer.
    /// </summary>
    public Dictionary<CellAddress, IReadOnlyList<CellTextRun>> RichTextRuns { get; } = [];

    /// <summary>True when the sheet is protected against edits.</summary>
    public bool IsProtected { get; set; }

    /// <summary>Password hash for sheet protection. Null means no password required.</summary>
    public string? ProtectionPassword { get; set; }

    /// <summary>Native Excel sheet protection metadata not yet modeled as editable fields.</summary>
    public NativeXmlPreserveBag? ProtectionMetadata { get; set; }

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

    /// <summary>Pre-size cell storage for bulk writers.</summary>
    public void EnsureCellCapacity(int capacity)
    {
        if (capacity > _cells.Count)
            _cells.EnsureCapacity(capacity);
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
        TrackUsedRangeCellSet(address.Row, address.Col);
        if (_cells.TryGetValue((address.Row, address.Col), out var existing))
        {
            TrackFormulaCellReplacement(address.Row, address.Col, existing, hasNewFormula: false);
            existing.Value = value;
            existing.FormulaText = null;
            existing.IgnoreFormulaError = false;
        }
        else
        {
            _cells[(address.Row, address.Col)] = Cell.FromValue(value);
        }
        ClearStyleOnly(address.Row, address.Col);
        _contentVersion++;
    }

    /// <summary>
    /// Set a cell with a formula at the given address.
    /// The value should be computed separately by the calc engine.
    /// </summary>
    public void SetFormula(CellAddress address, string formulaText)
    {
        ClearSpillRange(address);
        TrackUsedRangeCellSet(address.Row, address.Col);
        if (_cells.TryGetValue((address.Row, address.Col), out var existing))
        {
            TrackFormulaCellReplacement(address.Row, address.Col, existing, hasNewFormula: true);
            existing.FormulaText = formulaText;
            existing.IgnoreFormulaError = false;
        }
        else
        {
            _cells[(address.Row, address.Col)] = Cell.FromFormula(formulaText);
            _formulaCells.Add((address.Row, address.Col));
        }
        ClearStyleOnly(address.Row, address.Col);
        _contentVersion++;
    }

    /// <summary>Set a cell directly.</summary>
    public void SetCell(CellAddress address, Cell cell)
    {
        ClearSpillRange(address);
        TrackUsedRangeCellSet(address.Row, address.Col);
        if (_cells.TryGetValue((address.Row, address.Col), out var existing))
            TrackFormulaCellReplacement(address.Row, address.Col, existing, cell.HasFormula);
        else if (cell.HasFormula)
            _formulaCells.Add((address.Row, address.Col));

        _cells[(address.Row, address.Col)] = cell;
        ClearStyleOnly(address.Row, address.Col);
        _contentVersion++;
    }

    /// <summary>Remove a cell (clear its contents).</summary>
    public void ClearCell(uint row, uint col)
    {
        ClearSpillRange(new CellAddress(Id, row, col));
        if (_cells.Remove((row, col), out var removed))
        {
            if (removed.HasFormula)
                _formulaCells.Remove((row, col));
            TrackUsedRangeCellCleared(row, col);
            _contentVersion++;
        }
    }

    /// <summary>Remove a cell at the given address.</summary>
    public void ClearCell(CellAddress address)
    {
        ClearSpillRange(address);
        if (_cells.Remove((address.Row, address.Col), out var removed))
        {
            if (removed.HasFormula)
                _formulaCells.Remove((address.Row, address.Col));
            TrackUsedRangeCellCleared(address.Row, address.Col);
            _contentVersion++;
        }
    }

    /// <summary>
    /// Returns true if any non-anchor cell in the proposed spill range is occupied by user data
    /// or by a spill value from a different anchor.
    /// Provisional spill cells (cached values loaded from an XLSX for this anchor's own declared
    /// ref range) are transparent to this check — the anchor is allowed to overwrite them.
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
                if (_cells.ContainsKey(key))
                {
                    // A provisional cached spill cell loaded from the XLSX for THIS anchor does not
                    // block the anchor — it is overwritten when the anchor re-spills via SetSpillRange.
                    if (_provisionalSpillCells is not null &&
                        _provisionalSpillCells.TryGetValue(key, out var owningAnchor) &&
                        owningAnchor == (anchor.Row, anchor.Col))
                        continue;
                    return true;
                }
                if (_spillValues.ContainsKey(key)) return true;
            }
        return false;
    }

    /// <summary>
    /// Register a cell as a provisional cached spill value loaded from an XLSX.
    /// The cell is stored in <c>_cells</c> so the viewport can display it, but it is also
    /// tagged as provisional so <see cref="IsSpillBlocked"/> allows the owning anchor to
    /// overwrite it during recalculation via <see cref="SetSpillRange"/>.
    /// </summary>
    public void SetProvisionalSpillCell(CellAddress anchor, uint row, uint col, Cell cell)
    {
        var key = (row, col);
        _provisionalSpillCells ??= [];
        _provisionalSpillCells[key] = (anchor.Row, anchor.Col);
        // SetCell tracks _formulaCells and _usedRangeCache for us.
        SetCell(new CellAddress(Id, row, col), cell);
    }

    /// <summary>
    /// Write the spill range for a dynamic-array anchor cell.
    /// Clears any previous spill from this anchor first and removes any provisional cached spill
    /// cells registered for this anchor (replacing them with freshly computed spill values).
    /// Does NOT check for blockage — call IsSpillBlocked first.
    /// </summary>
    public void SetSpillRange(CellAddress anchor, RangeValue rv)
    {
        ClearSpillRange(anchor);
        int rows = rv.RowCount, cols = rv.ColCount;
        // Remove any provisional cached spill cells for this anchor from _cells now that the
        // anchor is re-spilling with freshly computed values.
        if (_provisionalSpillCells is { Count: > 0 })
        {
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (r == 0 && c == 0) continue;
                    var key = (anchor.Row + (uint)r, anchor.Col + (uint)c);
                    if (_provisionalSpillCells.TryGetValue(key, out var owning) &&
                        owning == (anchor.Row, anchor.Col))
                    {
                        _provisionalSpillCells.Remove(key);
                        _cells.Remove(key);
                        TrackUsedRangeCellCleared(key.Item1, key.Item2);
                    }
                }
        }
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (r == 0 && c == 0) continue;
                var row = anchor.Row + (uint)r;
                var col = anchor.Col + (uint)c;
                _spillValues[(row, col)] = rv.Cells[r, c];
                if (rv.Cells[r, c] is not BlankValue)
                    TrackUsedRangeCellSet(row, col);
            }
        _spillAnchors[(anchor.Row, anchor.Col)] = ((uint)rows, (uint)cols);
        _contentVersion++;
    }

    /// <summary>
    /// If <paramref name="anchor"/> is a dynamic-array spill anchor, returns its spill extent
    /// (rows × cols, including the anchor). Used by the saver to mark spilling formulas as array formulas.
    /// </summary>
    public bool TryGetSpillExtent(CellAddress anchor, out uint rows, out uint cols)
    {
        if (_spillAnchors.TryGetValue((anchor.Row, anchor.Col), out var extent))
        {
            rows = extent.Rows;
            cols = extent.Cols;
            return true;
        }
        rows = 0;
        cols = 0;
        return false;
    }

    /// <summary>Remove all spill values written by the given anchor cell's formula.</summary>
    public void ClearSpillRange(CellAddress anchor)
    {
        var hadSpillValues = false;
        if (_spillAnchors.TryGetValue((anchor.Row, anchor.Col), out var extent))
        {
            for (uint r = 0; r < extent.Rows; r++)
                for (uint c = 0; c < extent.Cols; c++)
                {
                    if (r == 0 && c == 0) continue;
                    var row = anchor.Row + r;
                    var col = anchor.Col + c;
                    if (_spillValues.Remove((row, col)))
                        TrackUsedRangeCellCleared(row, col);
                }
            _spillAnchors.Remove((anchor.Row, anchor.Col));
            hadSpillValues = true;
        }
        // Also clear any provisional spill cells from _cells that belong to this anchor.
        // These are loaded-from-xlsx cached values tagged as overwriteable by this anchor.
        if (_provisionalSpillCells is { Count: > 0 })
        {
            List<(uint, uint)>? toRemove = null;
            foreach (var (key, owningAnchor) in _provisionalSpillCells)
            {
                if (owningAnchor == (anchor.Row, anchor.Col))
                    (toRemove ??= []).Add(key);
            }
            if (toRemove is not null)
            {
                foreach (var key in toRemove)
                {
                    _provisionalSpillCells.Remove(key);
                    if (_cells.Remove(key))
                        TrackUsedRangeCellCleared(key.Item1, key.Item2);
                }
                hadSpillValues = true;
            }
        }
        if (hadSpillValues) _contentVersion++;
    }

    /// <summary>
    /// Enumerate all non-anchor cells that currently have a spill value (i.e. every cell that
    /// was written by <see cref="SetSpillRange"/> and not yet cleared by <see cref="ClearSpillRange"/>).
    /// Used by the recalc engine to discover formula cells whose results depend on spill targets so
    /// that a second evaluation pass can be triggered when needed.
    /// </summary>
    public IEnumerable<CellAddress> EnumerateSpillTargetCells()
    {
        foreach (var ((row, col), _) in _spillValues)
            yield return new CellAddress(Id, row, col);
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

    /// <summary>Enumerate positions whose effective value is not blank, including spill values.</summary>
    public IEnumerable<CellAddress> EnumerateValueBearingCells()
    {
        foreach (var ((row, col), cell) in _cells)
        {
            if (cell.Value is not BlankValue)
                yield return new CellAddress(Id, row, col);
        }

        foreach (var ((row, col), value) in _spillValues)
        {
            if (value is not BlankValue && !_cells.ContainsKey((row, col)))
                yield return new CellAddress(Id, row, col);
        }
    }

    /// <summary>Get all non-empty cell positions.</summary>
    public IReadOnlyCollection<(uint Row, uint Col)> GetOccupiedCells()
    {
        return _cells.Keys;
    }

    /// <summary>Get occupied cells keyed by primitive row and column coordinates.</summary>
    public IReadOnlyDictionary<(uint Row, uint Col), Cell> GetOccupiedCellMap()
    {
        return _cells;
    }

    /// <summary>Get all cells as address-cell pairs.</summary>
    public IEnumerable<(CellAddress Address, Cell Cell)> EnumerateCells()
    {
        foreach (var ((row, col), cell) in _cells)
        {
            yield return (new CellAddress(Id, row, col), cell);
        }
    }

    /// <summary>Enumerate cells that currently contain formulas.</summary>
    public IEnumerable<CellAddress> EnumerateFormulaCells()
    {
        foreach (var (row, col) in _formulaCells)
            yield return new CellAddress(Id, row, col);
    }

    /// <summary>Total number of non-empty cells.</summary>
    public int CellCount => _cells.Count;

    /// <summary>Number of cells that currently contain formulas.</summary>
    public int FormulaCellCount => _formulaCells.Count;

    /// <summary>Whether any cell on the sheet currently contains a formula.</summary>
    public bool HasFormulas => _formulaCells.Count > 0;

    /// <summary>Whether any spill values have been written to this sheet (i.e. at least one dynamic-array formula has spilled).</summary>
    public bool HasSpillValues => _spillValues.Count > 0;

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
        if (!_usedRangeCacheDirty)
            return _usedRangeCache;

        if (_cells.Count == 0 && _spillValues.Count == 0)
        {
            _usedRangeCache = null;
            _usedRangeCacheDirty = false;
            return null;
        }

        uint minRow = uint.MaxValue, maxRow = 0, minCol = uint.MaxValue, maxCol = 0;
        foreach (var (row, col) in _cells.Keys)
        {
            if (row < minRow) minRow = row;
            if (row > maxRow) maxRow = row;
            if (col < minCol) minCol = col;
            if (col > maxCol) maxCol = col;
        }
        foreach (var ((row, col), value) in _spillValues)
        {
            if (value is BlankValue || _cells.ContainsKey((row, col)))
                continue;

            if (row < minRow) minRow = row;
            if (row > maxRow) maxRow = row;
            if (col < minCol) minCol = col;
            if (col > maxCol) maxCol = col;
        }

        if (maxRow == 0)
        {
            _usedRangeCache = null;
            _usedRangeCacheDirty = false;
            return null;
        }

        _usedRangeCache = new GridRange(
            new CellAddress(Id, minRow, minCol),
            new CellAddress(Id, maxRow, maxCol));
        _usedRangeCacheDirty = false;
        return _usedRangeCache;
    }

    private void TrackUsedRangeCellSet(uint row, uint col)
    {
        if (_usedRangeCacheDirty)
            return;

        if (_usedRangeCache is not { } range)
        {
            _usedRangeCache = new GridRange(
                new CellAddress(Id, row, col),
                new CellAddress(Id, row, col));
            return;
        }

        if (row >= range.Start.Row &&
            row <= range.End.Row &&
            col >= range.Start.Col &&
            col <= range.End.Col)
        {
            return;
        }

        _usedRangeCache = new GridRange(
            new CellAddress(Id, Math.Min(range.Start.Row, row), Math.Min(range.Start.Col, col)),
            new CellAddress(Id, Math.Max(range.End.Row, row), Math.Max(range.End.Col, col)));
    }

    private void TrackUsedRangeCellCleared(uint row, uint col)
    {
        if (_usedRangeCacheDirty || _usedRangeCache is not { } range)
            return;

        if (row == range.Start.Row ||
            row == range.End.Row ||
            col == range.Start.Col ||
            col == range.End.Col)
        {
            _usedRangeCacheDirty = true;
        }
    }

    private void TrackFormulaCellReplacement(uint row, uint col, Cell existing, bool hasNewFormula)
    {
        if (existing.HasFormula == hasNewFormula)
            return;

        if (hasNewFormula)
            _formulaCells.Add((row, col));
        else
            _formulaCells.Remove((row, col));
    }

}

public enum WorksheetViewMode
{
    Normal,
    PageBreakPreview,
    PageLayout
}
