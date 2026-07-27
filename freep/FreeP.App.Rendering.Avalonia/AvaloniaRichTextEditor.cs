using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
    private static readonly DataFormat<byte[]> RichTextFormat =
        DataFormat.CreateBytesApplicationFormat(PresentationClipboardFormats.RichText);
    private static readonly DataFormat<byte[]> RichTextPlatformFormat =
        DataFormat.CreateBytesPlatformFormat(PresentationClipboardFormats.RichText);

    private readonly InCanvasRichTextEditBuffer _buffer;
    private readonly AvaloniaRichTextEditingSurface _richTextView;
    private readonly string _fallbackFontFamily;
    private readonly double _fallbackFontSizePt;
    private bool _synchronizing;
    private int _pointerSelectionAnchor;

    internal AvaloniaRichTextEditor(
        TextBody? body,
        byte backgroundAlpha,
        string fallbackFontFamily = InCanvasRichTextEditorDefaults.FallbackFontFamily,
        double fallbackFontSizePt = InCanvasRichTextEditorDefaults.ShapeFallbackFontSizePt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackFontFamily);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fallbackFontSizePt);

        _buffer = new InCanvasRichTextEditBuffer(body);
        _fallbackFontFamily = fallbackFontFamily;
        _fallbackFontSizePt = fallbackFontSizePt;
        _richTextView = new AvaloniaRichTextEditingSurface();
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
            Opacity = 0,
        };
        AutomationProperties.SetAutomationId(InputBox, "FreePRichTextEditorInput");

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

    internal InCanvasRichClipboardPayload CreateClipboardPayload()
    {
        SynchronizeText();
        return _buffer.CreateClipboardPayload(Selection);
    }

    internal async Task<bool> CopySelectionAsync() =>
        await WriteRichClipboardAsync(CreateClipboardPayload());

    internal async Task<bool> CutSelectionAsync()
    {
        if (Selection.IsCollapsed)
            return false;

        if (!await WriteRichClipboardAsync(CreateClipboardPayload()))
            return false;
        int caret;
        _buffer.ReplaceSelectionWithPlainText(Selection, string.Empty, out caret);
        ApplyBufferText(caret);
        return true;
    }

    internal async Task<bool> PasteClipboardAsync()
    {
        var clipboard = TopLevel.GetTopLevel(InputBox)?.Clipboard;
        if (clipboard is null)
            return false;

        using var transfer = await clipboard.TryGetDataAsync();
        if (transfer is null)
            return false;

        byte[]? richBytes = await TryGetValueAsync(
            transfer,
            OperatingSystem.IsWindows() ? RichTextPlatformFormat : RichTextFormat);
        richBytes ??= await TryGetValueAsync(
            transfer,
            OperatingSystem.IsWindows() ? RichTextFormat : RichTextPlatformFormat);
        var payload = InCanvasRichClipboardPlanner.Deserialize(richBytes);
        if (payload is not null)
        {
            _buffer.ApplyClipboardPayload(payload, Selection, out var caret);
            ApplyBufferText(caret);
            return true;
        }

        string? text = null;
        try { text = await transfer.TryGetTextAsync(); }
        catch { }
        if (text is null)
            return false;

        _buffer.ReplaceSelectionWithPlainText(Selection, text, out var textCaret);
        ApplyBufferText(textCaret);
        return true;
    }

    internal InCanvasTableCellRichTextEditPlan CurrentPlan()
    {
        SynchronizeText();
        return _buffer.Plan(Selection);
    }

    internal Hyperlink? SelectedRunHyperlink()
    {
        SynchronizeText();
        return _buffer.GetSelectedRunHyperlink(Selection);
    }

    internal bool ApplyHyperlink(Hyperlink? hyperlink) =>
        ApplyMutation(() => _buffer.ApplyHyperlink(hyperlink, Selection));

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

    internal bool InsertSoftBreak()
    {
        SynchronizeText();
        int start = Math.Min(SelectionStart, SelectionEnd);
        int end = Math.Max(SelectionStart, SelectionEnd);
        if (!_buffer.InsertSoftBreak(Selection))
            return false;

        string current = Text;
        string updated = string.Concat(
            current.AsSpan(0, start),
            "\n",
            current.AsSpan(end));

        _synchronizing = true;
        try
        {
            InputBox.Text = updated;
        }
        finally
        {
            _synchronizing = false;
        }

        SelectionStart = start + 1;
        SelectionEnd = start + 1;
        RenderBody();
        FocusEditor();
        return true;
    }

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
                _fallbackFontFamily,
                _fallbackFontSizePt);
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

    private async void OnInputNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            switch (e.Key)
            {
                case Key.C:
                    e.Handled = true;
                    await CopySelectionAsync();
                    return;
                case Key.X:
                    e.Handled = true;
                    await CutSelectionAsync();
                    return;
                case Key.V:
                    e.Handled = true;
                    await PasteClipboardAsync();
                    return;
            }
        }

        if (e.Key == Key.Enter
            && (e.KeyModifiers & KeyModifiers.Shift) != 0
            && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
        {
            e.Handled = InsertSoftBreak();
            return;
        }

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

    private void ApplyBufferText(int caret)
    {
        _synchronizing = true;
        try
        {
            InputBox.Text = _buffer.PlainText;
        }
        finally
        {
            _synchronizing = false;
        }

        SelectionStart = Math.Clamp(caret, 0, InputBox.Text?.Length ?? 0);
        SelectionEnd = SelectionStart;
        RenderBody();
        FocusEditor();
    }

    private async Task<bool> WriteRichClipboardAsync(InCanvasRichClipboardPayload payload)
    {
        if (payload.PlainText.Length == 0)
            return false;

        var clipboard = TopLevel.GetTopLevel(InputBox)?.Clipboard;
        if (clipboard is null)
            return false;

        var item = new DataTransferItem();
        var bytes = InCanvasRichClipboardPlanner.Serialize(payload);
        if (OperatingSystem.IsWindows())
            item.Set(RichTextPlatformFormat, bytes);
        else
            item.Set(RichTextFormat, bytes);
        item.SetText(payload.PlainText);

        var transfer = new DataTransfer();
        transfer.Add(item);
        try
        {
            await clipboard.SetDataAsync(transfer);
            try { await clipboard.FlushAsync(); }
            catch { }
            return true;
        }
        catch
        {
            ((IDisposable)transfer).Dispose();
            return false;
        }
    }

    private static async Task<T?> TryGetValueAsync<T>(
        IAsyncDataTransfer transfer,
        DataFormat<T> format)
        where T : class
    {
        try { return await transfer.TryGetValueAsync(format); }
        catch { return null; }
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
