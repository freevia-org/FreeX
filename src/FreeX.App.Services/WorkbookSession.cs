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

    public bool IsShowingGridlines => ActiveSheet.ShowGridlines;

    public bool IsShowingHeadings => ActiveSheet.ShowHeadings;

    public bool IsShowingFormulas => ActiveSheet.ShowFormulas;

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
    {
        var selection = _sheetSelectionService.SelectSheet(Workbook, sheetId);
        SheetTabs = selection.Tabs;
        if (ActiveSheet.Id == selection.Sheet.Id)
            return false;

        ActiveSheet = selection.Sheet;
        ActiveCell = GetInitialActiveCell(ActiveSheet);
        SelectedRange = new GridRange(ActiveCell, ActiveCell);
        FormulaEditAddress = null;
        RefreshViewport();
        return true;
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
        var address = FormulaEditAddress ?? ActiveCell;
        var result = _cellEditService.CommitCellText(
            Workbook,
            ActiveSheet.Id,
            address,
            text,
            useR1C1ReferenceStyle);

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

        var command = new PasteColumnWidthsCommand(
            ActiveSheet.Id,
            internalClipboard.SourceRange,
            ActiveCell.Col);
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
            new PasteCommentsCommand(
                ActiveSheet.Id,
                internalClipboard.SourceRange,
                destination,
                transpose));
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
            new PasteDataValidationCommand(
                ActiveSheet.Id,
                internalClipboard.SourceRange,
                destination,
                transpose));
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
        var linkedCells = PasteLinkService.CreateLinkedCells(
            internalClipboard.SourceRange,
            destination,
            sourceSheet.Name,
            transpose);
        IWorkbookCommand command = new EditCellsCommand(ActiveSheet.Id, linkedCells);
        if (keepSourceColumnWidths)
        {
            command = new CompositeWorkbookCommand(
                "Paste Link",
                [
                    command,
                    new PasteColumnWidthsCommand(ActiveSheet.Id, internalClipboard.SourceRange, destination.Col)
                ]);
        }

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
            new PasteRangeAsPictureCommand(
                ActiveSheet.Id,
                internalClipboard.SourceRange,
                sourceCells,
                destination,
                linkedPicture,
                sourceSheet?.Name));
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
            ClipboardPictureService.CreateInsertCommand(
                ActiveSheet.Id,
                destination,
                pngBytes,
                pixelWidth,
                pixelHeight));
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
        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            ActiveSheet.Id,
            destination,
            rows,
            preserveText);
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
            new ClearContentsCommand(ActiveSheet.Id, range));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeBold(bool enabled)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, new StyleDiff(Bold: enabled)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeItalic(bool enabled)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, new StyleDiff(Italic: enabled)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeUnderline(bool enabled)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, CreateUnderlineStyleDiff(enabled)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeStrikethrough(bool enabled)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, CreateStrikethroughStyleDiff(enabled)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeDoubleUnderline(bool enabled)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, CreateDoubleUnderlineStyleDiff(enabled)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeHorizontalAlignment(HorizontalAlignment alignment)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, new StyleDiff(HAlign: alignment)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeVerticalAlignment(VerticalAlignment alignment)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, new StyleDiff(VAlign: alignment)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeWrapText(bool enabled)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, new StyleDiff(WrapText: enabled)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult IncreaseSelectedRangeIndent() =>
        SetSelectedRangeIndentLevel(Math.Min(15, SelectedRangeStartIndentLevel + 1));

    public WorkbookCellEditResult DecreaseSelectedRangeIndent() =>
        SetSelectedRangeIndentLevel(Math.Max(0, SelectedRangeStartIndentLevel - 1));

    public WorkbookCellEditResult SetSelectedRangeIndentLevel(int indentLevel)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(
                ActiveSheet.Id,
                range,
                new StyleDiff(IndentLevel: Math.Clamp(indentLevel, 0, 15))));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeNumberFormat(string numberFormat)
    {
        ArgumentNullException.ThrowIfNull(numberFormat);

        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, new StyleDiff(NumberFormat: numberFormat)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeTextRotation(int textRotation)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, new StyleDiff(TextRotation: textRotation)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeCellStylePreset(CellStylePreset preset)
    {
        var range = SelectedRange;
        var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(preset, Workbook.Theme);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, diff));
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
            new CompositeWorkbookCommand(
                "Set Font Size",
                [
                    new ApplyStyleCommand(ActiveSheet.Id, range, new StyleDiff(FontSize: fontSize)),
                    new SetRowHeightCommand(ActiveSheet.Id, range.Start.Row, range.End.Row, rowHeight)
                ]));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeFontColor(CellColor fontColor)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, new StyleDiff(FontColor: fontColor)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeFillColor(CellColor fillColor)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, new StyleDiff(FillColor: fillColor)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult ClearSelectedRangeFill()
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyStyleCommand(ActiveSheet.Id, range, new StyleDiff(ClearFill: true)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

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

    private void ApplySuccessfulWorkbookStructureResult(SheetId preferredSheetId)
    {
        var selection = _sheetSelectionService.SelectSheet(Workbook, preferredSheetId);
        ActiveSheet = selection.Sheet;
        SheetTabs = selection.Tabs;
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
        var selection = _sheetSelectionService.SelectSheet(Workbook, preferredSheetId);
        ActiveSheet = selection.Sheet;
        SheetTabs = selection.Tabs;
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
            var selection = _sheetSelectionService.SelectSheet(Workbook, address.Sheet);
            ActiveSheet = selection.Sheet;
            SheetTabs = selection.Tabs;
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
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            Workbook,
            ActiveSheet.Id,
            clipboard.SourceRange,
            clipboard.Cells,
            destination,
            mode,
            options);
        if (keepSourceColumnWidths)
        {
            command = new CompositeWorkbookCommand(
                "Paste Special",
                [
                    command,
                    new PasteColumnWidthsCommand(ActiveSheet.Id, clipboard.SourceRange, destination.Col)
                ]);
        }

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
