using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FreeX.App.Presentation.Editing;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

// Excel's "Automatically Flash Fill": while a value is typed next to data, the pattern Flash Fill
// detects is previewed as grey ghost text in the cells below. Enter/Tab accepts (one undoable Flash
// Fill command), Esc or typing something that no longer fits dismisses. The pure decisions live in
// FlashFillSuggestionPlanner; this partial only renders the ghosts and routes keys.
public sealed partial class MainWindow
{
    private readonly FlashFillSuggestionState _flashFillSuggestionState = new();
    private readonly List<Control> _flashFillGhostVisuals = [];

    private void UpdateFlashFillSuggestion(CellAddress address, string? text)
    {
        var enabled = _optionsRuntimeSession.LiveOptions.AutomaticallyFlashFill &&
            _session.GetCurrentGroupedEditSheetIds().Count <= 1;
        if (_flashFillSuggestionState.Update(enabled, _session.Workbook, address, text))
            RefreshFlashFillGhostOverlay();
    }

    private void ClearFlashFillSuggestion()
    {
        if (_flashFillSuggestionState.Clear())
            RefreshFlashFillGhostOverlay();
    }

    private void RefreshFlashFillGhostOverlay()
    {
        if (_formulaReferenceGridOverlay is not { } overlay)
        {
            _flashFillGhostVisuals.Clear();
            return;
        }

        foreach (var visual in _flashFillGhostVisuals)
            overlay.Children.Remove(visual);
        _flashFillGhostVisuals.Clear();

        if (_flashFillSuggestionState.Current is not { } suggestion)
            return;

        AddFlashFillGhostOverlay(overlay, suggestion, _session.Viewport, _session.IsShowingHeadings, GetActiveZoomFactor());
    }

    private void AddFlashFillGhostOverlay(
        Canvas overlay,
        FlashFillSuggestion suggestion,
        ViewportModel viewport,
        bool showHeadings,
        double zoomFactor)
    {
        if (suggestion.SheetId != _session.ActiveSheet.Id)
            return;

        var fontFamily = ResolveAvaloniaCellFontFamily(null, _session.Workbook.Theme);
        var fontSize = Math.Max(1, ToWorksheetDisplayFontSize(null) * zoomFactor);
        foreach (var ghost in suggestion.Ghosts)
        {
            var address = new CellAddress(suggestion.SheetId, ghost.Row, ghost.Col);
            if (!TryGetDisplayedRangeBounds(
                    viewport,
                    new GridRange(address, address),
                    showHeadings,
                    zoomFactor,
                    out var left,
                    out var top,
                    out var width,
                    out var height))
            {
                continue;
            }

            var text = new TextBlock
            {
                Text = ghost.Text,
                Foreground = Brushes.Gray,
                FontFamily = fontFamily,
                FontSize = fontSize,
                Width = width,
                Height = height,
                Padding = new Thickness(3, 0, 3, 1),
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = AvaloniaVerticalAlignment.Bottom,
                ClipToBounds = true,
                IsHitTestVisible = false,
            };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(text, "WorksheetFlashFillGhostCell");
            Canvas.SetLeft(text, left);
            Canvas.SetTop(text, top);
            overlay.Children.Add(text);
            _flashFillGhostVisuals.Add(text);
        }
    }

    /// <summary>
    /// Enter/Tab with a live suggestion: cancels the in-cell edit and runs the undoable Flash Fill
    /// command. Returns true when the key was consumed.
    /// </summary>
    private bool TryAcceptFlashFillSuggestion(Key key, KeyModifiers modifiers)
    {
        var mapped = key switch
        {
            Key.Enter => FlashFillSuggestionKey.Enter,
            Key.Tab => FlashFillSuggestionKey.Tab,
            Key.Escape => FlashFillSuggestionKey.Escape,
            _ => FlashFillSuggestionKey.Other,
        };
        var suggestion = _flashFillSuggestionState.Current;
        if (FlashFillSuggestionPlanner.Resolve(suggestion, mapped, modifiers != KeyModifiers.None) !=
            FlashFillSuggestionKeyAction.Accept || suggestion is null)
        {
            return false;
        }

        _flashFillSuggestionState.Clear();
        var formulaEditAddress = _session.FormulaEditAddress;
        _session.CancelFormulaEdit();
        if (formulaEditAddress is { } address)
            _session.SelectCell(address);
        _formulaBoxEditOriginalText = null;
        ClearFormulaRangeEntryState();
        ClearInlineCellEditorState();

        var result = _session.AcceptFlashFillSuggestion(suggestion);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("MainLoc_FlashFillFailed"));
            return true;
        }

        RefreshShell(UiText.Format("MainLoc_FlashFilledX", FormatRangeReference(suggestion.AffectedRange)));
        FocusShellRegion(ShellFocusTarget.Worksheet);
        return true;
    }
}
