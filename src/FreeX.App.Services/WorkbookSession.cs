using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed class WorkbookSession
{
    private sealed record InternalClipboard(
        GridRange SourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> Cells,
        string Text,
        bool IsCut);

    private const double MaximumRowHeight = 409.5;

    private readonly IReadOnlyList<IFileAdapter> _adapters;
    private readonly StartupWorkbookLoadResult _source;
    private readonly WorkbookCellEditService _cellEditService;
    private readonly WorkbookSheetSelectionService _sheetSelectionService;
    private readonly IViewportService _viewportService;
    private readonly bool _includeObjects;
    private readonly WorkbookSelectionStatsCache _selectionStatsCache = new();
    private readonly HashSet<SheetId> _groupedSheetIds = [];
    private SheetId? _sheetGroupAnchor;
    private InternalClipboard? _internalClipboard;
    private double _viewportHeight;
    private double _viewportWidth;
    private ulong _selectionStatsRevision;

    internal WorkbookSession(
        StartupWorkbookLoadResult source,
        IReadOnlyList<IFileAdapter> adapters,
        WorkbookCellEditService cellEditService,
        WorkbookSheetSelectionService sheetSelectionService,
        IViewportService viewportService,
        double viewportHeight,
        double viewportWidth,
        bool includeObjects)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(cellEditService);
        ArgumentNullException.ThrowIfNull(sheetSelectionService);
        ArgumentNullException.ThrowIfNull(viewportService);

        _source = source;
        _adapters = adapters;
        _cellEditService = cellEditService;
        _sheetSelectionService = sheetSelectionService;
        _viewportService = viewportService;
        _viewportHeight = NormalizeViewportDimension(viewportHeight, fallback: 1);
        _viewportWidth = NormalizeViewportDimension(viewportWidth, fallback: 1);
        _includeObjects = includeObjects;

        Workbook = source.Workbook;
        CurrentFilePath = source.OpenedAsTemplate ? null : source.SourcePath;
        CurrentXlsxFeatureReport = source.FeatureReport;
        OpenFormats = BuildFormats(adapters, static format => format.CanOpen);
        SaveFormats = BuildFormats(adapters, static format => format.CanSave);

        var selection = _sheetSelectionService.EnsureActiveSheet(Workbook);
        ActiveSheet = selection.Sheet;
        SheetTabs = selection.Tabs;
        SelectSingleSheetGroup(ActiveSheet.Id);
        RefreshSheetTabsForActiveSheet();
        ActiveCell = GetInitialActiveCell(ActiveSheet);
        SelectedRange = new GridRange(ActiveCell, ActiveCell);
        Viewport = BuildViewport();
    }

    public Workbook Workbook { get; }

    public Sheet ActiveSheet { get; private set; }

    public ViewportModel Viewport { get; private set; }

    public double ViewportHeight => _viewportHeight;

    public double ViewportWidth => _viewportWidth;

    public CellAddress ActiveCell { get; private set; }

    public GridRange SelectedRange { get; private set; }

    public CellAddress? FormulaEditAddress { get; private set; }

    public IReadOnlyList<WorkbookSheetTab> SheetTabs { get; private set; }

    public bool IsWorkbookGrouped =>
        _groupedSheetIds.Contains(ActiveSheet.Id) &&
        GetSelectableSheetIds().Count(_groupedSheetIds.Contains) > 1;

    public bool IsShowingGridlines => ActiveSheet.ShowGridlines;

    public bool IsShowingHeadings => ActiveSheet.ShowHeadings;

    public bool IsShowingFormulas => ActiveSheet.ShowFormulas;

    public int ZoomPercent => ActiveSheet.ZoomPercent;

    public IReadOnlyList<WorkbookHiddenSheet> HiddenSheets =>
        Workbook.Sheets
            .Where(sheet => sheet.IsHidden && !sheet.IsVeryHidden)
            .Select(sheet => new WorkbookHiddenSheet(sheet.Id, sheet.Name))
            .ToList();

    public bool CanHideActiveSheet =>
        Workbook.Sheets.Any(sheet => sheet.Id != ActiveSheet.Id && !sheet.IsHidden && !sheet.IsVeryHidden);

    public string? CurrentFilePath { get; private set; }

    public XlsxFeatureReport? CurrentXlsxFeatureReport { get; private set; }

    public bool IsDirty { get; private set; }

    public bool IsFallback => _source.IsFallback;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(CurrentFilePath)
            ? _source.DisplayName
            : Path.GetFileName(CurrentFilePath);

    public string StartupStatus => FormatStartupStatus(_source);

    public IReadOnlyList<FileFormatDescriptor> OpenFormats { get; }

    public IReadOnlyList<FileFormatDescriptor> SaveFormats { get; }

    public bool CanUndo => _cellEditService.CanUndo(Workbook.Id);

    public bool CanRedo => _cellEditService.CanRedo(Workbook.Id);

    public bool IsSelectedRangeStartBold => GetCellStyle(SelectedRange.Start).Bold;

    public bool IsSelectedRangeStartItalic => GetCellStyle(SelectedRange.Start).Italic;

    public bool IsSelectedRangeStartUnderline
    {
        get
        {
            var style = GetCellStyle(SelectedRange.Start);
            return style.Underline && !style.Strikethrough;
        }
    }

    public bool IsSelectedRangeStartStrikethrough => GetCellStyle(SelectedRange.Start).Strikethrough;

    public bool IsSelectedRangeStartDoubleUnderline => GetCellStyle(SelectedRange.Start).DoubleUnderline;

    public bool IsSelectedRangeStartWrapText => GetCellStyle(SelectedRange.Start).WrapText;

    public HorizontalAlignment SelectedRangeStartHorizontalAlignment =>
        GetCellStyle(SelectedRange.Start).HorizontalAlignment;

    public VerticalAlignment SelectedRangeStartVerticalAlignment =>
        GetCellStyle(SelectedRange.Start).VerticalAlignment;

    public int SelectedRangeStartIndentLevel =>
        GetCellStyle(SelectedRange.Start).IndentLevel;

    public double SelectedRangeStartFontSize =>
        GetCellStyle(SelectedRange.Start).FontSize;

    public int SelectedRangeStartTextRotation =>
        GetCellStyle(SelectedRange.Start).TextRotation;

    public CellColor SelectedRangeStartFontColor =>
        GetCellStyle(SelectedRange.Start).FontColor;

    public CellColor? SelectedRangeStartFillColor =>
        GetCellStyle(SelectedRange.Start).FillColor;

    public string SelectedRangeStartNumberFormat =>
        GetCellStyle(SelectedRange.Start).NumberFormat;

    public WorkbookSelectionStats SelectionStats =>
        _selectionStatsCache.GetOrCalculate(ActiveSheet, SelectedRange, _selectionStatsRevision);

    public string SelectionStatsText =>
        WorkbookSelectionStatsFormatter.Format(SelectionStats);

    public void SelectCell(CellAddress address)
    {
        ActiveCell = address;
        ActiveSheet.ActiveRow = address.Row;
        ActiveSheet.ActiveCol = address.Col;
        SelectedRange = new GridRange(address, address);
        FormulaEditAddress = null;
        EnsureActiveCellVisible();
    }

    public void SelectRange(GridRange range)
    {
        if (!range.Start.Sheet.Equals(ActiveSheet.Id))
            throw new ArgumentException("Selected range must be on the active sheet.", nameof(range));
        if (!IsValidAddress(range.Start) || !IsValidAddress(range.End))
            throw new ArgumentOutOfRangeException(nameof(range), "Selected range must be inside the worksheet bounds.");

        SelectedRange = range;
        ActiveCell = range.Start;
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        FormulaEditAddress = null;
        EnsureActiveCellVisible();
    }

    public GridRange SelectCurrentRegionOrAll()
    {
        if (SelectionRangeService.GetCurrentRegion(ActiveSheet, ActiveCell) is { } currentRegion &&
            SelectedRange != currentRegion)
        {
            SelectRange(currentRegion);
            return currentRegion;
        }

        var wholeSheet = new GridRange(
            new CellAddress(ActiveSheet.Id, 1, 1),
            new CellAddress(ActiveSheet.Id, CellAddress.MaxRow, CellAddress.MaxCol));
        SelectRange(wholeSheet);
        return wholeSheet;
    }

    public void MoveActiveCell(int rowDelta, int colDelta)
    {
        var address = new CellAddress(
            ActiveSheet.Id,
            Offset(ActiveCell.Row, rowDelta, CellAddress.MaxRow),
            Offset(ActiveCell.Col, colDelta, CellAddress.MaxCol));
        SelectCell(address);
    }

    public bool PanViewport(int rowDelta, int colDelta)
    {
        var nextTopRow = Offset(ActiveSheet.ViewTopRow ?? GetScrollableRowStart(), rowDelta, CellAddress.MaxRow);
        var nextLeftCol = Offset(ActiveSheet.ViewLeftCol ?? GetScrollableColumnStart(), colDelta, CellAddress.MaxCol);
        return SetViewportOrigin(nextTopRow, nextLeftCol);
    }

    public bool SetViewportOrigin(uint topRow, uint leftCol)
    {
        var normalizedTopRow = Math.Clamp(topRow, GetScrollableRowStart(), CellAddress.MaxRow);
        var normalizedLeftCol = Math.Clamp(leftCol, GetScrollableColumnStart(), CellAddress.MaxCol);
        var currentTopRow = ActiveSheet.ViewTopRow ?? GetScrollableRowStart();
        var currentLeftCol = ActiveSheet.ViewLeftCol ?? GetScrollableColumnStart();
        if (normalizedTopRow == currentTopRow && normalizedLeftCol == currentLeftCol)
            return false;

        ActiveSheet.ViewTopRow = normalizedTopRow;
        ActiveSheet.ViewLeftCol = normalizedLeftCol;
        RefreshViewport();
        return true;
    }

    public bool UpdateViewportSize(double viewportHeight, double viewportWidth)
    {
        var normalizedHeight = NormalizeViewportDimension(viewportHeight, _viewportHeight);
        var normalizedWidth = NormalizeViewportDimension(viewportWidth, _viewportWidth);
        if (normalizedHeight == _viewportHeight && normalizedWidth == _viewportWidth)
            return false;

        _viewportHeight = normalizedHeight;
        _viewportWidth = normalizedWidth;
        RefreshViewport();
        EnsureActiveCellVisible();
        return true;
    }

    public bool SelectSheet(SheetId sheetId)
        => SelectSheet(sheetId, selectRange: false, toggle: false);

    public bool SelectSheetFromTab(SheetId sheetId, bool selectRange, bool toggle)
        => SelectSheet(sheetId, selectRange, toggle);

    private bool SelectSheet(SheetId sheetId, bool selectRange, bool toggle)
    {
        var previousSheetId = ActiveSheet.Id;
        var previousGroupedSheetIds = _groupedSheetIds.ToHashSet();
        var selection = _sheetSelectionService.SelectSheet(Workbook, sheetId);
        var sheetChanged = previousSheetId != selection.Sheet.Id;

        ActiveSheet = selection.Sheet;
        UpdateGroupedSheetsForTabSelection(ActiveSheet.Id, selectRange, toggle);
        RefreshSheetTabsForActiveSheet();
        FormulaEditAddress = null;

        if (sheetChanged)
        {
            ActiveCell = GetInitialActiveCell(ActiveSheet);
            SelectedRange = new GridRange(ActiveCell, ActiveCell);
            RefreshViewport();
        }

        return sheetChanged || !previousGroupedSheetIds.SetEquals(_groupedSheetIds);
    }

    public bool SelectAllVisibleSheets()
    {
        var changed = SetGroupedSheetIds(
            SheetGroupSelectionService.SelectAll(GetSelectableSheetIds()),
            ActiveSheet.Id);
        _sheetGroupAnchor = ActiveSheet.Id;
        RefreshSheetTabsForActiveSheet();
        return changed;
    }

    public bool UngroupSheets()
    {
        var changed = SetGroupedSheetIds([ActiveSheet.Id], ActiveSheet.Id);
        _sheetGroupAnchor = ActiveSheet.Id;
        RefreshSheetTabsForActiveSheet();
        return changed;
    }

    public WorkbookCellEditResult AddSheet()
    {
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new AddSheetCommand(WorkbookSheetNameGenerator.GenerateUniqueSheetName(Workbook)));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookStructureResult(Workbook.Sheets[^1].Id);
        return result;
    }

    public WorkbookCellEditResult DuplicateActiveSheet()
    {
        var sourceSheetId = ActiveSheet.Id;
        var sourceIndex = Workbook.Sheets.ToList().FindIndex(sheet => sheet.Id == sourceSheetId);
        if (sourceIndex < 0)
        {
            return new WorkbookCellEditResult(
                false,
                "Active sheet was not found.",
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new DuplicateSheetCommand(sourceSheetId));
        if (!result.Success)
            return result;

        var copyIndex = Math.Min(sourceIndex + 1, Workbook.Sheets.Count - 1);
        ApplySuccessfulWorkbookStructureResult(Workbook.Sheets[copyIndex].Id);
        return result;
    }

    public WorkbookCellEditResult MoveActiveSheetLeft() =>
        MoveActiveSheetBy(offset: -1);

    public WorkbookCellEditResult MoveActiveSheetRight() =>
        MoveActiveSheetBy(offset: 1);

    public WorkbookCellEditResult SetActiveSheetTabColor(CellColor? color)
    {
        if (ActiveSheet.TabColor == color)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetSheetTabColorCommand(ActiveSheet.Id, color));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    public WorkbookCellEditResult SetShowFormulas(bool showFormulas)
    {
        if (ActiveSheet.ShowFormulas == showFormulas)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetWorksheetShowFormulasCommand(ActiveSheet.Id, showFormulas));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    public WorkbookCellEditResult SetShowGridlines(bool showGridlines)
    {
        if (ActiveSheet.ShowGridlines == showGridlines)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        return SetWorksheetViewOptions(
            showGridlines,
            ActiveSheet.ShowHeadings,
            ActiveSheet.ShowRulers);
    }

    public WorkbookCellEditResult SetShowHeadings(bool showHeadings)
    {
        if (ActiveSheet.ShowHeadings == showHeadings)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        return SetWorksheetViewOptions(
            ActiveSheet.ShowGridlines,
            showHeadings,
            ActiveSheet.ShowRulers);
    }

    public WorkbookCellEditResult SetZoomPercent(int zoomPercent)
    {
        zoomPercent = Math.Clamp(
            zoomPercent,
            SetWorksheetZoomCommand.MinZoomPercent,
            SetWorksheetZoomCommand.MaxZoomPercent);
        if (ActiveSheet.ZoomPercent == zoomPercent)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetWorksheetZoomCommand(ActiveSheet.Id, zoomPercent));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    public WorkbookCellEditResult FreezePanesAtActiveCell()
    {
        var frozenRows = ActiveCell.Row > 1 ? ActiveCell.Row - 1 : 0;
        var frozenCols = ActiveCell.Col > 1 ? ActiveCell.Col - 1 : 0;
        return SetFreezePanes(frozenRows, frozenCols);
    }

    public WorkbookCellEditResult FreezeTopRow() =>
        SetFreezePanes(frozenRows: 1, frozenCols: 0);

    public WorkbookCellEditResult FreezeFirstColumn() =>
        SetFreezePanes(frozenRows: 0, frozenCols: 1);

    public WorkbookCellEditResult UnfreezePanes() =>
        SetFreezePanes(frozenRows: 0, frozenCols: 0);

    public WorkbookCellEditResult HideActiveSheet()
    {
        var sheetId = ActiveSheet.Id;
        var sheetIndex = Workbook.Sheets.ToList().FindIndex(sheet => sheet.Id == sheetId);
        if (sheetIndex < 0)
        {
            return new WorkbookCellEditResult(
                false,
                "Active sheet was not found.",
                [],
                RecalcReport: null);
        }

        var preferredSheetId = FindPreferredVisibleSheetIdAfterHidden(sheetIndex, sheetId);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetSheetHiddenCommand(sheetId, hidden: true));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookStructureResult(preferredSheetId ?? ActiveSheet.Id);
        return result;
    }

    public WorkbookCellEditResult UnhideSheet(SheetId sheetId)
    {
        var sheet = Workbook.GetSheet(sheetId);
        if (sheet is null)
        {
            return new WorkbookCellEditResult(
                false,
                "Hidden sheet was not found.",
                [],
                RecalcReport: null);
        }

        if (sheet.IsVeryHidden)
        {
            return new WorkbookCellEditResult(
                false,
                "Very hidden sheets cannot be unhidden from this menu.",
                [],
                RecalcReport: null);
        }

        if (!sheet.IsHidden)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetSheetHiddenCommand(sheetId, hidden: false));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookStructureResult(sheetId);
        return result;
    }

    public WorkbookCellEditResult DeleteActiveSheet()
    {
        var sheetId = ActiveSheet.Id;
        var sheetIndex = Workbook.Sheets.ToList().FindIndex(sheet => sheet.Id == sheetId);
        if (sheetIndex < 0)
        {
            return new WorkbookCellEditResult(
                false,
                "Active sheet was not found.",
                [],
                RecalcReport: null);
        }

        var preferredSheetId = FindPreferredSheetIdAfterRemoval(sheetIndex, sheetId);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new RemoveSheetCommand(sheetId));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookStructureResult(preferredSheetId ?? Workbook.Sheets[0].Id);
        return result;
    }

    public WorkbookCellEditResult RenameActiveSheet(string? name)
    {
        var newName = (name ?? "").Trim();
        if (string.Equals(newName, ActiveSheet.Name, StringComparison.Ordinal))
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new RenameSheetCommand(ActiveSheet.Id, newName));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    private WorkbookCellEditResult MoveActiveSheetBy(int offset)
    {
        var sheetId = ActiveSheet.Id;
        var fromIndex = Workbook.Sheets.ToList().FindIndex(sheet => sheet.Id == sheetId);
        if (fromIndex < 0)
        {
            return new WorkbookCellEditResult(
                false,
                "Active sheet was not found.",
                [],
                RecalcReport: null);
        }

        var toIndex = fromIndex + offset;
        if (toIndex < 0 || toIndex >= Workbook.Sheets.Count)
        {
            var edge = offset < 0 ? "first" : "last";
            return new WorkbookCellEditResult(
                false,
                $"Active sheet is already the {edge} sheet.",
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new MoveSheetCommand(fromIndex, toIndex));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(sheetId);
        return result;
    }

    public void BeginFormulaEdit(CellAddress address)
    {
        ActiveCell = address;
        ActiveSheet.ActiveRow = address.Row;
        ActiveSheet.ActiveCol = address.Col;
        SelectedRange = new GridRange(address, address);
        FormulaEditAddress = address;
    }

    public void CancelFormulaEdit()
    {
        FormulaEditAddress = null;
    }

    public WorkbookCellEditResult CommitCellText(string text, bool useR1C1ReferenceStyle = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        var address = FormulaEditAddress ?? ActiveCell;
        if (!address.Sheet.Equals(ActiveSheet.Id))
            throw new InvalidOperationException("Cell edit address must belong to the active sheet.");

        var cell = CellEntryParser.CreateCell(text, address, useR1C1ReferenceStyle);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateEditCellsCommand([(address, cell)]));

        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, address);
        return result;
    }

    public string CopyActiveCellText()
    {
        var range = new GridRange(ActiveCell, ActiveCell);
        return ClipboardSerializer.Serialize(Viewport, range);
    }

    public string CopySelectedRangeText()
    {
        var text = ClipboardSerializer.Serialize(Viewport, SelectedRange);
        _internalClipboard = CaptureInternalClipboard(SelectedRange, text, isCut: false);
        return text;
    }

    public string CutSelectedRangeText()
    {
        var text = ClipboardSerializer.Serialize(Viewport, SelectedRange);
        _internalClipboard = CaptureInternalClipboard(SelectedRange, text, isCut: true);
        return text;
    }

    public WorkbookCellEditResult PasteClipboardTextAtActiveCell(string? text, bool preserveText = false)
    {
        if (_internalClipboard is { } internalClipboard)
        {
            if (text is null || string.Equals(internalClipboard.Text, text, StringComparison.Ordinal))
                return PasteInternalClipboardAtActiveCell(internalClipboard, PasteCellsMode.All, default);

            _internalClipboard = null;
        }

        if (string.IsNullOrEmpty(text))
        {
            return new WorkbookCellEditResult(
                false,
                "Clipboard does not contain text.",
                [],
                RecalcReport: null);
        }

        return PasteExternalTextAtActiveCell(text, preserveText);
    }

    public WorkbookCellEditResult PasteSpecialClipboardAtActiveCell(
        string? text,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths = false)
    {
        if (!Enum.IsDefined(mode))
        {
            return new WorkbookCellEditResult(
                false,
                "Paste Special mode is not supported.",
                [],
                RecalcReport: null);
        }

        if (_internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _internalClipboard = null;
            return new WorkbookCellEditResult(
                false,
                "Paste Special requires copied FreeX cells.",
                [],
                RecalcReport: null);
        }

        return PasteInternalClipboardAtActiveCell(internalClipboard, mode, options, keepSourceColumnWidths);
    }

    public WorkbookCellEditResult PasteColumnWidthsFromClipboardAtActiveCell(string? text)
    {
        if (_internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _internalClipboard = null;
            return new WorkbookCellEditResult(
                false,
                "Paste Column Widths requires copied FreeX cells.",
                [],
                RecalcReport: null);
        }

        var destination = ActiveCell;
        var command = CreateGroupedSheetCommand(
            "Paste Column Widths",
            sheetId => new PasteColumnWidthsCommand(
                sheetId,
                internalClipboard.SourceRange,
                RemapAddressToSheet(destination, sheetId).Col));
        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    public WorkbookCellEditResult PasteCommentsFromClipboardAtActiveCell(string? text, bool transpose = false)
    {
        if (_internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _internalClipboard = null;
            return new WorkbookCellEditResult(
                false,
                "Paste Comments requires copied FreeX cells.",
                [],
                RecalcReport: null);
        }

        var destination = ActiveCell;
        var pasteSize = GetPasteDimensions(internalClipboard.SourceRange, transpose);
        if (!TryGetRectangleEnd(destination, pasteSize.RowCount, pasteSize.ColCount, out _))
        {
            return new WorkbookCellEditResult(
                false,
                "Paste destination range is outside the worksheet bounds.",
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateGroupedSheetCommand(
                "Paste Comments",
                sheetId => new PasteCommentsCommand(
                    sheetId,
                    internalClipboard.SourceRange,
                    RemapAddressToSheet(destination, sheetId),
                    transpose)));
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        SelectPastedRange(destination, pasteSize.RowCount, pasteSize.ColCount);
        return result;
    }

    public WorkbookCellEditResult PasteDataValidationFromClipboardAtActiveCell(string? text, bool transpose = false)
    {
        if (_internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _internalClipboard = null;
            return new WorkbookCellEditResult(
                false,
                "Paste Validation requires copied FreeX cells.",
                [],
                RecalcReport: null);
        }

        var destination = ActiveCell;
        var pasteSize = GetPasteDimensions(internalClipboard.SourceRange, transpose);
        if (!TryGetRectangleEnd(destination, pasteSize.RowCount, pasteSize.ColCount, out _))
        {
            return new WorkbookCellEditResult(
                false,
                "Paste destination range is outside the worksheet bounds.",
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateGroupedSheetCommand(
                "Paste Data Validation",
                sheetId => new PasteDataValidationCommand(
                    sheetId,
                    internalClipboard.SourceRange,
                    RemapAddressToSheet(destination, sheetId),
                    transpose)));
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        SelectPastedRange(destination, pasteSize.RowCount, pasteSize.ColCount);
        return result;
    }

    public WorkbookCellEditResult PasteLinkFromClipboardAtActiveCell(
        string? text,
        bool transpose = false,
        bool keepSourceColumnWidths = false)
    {
        if (_internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _internalClipboard = null;
            return new WorkbookCellEditResult(
                false,
                "Paste Link requires copied FreeX cells.",
                [],
                RecalcReport: null);
        }

        var sourceSheet = Workbook.GetSheet(internalClipboard.SourceRange.Start.Sheet);
        if (sourceSheet is null)
        {
            return new WorkbookCellEditResult(
                false,
                "Paste Link source sheet was not found.",
                [],
                RecalcReport: null);
        }

        var destination = ActiveCell;
        var command = CreatePasteLinkCommand(
            internalClipboard,
            sourceSheet.Name,
            destination,
            transpose,
            keepSourceColumnWidths);

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        var pasteSize = GetPasteDimensions(internalClipboard.SourceRange, transpose);
        SelectPastedRange(destination, pasteSize.RowCount, pasteSize.ColCount);
        return result;
    }

    public WorkbookCellEditResult PastePictureFromClipboardAtActiveCell(
        string? text,
        bool linkedPicture = false)
    {
        if (_internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _internalClipboard = null;
            return new WorkbookCellEditResult(
                false,
                linkedPicture
                    ? "Paste Linked Picture requires copied FreeX cells."
                    : "Paste Picture requires copied FreeX cells.",
                [],
                RecalcReport: null);
        }

        var sourceSheet = linkedPicture
            ? Workbook.GetSheet(internalClipboard.SourceRange.Start.Sheet)
            : null;
        if (linkedPicture && sourceSheet is null)
        {
            return new WorkbookCellEditResult(
                false,
                "Paste Linked Picture source sheet was not found.",
                [],
                RecalcReport: null);
        }

        var destination = ActiveCell;
        var sourceCells = internalClipboard.Cells
            .Select(static cell => (cell.Source, FormatPictureCellText(cell.Cell.Value)))
            .ToList();
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateGroupedSheetCommand(
                linkedPicture ? "Paste Linked Picture" : "Paste Picture",
                sheetId => new PasteRangeAsPictureCommand(
                    sheetId,
                    internalClipboard.SourceRange,
                    sourceCells,
                    RemapAddressToSheet(destination, sheetId),
                    linkedPicture,
                    sourceSheet?.Name)));
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        return result;
    }

    public bool ShouldPreferExternalClipboardImage(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            return false;

        return _internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal));
    }

    public WorkbookCellEditResult PasteClipboardImageAtActiveCell(
        IReadOnlyCollection<byte> pngBytes,
        int pixelWidth,
        int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);

        var destination = ActiveCell;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateGroupedSheetCommand(
                "Insert Picture",
                sheetId => ClipboardPictureService.CreateInsertCommand(
                    sheetId,
                    RemapAddressToSheet(destination, sheetId),
                    pngBytes,
                    pixelWidth,
                    pixelHeight)));
        if (!result.Success)
            return result;

        _internalClipboard = null;
        ApplySuccessfulEditResult(result, destination);
        return result;
    }

    public WorkbookCellEditResult PasteExternalTextAtActiveCell(string text, bool preserveText = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        var destination = ActiveCell;
        var rows = ClipboardSerializer.Deserialize(text);
        var columnCount = rows.Length == 0 ? 0 : rows.Max(static row => row.Length);
        var command = CreateExternalTextPasteCommand(destination, rows, preserveText);
        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        SelectPastedRange(destination, (ulong)rows.Length, (ulong)columnCount);
        return result;
    }

    public WorkbookCellEditResult ClearSelectedRangeContents()
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateRangeCommand(
                range,
                "Clear Contents",
                static (sheetId, sheetRange) => new ClearContentsCommand(sheetId, sheetRange)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeBold(bool enabled) =>
        ApplySelectedRangeStyle(new StyleDiff(Bold: enabled));

    public WorkbookCellEditResult SetSelectedRangeItalic(bool enabled) =>
        ApplySelectedRangeStyle(new StyleDiff(Italic: enabled));

    public WorkbookCellEditResult SetSelectedRangeUnderline(bool enabled) =>
        ApplySelectedRangeStyle(CreateUnderlineStyleDiff(enabled));

    public WorkbookCellEditResult SetSelectedRangeStrikethrough(bool enabled) =>
        ApplySelectedRangeStyle(CreateStrikethroughStyleDiff(enabled));

    public WorkbookCellEditResult SetSelectedRangeDoubleUnderline(bool enabled) =>
        ApplySelectedRangeStyle(CreateDoubleUnderlineStyleDiff(enabled));

    public WorkbookCellEditResult SetSelectedRangeHorizontalAlignment(HorizontalAlignment alignment) =>
        ApplySelectedRangeStyle(new StyleDiff(HAlign: alignment));

    public WorkbookCellEditResult SetSelectedRangeVerticalAlignment(VerticalAlignment alignment) =>
        ApplySelectedRangeStyle(new StyleDiff(VAlign: alignment));

    public WorkbookCellEditResult SetSelectedRangeWrapText(bool enabled) =>
        ApplySelectedRangeStyle(new StyleDiff(WrapText: enabled));

    public WorkbookCellEditResult IncreaseSelectedRangeIndent() =>
        SetSelectedRangeIndentLevel(Math.Min(15, SelectedRangeStartIndentLevel + 1));

    public WorkbookCellEditResult DecreaseSelectedRangeIndent() =>
        SetSelectedRangeIndentLevel(Math.Max(0, SelectedRangeStartIndentLevel - 1));

    public WorkbookCellEditResult SetSelectedRangeIndentLevel(int indentLevel)
        => ApplySelectedRangeStyle(new StyleDiff(IndentLevel: Math.Clamp(indentLevel, 0, 15)));

    public WorkbookCellEditResult SetSelectedRangeNumberFormat(string numberFormat)
    {
        ArgumentNullException.ThrowIfNull(numberFormat);

        return ApplySelectedRangeStyle(new StyleDiff(NumberFormat: numberFormat));
    }

    public WorkbookCellEditResult SetSelectedRangeTextRotation(int textRotation)
        => ApplySelectedRangeStyle(new StyleDiff(TextRotation: textRotation));

    public WorkbookCellEditResult SetSelectedRangeCellStylePreset(CellStylePreset preset)
    {
        var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(preset, Workbook.Theme);
        return ApplySelectedRangeStyle(diff);
    }

    public WorkbookCellEditResult SetSelectedRangeBorderPreset(CellBorderPreset preset)
    {
        var range = SelectedRange;
        if (!HasBorderPresetChanges(range, preset))
            return new WorkbookCellEditResult(true, null, [], RecalcReport: null);

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateBorderPresetCommand(range, preset));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult IncreaseSelectedRangeDecimalPlaces() =>
        SetSelectedRangeNumberFormat(NumberFormatDecimalAdjuster.AddDecimalPlace(SelectedRangeStartNumberFormat));

    public WorkbookCellEditResult DecreaseSelectedRangeDecimalPlaces() =>
        SetSelectedRangeNumberFormat(NumberFormatDecimalAdjuster.RemoveDecimalPlace(SelectedRangeStartNumberFormat));

    public WorkbookCellEditResult IncreaseSelectedRangeFontSize() =>
        SetSelectedRangeFontSize(FontSizePlanner.Increase(SelectedRangeStartFontSize));

    public WorkbookCellEditResult DecreaseSelectedRangeFontSize() =>
        SetSelectedRangeFontSize(FontSizePlanner.Decrease(SelectedRangeStartFontSize));

    public WorkbookCellEditResult SetSelectedRangeFontSize(double fontSize)
    {
        var range = SelectedRange;
        var rowHeight = Math.Min(MaximumRowHeight, FontSizePlanner.EstimateFittingRowHeight(fontSize));
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateSetFontSizeCommand(range, fontSize, rowHeight));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeFontColor(CellColor fontColor) =>
        ApplySelectedRangeStyle(new StyleDiff(FontColor: fontColor));

    public WorkbookCellEditResult SetSelectedRangeFillColor(CellColor fillColor) =>
        ApplySelectedRangeStyle(new StyleDiff(FillColor: fillColor));

    public WorkbookCellEditResult ClearSelectedRangeFill() =>
        ApplySelectedRangeStyle(new StyleDiff(ClearFill: true));

    public WorkbookCellEditResult UndoLastEdit()
    {
        var sheetIdsBefore = CaptureSheetIds();
        var result = _cellEditService.UndoLastEdit(Workbook);
        if (!result.Success)
            return result;

        ApplySuccessfulHistoryResult(result, sheetIdsBefore);
        return result;
    }

    public WorkbookCellEditResult RedoLastEdit()
    {
        var sheetIdsBefore = CaptureSheetIds();
        var result = _cellEditService.RedoLastEdit(Workbook);
        if (!result.Success)
            return result;

        ApplySuccessfulHistoryResult(result, sheetIdsBefore);
        return result;
    }

    public bool CanSaveCurrentSource(out FileSaveTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(CurrentFilePath))
            return false;

        return TryResolveSaveTarget(CurrentFilePath, out target, out _);
    }

    public bool TryResolveOpenTarget(string path, out WorkbookOpenTarget? target, out string message)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            message = "Open requires a local file path.";
            return false;
        }

        var openPath = path.Trim();
        if (!TryGetExtension(openPath, out var extension))
        {
            message = "Unsupported file type.";
            return false;
        }

        var adapter = FileFormatResolver.FindOpenAdapter(_adapters, extension, out var format);
        if (adapter is null || format is null)
        {
            message = $"Unsupported file type: {extension}.";
            return false;
        }

        target = new WorkbookOpenTarget(openPath, adapter, extension, format);
        message = "";
        return true;
    }

    public bool TryResolveSaveTarget(string path, out FileSaveTarget? target, out string message)
    {
        target = null;
        if (!FileSavePlanner.TryResolveExistingPath(path, _adapters, out var resolvedTarget) ||
            resolvedTarget is null)
        {
            message = "Unsupported save format.";
            return false;
        }

        if (!CanWriteTarget(resolvedTarget.Path, out message))
            return false;

        target = resolvedTarget;
        message = "";
        return true;
    }

    public void MarkSaved(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        IsDirty = false;
        CurrentFilePath = path;
        CurrentXlsxFeatureReport = null;
        Workbook.Name = Path.GetFileName(path);
    }

    public string BuildSuggestedSaveAsFileName(string defaultExtension)
    {
        var normalizedExtension = FileFormatResolver.NormalizeExtension(defaultExtension);
        var sourceName = string.IsNullOrWhiteSpace(Workbook.Name)
            ? DisplayName
            : Workbook.Name;
        var baseName = Path.GetFileNameWithoutExtension(sourceName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Workbook";

        return baseName + normalizedExtension;
    }

    public static string EnsureSaveExtension(string path, string defaultExtension)
    {
        try
        {
            return string.IsNullOrWhiteSpace(Path.GetExtension(path))
                ? path + FileFormatResolver.NormalizeExtension(defaultExtension)
                : path;
        }
        catch (ArgumentException)
        {
            return path;
        }
        catch (NotSupportedException)
        {
            return path;
        }
        catch (PathTooLongException)
        {
            return path;
        }
    }

    private void RefreshViewport()
    {
        Viewport = BuildViewport();
    }

    private HashSet<SheetId> CaptureSheetIds() =>
        Workbook.Sheets.Select(sheet => sheet.Id).ToHashSet();

    private void ApplySuccessfulHistoryResult(
        WorkbookCellEditResult result,
        IReadOnlySet<SheetId> sheetIdsBefore)
    {
        if (result.AffectedCells.Count > 0)
        {
            ApplySuccessfulEditResult(result, ActiveCell);
            return;
        }

        if (FindNewSheetId(sheetIdsBefore) is { } newSheetId)
        {
            ApplySuccessfulWorkbookStructureResult(newSheetId);
            return;
        }

        if (Workbook.GetSheet(ActiveSheet.Id) is not null)
        {
            ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
            return;
        }

        ApplySuccessfulWorkbookStructureResult(ActiveSheet.Id);
    }

    private SheetId? FindNewSheetId(IReadOnlySet<SheetId> sheetIdsBefore)
    {
        foreach (var sheet in Workbook.Sheets)
        {
            if (!sheetIdsBefore.Contains(sheet.Id))
                return sheet.Id;
        }

        return null;
    }

    private SheetId? FindPreferredSheetIdAfterRemoval(int removedIndex, SheetId removedSheetId)
    {
        for (var index = removedIndex + 1; index < Workbook.Sheets.Count; index++)
        {
            var sheet = Workbook.Sheets[index];
            if (sheet.Id != removedSheetId)
                return sheet.Id;
        }

        for (var index = removedIndex - 1; index >= 0; index--)
        {
            var sheet = Workbook.Sheets[index];
            if (sheet.Id != removedSheetId)
                return sheet.Id;
        }

        return null;
    }

    private SheetId? FindPreferredVisibleSheetIdAfterHidden(int hiddenIndex, SheetId hiddenSheetId)
    {
        for (var index = hiddenIndex + 1; index < Workbook.Sheets.Count; index++)
        {
            var sheet = Workbook.Sheets[index];
            if (sheet.Id != hiddenSheetId && !sheet.IsHidden && !sheet.IsVeryHidden)
                return sheet.Id;
        }

        for (var index = hiddenIndex - 1; index >= 0; index--)
        {
            var sheet = Workbook.Sheets[index];
            if (sheet.Id != hiddenSheetId && !sheet.IsHidden && !sheet.IsVeryHidden)
                return sheet.Id;
        }

        return null;
    }

    private IReadOnlyList<SheetId> GetSelectableSheetIds()
    {
        var visible = Workbook.Sheets
            .Where(sheet => !sheet.IsHidden && !sheet.IsVeryHidden)
            .Select(sheet => sheet.Id)
            .ToList();

        return visible.Count > 0
            ? visible
            : Workbook.Sheets.Select(sheet => sheet.Id).ToList();
    }

    private void SelectSingleSheetGroup(SheetId sheetId) =>
        UpdateGroupedSheetsForTabSelection(sheetId, selectRange: false, toggle: false);

    private void UpdateGroupedSheetsForTabSelection(SheetId sheetId, bool selectRange, bool toggle)
    {
        var selectableSheetIds = GetSelectableSheetIds();
        IReadOnlyList<SheetId> selectedSheetIds;

        if (selectRange && _sheetGroupAnchor.HasValue)
        {
            selectedSheetIds = SheetGroupSelectionService.SelectRange(
                selectableSheetIds,
                _sheetGroupAnchor.Value,
                sheetId);
        }
        else if (toggle)
        {
            selectedSheetIds = SheetGroupSelectionService.Toggle(sheetId, _groupedSheetIds);
            _sheetGroupAnchor = sheetId;
        }
        else
        {
            selectedSheetIds = SheetGroupSelectionService.SelectSingle(sheetId);
            _sheetGroupAnchor = sheetId;
        }

        SetGroupedSheetIds(selectedSheetIds, sheetId);
    }

    private bool SetGroupedSheetIds(IEnumerable<SheetId> sheetIds, SheetId fallbackSheetId)
    {
        var previous = _groupedSheetIds.ToHashSet();
        var selectableSheetIds = GetSelectableSheetIds().ToHashSet();
        _groupedSheetIds.Clear();

        foreach (var sheetId in sheetIds)
        {
            if (selectableSheetIds.Contains(sheetId))
                _groupedSheetIds.Add(sheetId);
        }

        if (_groupedSheetIds.Count == 0 || !_groupedSheetIds.Contains(fallbackSheetId))
        {
            _groupedSheetIds.Clear();
            if (selectableSheetIds.Contains(fallbackSheetId))
                _groupedSheetIds.Add(fallbackSheetId);
            else if (selectableSheetIds.Count > 0)
                _groupedSheetIds.Add(selectableSheetIds.First());
        }

        return !previous.SetEquals(_groupedSheetIds);
    }

    private void RefreshSheetTabsForActiveSheet()
    {
        SetGroupedSheetIds(_groupedSheetIds.ToArray(), ActiveSheet.Id);
        var selection = _sheetSelectionService.SelectSheet(
            Workbook,
            ActiveSheet.Id,
            IsWorkbookGrouped ? _groupedSheetIds : null);
        ActiveSheet = selection.Sheet;
        SheetTabs = selection.Tabs;
    }

    private IReadOnlyList<SheetId> CurrentGroupedEditSheetIds()
    {
        if (!IsWorkbookGrouped)
            return [ActiveSheet.Id];

        var groupedVisibleSheetIds = GetSelectableSheetIds()
            .Where(_groupedSheetIds.Contains)
            .ToList();
        if (groupedVisibleSheetIds.Count <= 1 || !groupedVisibleSheetIds.Contains(ActiveSheet.Id))
            return [ActiveSheet.Id];

        return [ActiveSheet.Id, .. groupedVisibleSheetIds.Where(sheetId => sheetId != ActiveSheet.Id)];
    }

    private IWorkbookCommand CreateEditCellsCommand(IReadOnlyList<(CellAddress Address, Cell NewCell)> edits)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        return targetSheetIds.Count > 1
            ? new GroupedEditCellsCommand(targetSheetIds, ActiveSheet.Id, edits)
            : new EditCellsCommand(ActiveSheet.Id, edits);
    }

    private IWorkbookCommand CreateApplyStyleCommand(GridRange range, StyleDiff diff)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        return targetSheetIds.Count > 1
            ? new GroupedApplyStyleCommand(targetSheetIds, range, diff)
            : new ApplyStyleCommand(ActiveSheet.Id, range, diff);
    }

    private IWorkbookCommand CreateBorderPresetCommand(GridRange range, CellBorderPreset preset)
    {
        if (!CellBorderPresetPlanner.RequiresPerCellPlanning(preset))
            return CreateApplyStyleCommand(range, CellBorderPresetPlanner.Plan(preset, range, range.Start));

        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>();
        foreach (var address in range.AllCells())
        {
            var diff = CellBorderPresetPlanner.Plan(preset, range, address);
            if (!BorderShortcutService.HasBorderChanges(diff))
                continue;

            var sourceRange = new GridRange(address, address);
            commands.Add(targetSheetIds.Count > 1
                ? new GroupedApplyStyleCommand(targetSheetIds, sourceRange, diff)
                : new ApplyStyleCommand(
                    ActiveSheet.Id,
                    RemapRangeToSheet(sourceRange, ActiveSheet.Id),
                    diff));
        }

        return ToCommand(CellBorderPresetPlanner.GetDisplayName(preset), commands);
    }

    private IWorkbookCommand CreateSetFontSizeCommand(GridRange range, double fontSize, double rowHeight)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count * 2);
        foreach (var sheetId in targetSheetIds)
        {
            var sheetRange = RemapRangeToSheet(range, sheetId);
            commands.Add(new ApplyStyleCommand(sheetId, sheetRange, new StyleDiff(FontSize: fontSize)));
            commands.Add(new SetRowHeightCommand(sheetId, sheetRange.Start.Row, sheetRange.End.Row, rowHeight));
        }

        return ToCommand("Set Font Size", commands);
    }

    private IWorkbookCommand CreateExternalTextPasteCommand(
        CellAddress destination,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool preserveText)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = targetSheetIds
            .Select(sheetId => PasteCommandFactory.CreateExternalTextPasteCommand(
                sheetId,
                RemapAddressToSheet(destination, sheetId),
                rows,
                preserveText))
            .ToList();
        return ToCommand("Paste", commands);
    }

    private IWorkbookCommand CreateInternalPasteCommand(
        InternalClipboard clipboard,
        CellAddress destination,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count);
        foreach (var sheetId in targetSheetIds)
        {
            var sheetDestination = RemapAddressToSheet(destination, sheetId);
            var command = PasteCommandFactory.CreateInternalPasteCommand(
                Workbook,
                sheetId,
                clipboard.SourceRange,
                clipboard.Cells,
                sheetDestination,
                mode,
                options);
            if (keepSourceColumnWidths)
            {
                command = new CompositeWorkbookCommand(
                    "Paste Special",
                    [
                        command,
                        new PasteColumnWidthsCommand(sheetId, clipboard.SourceRange, sheetDestination.Col)
                    ]);
            }

            commands.Add(command);
        }

        var label = mode == PasteCellsMode.All && options == default && !keepSourceColumnWidths
            ? "Paste"
            : "Paste Special";
        return ToCommand(label, commands);
    }

    private IWorkbookCommand CreatePasteLinkCommand(
        InternalClipboard clipboard,
        string sourceSheetName,
        CellAddress destination,
        bool transpose,
        bool keepSourceColumnWidths)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count);
        foreach (var sheetId in targetSheetIds)
        {
            var sheetDestination = RemapAddressToSheet(destination, sheetId);
            var linkedCells = PasteLinkService.CreateLinkedCells(
                clipboard.SourceRange,
                sheetDestination,
                sourceSheetName,
                transpose);
            IWorkbookCommand command = new EditCellsCommand(sheetId, linkedCells);
            if (keepSourceColumnWidths)
            {
                command = new CompositeWorkbookCommand(
                    "Paste Link",
                    [
                        command,
                        new PasteColumnWidthsCommand(sheetId, clipboard.SourceRange, sheetDestination.Col)
                    ]);
            }

            commands.Add(command);
        }

        return ToCommand("Paste Link", commands);
    }

    private IWorkbookCommand CreateRangeCommand(
        GridRange range,
        string title,
        Func<SheetId, GridRange, IWorkbookCommand> createCommand)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = targetSheetIds
            .Select(sheetId => createCommand(sheetId, RemapRangeToSheet(range, sheetId)))
            .ToList();
        return ToCommand(title, commands);
    }

    private IWorkbookCommand CreateGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = targetSheetIds
            .Select(createCommand)
            .ToList();
        return ToCommand(title, commands);
    }

    private WorkbookCellEditResult ApplySelectedRangeStyle(StyleDiff diff)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateApplyStyleCommand(range, diff));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    private static IWorkbookCommand ToCommand(string title, IReadOnlyList<IWorkbookCommand> commands) =>
        commands.Count == 1
            ? commands[0]
            : new CompositeWorkbookCommand(title, commands);

    private static bool HasBorderPresetChanges(GridRange range, CellBorderPreset preset)
    {
        if (!CellBorderPresetPlanner.RequiresPerCellPlanning(preset))
            return true;

        return range
            .AllCells()
            .Any(address => BorderShortcutService.HasBorderChanges(CellBorderPresetPlanner.Plan(preset, range, address)));
    }

    private static GridRange RemapRangeToSheet(GridRange range, SheetId sheetId) =>
        new(
            RemapAddressToSheet(range.Start, sheetId),
            RemapAddressToSheet(range.End, sheetId));

    private static CellAddress RemapAddressToSheet(CellAddress address, SheetId sheetId) =>
        new(sheetId, address.Row, address.Col);

    private void ApplySuccessfulWorkbookStructureResult(SheetId preferredSheetId)
    {
        var selection = _sheetSelectionService.SelectSheet(Workbook, preferredSheetId);
        ActiveSheet = selection.Sheet;
        SelectSingleSheetGroup(ActiveSheet.Id);
        RefreshSheetTabsForActiveSheet();
        ActiveCell = GetInitialActiveCell(ActiveSheet);
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        SelectedRange = new GridRange(ActiveCell, ActiveCell);
        FormulaEditAddress = null;
        IsDirty = true;
        _selectionStatsRevision++;
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private void ApplySuccessfulWorkbookMetadataResult(SheetId preferredSheetId)
    {
        var selection = _sheetSelectionService.SelectSheet(Workbook, preferredSheetId, _groupedSheetIds);
        ActiveSheet = selection.Sheet;
        RefreshSheetTabsForActiveSheet();
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        FormulaEditAddress = null;
        IsDirty = true;
        _selectionStatsRevision++;
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private void ApplySuccessfulEditResult(WorkbookCellEditResult result, CellAddress fallbackAddress)
    {
        var address = result.AffectedCells.FirstOrDefault(fallbackAddress);
        if (!ActiveSheet.Id.Equals(address.Sheet))
        {
            var selection = _sheetSelectionService.SelectSheet(Workbook, address.Sheet, _groupedSheetIds);
            ActiveSheet = selection.Sheet;
            RefreshSheetTabsForActiveSheet();
        }

        ActiveCell = address;
        ActiveSheet.ActiveRow = address.Row;
        ActiveSheet.ActiveCol = address.Col;
        SelectedRange = new GridRange(address, address);
        FormulaEditAddress = null;
        IsDirty = true;
        _selectionStatsRevision++;
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private void ApplySuccessfulRangeEditResult(WorkbookCellEditResult result, GridRange selectedRange)
    {
        ActiveCell = selectedRange.Start;
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        SelectedRange = selectedRange;
        FormulaEditAddress = null;
        IsDirty = true;
        _selectionStatsRevision++;
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private WorkbookCellEditResult SetFreezePanes(uint frozenRows, uint frozenCols)
    {
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetFreezePanesCommand(ActiveSheet.Id, frozenRows, frozenCols));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    private WorkbookCellEditResult SetWorksheetViewOptions(bool showGridlines, bool showHeadings, bool showRulers)
    {
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetWorksheetViewOptionsCommand(ActiveSheet.Id, showGridlines, showHeadings, showRulers));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    private CellStyle GetCellStyle(CellAddress address)
    {
        var sheet = Workbook.GetSheet(address.Sheet);
        var styleId = sheet?.GetCell(address)?.StyleId ??
            sheet?.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        return Workbook.GetStyle(styleId);
    }

    private static StyleDiff CreateUnderlineStyleDiff(bool enabled) =>
        new(Underline: enabled, Strikethrough: enabled ? false : null);

    private static StyleDiff CreateStrikethroughStyleDiff(bool enabled) =>
        new(Strikethrough: enabled, Underline: enabled ? false : null, DoubleUnderline: enabled ? false : null);

    private static StyleDiff CreateDoubleUnderlineStyleDiff(bool enabled) =>
        new(DoubleUnderline: enabled, Underline: enabled ? false : null, Strikethrough: enabled ? false : null);

    private WorkbookCellEditResult PasteInternalClipboardAtActiveCell(
        InternalClipboard clipboard,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths = false)
    {
        var destination = ActiveCell;
        var command = CreateInternalPasteCommand(
            clipboard,
            destination,
            mode,
            options,
            keepSourceColumnWidths);

        if (ShouldClearCutSourceAfterPaste(clipboard, destination, mode, options, keepSourceColumnWidths))
        {
            command = new CompositeWorkbookCommand(
                "Cut and Paste",
                [
                    command,
                    new ClearContentsCommand(clipboard.SourceRange.Start.Sheet, clipboard.SourceRange)
                ]);
        }

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        var pasteSize = GetPasteDimensions(clipboard.SourceRange, options.Transpose);
        SelectPastedRange(destination, pasteSize.RowCount, pasteSize.ColCount);
        if (clipboard.IsCut)
            _internalClipboard = null;
        return result;
    }

    private InternalClipboard CaptureInternalClipboard(GridRange range, string text, bool isCut)
    {
        var sheet = Workbook.GetSheet(range.Start.Sheet);
        var cells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var address in range.AllCells())
        {
            var cell = sheet?.GetCell(address)?.Clone() ?? Cell.FromValue(BlankValue.Instance);
            cells.Add((address, cell));
        }

        return new InternalClipboard(range, cells, text, isCut);
    }

    private static bool ShouldClearCutSourceAfterPaste(
        InternalClipboard clipboard,
        CellAddress destination,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths)
    {
        if (!clipboard.IsCut || mode == PasteCellsMode.Formats || keepSourceColumnWidths)
            return false;

        var rowCount = options.Transpose ? clipboard.SourceRange.ColCount : clipboard.SourceRange.RowCount;
        var colCount = options.Transpose ? clipboard.SourceRange.RowCount : clipboard.SourceRange.ColCount;

        if (!TryGetRectangleEnd(
                destination,
                rowCount,
                colCount,
                out var pastedEnd))
        {
            return false;
        }

        return !clipboard.SourceRange.Overlaps(new GridRange(destination, pastedEnd));
    }

    private static (ulong RowCount, ulong ColCount) GetPasteDimensions(GridRange sourceRange, bool transpose) =>
        transpose
            ? (sourceRange.ColCount, sourceRange.RowCount)
            : (sourceRange.RowCount, sourceRange.ColCount);

    private void SelectPastedRange(CellAddress start, ulong rowCount, ulong colCount)
    {
        if (rowCount == 0 || colCount == 0)
            return;

        if (!TryGetRectangleEnd(start, rowCount, colCount, out var end))
            return;

        SelectedRange = new GridRange(start, end);
    }

    private void EnsureActiveCellVisible()
    {
        var changed = false;
        if (TryGetScrollableRowRange(out var firstRow, out var lastRow) &&
            !IsFrozenRow(ActiveCell.Row) &&
            (ActiveCell.Row < firstRow || ActiveCell.Row > lastRow))
        {
            ActiveSheet.ViewTopRow = CalculateScrollOrigin(
                ActiveCell.Row,
                firstRow,
                lastRow,
                ActiveSheet.ViewTopRow ?? GetScrollableRowStart(),
                CellAddress.MaxRow);
            changed = true;
        }

        if (TryGetScrollableColumnRange(out var firstCol, out var lastCol) &&
            !IsFrozenColumn(ActiveCell.Col) &&
            (ActiveCell.Col < firstCol || ActiveCell.Col > lastCol))
        {
            ActiveSheet.ViewLeftCol = CalculateScrollOrigin(
                ActiveCell.Col,
                firstCol,
                lastCol,
                ActiveSheet.ViewLeftCol ?? GetScrollableColumnStart(),
                CellAddress.MaxCol);
            changed = true;
        }

        if (changed)
            RefreshViewport();
    }

    private bool TryGetScrollableRowRange(out uint firstRow, out uint lastRow)
    {
        var frozenRows = ActiveSheet.FrozenRows;
        firstRow = 1;
        lastRow = 1;
        var found = false;
        foreach (var metric in Viewport.RowMetrics)
        {
            if (metric.Row <= frozenRows)
                continue;

            if (!found)
            {
                firstRow = metric.Row;
                lastRow = metric.Row;
                found = true;
            }
            else
            {
                lastRow = metric.Row;
            }
        }

        return found;
    }

    private bool TryGetScrollableColumnRange(out uint firstCol, out uint lastCol)
    {
        var frozenCols = ActiveSheet.FrozenCols;
        firstCol = 1;
        lastCol = 1;
        var found = false;
        foreach (var metric in Viewport.ColMetrics)
        {
            if (metric.Col <= frozenCols)
                continue;

            if (!found)
            {
                firstCol = metric.Col;
                lastCol = metric.Col;
                found = true;
            }
            else
            {
                lastCol = metric.Col;
            }
        }

        return found;
    }

    private bool IsFrozenRow(uint row) =>
        ActiveSheet.FrozenRows > 0 && row <= ActiveSheet.FrozenRows;

    private bool IsFrozenColumn(uint col) =>
        ActiveSheet.FrozenCols > 0 && col <= ActiveSheet.FrozenCols;

    private uint GetScrollableRowStart() =>
        Math.Min(CellAddress.MaxRow, Math.Max(1, ActiveSheet.FrozenRows + 1));

    private uint GetScrollableColumnStart() =>
        Math.Min(CellAddress.MaxCol, Math.Max(1, ActiveSheet.FrozenCols + 1));

    private static uint CalculateScrollOrigin(
        uint active,
        uint firstVisible,
        uint lastVisible,
        uint currentOrigin,
        uint max)
    {
        if (active < firstVisible)
            return active;

        if (active > lastVisible)
            return Offset(currentOrigin, checked((int)(active - lastVisible)), max);

        return currentOrigin;
    }

    private static uint Offset(uint value, int delta, uint max)
    {
        var candidate = (long)value + delta;
        return (uint)Math.Clamp(candidate, 1, max);
    }

    private static bool IsValidAddress(CellAddress address) =>
        address.Row is >= 1 and <= CellAddress.MaxRow &&
        address.Col is >= 1 and <= CellAddress.MaxCol;

    private static bool TryGetRectangleEnd(
        CellAddress start,
        ulong rowCount,
        ulong colCount,
        out CellAddress end)
    {
        end = default;
        if (!IsValidAddress(start))
            return false;

        try
        {
            var endRow = checked((ulong)start.Row + rowCount - 1UL);
            var endCol = checked((ulong)start.Col + colCount - 1UL);
            if (endRow > CellAddress.MaxRow || endCol > CellAddress.MaxCol)
                return false;

            end = new CellAddress(start.Sheet, (uint)endRow, (uint)endCol);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static double NormalizeViewportDimension(double value, double fallback)
    {
        if (!double.IsFinite(value) || value <= 0)
            return Math.Max(1, Math.Ceiling(fallback));

        return Math.Max(1, Math.Ceiling(value));
    }

    private static string FormatPictureCellText(ScalarValue value) =>
        value switch
        {
            BlankValue => "",
            NumberValue number => number.Value.ToString(CultureInfo.CurrentCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            TextValue text => text.Value,
            ErrorValue error => error.Code,
            _ => value.ToString() ?? ""
        };

    private static IReadOnlyList<FileFormatDescriptor> BuildFormats(
        IReadOnlyList<IFileAdapter> adapters,
        Func<FileFormatDescriptor, bool> predicate) =>
        adapters
            .SelectMany(adapter => adapter.Formats)
            .Where(predicate)
            .GroupBy(format => FileFormatResolver.NormalizeExtension(format.Extension), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

    private ViewportModel BuildViewport() =>
        _viewportService.GetViewport(
            Workbook,
            ActiveSheet.Id,
            new ViewportRequest(
                ActiveSheet.ViewTopRow ?? 1,
                ActiveSheet.ViewLeftCol ?? 1,
                AvailableHeight: _viewportHeight,
                AvailableWidth: _viewportWidth,
                IncludeObjects: _includeObjects));

    private bool CanWriteTarget(string path, out string message)
    {
        if (IsXlsxPath(path) && CurrentXlsxFeatureReport?.HasUnsupportedFeatures == true)
        {
            message = "Save As FreeX Workbook to avoid dropping unsupported XLSX features.";
            return false;
        }

        message = "";
        return true;
    }

    private static bool IsXlsxPath(string path) =>
        string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetExtension(string path, out string extension)
    {
        try
        {
            if (path.Contains('\0', StringComparison.Ordinal) ||
                path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                extension = "";
                return false;
            }

            extension = Path.GetExtension(path) ?? "";
            return !string.IsNullOrWhiteSpace(extension);
        }
        catch (ArgumentException)
        {
            extension = "";
            return false;
        }
        catch (NotSupportedException)
        {
            extension = "";
            return false;
        }
        catch (PathTooLongException)
        {
            extension = "";
            return false;
        }
    }

    private static CellAddress GetInitialActiveCell(Sheet sheet) =>
        new(sheet.Id, Math.Max(1, sheet.ActiveRow ?? 1), Math.Max(1, sheet.ActiveCol ?? 1));

    private static string FormatStartupStatus(StartupWorkbookLoadResult source)
    {
        var status = source.Status;
        if (source.OpenedAsTemplate)
            status += " Opened as template.";
        if (source.FeatureReport?.HasUnsupportedFeatures == true)
            status += " Unsupported XLSX features detected.";
        if (source.LoadWarnings is { Count: > 0 } warnings)
            status += $" {warnings.Count} load warning{(warnings.Count == 1 ? "" : "s")}.";

        return status;
    }
}
