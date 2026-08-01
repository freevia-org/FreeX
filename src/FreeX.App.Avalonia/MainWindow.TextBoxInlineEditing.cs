using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private TextBox? _textBoxInlineEditor;
    private Border? _textBoxInlineEditorChrome;
    private Guid? _textBoxInlineEditingId;
    private string? _textBoxInlineOriginalText;

    private bool IsTextBoxInlineEditorVisible =>
        _textBoxInlineEditor is { IsVisible: true } && _textBoxInlineEditingId is not null;

    private bool IsTextBoxInlineEditorActive => IsTextBoxInlineEditorVisible;

    private void BeginTextBoxInlineEdit(Guid textBoxId)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        HideDataValidationDropdown();

        if (IsTextBoxInlineEditorVisible &&
            _textBoxInlineEditingId != textBoxId &&
            !HideTextBoxInlineEditor(commit: true))
        {
            return;
        }

        var textBox = GetCurrentSheetTextBox(textBoxId);
        if (textBox is null)
            return;

        EnsureTextBoxInlineEditor();
        _textBoxInlineEditingId = textBox.Id;
        _textBoxInlineOriginalText = textBox.Text;
        _textBoxInlineEditor!.Text = textBox.Text;
        _textBoxInlineEditor.CaretIndex = _textBoxInlineEditor.Text?.Length ?? 0;
        _textBoxInlineEditor.SelectionStart = _textBoxInlineEditor.CaretIndex;
        _textBoxInlineEditor.SelectionEnd = _textBoxInlineEditor.CaretIndex;
        _selectedDrawingObjectKind = SelectionPaneObjectKind.TextBox;
        _selectedDrawingObjectId = textBox.Id;
        _ribbonContextSource.OnDrawingObjectSelected(SelectionPaneObjectKind.TextBox);
        RefreshTableContextualTab();
        RefreshPivotContextualTab();
        RefreshShell("Ready");
        FocusTextBoxInlineEditor();
    }

    private void EnsureTextBoxInlineEditor()
    {
        if (_textBoxInlineEditor is not null)
            return;

        _textBoxInlineEditorChrome = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 109, 140)),
            BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false,
            IsVisible = false,
            ZIndex = 100,
        };
        AutomationProperties.SetAutomationId(_textBoxInlineEditorChrome, "TextBoxInlineEditorChrome");
        _textBoxInlineEditor = new TextBox
        {
            AcceptsReturn = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Calibri"),
            FontSize = 12,
            Foreground = Brushes.Black,
            Padding = new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            IsVisible = false,
            ZIndex = 101,
        };
        _textBoxInlineEditor.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _textBoxInlineEditor.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        AutomationProperties.SetAutomationId(_textBoxInlineEditor, "TextBoxInlineEditor");
        AutomationProperties.SetName(_textBoxInlineEditor, "Text box inline editor");
        AutomationProperties.SetHelpText(_textBoxInlineEditor, "Edits the selected text box in place.");
        _textBoxInlineEditor.KeyDown += TextBoxInlineEditor_KeyDown;
        _textBoxInlineEditor.LostFocus += TextBoxInlineEditor_LostFocus;
    }

    private void AddTextBoxInlineEditorOverlay(
        Canvas overlay,
        ViewportModel viewport,
        bool showHeadings,
        double zoomFactor)
    {
        if (_textBoxInlineEditingId is not { } textBoxId ||
            GetCurrentSheetTextBox(textBoxId) is not { } textBox)
        {
            return;
        }

        EnsureTextBoxInlineEditor();
        var editor = _textBoxInlineEditor!;
        var chrome = _textBoxInlineEditorChrome!;
        if (!TryGetDisplayedTextBoxLayout(viewport, textBox, showHeadings, zoomFactor, out var layout))
        {
            HideTextBoxInlineEditor(commit: true, refresh: false);
            return;
        }

        Canvas.SetLeft(chrome, layout.Bounds.Left);
        Canvas.SetTop(chrome, layout.Bounds.Top);
        chrome.Width = layout.Bounds.Width;
        chrome.Height = layout.Bounds.Height;
        Canvas.SetLeft(editor, layout.TextBounds.Left);
        Canvas.SetTop(editor, layout.TextBounds.Top);
        editor.Width = layout.TextBounds.Width;
        editor.Height = layout.TextBounds.Height;
        chrome.IsVisible = true;
        editor.IsVisible = true;
        DetachInlineEditorControl(chrome);
        DetachInlineEditorControl(editor);
        overlay.Children.Add(chrome);
        overlay.Children.Add(editor);
    }

    private static void DetachInlineEditorControl(Control control)
    {
        if (control.Parent is Panel parent)
            parent.Children.Remove(control);
    }

    private bool TryGetDisplayedTextBoxLayout(
        ViewportModel viewport,
        TextBoxModel textBox,
        bool showHeadings,
        double zoomFactor,
        out TextBoxFrameLayout layout)
    {
        layout = default;
        if (!DrawingObjectViewportPlanner.TryCreateAnchoredObjectRect(
                viewport,
                textBox.Anchor,
                showHeadings ? GetRowHeaderWidth(viewport, zoomFactor) : 0,
                showHeadings ? GetColumnHeaderHeight(viewport, zoomFactor) : 0,
                textBox.Width,
                textBox.Height,
                TextBoxFrameLayoutPlanner.MinimumWidth,
                TextBoxFrameLayoutPlanner.MinimumHeight,
                out var bounds,
                textBox.AnchorOffsetX,
                textBox.AnchorOffsetY))
        {
            return false;
        }

        layout = TextBoxFrameLayoutPlanner.CreateScaled(bounds, zoomFactor);
        return DrawingObjectViewportPlanner.ShouldDisplayObjectRect(
            layout.Bounds,
            textBox.RotationDegrees,
            CalculateDisplayedGridWidth(viewport, showHeadings, zoomFactor),
            CalculateDisplayedGridHeight(viewport, showHeadings, zoomFactor));
    }

    private void FocusTextBoxInlineEditor()
    {
        if (!IsTextBoxInlineEditorVisible || _textBoxInlineEditor is null)
            return;

        _textBoxInlineEditor.Focus();
        _textBoxInlineEditor.CaretIndex = _textBoxInlineEditor.Text?.Length ?? 0;
        _textBoxInlineEditor.SelectionStart = _textBoxInlineEditor.CaretIndex;
        _textBoxInlineEditor.SelectionEnd = _textBoxInlineEditor.CaretIndex;
    }

    private bool HideTextBoxInlineEditor(bool commit, bool refresh = true)
    {
        if (_textBoxInlineEditor is null || _textBoxInlineEditingId is not { } textBoxId)
            return true;

        var textChanged = false;
        if (commit)
        {
            var plan = TextBoxInlineEditPlanner.CreateCommitPlan(
                _textBoxInlineOriginalText,
                _textBoxInlineEditor.Text ?? string.Empty);
            if (plan.TextChanged)
            {
                var result = _session.ExecuteReviewCommand(
                    new SetTextBoxTextCommand(_session.ActiveSheet.Id, textBoxId, plan.Text));
                if (!result.Success)
                {
                    ShowEditIssue(result.ErrorMessage ?? "Could not edit text box.");
                    return false;
                }

                textChanged = true;
            }
        }

        _textBoxInlineEditor.IsVisible = false;
        if (_textBoxInlineEditorChrome is not null)
            _textBoxInlineEditorChrome.IsVisible = false;
        _textBoxInlineEditingId = null;
        _textBoxInlineOriginalText = null;
        if (refresh)
            RefreshShell(textChanged ? "Edit Text Box" : "Ready");
        return true;
    }

    private void TextBoxInlineEditor_KeyDown(object? sender, KeyEventArgs args)
    {
        var action = TextBoxInlineEditPlanner.PlanKeyDown(
            ToTextBoxInlineEditKey(args.Key),
            args.KeyModifiers != KeyModifiers.None);
        if (action == TextBoxInlineEditKeyAction.None)
            return;

        if (action == TextBoxInlineEditKeyAction.Cancel)
        {
            _textBoxInlineEditor!.Text = _textBoxInlineOriginalText ?? string.Empty;
            HideTextBoxInlineEditor(commit: false);
            _sheetGridHost.Focus();
            args.Handled = true;
            return;
        }

        if (HideTextBoxInlineEditor(commit: true))
        {
            _sheetGridHost.Focus();
        }

        args.Handled = true;
    }

    private void TextBoxInlineEditor_LostFocus(object? sender, RoutedEventArgs args)
    {
        Dispatcher.UIThread.Post(
            CommitTextBoxInlineEditorLostFocusIfNeeded,
            DispatcherPriority.Input);
    }

    private void CommitTextBoxInlineEditorLostFocusIfNeeded()
    {
        if (!TextBoxInlineEditPlanner.ShouldCommitLostFocus(
                IsTextBoxInlineEditorVisible,
                _textBoxInlineEditor?.IsFocused == true,
                ReferenceEquals(FocusManager?.GetFocusedElement(), _textBoxInlineEditor)))
        {
            return;
        }

        HideTextBoxInlineEditor(commit: true);
    }

    private static TextBoxInlineEditKey ToTextBoxInlineEditKey(Key key) =>
        key switch
        {
            Key.Escape => TextBoxInlineEditKey.Escape,
            Key.Enter => TextBoxInlineEditKey.Enter,
            Key.Tab => TextBoxInlineEditKey.Tab,
            _ => TextBoxInlineEditKey.Other,
        };

    private TextBoxModel? GetCurrentSheetTextBox(Guid textBoxId) =>
        TextBoxModel.FindById(_session.ActiveSheet.TextBoxes, textBoxId);

    private void HideDataValidationDropdown()
    {
        if (_activeDataValidationDropdown is { } dropdown)
        {
            dropdown.IsDropDownOpen = false;
            dropdown.IsVisible = false;
        }

        _activeDataValidationDropdown = null;
    }

    internal bool IsTextBoxInlineEditorActiveForTest => IsTextBoxInlineEditorActive;
    internal TextBox? TextBoxInlineEditorForTest => _textBoxInlineEditor;

    internal void BeginTextBoxInlineEditForTest(Guid textBoxId) => BeginTextBoxInlineEdit(textBoxId);

    internal void RaiseTextBoxInlineEditorKeyDownForTest(KeyEventArgs args)
    {
        if (_textBoxInlineEditor is null)
            throw new InvalidOperationException("No text box inline editor exists.");

        TextBoxInlineEditor_KeyDown(_textBoxInlineEditor, args);
    }

    internal void InsertTextBoxAtActiveCellForTest() => InsertTextBoxAtActiveCell();

    internal void RefreshShellForViewportPanForTest() => RefreshShellForViewportPan("Ready");
}
