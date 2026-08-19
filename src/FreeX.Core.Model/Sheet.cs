namespace FreeX.Core.Model;

public sealed record CommentReply(string Text, string Author = "FreeX")
{
    public DateTimeOffset? CreatedAtUtc { get; init; }
    public DateTimeOffset? ModifiedAtUtc { get; init; }

    /// <summary>
    /// The stable threadedComment id (a GUID string, e.g. "{5A2F...}") this reply was loaded
    /// with from the source XLSX, or null for a reply created in this session that has not
    /// yet been saved. Preserved across saves so reply ids/parentId linkage do not churn when
    /// unrelated content (e.g. the root comment's text) changes.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// The raw, unparsed <c>&lt;mentions&gt;</c> and/or <c>&lt;extLst&gt;</c> XML fragment(s) (if
    /// any), concatenated in source schema order, from the source threadedComment element, e.g.
    /// Excel's @mention metadata. Round-tripped verbatim on save since FreeX does not model
    /// @mention linkage.
    /// </summary>
    public string? MentionsXml { get; init; }

    /// <summary>
    /// The source <c>&lt;threadedComment&gt;/@personId</c> this reply was loaded with, preserved
    /// only when <see cref="MentionsXml"/> is also preserved. A save that carries an @mention
    /// referencing this person id (e.g. <c>mtc:mention/@mentionpersonId</c>) must keep resolving
    /// after the persons part is rewritten, so the writer prefers this id over a freshly minted
    /// per-author guid when present.
    /// </summary>
    public string? SourcePersonId { get; init; }

    /// <summary>
    /// Display names, by source person id, for every person referenced by a
    /// <c>mtc:mention/@mentionpersonId</c> inside <see cref="MentionsXml"/> who is NOT themselves
    /// this reply's (or any other comment/reply's) author -- e.g. an @-mentioned person who has
    /// never posted a comment. FreeX does not model @mention linkage as first-class data, but a
    /// mentioned person's <c>&lt;person&gt;</c> record must still be written to
    /// <c>xl/persons/person.xml</c> on save so the mention keeps resolving; without this, the
    /// mentioned (non-authoring) person's record silently disappears after a save because
    /// person.xml is rewritten solely from comment/reply authors.
    /// </summary>
    public IReadOnlyDictionary<string, string>? MentionedPersonDisplayNames { get; init; }
}

public sealed record ThreadedComment(string Text, string Author = "FreeX")
{
    public IReadOnlyList<CommentReply> Replies { get; init; } = [];
    public bool IsResolved { get; init; } = false;
    public DateTimeOffset? CreatedAtUtc { get; init; }
    public DateTimeOffset? ModifiedAtUtc { get; init; }

    /// <summary>
    /// The UTC time the ROOT comment's own text was last genuinely edited (via
    /// UpdateThreadedCommentTextCommand, or the root-text branch of
    /// ApplyThreadedCommentChangesCommand, in FreeX.Core.Commands), distinct from
    /// <see cref="ModifiedAtUtc"/> which also gets bumped by unrelated thread activity (a reply
    /// being added/edited/removed elsewhere in the thread). Null when the root text has never
    /// been edited since creation, or for a comment freshly loaded from a source XLSX -- in
    /// either case the writer falls back to inferring the root's persisted dT from
    /// ModifiedAtUtc/CreatedAtUtc/replies (see
    /// XlsxWorksheetThreadedCommentMapper.ResolveRootThreadedCommentTimestamp). Once set, this
    /// value always wins over any later reply-driven ModifiedAtUtc bump when the writer persists
    /// the root &lt;threadedComment&gt; element's own dT, so a genuine root-text edit is never
    /// silently reverted/overwritten by subsequent reply activity in the same session (see
    /// R35-deferred-comment-edit-timestamp-1).
    /// </summary>
    public DateTimeOffset? RootTextEditedAtUtc { get; init; }

    /// <summary>
    /// The stable threadedComment id (a GUID string, e.g. "{5A2F...}") this root comment was
    /// loaded with from the source XLSX, or null for a comment created in this session that has
    /// not yet been saved. Preserved across saves so this id (and every reply's parentId, which
    /// references it) does not regenerate/cascade-change when the comment's text is edited.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// The raw, unparsed <c>&lt;mentions&gt;</c> and/or <c>&lt;extLst&gt;</c> XML fragment(s) (if
    /// any), concatenated in source schema order, from the source threadedComment element, e.g.
    /// Excel's @mention metadata. Round-tripped verbatim on save since FreeX does not model
    /// @mention linkage.
    /// </summary>
    public string? MentionsXml { get; init; }

    /// <summary>
    /// The source <c>&lt;threadedComment&gt;/@personId</c> this root comment was loaded with,
    /// preserved only when <see cref="MentionsXml"/> is also preserved. A save that carries an
    /// @mention referencing this person id (e.g. <c>mtc:mention/@mentionpersonId</c>) must keep
    /// resolving after the persons part is rewritten, so the writer prefers this id over a
    /// freshly minted per-author guid when present.
    /// </summary>
    public string? SourcePersonId { get; init; }

