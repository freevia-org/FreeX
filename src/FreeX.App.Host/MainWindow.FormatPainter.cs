using System.Windows;
using System.Windows.Input;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private bool _formatPainterActive;
    private bool _formatPainterPersistent;
    private bool _formatPainterTargetSelectionActive;
    private SheetId? _formatPainterSourceSheetId;
    private GridRange? _formatPainterSourceRange;

    private void FormatPainterBtn_Click(object sender, RoutedEventArgs e)
    {
        CaptureFormatPainterSource(persistent: false);
    }

    private void FormatPainterBtn_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;

        CaptureFormatPainterSource(persistent: true);
        e.Handled = true;
    }

    private void CaptureFormatPainterSource(bool persistent)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        _formatPainterSourceSheetId = _currentSheetId;
        _formatPainterSourceRange = range;
        _formatPainterActive = true;
        _formatPainterPersistent = persistent;
    }

    private void CancelFormatPainter()
    {
        _formatPainterActive = false;
        _formatPainterPersistent = false;
        _formatPainterTargetSelectionActive = false;
        _formatPainterSourceSheetId = null;
        _formatPainterSourceRange = null;
    }

    private bool TryApplyFormatPainter(GridRange targetRange)
    {
        if (!_formatPainterActive) return false;

        if (_formatPainterSourceSheetId is not { } sourceSheetId ||
            _formatPainterSourceRange is not { } sourceRange ||
            _workbook.GetSheet(sourceSheetId) is not { } sourceSheet)
        {
            if (!_formatPainterPersistent)
                CancelFormatPainter();
            return true;
        }

        var targetRanges = GetCurrentSelectionRanges(targetRange);
        var command = SelectionStyleCommandPlanner.CreateRangeCommand(
            CurrentGroupedEditSheetIds(),
            targetRanges,
            (sheetId, sheetTargetRange) => FormatPainterCommandFactory.Create(
                _workbook,
                sourceSheet,
                sourceRange,
                sheetTargetRange),
            "Format Painter");
        if (!TryExecuteCommand(command, "Format Painter"))
        {
            if (!_formatPainterPersistent)
                CancelFormatPainter();
            return true;
        }

        if (!_formatPainterPersistent)
            CancelFormatPainter();

        UpdateViewport();
        return true;
    }

    // ── Paste Special ────────────────────────────────────────────────────────
}
