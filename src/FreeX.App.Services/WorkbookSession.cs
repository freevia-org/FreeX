using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed class WorkbookSession
{
    private readonly IReadOnlyList<IFileAdapter> _adapters;
    private readonly StartupWorkbookLoadResult _source;
    private readonly WorkbookCellEditService _cellEditService;
    private readonly WorkbookSheetSelectionService _sheetSelectionService;
    private readonly IViewportService _viewportService;
    private readonly bool _includeObjects;
    private double _viewportHeight;
    private double _viewportWidth;

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
        Viewport = BuildViewport();
    }

    public Workbook Workbook { get; }

    public Sheet ActiveSheet { get; private set; }

    public ViewportModel Viewport { get; private set; }

    public double ViewportHeight => _viewportHeight;

    public double ViewportWidth => _viewportWidth;

    public CellAddress ActiveCell { get; private set; }

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

    public void SelectCell(CellAddress address)
    {
        ActiveCell = address;
        ActiveSheet.ActiveRow = address.Row;
        ActiveSheet.ActiveCol = address.Col;
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
        FormulaEditAddress = null;
        RefreshViewport();
        return true;
    }

    public void BeginFormulaEdit(CellAddress address)
    {
        ActiveCell = address;
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

        ActiveCell = address;
        FormulaEditAddress = null;
        IsDirty = true;
        RefreshViewport();
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