    /// <summary>
    /// Display names, by source person id, for every person referenced by a
    /// <c>mtc:mention/@mentionpersonId</c> inside <see cref="MentionsXml"/> who is NOT themselves
    /// this comment's (or any reply's) author -- e.g. an @-mentioned person who has never posted a
    /// comment. FreeX does not model @mention linkage as first-class data, but a mentioned
    /// person's <c>&lt;person&gt;</c> record must still be written to
    /// <c>xl/persons/person.xml</c> on save so the mention keeps resolving; without this, the
    /// mentioned (non-authoring) person's record silently disappears after a save because
    /// person.xml is rewritten solely from comment/reply authors.
    /// </summary>
    public IReadOnlyDictionary<string, string>? MentionedPersonDisplayNames { get; init; }
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
/// Metadata for a registered What-If Analysis Data Table (see <see cref="Sheet.RegisterDataTableRange"/>).
/// <paramref name="SecondInputCell"/> is non-null only for a two-variable table -- it then holds the
/// column-input cell, with <paramref name="InputCell"/> holding the row-input cell (and
/// <paramref name="IsRowOriented"/> is meaningless). For a one-variable table
/// <paramref name="SecondInputCell"/> is always null, <paramref name="InputCell"/> holds the single
/// substituted input cell, and <paramref name="IsRowOriented"/> selects the orientation. Kept as
/// plain data in FreeX.Core.Model (rather than referencing FreeX.Core.Commands' own
/// DataTableInputOrientation enum) since this project has no dependency on FreeX.Core.Commands --
/// OneVariableDataTableCommand/TwoVariableDataTableCommand and DataTableAutoRefreshEffects there are
/// the sole writer/readers.
/// </summary>
public readonly record struct DataTableRegistration(
    GridRange TableRange,
    CellAddress FormulaCell,
    CellAddress InputCell,
    CellAddress? SecondInputCell,
    bool IsRowOriented)
{
    /// <summary>The result body -- the full table range minus its header row/column of trial input
    /// values, i.e. rows [Start.Row+1..End.Row] x cols [Start.Col+1..End.Col].</summary>
    public GridRange BodyRange => new(
        new CellAddress(TableRange.Start.Sheet, TableRange.Start.Row + 1, TableRange.Start.Col + 1),
        TableRange.End);
}

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

    /// <summary>
    /// Bumps <see cref="ContentVersion"/> without otherwise mutating sheet state. The recalc engine
    /// writes fresh formula results straight into <see cref="Cell.Value"/> (it does not go through
    /// <see cref="SetCell(CellAddress, ScalarValue)"/>/<see cref="SetFormula"/>, which would wrongly
    /// clear the formula/reset array mode), so those value changes would otherwise never be visible
    /// to caches keyed on <see cref="ContentVersion"/> — e.g. conditional-format evaluation context,
    /// which must be rebuilt whenever a dependent cell's value changes as a result of a cross-sheet
    /// edit or a volatile (F9) recalculation, not just a direct edit to this sheet.
    /// </summary>
    public void NotifyContentRecalculated() => _contentVersion++;

    /// <summary>Display name of the sheet (shown on tab).</summary>
    public string Name { get; set; }

    /// <summary>Column widths override (1-based column index → width in characters).</summary>
    public Dictionary<uint, double> ColumnWidths { get; } = [];

    /// <summary>Row heights override (1-based row index → height in pixels).</summary>
    public Dictionary<uint, double> RowHeights { get; } = [];

    /// <summary>
    /// Whole-column default style (1-based column index → StyleId), from an XLSX <c>&lt;col
    /// style="..."&gt;</c> range. Formats every cell in that column that carries no explicit style
    /// of its own -- including still-empty cells -- and only applies as a fallback when the cell
    /// has neither its own style nor a row default; see <see cref="GetStyleOnly"/> and
    /// R136-io-worksheet-props-col-row-default-style.
    /// </summary>
    public Dictionary<uint, StyleId> ColumnStyles { get; } = [];

    /// <summary>
    /// Whole-row default style (1-based row index → StyleId), from an XLSX row's "s"+"customFormat"
    /// pair. Formats every cell in that row that carries no explicit style of its own -- including
    /// still-empty cells -- and takes precedence over a column default per Excel's cell &gt; row &gt;
    /// column resolution order; see <see cref="GetStyleOnly"/> and
    /// R136-io-worksheet-props-col-row-default-style.
    /// </summary>
    public Dictionary<uint, StyleId> RowStyles { get; } = [];

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
    /// True when this sheet is authored right-to-left (OOXML <c>sheetView/@rightToLeft</c>), as Excel
    /// sets via Page Layout &gt; Sheet Right-to-Left for Arabic/Hebrew workbooks. Mirrors column order
    /// (column A on the right), header side, and scrollbar side; consumers that render the grid or
    /// resolve cell alignment must consult this alongside <see cref="CellStyle.ReadingOrder"/>.
    /// </summary>
    public bool IsRightToLeft { get; set; }

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
    public WorksheetPageMargins PageMargins { get; set; } = WorksheetPageMargins.Normal;

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
    /// <remarks>
    /// Setting this clears <see cref="TabThemeColor"/> so an explicit tab-color pick never leaves a stale
    /// theme link behind on save (see R123-tab-theme-color-1). File-format loaders that populate a
    /// theme-relative tab color must assign <see cref="TabThemeColor"/> AFTER this property.
    /// </remarks>
    public CellColor? TabColor
    {
        get => _tabColor;
        set
        {
            _tabColor = value;
            TabThemeColor = null;
        }
    }
    private CellColor? _tabColor;

    /// <summary>
    /// Optional theme-color reference (slot + tint) for the worksheet tab color, captured from an XLSX
    /// <c>&lt;tabColor theme="n" tint="t"/&gt;</c>. Mirrors <see cref="CellStyle.FillThemeColor"/> so a
    /// theme-relative tab color can re-resolve live against the current <see cref="WorkbookTheme"/> and
    /// round-trip its theme link on save, instead of being permanently baked to RGB at load time
    /// (see R123-tab-theme-color-1). Null when the tab color is a literal RGB or unset.
    /// </summary>
    public WorkbookThemeColorReference? TabThemeColor { get; set; }

