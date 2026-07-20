using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Rich in-canvas editor for Avalonia. A native TextBox owns input, IME, clipboard, and local
/// text undo while a synchronized layout surface renders mixed runs, selection, and caret.
/// </summary>
internal sealed class AvaloniaRichTextEditor : Grid
{
    private readonly InCanvasRichTextEditBuffer _buffer;
    private readonly AvaloniaRichTextEditingSurface _richTextView;
    private bool _synchronizing;
    private int _pointerSelectionAnchor;

    internal AvaloniaRichTextEditor(TextBody? body, byte backgroundAlpha)
    {
        _buffer = new InCanvasRichTextEditBuffer(body);
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.FromArgb(backgroundAlpha, 0xFF, 0xFF, 0xFF));

        InputBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Text = _buffer.PlainText,
            Padding = new Thickness(2),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
            BorderThickness = new Thickness(1.5),
            Foreground = Brushes.Transparent,
            CaretBrush = Brushes.Transparent,
            SelectionBrush = Brushes.Transparent,
            SelectionForegroundBrush = Brushes.Transparent,
        };
        AutomationProperties.SetAutomationId(InputBox, "FreePRichTextEditorInput");

        _richTextView = new AvaloniaRichTextEditingSurface();
        AutomationProperties.SetAccessibilityView(_richTextView, AccessibilityView.Raw);

        Children.Add(_richTextView);
        Children.Add(InputBox);

        InputBox.TextChanged += OnInputTextChanged;
        InputBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.SelectionStartProperty
                || args.Property == TextBox.SelectionEndProperty)
            {
                UpdateSurfaceSelection();
            }
        };
        InputBox.GotFocus += (_, _) => UpdateSurfaceSelection();
        InputBox.LostFocus += (_, _) => UpdateSurfaceSelection();
        InputBox.AddHandler(
            InputElement.PointerPressedEvent,
            OnInputPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        InputBox.AddHandler(
            InputElement.PointerMovedEvent,
            OnInputPointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        InputBox.AddHandler(
            InputElement.PointerReleasedEvent,
            OnInputPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        InputBox.AddHandler(
            InputElement.KeyDownEvent,
            OnInputNavigationKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        RenderBody();
    }

    internal TextBox InputBox { get; }

    internal AvaloniaRichTextEditingSurface RichTextView => _richTextView;

    internal TextBody EditedBody
    {
        get
        {
            SynchronizeText();
            return _buffer.Body;
        }
    }

    internal string Text
    {
        get => InputBox.Text ?? string.Empty;
        set => InputBox.Text = value;
    }

    internal int SelectionStart
    {
        get => InputBox.SelectionStart;
        set => InputBox.SelectionStart = value;
    }

    internal int SelectionEnd
    {
        get => InputBox.SelectionEnd;
        set => InputBox.SelectionEnd = value;
    }

    internal InCanvasEditorTextSelection Selection =>
        new(SelectionStart, SelectionEnd);

    internal bool FocusEditor() => InputBox.Focus();

    internal InCanvasTableCellRichTextEditPlan CurrentPlan()
    {
        SynchronizeText();
        return _buffer.Plan(Selection);
    }

    internal bool ToggleTextFormat(TableCellTextFormatKind kind) =>
        ApplyMutation(() => _buffer.ToggleTextFormat(kind, Selection));

    internal bool ApplyFontFamily(string? fontFamily) =>
        ApplyMutation(() => _buffer.ApplyValueFormat(
            TableCellTextValueFormatKind.FontFamily,
            fontFamily,
            Selection));

    internal bool ApplyFontSize(double? sizePt) =>
        ApplyMutation(() => _buffer.ApplyValueFormat(
            TableCellTextValueFormatKind.FontSize,
            sizePt,
            Selection));

    internal bool ApplyColor(ThemeAwareColor? color) =>
        ApplyMutation(() => _buffer.ApplyValueFormat(
            TableCellTextValueFormatKind.Color,
            color,
            Selection));

    internal bool ApplyParagraphAlignment(TextAlign alignment) =>
        ApplyMutation(() => _buffer.ApplyParagraphAlignment(alignment, Selection));

    internal bool ToggleParagraphBullets() =>
        ApplyMutation(() => _buffer.ToggleParagraphBullets(Selection));

    internal bool ToggleParagraphNumbering() =>
        ApplyMutation(() => _buffer.ToggleParagraphNumbering(Selection));

    internal bool ApplyParagraphListPreset(TableCellListPresetDescriptor preset) =>
        ApplyMutation(() => _buffer.ApplyParagraphListPreset(preset, Selection));

    internal bool ApplyParagraphPictureBullet(PresentationPictureBulletPayload payload) =>
        ApplyMutation(() => _buffer.ApplyParagraphPictureBullet(payload, Selection));

    internal bool ApplyParagraphIndent(bool increase) =>
        ApplyMutation(() => _buffer.ApplyParagraphIndent(increase, Selection));

    internal void ApplyPlanMetadata(
        InCanvasTableCellRichTextEditPlan plan,
        string richClass,
        string mixedClass)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Tag = plan;
        InputBox.Tag = plan;
        Classes.Set(richClass, plan.HasRichFormatting);
        Classes.Set(mixedClass, plan.HasMixedFormatting);
        InputBox.Classes.Set(richClass, plan.HasRichFormatting);
        InputBox.Classes.Set(mixedClass, plan.HasMixedFormatting);
        ApplyInputMetrics(plan.SuggestedEditorStyle);
        RenderBody();
    }

    private bool ApplyMutation(Func<bool> mutate)
    {
        SynchronizeText();
        int selectionStart = SelectionStart;
        int selectionEnd = SelectionEnd;
        if (!mutate())
            return false;

        RenderBody();
        SelectionStart = selectionStart;
        SelectionEnd = selectionEnd;
        FocusEditor();
        return true;
    }

    private void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_synchronizing)
            return;

        _buffer.ReplacePlainText(InputBox.Text);
        RenderBody();
    }

    private void SynchronizeText() =>
        _buffer.ReplacePlainText(InputBox.Text);

    private void RenderBody()
    {
        _synchronizing = true;
        try
        {
            var body = _buffer.Body;
            _richTextView.UpdateBody(
                body,
                InputBox.FontFamily.Name,
                InputBox.FontSize * (72.0 / 96.0));
            UpdateSurfaceSelection();
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private void OnInputPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(InputBox).Properties.IsLeftButtonPressed)
            return;

        InputBox.Focus();
        int logicalPosition = _richTextView.HitTestLogicalPosition(e.GetPosition(_richTextView));
        if (e.ClickCount >= 3)
        {
            SelectParagraph(logicalPosition);
        }
        else if (e.ClickCount == 2)
        {
            SelectWord(logicalPosition);
        }
        else if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            _pointerSelectionAnchor = CurrentSelectionAnchor();
            InputBox.SelectionStart = _pointerSelectionAnchor;
            InputBox.SelectionEnd = logicalPosition;
        }
        else
        {
            _pointerSelectionAnchor = logicalPosition;
            InputBox.SelectionStart = logicalPosition;
            InputBox.SelectionEnd = logicalPosition;
        }

        e.Pointer.Capture(InputBox);
        e.Handled = true;
        UpdateSurfaceSelection();
    }

    private void OnInputPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Captured != InputBox
            || !e.GetCurrentPoint(InputBox).Properties.IsLeftButtonPressed)
            return;

        int logicalPosition = _richTextView.HitTestLogicalPosition(e.GetPosition(_richTextView));
        InputBox.SelectionStart = _pointerSelectionAnchor;
        InputBox.SelectionEnd = logicalPosition;
        e.Handled = true;
        UpdateSurfaceSelection();
    }

    private void OnInputPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured != InputBox)
            return;
        e.Pointer.Capture(null);
        e.Handled = true;
        UpdateSurfaceSelection();
    }

    private void OnInputNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0)
            return;

        int target = e.Key switch
        {
            Key.Up => _richTextView.MoveCaretVertically(InputBox.CaretIndex, -1),
            Key.Down => _richTextView.MoveCaretVertically(InputBox.CaretIndex, 1),
            Key.Home => _richTextView.MoveCaretToVisualLineBoundary(InputBox.CaretIndex, end: false),
            Key.End => _richTextView.MoveCaretToVisualLineBoundary(InputBox.CaretIndex, end: true),
            _ => -1,
        };
        if (target < 0)
            return;

        if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            _pointerSelectionAnchor = CurrentSelectionAnchor();
            InputBox.SelectionStart = _pointerSelectionAnchor;
            InputBox.SelectionEnd = target;
        }
        else
        {
            _pointerSelectionAnchor = target;
            InputBox.SelectionStart = target;
            InputBox.SelectionEnd = target;
        }

        e.Handled = true;
        UpdateSurfaceSelection();
    }

    private void SelectWord(int logicalPosition)
    {
        string text = Text;
        if (text.Length == 0)
            return;
        int index = Math.Clamp(logicalPosition, 0, text.Length - 1);
        if (char.IsWhiteSpace(text[index]))
        {
            InputBox.SelectionStart = index;
            InputBox.SelectionEnd = Math.Min(text.Length, index + 1);
            _pointerSelectionAnchor = index;
            return;
        }

        int start = index;
        int end = index + 1;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            start--;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;
        InputBox.SelectionStart = start;
        InputBox.SelectionEnd = end;
        _pointerSelectionAnchor = start;
    }

    private void SelectParagraph(int logicalPosition)
    {
        string text = Text;
        int position = Math.Clamp(logicalPosition, 0, text.Length);
        int start = position;
        int end = position;
        while (start > 0 && text[start - 1] != '\n')
            start--;
        while (end < text.Length && text[end] != '\n')
            end++;
        InputBox.SelectionStart = start;
        InputBox.SelectionEnd = end;
        _pointerSelectionAnchor = start;
    }

    private void UpdateSurfaceSelection()
    {
        _richTextView.UpdateSelection(
            SelectionStart,
            SelectionEnd,
            InputBox.IsFocused);
    }

    private int CurrentSelectionAnchor()
    {
        if (InputBox.SelectionStart == InputBox.SelectionEnd)
            return InputBox.CaretIndex;
        return InputBox.CaretIndex == InputBox.SelectionStart
            ? InputBox.SelectionEnd
            : InputBox.SelectionStart;
    }

    private void ApplyInputMetrics(InCanvasEditorTextStyleState style)
    {
        if (!string.IsNullOrWhiteSpace(style.FontFamily))
            InputBox.FontFamily = new FontFamily(style.FontFamily);
        if (style.FontSizePt is { } fontSizePt)
            InputBox.FontSize = fontSizePt * (96.0 / 72.0);
        InputBox.FontWeight = style.Bold == true ? FontWeight.Bold : FontWeight.Normal;
        InputBox.FontStyle = style.Italic == true ? FontStyle.Italic : FontStyle.Normal;
        InputBox.Classes.Set("freep-rich-editor-underline", style.Underline == true);
        InputBox.Classes.Set("freep-shape-underline", style.Underline == true);
        InputBox.Classes.Set("freep-table-cell-underline", style.Underline == true);
        InputBox.BorderThickness = style.Underline == true
            ? new Thickness(1.5, 1.5, 1.5, 3.0)
            : new Thickness(1.5);
    }
}
