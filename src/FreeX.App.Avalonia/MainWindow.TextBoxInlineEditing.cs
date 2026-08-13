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
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private TextBox? _textBoxInlineEditor;
    private Border? _textBoxInlineEditorChrome;
    private readonly TextBoxInlineEditSession _textBoxInlineEditSession = new();

    private bool IsTextBoxInlineEditorVisible =>
        _textBoxInlineEditor is { IsVisible: true } && _textBoxInlineEditSession.IsActive;

    private bool IsTextBoxInlineEditorActive => IsTextBoxInlineEditorVisible;

    private void BeginTextBoxInlineEdit(Guid textBoxId)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        HideDataValidationDropdown();

        if (IsTextBoxInlineEditorVisible &&
            !_textBoxInlineEditSession.IsEditing(textBoxId) &&
            !HideTextBoxInlineEditor(commit: true))
        {
            return;
        }

        var textBox = GetCurrentSheetTextBox(textBoxId);
        if (textBox is null)
            return;

        EnsureTextBoxInlineEditor();
        var startPlan = _textBoxInlineEditSession.Begin(textBox);
        _textBoxInlineEditor!.Text = startPlan.Text;
        _textBoxInlineEditor.CaretIndex = _textBoxInlineEditor.Text?.Length ?? 0;
        _textBoxInlineEditor.SelectionStart = _textBoxInlineEditor.CaretIndex;
        _textBoxInlineEditor.SelectionEnd = _textBoxInlineEditor.CaretIndex;
        _selectedDrawingObjectKind = SelectionPaneObjectKind.TextBox;
        _selectedDrawingObjectId = textBox.Id;
        _ribbonContextSource.OnDrawingObjectSelected(SelectionPaneObjectKind.TextBox);
        RefreshTableContextualTab();
        RefreshPivotContextualTab();
        RequestOptionalTextBoxInlineLayoutObservation();
        RefreshShell(UiText.Get("MainLoc_Ready"));
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
        AutomationProperties.SetName(_textBoxInlineEditor, UiText.Get("TextBoxInlineEditor_AutomationName"));
        AutomationProperties.SetHelpText(_textBoxInlineEditor, UiText.Get("TextBoxInlineEditor_HelpText"));
        _textBoxInlineEditor.KeyDown += TextBoxInlineEditor_KeyDown;
        _textBoxInlineEditor.TextChanged += (_, _) =>
        {
            if (_textBoxInlineEditSession.EditingTextBoxId is { } textBoxId && _textBoxInlineEditor.IsVisible)
                RecordOptionalTextBoxInlineObservation("editing", textBoxId);
        };
        _textBoxInlineEditor.LostFocus += TextBoxInlineEditor_LostFocus;
        AttachOptionalTextBoxInlineObservation();
    }

    private void AddTextBoxInlineEditorOverlay(
        Canvas overlay,
        ViewportModel viewport,
        bool showHeadings,
        double zoomFactor)
    {
        if (_textBoxInlineEditSession.EditingTextBoxId is not { } textBoxId ||
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
        if (_textBoxInlineEditor is null || !_textBoxInlineEditSession.IsActive)
            return true;

        var textChanged = false;
        if (commit)
        {
            var plan = _textBoxInlineEditSession.CreateCommitPlan(
                _session.ActiveSheet.Id,
                _textBoxInlineEditor.Text)!;
            if (plan.TextChanged)
            {
                var result = _session.ExecuteReviewCommand(plan.Command!);
                if (!result.Success)
                {
                    ShowEditIssue(result.ErrorMessage ?? UiText.Get("TextBoxInlineEditor_EditFailed"));
                    return false;
                }

                textChanged = true;
            }
        }

        _textBoxInlineEditor.IsVisible = false;
        if (_textBoxInlineEditorChrome is not null)
            _textBoxInlineEditorChrome.IsVisible = false;
        _textBoxInlineEditSession.Complete();
        if (refresh)
            RefreshShell(textChanged ? "Edit Text Box" : "Ready");
        return true;
    }

    private void TextBoxInlineEditor_KeyDown(object? sender, KeyEventArgs args)
    {
        var action = _textBoxInlineEditSession.PlanKeyDown(
            ToTextBoxInlineEditKey(args.Key),
            args.KeyModifiers != KeyModifiers.None);
        if (action == TextBoxInlineEditKeyAction.None)
            return;

        if (action == TextBoxInlineEditKeyAction.Cancel)
        {
            var cancelPlan = _textBoxInlineEditSession.CreateCancelPlan();
            _textBoxInlineEditor!.Text = cancelPlan?.OriginalText ?? string.Empty;
            HideTextBoxInlineEditor(commit: false);
            if (cancelPlan is { } canceled)
                RecordOptionalTextBoxInlineObservation("canceled", canceled.TextBoxId);
            _sheetGridHost.Focus();
            args.Handled = true;
            return;
        }

        var committedTextBoxId = _textBoxInlineEditSession.EditingTextBoxId;
        if (HideTextBoxInlineEditor(commit: true))
        {
            if (committedTextBoxId is { } textBoxId)
                RecordOptionalTextBoxInlineObservation("committed", textBoxId);
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
        if (!_textBoxInlineEditSession.ShouldCommitLostFocus(
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
}