    /// <summary>Resolves the effective tab color against <paramref name="theme"/>, preferring the theme link when present.</summary>
    public CellColor? ResolveTabColor(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return TabThemeColor?.Resolve(theme) ?? TabColor;
    }

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

    /// <summary>
    /// R121-model-drawing-delete-1: cNvPr@name of every drawing object (picture, text box, shape, or
    /// chart) that DeleteDrawingObjectCommand removed from <see cref="Pictures"/>/<see cref="TextBoxes"/>/
    /// <see cref="DrawingShapes"/>/<see cref="Charts"/> this session -- a tombstone list, NOT a live
    /// collection. A deleted object that traces back to the opened .xlsx (its name may still exist as an
    /// ORIGINAL anchor in the true source package, whether or not <c>IsSourceLoaded</c> was still set at
    /// the moment of deletion -- an edited-then-deleted object's stale original anchor is just as stale as
    /// a never-edited one's) simply vanishes from the in-memory model; nothing else records that it must
    /// NOT be merged back in from the source package on the next save.
    /// <c>FreeX.Core.IO</c>'s <c>XlsxWorksheetDrawingObjectWriter.GetRewrittenSourceObjectNames</c>
    /// unions this list into the superseded-name set it hands <c>XlsxWorksheetDrawingPartMerger</c>, so a
    /// deleted object's original anchor is skipped exactly like an edited one's already is.
    /// <para>
    /// A <c>List</c>, not a <c>HashSet</c>: Excel's default per-sheet naming ("Picture 1", "Shape 1", ...)
    /// can be reused by two distinct objects, and <c>DeleteDrawingObjectCommand</c>'s undo removes only
    /// ONE matching entry (<see cref="List{T}.Remove(T)"/>) so deleting two same-named objects and
    /// undoing just one leaves the other's tombstone intact.
    /// </para>
    /// </summary>
    public List<string> DeletedSourceDrawingObjectNames { get; } = [];

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
    /// Runtime per-column value-filter state, keyed by absolute 1-based column index.
    /// Each entry is the set of allowed cell-text values for that column's active AutoFilter criteria.
    /// Excel ANDs AutoFilter criteria across columns: a row is hidden if it fails ANY active column's
    /// filter. <see cref="FilterHiddenRows"/> is kept as the recomputed union of every column's
    /// exclusions (see FreeX.Core.Commands.FilterCommand, finding F8) so applying/clearing one column's
    /// filter never disturbs another column's hidden rows. This is separate from the heavyweight
    /// XLSX-serialization AutoFilter model — it exists purely to drive that recompute. Keyed by
    /// absolute column index like <see cref="ColumnWidths"/>, so it must shift the same way on
    /// column insert/delete, and roll back on undo (see finding G1). Persisted alongside
    /// <see cref="FilterHiddenRows"/> so a reload doesn't leave the two out of sync (finding G32).
    /// </summary>
    public Dictionary<uint, IReadOnlyList<string>> ActiveValueFilterColumns { get; } = [];

    /// <summary>
    /// Runtime bookkeeping: the subset of <see cref="FilterHiddenRows"/> that is attributable to
    /// <see cref="ActiveValueFilterColumns"/>'s AND-across-columns recompute (see
    /// FreeX.Core.Commands.FilterCommand.RecomputeHiddenRows, finding G7). Other filter mechanisms
    /// (Top 10/Above-Average/color/custom-condition filters) hide rows by mutating
    /// <see cref="FilterHiddenRows"/> directly without registering anything here, so recompute must
    /// never blindly un-hide a row outside this set — doing so would silently discard those other
    /// filters' hidden rows the next time a value-list filter is applied on a different column.
    /// </summary>
    public HashSet<uint> ValueFilterHiddenRows { get; } = [];

    /// <summary>
    /// Runtime per-column ownership state for the non-value-list AutoFilter mechanisms (condition/
    /// custom-criterion, Top 10/Above-Average, and cell/font-color filters), keyed by absolute
    /// 1-based column index. Each entry is exactly the set of rows THAT column's own filter last
    /// decided to hide. Excel ANDs AutoFilter criteria across every active column (a row stays
    /// hidden if it fails ANY active column's filter), so when one of these mechanisms re-evaluates
    /// its own column it must only ever un-hide rows found in its OWN entry here — never a row some
    /// other column's mechanism (a value-list filter via <see cref="ActiveValueFilterColumns"/>, or
    /// another condition/average/top-bottom/color filter on a different column) hid (finding
    /// R12-sort-filter-1). See FreeX.Core.Commands.FilterHiddenRowUpdater.ApplyColumnOwnedVisibility.
    /// </summary>
    public Dictionary<uint, HashSet<uint>> ColumnFilterOwnedRows { get; } = [];

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

    /// <summary>
    /// Rows that are the visible "anchor" (subtotal/summary) row of a collapsed outline group --
    /// i.e. they carry Excel's <c>collapsed="1"</c> outline marker while remaining visible
    /// themselves (they are NOT in <see cref="GroupHiddenRows"/>, which tracks the hidden detail
    /// rows the anchor summarizes). Distinguishing the two is required because a full-rebuild XLSX
    /// save must apply <c>collapsed="1"</c> to the anchor row and only <c>hidden="1"</c> (never
    /// also <c>collapsed="1"</c>) to the detail rows in <see cref="GroupHiddenRows"/> -- conflating
    /// them stamps a spurious <c>collapsed="1"</c> onto interior hidden rows and silently drops the
    /// marker Excel actually used to place the group's "+/-" outline toggle. A row can legitimately
    /// appear in both sets at once (it is hidden as a nested detail row of an outer group while also
    /// anchoring its own now-collapsed inner group).
    /// </summary>
    public HashSet<uint> CollapsedAnchorRows { get; } = [];

