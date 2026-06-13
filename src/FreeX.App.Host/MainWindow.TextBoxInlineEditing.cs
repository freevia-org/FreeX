using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const double TextBoxInlineEditorMinimumWidth = 24.0;
    private const double TextBoxInlineEditorMinimumHeight = 18.0;
    private const double TextBoxInlineEditorInset = 4.0;

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
        _textBoxInlineEditingId = textBox.Id;
        _textBoxInlineOriginalText = textBox.Text;
        _textBoxInlineEditor!.Text = textBox.Text;
        SheetGrid.EditingTextBoxId = textBox.Id;
        SheetGrid.SelectedObjectId = textBox.Id;
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
        SetStatusBarModeText(UiText.Get("StatusBar_EditMode"));
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
                TextBoxInlineEditorMinimumWidth,
                TextBoxInlineEditorMinimumHeight,
                out var unscaledRect))
        {
            return false;
        }

        var zoom = _zoomLevel;
        var rect = new Rect(
            unscaledRect.Left * zoom,
            unscaledRect.Top * zoom,
            unscaledRect.Width * zoom,
            unscaledRect.Height * zoom);

        Canvas.SetLeft(_textBoxInlineEditorChrome, rect.Left);
        Canvas.SetTop(_textBoxInlineEditorChrome, rect.Top);
        _textBoxInlineEditorChrome.Width = rect.Width;
        _textBoxInlineEditorChrome.Height = rect.Height;

        Canvas.SetLeft(_textBoxInlineEditor, rect.Left + TextBoxInlineEditorInset);
        Canvas.SetTop(_textBoxInlineEditor, rect.Top + TextBoxInlineEditorInset);
        _textBoxInlineEditor.Width = Math.Max(1, rect.Width - (TextBoxInlineEditorInset * 2));
        _textBoxInlineEditor.Height = Math.Max(1, rect.Height - (TextBoxInlineEditorInset * 2));
        return true;
    }

    private void RefreshTextBoxInlineEditorPosition()
    {
        if (_textBoxInlineEditor?.IsVisible != true ||
            _textBoxInlineEditingId is not { } textBoxId)
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
        _textBoxInlineEditingId = null;
        _textBoxInlineOriginalText = null;
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

    private bool TryCommitTextBoxInlineEdit(out bool textChanged)
    {
        textChanged = false;
        if (_textBoxInlineEditor is null ||
            _textBoxInlineEditingId is not { } textBoxId)
        {
            return true;
        }

        var newText = _textBoxInlineEditor.Text;
        if (string.Equals(_textBoxInlineOriginalText, newText, StringComparison.Ordinal))
            return true;

        if (!TryExecuteCommand(new SetTextBoxTextCommand(_currentSheetId, textBoxId, newText), "Edit Text Box"))
            return false;

        _textBoxInlineOriginalText = newText;
        textChanged = true;
        return true;
    }

    private void TextBoxInlineEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (_textBoxInlineEditor is not null && _textBoxInlineOriginalText is not null)
                _textBoxInlineEditor.Text = _textBoxInlineOriginalText;

            HideTextBoxInlineEditor(commit: false);
            FocusSheetGridIfNeeded();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Return && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (HideTextBoxInlineEditor(commit: true))
                FocusSheetGridIfNeeded();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab)
        {
            if (HideTextBoxInlineEditor(commit: true))
                FocusSheetGridIfNeeded();
            e.Handled = true;
        }
    }

    private void TextBoxInlineEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(CommitTextBoxInlineEditorLostFocusIfNeeded));
    }

    private void CommitTextBoxInlineEditorLostFocusIfNeeded()
    {
        if (_textBoxInlineEditor?.IsVisible != true)
            return;

        if (ReferenceEquals(Keyboard.FocusedElement, _textBoxInlineEditor) ||
            ReferenceEquals(FocusManager.GetFocusedElement(this), _textBoxInlineEditor))
            return;

        HideTextBoxInlineEditor(commit: true);
    }

    private TextBoxModel? GetCurrentSheetTextBox(Guid textBoxId)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return null;

        foreach (var textBox in sheet.TextBoxes)
        {
            if (textBox.Id == textBoxId)
                return textBox;
        }

        return null;
    }
}
