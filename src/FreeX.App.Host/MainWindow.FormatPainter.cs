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
    // Set by the second mouse-down of a double-click (which arms sticky mode via the Preview
    // handler below) so the immediately-following Click (its mouse-up) doesn't turn around and
    // cancel the sticky mode it just armed.
    private bool _formatPainterSuppressNextClickToggle;

    private void FormatPainterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_formatPainterSuppressNextClickToggle)
        {
            _formatPainterSuppressNextClickToggle = false;
            return;
        }

        // Matches Excel: clicking the already-pressed Format Painter button (single-shot or
        // sticky/double-click mode) cancels it, rather than re-capturing a new source and
        // leaving it active. The double-click handler below re-arms sticky mode afterward when
        // the second click of a genuine double-click comes through.
        if (_formatPainterActive)
        {
            CancelFormatPainter();
            return;
        }

        CaptureFormatPainterSource(persistent: false);
    }

    private void FormatPainterBtn_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;

        CaptureFormatPainterSource(persistent: true);
        _formatPainterSuppressNextClickToggle = true;
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