    /// <summary>Columns that are the visible anchor of a collapsed outline group. See <see cref="CollapsedAnchorRows"/>.</summary>
    public HashSet<uint> CollapsedAnchorCols { get; } = [];

    /// <summary>
    /// Rows that <see cref="FreeX.Core.Commands.SubtotalCommand"/> itself inserted (each group's
    /// own subtotal row and the grand-total row), tracked as real state authored by that command --
    /// NOT re-derived by scanning cell formula text for a "SUBTOTAL(" prefix. A hand-authored
    /// formula that happens to start with SUBTOTAL( (e.g. a user's own running total) must never be
    /// mistaken for a row the Subtotal command created, or Data &gt; Subtotal &gt; Remove All /
    /// Replace deletes that user's own row and its data (see the review finding that introduced this
    /// set). This is intentionally NOT persisted to any file format (XLSX/JSON/legacy adapters): a
    /// freshly-loaded workbook simply has an empty set, so after a save/reload, Remove/Replace
    /// Subtotals degrades safely to finding nothing (a no-op) rather than ever falling back to the
    /// old text-matching heuristic and risking deleting unrelated rows.
    /// </summary>
    public HashSet<uint> SubtotalRows { get; } = [];

    /// <summary>True if the row is hidden by any mechanism (filter, manual, or group collapse).</summary>
    public bool IsRowEffectivelyHidden(uint row) =>
        HiddenRows.Contains(row) || FilterHiddenRows.Contains(row) || GroupHiddenRows.Contains(row);

    /// <summary>True if the column is hidden by any mechanism.</summary>
    public bool IsColEffectivelyHidden(uint col) =>
        HiddenCols.Contains(col) || GroupHiddenCols.Contains(col);

