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
                return PasteInternalClipboardAtActiveCell(internalClipboard);

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

    public WorkbookCellEditResult UndoLastEdit()
    {
        var result = _cellEditService.UndoLastEdit(Workbook);
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, ActiveCell);
        return result;
    }

    public WorkbookCellEditResult RedoLastEdit()
    {
        var result = _cellEditService.RedoLastEdit(Workbook);
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, ActiveCell);
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

    private CellStyle GetCellStyle(CellAddress address)
    {
        var sheet = Workbook.GetSheet(address.Sheet);
        var styleId = sheet?.GetCell(address)?.StyleId ??
            sheet?.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        return Workbook.GetStyle(styleId);
    }

    private WorkbookCellEditResult PasteInternalClipboardAtActiveCell(InternalClipboard clipboard)
    {
        var destination = ActiveCell;
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            Workbook,
            ActiveSheet.Id,
            clipboard.SourceRange,
            clipboard.Cells,
            destination,
            PasteCellsMode.All,
            default);
        if (ShouldClearCutSourceAfterPaste(clipboard, destination))
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
        SelectPastedRange(destination, clipboard.SourceRange.RowCount, clipboard.SourceRange.ColCount);
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

    private static bool ShouldClearCutSourceAfterPaste(InternalClipboard clipboard, CellAddress destination)
    {
        if (!clipboard.IsCut)
            return false;

        if (!TryGetRectangleEnd(
                destination,
                clipboard.SourceRange.RowCount,
                clipboard.SourceRange.ColCount,
                out var pastedEnd))
        {
            return false;
        }

        return !clipboard.SourceRange.Overlaps(new GridRange(destination, pastedEnd));
    }

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
