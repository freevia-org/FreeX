using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeX.App.Services;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private readonly TextBoxInlineEditSession _textBoxInlineEditSession = new();

    private void OnTextBoxEditRequested(Guid textBoxId) =>
        BeginTextBoxInlineEdit(textBoxId);

    private void BeginTextBoxInlineEdit(Guid textBoxId)
    {
        if (_inlineEditor?.IsVisible == true)
        {
            FormulaBar.Text = _inlineEditor.Text;
            if (!CommitEdit())
                return;

            HideInlineEditor(commit: false);
            ClearFormulaRangeEntryState();
        }

        HideValidationDropdown();
        if (_textBoxInlineEditor?.IsVisible == true &&
            !HideTextBoxInlineEditor(commit: true))
        {
            return;
        }

        if (GetCurrentSheetTextBox(textBoxId) is not { } textBox)
            return;

        EnsureTextBoxInlineEditor();
        var startPlan = _textBoxInlineEditSession.Begin(textBox);
        _textBoxInlineEditor!.Text = startPlan.Text;
        SheetGrid.EditingTextBoxId = startPlan.TextBoxId;
        SheetGrid.SelectedObjectId = startPlan.TextBoxId;
        SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.TextBox;
        if (!PositionTextBoxInlineEditor(textBox))
        {
            HideTextBoxInlineEditor(commit: false);
            return;
        }

        _textBoxInlineEditorChrome!.Visibility = Visibility.Visible;
        _textBoxInlineEditor.Visibility = Visibility.Visible;
        EditOverlay.IsHitTestVisible = true;
        FocusManager.SetFocusedElement(this, _textBoxInlineEditor);
        _textBoxInlineEditor.Focus();
        Keyboard.Focus(_textBoxInlineEditor);
        _textBoxInlineEditor.CaretIndex = _textBoxInlineEditor.Text.Length;
        _textBoxInlineEditor.SelectionLength = 0;
        SetFormulaEditStatusBarMode(pointMode: false);
        SheetGrid.InvalidateVisual();
    }

    private void EnsureTextBoxInlineEditor()
    {
        if (_textBoxInlineEditor is not null)
            return;

        _textBoxInlineEditorChrome = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 109, 140)),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        _textBoxInlineEditor = new WpfTextBox
        {
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontFamily = new FontFamily("Calibri"),
            FontSize = 12.0,
            Foreground = Brushes.Black,
            Background = Brushes.Transparent,
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Top
        };
        AutomationProperties.SetAutomationId(_textBoxInlineEditor, "TextBoxInlineEditor");
        TextOptions.SetTextFormattingMode(_textBoxInlineEditor, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(_textBoxInlineEditor, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(_textBoxInlineEditor, TextHintingMode.Fixed);
        _textBoxInlineEditor.PreviewKeyDown += TextBoxInlineEditor_KeyDown;
        _textBoxInlineEditor.LostFocus += TextBoxInlineEditor_LostFocus;
        Panel.SetZIndex(_textBoxInlineEditorChrome, 30);
        Panel.SetZIndex(_textBoxInlineEditor, 31);
        EditOverlay.Children.Add(_textBoxInlineEditorChrome);
        EditOverlay.Children.Add(_textBoxInlineEditor);
    }

    private bool PositionTextBoxInlineEditor(TextBoxModel textBox)
    {
        if (_textBoxInlineEditor is null || _textBoxInlineEditorChrome is null)
            return false;

        if (!SheetGrid.TryCreateAnchoredObjectRect(
                textBox.Anchor,
                textBox.Width,
                textBox.Height,
                TextBoxFrameLayoutPlanner.MinimumWidth,
                TextBoxFrameLayoutPlanner.MinimumHeight,
                out var unscaledRect,
                textBox.AnchorOffsetX,
                textBox.AnchorOffsetY))
        {
            return false;
        }

        var zoom = _zoomLevel;
        var layout = TextBoxFrameLayoutPlanner.CreateScaled(ToLayoutRect(unscaledRect), zoom);
        ApplyTextBoxInlineElementBounds(_textBoxInlineEditorChrome, layout.Bounds);
        ApplyTextBoxInlineElementBounds(_textBoxInlineEditor, layout.TextBounds);
        return true;
    }

    private void RefreshTextBoxInlineEditorPosition()
    {
        if (_textBoxInlineEditor?.IsVisible != true ||
            _textBoxInlineEditSession.EditingTextBoxId is not { } textBoxId)
        {
            return;
        }

        if (GetCurrentSheetTextBox(textBoxId) is not { } textBox ||
            !PositionTextBoxInlineEditor(textBox))
        {
            HideTextBoxInlineEditor(commit: true);
        }
    }

    private bool HideTextBoxInlineEditor(bool commit)
    {
        if (_textBoxInlineEditor is null)
            return true;

        var textChanged = false;
        if (commit && !TryCommitTextBoxInlineEdit(out textChanged))
            return false;

        _textBoxInlineEditor.Visibility = Visibility.Collapsed;
        if (_textBoxInlineEditorChrome is not null)
            _textBoxInlineEditorChrome.Visibility = Visibility.Collapsed;
        _textBoxInlineEditSession.Complete();
        SheetGrid.EditingTextBoxId = null;
        if (_inlineEditor?.IsVisible != true &&
            _validationDropdown?.Visibility != Visibility.Visible)
            EditOverlay.IsHitTestVisible = false;

        if (textChanged)
            UpdateViewport();
        else
            SheetGrid.InvalidateVisual();

        return true;
    }

    private static LayoutRect ToLayoutRect(Rect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static void ApplyTextBoxInlineElementBounds(FrameworkElement element, LayoutRect bounds)
    {
        Canvas.SetLeft(element, bounds.Left);
        Canvas.SetTop(element, bounds.Top);
        element.Width = bounds.Width;
        element.Height = bounds.Height;
    }

    private bool TryCommitTextBoxInlineEdit(out bool textChanged)
    {
        textChanged = false;
        if (_textBoxInlineEditor is null ||
            _textBoxInlineEditSession.CreateCommitPlan(_currentSheetId, _textBoxInlineEditor.Text) is not { } plan)
        {
            return true;
        }

        if (!plan.TextChanged)
            return true;

        if (!TryExecuteCommand(
                plan.Command!,
                TextBoxInlineEditSession.CommitCommandTitle))
            return false;

        textChanged = true;
        return true;
    }

    private void TextBoxInlineEditor_KeyDown(object sender, KeyEventArgs e)
    {
        var action = _textBoxInlineEditSession.PlanKeyDown(
            ToTextBoxInlineEditKey(e.Key),
            Keyboard.Modifiers != ModifierKeys.None);
        if (action == TextBoxInlineEditKeyAction.None)
            return;

        if (action == TextBoxInlineEditKeyAction.Cancel)
        {
            if (_textBoxInlineEditor is not null &&
                _textBoxInlineEditSession.CreateCancelPlan() is { } cancelPlan)
            {
                _textBoxInlineEditor.Text = cancelPlan.OriginalText;
            }

            HideTextBoxInlineEditor(commit: false);
            FocusSheetGridIfNeeded();
            e.Handled = true;
            return;
        }

        if (action == TextBoxInlineEditKeyAction.Commit)
        {
            if (HideTextBoxInlineEditor(commit: true))
                FocusSheetGridIfNeeded();
            e.Handled = true;
        }
    }

    private static TextBoxInlineEditKey ToTextBoxInlineEditKey(Key key) =>
        key switch
        {
            Key.Escape => TextBoxInlineEditKey.Escape,
            Key.Enter => TextBoxInlineEditKey.Enter,
            Key.Tab => TextBoxInlineEditKey.Tab,
            _ => TextBoxInlineEditKey.Other
        };

    private void TextBoxInlineEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(CommitTextBoxInlineEditorLostFocusIfNeeded));
    }

    private void CommitTextBoxInlineEditorLostFocusIfNeeded()
    {
        if (!_textBoxInlineEditSession.ShouldCommitLostFocus(
                _textBoxInlineEditor?.IsVisible == true,
                ReferenceEquals(Keyboard.FocusedElement, _textBoxInlineEditor),
                ReferenceEquals(FocusManager.GetFocusedElement(this), _textBoxInlineEditor)))
            return;

        HideTextBoxInlineEditor(commit: true);
    }

    private TextBoxModel? GetCurrentSheetTextBox(Guid textBoxId)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return null;

        return TextBoxModel.FindById(sheet.TextBoxes, textBoxId);
    }
}