    /// <summary>
    /// True if the row is hidden specifically by an active AutoFilter, as opposed to a manual
    /// Format &gt; Hide Row or an outline-group collapse. Excel's status-bar AutoCalculate
    /// (Sum/Average/Count/...) over a plain rectangular selection still includes manually-hidden
    /// and group-collapsed rows -- only AutoFilter-hidden rows are genuinely absent from the
    /// selection's contribution (that distinction is exactly why SUBTOTAL(109,...) exists as a
    /// separate mechanism to additionally exclude manual/group-hidden rows). Callers that compute
    /// selection-scoped aggregates (see WorkbookSelectionStatsCalculator) must use this predicate
    /// instead of <see cref="IsRowEffectivelyHidden"/>, which is intended for rendering/print/
    /// navigation, where every hiding mechanism is equally "not shown".
    /// </summary>
    public bool IsRowFilterHidden(uint row) => FilterHiddenRows.Contains(row);

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
    /// R106-io-hyperlink-range-shift: whole-column ("C:C"), whole-row ("3:3"), and oversized
    /// bounded-range (over 100,000 cells) hyperlink refs. These can never enter
    /// <see cref="Hyperlinks"/>/<see cref="HyperlinkMetadata"/> (both are single-<see cref="CellAddress"/>-
    /// keyed, and ClosedXML would otherwise materialize one entry per cell in the range -- up to ~1M
    /// entries for a whole column). Key is the ORIGINAL ref string exactly as first read from the
    /// source file (a stable identity used to re-correlate with the pristine source-package XML
    /// snapshot at save time); value is the CURRENT (live) <see cref="GridRange"/>, kept up to date by
    /// every row/column insert or delete the session performs via RowColumnShiftHelpers, mirroring how
    /// DataValidation/ConditionalFormat ranges are shifted. A whole-column/row range's GridRange spans
    /// the full row extent (1..<see cref="CellAddress.MaxRow"/>) or column extent
    /// (1..<see cref="CellAddress.MaxCol"/>) respectively, so the same shift helpers that already
    /// special-case a full-column/row selection apply unchanged.
    /// </summary>
    public Dictionary<string, GridRange> RangeHyperlinks { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Per-cell rich-text run sequences, keyed by cell address.
    /// Only populated when a text cell has more than one run <em>or</em> a run deviates from the
    /// cell's <see cref="CellStyle"/>.  The plain-text value in <c>Cell.Value</c> (a
    /// <see cref="ScalarValue"/> <c>TextValue</c>) always remains the authoritative string for
    /// formulas, search, and number-format — this map is a parallel decoration layer.
    /// </summary>
    public Dictionary<CellAddress, IReadOnlyList<CellTextRun>> RichTextRuns { get; } = [];

    /// <summary>
    /// Per-cell phonetic-guide (furigana) native passthrough for rich-text cells, keyed by the
    /// same <see cref="CellAddress"/> as <see cref="RichTextRuns"/>. Populated from a cell's
    /// <c>&lt;is&gt;</c>/<c>&lt;si&gt;</c> <c>&lt;rPh&gt;</c>/<c>&lt;phoneticPr&gt;</c> children
    /// on load and re-emitted verbatim alongside the rewritten runs on save, so an edit to a
    /// run's formatting does not drop the phonetic guide.
    /// </summary>
    public Dictionary<CellAddress, CellPhoneticGuide> CellPhoneticGuides { get; } = [];

    private bool _isProtected;

    /// <summary>
    /// True when the sheet is protected against edits. Toggling this (in either direction — on
    /// protect or on unprotect) clears <see cref="UnlockedAllowEditRanges"/> so a per-session
    /// range unlock granted under a previous protection/password never survives a re-protect with
    /// a changed (or newly (re)added) range password; callers must supply the range password again
    /// under the new protection state. Assigning the same value the property already holds is a
    /// no-op and does not clear the set (e.g. <c>Revert</c> restoring an unchanged prior state).
    /// </summary>
    public bool IsProtected
    {
        get => _isProtected;
        set
        {
            if (_isProtected == value)
                return;

            _isProtected = value;
            UnlockedAllowEditRanges.Clear();
        }
    }

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

    /// <summary>
    /// Per-range password for entries in <see cref="AllowEditRanges"/> (Excel's Allow Users to Edit
    /// Ranges "Range Password", distinct from the sheet's own <see cref="ProtectionPassword"/>).
    /// Keyed by the exact <see cref="GridRange"/> as stored in <see cref="AllowEditRanges"/>. Stored
    /// in the same encoded form <see cref="ProtectionPasswordHelper"/> understands (a plain-hashed
    /// legacy verifier or an <see cref="ProtectionPasswordHelper.EncodeIso29500Hash"/>-encoded modern
    /// hash), so it can be verified with <see cref="ProtectionPasswordHelper.VerifyStoredPassword"/>.
    /// A range absent from this dictionary (or mapped to null/empty) has no password of its own.
    /// </summary>
    public Dictionary<GridRange, string?> AllowEditRangePasswords { get; } = [];

    /// <summary>
    /// Ranges from <see cref="AllowEditRanges"/> that have already been unlocked in the current
    /// session (the user supplied the correct range password once, verified via
    /// <c>CommandGuards.TryUnlockAllowEditRange</c>). Not persisted to the workbook file or undo
    /// history — purely an in-memory, per-session gate so the password prompt is not repeated for
    /// every edit. Cleared automatically whenever <see cref="IsProtected"/> actually changes value
    /// (protect or unprotect, including undo/redo of either), so a stale unlock granted under a
    /// previous protection cannot silently survive a re-protect with a new/changed range password.
    /// Still not cleared by directly mutating <see cref="AllowEditRangePasswords"/> or
    /// <see cref="AllowEditRanges"/> without also toggling <see cref="IsProtected"/>; callers that
    /// change a range's password while the sheet stays protected must clear the relevant entry (or
    /// the whole set) themselves.
    /// </summary>
    public HashSet<GridRange> UnlockedAllowEditRanges { get; } = [];

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

    /// <summary>
    /// Get the cell at the given address, or null if no cell exists there.
    /// </summary>
    /// <remarks>
    /// <b>Does NOT see the dynamic-array spill overlay.</b> A non-anchor member of a spilled
    /// array (e.g. the B1 in <c>=SEQUENCE(5)</c> anchored at A1) has no entry in <c>_cells</c> --
    /// its value lives only in the separate spill overlay -- so this method returns null for it
    /// even though the grid visibly shows a value there. See <see cref="GetValue(uint,uint)"/>,
    /// which checks the overlay and is what the divergence is deliberately routed through.
    /// Callers asking a VALUE question (is this blank? what type/value does this cell show?)
    /// must call <see cref="GetValue(uint,uint)"/> instead of calling this and then inspecting
    /// <see cref="Cell.Value"/> -- doing the latter silently mis-reads spill members as blank.
    /// This method remains correct for callers that specifically want the backing <see cref="Cell"/>
    /// object itself (its formula, style, comments, etc.) rather than its effective value.
    /// </remarks>
    public Cell? GetCell(uint row, uint col)
    {
        return _cells.GetValueOrDefault((row, col));
    }

    /// <summary>
    /// Get the cell at the given address, or null if no cell exists there.
    /// </summary>
    /// <remarks>
    /// See the remarks on <see cref="GetCell(uint,uint)"/>: this overload has the same
    /// spill-overlay blind spot. Use <see cref="GetValue(CellAddress)"/> for value questions.
    /// </remarks>
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
                bool isAnchor = r == 0 && c == 0;
                long targetRow = (long)anchor.Row + r;
                long targetCol = (long)anchor.Col + c;
                if (targetRow > CellAddress.MaxRow || targetCol > CellAddress.MaxCol) return true;
                var key = ((uint)targetRow, (uint)targetCol);
                // Excel refuses to spill into a merged cell ("Spill range has merged cells"),
                // even when that region is otherwise empty (no _cells/_spillValues entry). This
                // check also applies to the anchor cell itself: if the anchor is part of a merged
                // region, Excel still refuses the spill ("merged cells") even though the anchor is
                // the cell holding the formula.
                if (IsMerged(new CellAddress(anchor.Sheet, key.Item1, key.Item2))) return true;
                // R20-array-dynamic-spill-2: Excel also refuses to spill into (or through) an Excel
                // Table's footprint ("Spill range has table"), even for a blank table body cell that
                // has no _cells/_spillValues entry of its own. This also applies to the anchor cell.
                if (StructuredTables.Count > 0)
                {
                    var candidate = new CellAddress(anchor.Sheet, key.Item1, key.Item2);
                    foreach (var table in StructuredTables)
                    {
                        if (table.Range.Contains(candidate)) return true;
                    }
                }
                // The occupied-cell checks below only apply to non-anchor cells: the anchor cell
                // itself already holds the formula being evaluated, so it is expected to be
                // "occupied" and must not block its own spill.
                if (isAnchor) continue;
                if (_cells.TryGetValue(key, out var occupant))
                {
                    // A provisional cached spill cell loaded from the XLSX for THIS anchor does not
                    // block the anchor — it is overwritten when the anchor re-spills via SetSpillRange.
                    if (_provisionalSpillCells is not null &&
                        _provisionalSpillCells.TryGetValue(key, out var owningAnchor) &&
                        owningAnchor == (anchor.Row, anchor.Col))
                        continue;
                    // Excel's spill-blocking rule looks at whether the destination cell has actual
                    // content (a value or a formula), not whether it merely carries formatting. A cell
                    // that was cleared via Clear Contents (or had formatting pasted onto it) but keeps
                    // its StyleId in a live _cells entry — Value is BlankValue and there's no formula —
                    // is not "occupied" and must not block a spill.
                    if (occupant.HasFormula || occupant.Value is not BlankValue)
                        return true;
                    continue;
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

    /// <summary>
    /// Captures the live spill payload rooted at <paramref name="anchor"/>, if it is currently a
    /// spill anchor, as a <see cref="RangeValue"/> that can be replayed via <see cref="SetSpillRange"/>
    /// once the anchor's formula cell has been relocated by a structural edit (a row/column
    /// insert/delete shift, or a range Move). MUST be called BEFORE the anchor cell is
    /// cleared/moved: <see cref="ClearCell(CellAddress)"/> and <see cref="SetCell(CellAddress, Cell)"/>
    /// both tear down the spill via <see cref="ClearSpillRange"/> as a side effect and, unless the
    /// caller re-establishes it at the new address afterward, the array's spilled members are
    /// permanently lost (R20-array-dynamic-spill-1). Returns null when <paramref name="anchor"/> is
    /// not currently a live spill anchor.
    /// </summary>
    public RangeValue? CaptureSpillForRelocate(CellAddress anchor)
    {
        if (!_spillAnchors.TryGetValue((anchor.Row, anchor.Col), out var extent))
            return null;

        var cells = new ScalarValue[extent.Rows, extent.Cols];
        for (uint r = 0; r < extent.Rows; r++)
            for (uint c = 0; c < extent.Cols; c++)
            {
                if (r == 0 && c == 0)
                {
                    // The anchor's own value is carried by the moved formula cell itself;
                    // SetSpillRange ignores slot [0,0] of the supplied RangeValue.
                    cells[r, c] = BlankValue.Instance;
                    continue;
                }
                cells[r, c] = _spillValues.TryGetValue((anchor.Row + r, anchor.Col + c), out var v)
                    ? v
                    : BlankValue.Instance;
            }
        return new RangeValue(cells);
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

    /// <summary>
    /// If <paramref name="address"/> is any cell belonging to a live dynamic-array spill range or a
    /// legacy CSE array's declared range — whether the anchor itself or one of its covered members —
    /// returns the anchor address and the array's full extent (rows × cols, including the anchor).
    /// Covers provisional cached-spill cells loaded from an XLSX before the first recalculation has
    /// run, as well as anchors/members produced by a live in-session <see cref="SetSpillRange"/>.
    /// Returns false for cells with no array/spill membership at all.
    /// Used to block edits/deletes that would split an array — Excel's "You cannot change part of
    /// an array" rule — while still allowing the whole array to be edited as a unit.
    /// </summary>
    public bool TryGetArrayExtent(CellAddress address, out CellAddress anchor, out uint rows, out uint cols)
    {
        var key = (address.Row, address.Col);

        // Address is itself a live spill anchor.
        if (_spillAnchors.TryGetValue(key, out var ownExtent))
        {
            anchor = address;
            rows = ownExtent.Rows;
            cols = ownExtent.Cols;
            return true;
        }

        // Address is a member covered by some other live spill anchor.
        foreach (var (anchorKey, extent) in _spillAnchors)
        {
            if (address.Row < anchorKey.Row || address.Col < anchorKey.Col) continue;
            if (address.Row >= anchorKey.Row + extent.Rows || address.Col >= anchorKey.Col + extent.Cols) continue;
            anchor = new CellAddress(Id, anchorKey.Row, anchorKey.Col);
            rows = extent.Rows;
            cols = extent.Cols;
            return true;
        }

        // Provisional cached-spill cell (legacy CSE array or dynamic array loaded from XLSX, not yet
        // recalculated) — either the address is itself tagged as a member, or it is the anchor of one
        // or more provisional members (the anchor itself is never a key in _provisionalSpillCells).
        if (_provisionalSpillCells is { Count: > 0 })
        {
            (uint AnchorRow, uint AnchorCol) owningAnchor;
            if (!_provisionalSpillCells.TryGetValue(key, out owningAnchor))
                owningAnchor = (address.Row, address.Col);

            rows = 0;
            cols = 0;
            var found = false;
            foreach (var (memberKey, memberOwner) in _provisionalSpillCells)
            {
                if (memberOwner != owningAnchor) continue;
                found = true;
                rows = Math.Max(rows, memberKey.Row - owningAnchor.AnchorRow + 1);
                cols = Math.Max(cols, memberKey.Col - owningAnchor.AnchorCol + 1);
            }
            if (found)
            {
                anchor = new CellAddress(Id, owningAnchor.AnchorRow, owningAnchor.AnchorCol);
                rows = Math.Max(rows, 1);
                cols = Math.Max(cols, 1);
                return true;
            }
        }

        anchor = default;
        rows = 0;
        cols = 0;
        return false;
    }

    /// <summary>
    /// Get the value at a cell address, returning BlankValue if no cell exists.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="GetCell(uint,uint)"/>, this DOES see the dynamic-array spill overlay:
    /// a non-anchor spill member with no <c>_cells</c> entry still returns its spilled value here
    /// (via the <c>_spillValues</c> fallback below). This is the correct method for any value
    /// question -- blank test, type test, comparison, conditional-format input, etc.
    /// </remarks>
    public ScalarValue GetValue(uint row, uint col)
    {
        if (_cells.TryGetValue((row, col), out var cell)) return cell.Value;
        if (_spillValues.TryGetValue((row, col), out var spill)) return spill;
        return BlankValue.Instance;
    }

    /// <summary>
    /// Get the value at a cell address, returning BlankValue if no cell exists.
    /// </summary>
    /// <remarks>See the remarks on <see cref="GetValue(uint,uint)"/>.</remarks>
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

    /// <summary>
    /// Number of cells holding a dynamic-array spill value that lives only in the spill overlay
    /// (i.e. not also tracked in <see cref="CellCount"/>). Callers that need an upper bound on the
    /// number of value-bearing cells on the sheet -- e.g. to choose between a sparse scan over
    /// <see cref="EnumerateValueBearingCells"/> and a dense range walk -- should add this to
    /// <see cref="CellCount"/> so a large spill doesn't make the estimate look sparser than it is.
    /// </summary>
    public int SpillValueCount => _spillValues.Count;

    /// <summary>Number of cells that currently contain formulas.</summary>
    public int FormulaCellCount => _formulaCells.Count;

    /// <summary>Whether any cell on the sheet currently contains a formula.</summary>
    public bool HasFormulas => _formulaCells.Count > 0;

    /// <summary>Whether any spill values have been written to this sheet (i.e. at least one dynamic-array formula has spilled).</summary>
    public bool HasSpillValues => _spillValues.Count > 0;

    /// <summary>
    /// Whether this sheet has any live spill anchor or provisional cached-spill cell at all — i.e.
    /// whether <see cref="TryGetArrayExtent"/> could possibly return true for any address. Lets
    /// callers cheaply skip a per-cell array-membership scan over a large range when the sheet has
    /// no arrays/spills whatsoever.
    /// </summary>
    public bool HasArrayOrSpillMembers => _spillAnchors.Count > 0 || _provisionalSpillCells is { Count: > 0 };

    /// <summary>
    /// R90-app-goalseek-whatif-5-3: registered What-If Analysis "Data Table" result-body ranges
    /// (Data &gt; What-If Analysis &gt; Data Table). Excel writes a Data Table's body as a single
    /// {=TABLE(,...)} array and refuses to edit or delete just one interior cell of it ("You cannot
    /// change part of a Data Table"), even though FreeX stores each body cell as its own ordinary
    /// formula cell (see OneVariableDataTableCommand/TwoVariableDataTableCommand). This is a
    /// lightweight, independent registry -- NOT the dynamic-array spill/legacy-CSE machinery
    /// (<see cref="_spillAnchors"/>/<see cref="_provisionalSpillCells"/>), whose lifecycle (values
    /// stored in a separate overlay, torn down via <see cref="SetSpillRange"/>/<see cref="ClearSpillRange"/>)
    /// doesn't fit a Data Table's plain-formula-cell body. Consulted by
    /// <see cref="CommandGuards.RejectIfSplitsArray"/> alongside the array/spill check.
    ///
    /// R115-data-table-master-formula-refresh: also carries the driver-cell metadata (master
    /// formula cell + input cell(s) + orientation) needed to re-derive the body when the master
    /// formula is edited after the table already exists -- see <see cref="DataTableRegistrations"/>
    /// and FreeX.Core.Commands' DataTableAutoRefreshEffects, the sole reader of that metadata (kept
    /// as plain data here since FreeX.Core.Model has no dependency on FreeX.Core.Commands).
    /// </summary>
    private readonly List<DataTableRegistration> _dataTableRanges = [];

    /// <summary>Whether this sheet has any registered Data Table range at all -- lets callers cheaply
    /// skip the per-address scan when no Data Table has ever been created (see <see cref="_dataTableRanges"/>).</summary>
    public bool HasDataTableRanges => _dataTableRanges.Count > 0;

    /// <summary>
    /// Registers <paramref name="registration"/> (the Data Table's full range plus its driver
    /// formula/input cells) so edits/deletes of a single body cell are blocked, and so a later edit
    /// of the master/header formula cell(s) can be detected and the body refreshed. Replaces any
    /// previously-registered table sharing the same top-left corner, so re-running the Data Table
    /// command over a resized range doesn't leave a stale, differently-sized registration.
    /// </summary>
    public void RegisterDataTableRange(DataTableRegistration registration)
    {
        _dataTableRanges.RemoveAll(r => r.TableRange.Start == registration.TableRange.Start);
        _dataTableRanges.Add(registration);
    }

    /// <summary>Removes a previously-registered Data Table (matched by its result-body range, e.g.
    /// on command undo).</summary>
    public void UnregisterDataTableRange(GridRange bodyRange) =>
        _dataTableRanges.RemoveAll(r => r.BodyRange.Equals(bodyRange));

    /// <summary>If <paramref name="address"/> falls within a registered Data Table's result body, returns it.</summary>
    public bool TryGetDataTableRange(CellAddress address, out GridRange range)
    {
        foreach (var candidate in _dataTableRanges)
        {
            if (candidate.BodyRange.Contains(address))
            {
                range = candidate.BodyRange;
                return true;
            }
        }

        range = default;
        return false;
    }

    /// <summary>Every registered Data Table on this sheet, including its driver-cell metadata --
    /// see <see cref="RegisterDataTableRange"/>.</summary>
    public IReadOnlyList<DataTableRegistration> DataTableRegistrations => _dataTableRanges;

    /// <summary>Get all non-empty cells as a dictionary keyed by CellAddress.</summary>
    public Dictionary<CellAddress, Cell> GetUsedCells()
    {
        var result = new Dictionary<CellAddress, Cell>(_cells.Count);
        foreach (var ((row, col), cell) in _cells)
            result[new CellAddress(Id, row, col)] = cell;
        return result;
    }

    /// <summary>
    /// Get the bounding range of all non-empty cells, or null if the sheet is empty. Excel's used
    /// range (and Ctrl+End) also extends over cells that carry formatting but no value, so the
    /// result additionally accounts for style-only cells (<see cref="_styleOnly"/>/<see cref="_styleOnlyRuns"/>).
    /// </summary>
    public GridRange? GetUsedRange()
    {
        if (_usedRangeCacheDirty)
        {
            _usedRangeCache = ComputeValueUsedRange();
            _usedRangeCacheDirty = false;
        }

        return MergeStyleOnlyIntoUsedRange(_usedRangeCache);
    }

    private GridRange? ComputeValueUsedRange()
    {
        if (_cells.Count == 0 && _spillValues.Count == 0)
            return null;

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
            return null;

        return new GridRange(
            new CellAddress(Id, minRow, minCol),
            new CellAddress(Id, maxRow, maxCol));
    }

    /// <summary>
    /// Get the bounding range of all non-empty (or style-only) cells whose column falls within
    /// [<paramref name="startCol"/>, <paramref name="endCol"/>], or null if there are none. Unlike
    /// <see cref="GetUsedRange"/> (the whole sheet's bounding box, across every column), this scopes
    /// the scan to just the given column band -- used to clamp a whole-column selection to the real
    /// data extent of the columns actually selected, so a stray cell sitting in some other, unselected
    /// column can't inflate the result.
    /// </summary>
    public GridRange? GetUsedRangeInColumns(uint startCol, uint endCol) =>
        ComputeUsedRangeWithinBand(inRows: false, startCol, endCol);

    /// <summary>
    /// Get the bounding range of all non-empty (or style-only) cells whose row falls within
    /// [<paramref name="startRow"/>, <paramref name="endRow"/>], or null if there are none. The
    /// row-scoped counterpart of <see cref="GetUsedRangeInColumns"/>, used to clamp a whole-row
    /// selection to the real data extent of the rows actually selected.
    /// </summary>
    public GridRange? GetUsedRangeInRows(uint startRow, uint endRow) =>
        ComputeUsedRangeWithinBand(inRows: true, startRow, endRow);

    private GridRange? ComputeUsedRangeWithinBand(bool inRows, uint bandStart, uint bandEnd)
    {
        uint minRow = uint.MaxValue, maxRow = 0, minCol = uint.MaxValue, maxCol = 0;
        var found = false;

        void Consider(uint row, uint col)
        {
            var band = inRows ? row : col;
            if (band < bandStart || band > bandEnd)
                return;

            found = true;
            if (row < minRow) minRow = row;
            if (row > maxRow) maxRow = row;
            if (col < minCol) minCol = col;
            if (col > maxCol) maxCol = col;
        }

        foreach (var (row, col) in _cells.Keys)
            Consider(row, col);

        foreach (var ((row, col), value) in _spillValues)
        {
            if (value is BlankValue || _cells.ContainsKey((row, col)))
                continue;

            Consider(row, col);
        }

        foreach (var (row, col) in _styleOnly.Keys)
            Consider(row, col);

        if (_styleOnlyRuns is { Count: > 0 } runs)
        {
            foreach (var run in runs)
            {
                if (inRows)
                {
                    Consider(run.Row, run.StartCol);
                    Consider(run.Row, run.EndCol);
                }
                else
                {
                    var runStart = Math.Max(run.StartCol, bandStart);
                    var runEnd = Math.Min(run.EndCol, bandEnd);
                    if (runStart > runEnd)
                        continue;

                    Consider(run.Row, runStart);
                    Consider(run.Row, runEnd);
                }
            }
        }

        if (!found)
            return null;

        return new GridRange(
            new CellAddress(Id, minRow, minCol),
            new CellAddress(Id, maxRow, maxCol));
    }

    /// <summary>
    /// Widens <paramref name="valueRange"/> (the cached value/spill bounding box) to also cover any
    /// style-only (formatting-only, empty) cells. Style-only writes don't flow through
    /// <see cref="TrackUsedRangeCellSet"/>/<see cref="TrackUsedRangeCellCleared"/>, so this is
    /// recomputed on every call instead of being folded into the incremental cache — cheap since it
    /// only walks the (typically small) style-only overlay dictionary plus the compressed run list,
    /// not the full grid.
    /// </summary>
    private GridRange? MergeStyleOnlyIntoUsedRange(GridRange? valueRange)
    {
        if (_styleOnly.Count == 0 && _styleOnlyRuns is not { Count: > 0 })
            return valueRange;

        uint minRow, maxRow, minCol, maxCol;
        if (valueRange is { } range)
        {
            minRow = range.Start.Row;
            maxRow = range.End.Row;
            minCol = range.Start.Col;
            maxCol = range.End.Col;
        }
        else
        {
            minRow = uint.MaxValue;
            maxRow = 0;
            minCol = uint.MaxValue;
            maxCol = 0;
        }

        foreach (var (row, col) in _styleOnly.Keys)
        {
            if (row < minRow) minRow = row;
            if (row > maxRow) maxRow = row;
            if (col < minCol) minCol = col;
            if (col > maxCol) maxCol = col;
        }

        if (_styleOnlyRuns is { Count: > 0 } runs)
        {
            foreach (var run in runs)
            {
                if (run.Row < minRow) minRow = run.Row;
                if (run.Row > maxRow) maxRow = run.Row;
                if (run.StartCol < minCol) minCol = run.StartCol;
                if (run.EndCol > maxCol) maxCol = run.EndCol;
            }
        }

        return new GridRange(
            new CellAddress(Id, minRow, minCol),
            new CellAddress(Id, maxRow, maxCol));
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
