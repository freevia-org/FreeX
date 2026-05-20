using System.Linq;
using System.Windows;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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

        IWorkbookCommand CreateCommand(SheetId sheetId)
        {
            var sheetTargetRange = new GridRange(
                new CellAddress(sheetId, targetRange.Start.Row, targetRange.Start.Col),
                new CellAddress(sheetId, targetRange.End.Row, targetRange.End.Col));
            return FormatPainterCommandFactory.Create(_workbook, sourceSheet, sourceRange, sheetTargetRange);
        }

        var targetSheetIds = CurrentGroupedEditSheetIds();
        var command = targetSheetIds.Count > 1
            ? new CompositeWorkbookCommand("Format Painter", targetSheetIds.Select(CreateCommand).ToList())
            : FormatPainterCommandFactory.Create(_workbook, sourceSheet, sourceRange, targetRange);
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
