using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using AvaloniaRun = Avalonia.Controls.Documents.Run;
using ModelRun = FreeP.Core.Model.Run;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Rich in-canvas editor for Avalonia. A native TextBox owns input, caret, selection, IME,
/// clipboard, and local text undo while a synchronized inline layer renders mixed model runs.
/// </summary>
internal sealed class AvaloniaRichTextEditor : Grid
{
    private readonly InCanvasRichTextEditBuffer _buffer;
    private readonly TextBlock _richTextView;
    private bool _synchronizing;

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
            CaretBrush = Brushes.Black,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(0x78, 0xAD, 0xD6, 0xFF)),
            SelectionForegroundBrush = Brushes.Transparent,
        };
        AutomationProperties.SetAutomationId(InputBox, "FreePRichTextEditorInput");

        _richTextView = new TextBlock
        {
            Margin = new Thickness(4, 3, 4, 3),
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false,
        };
        AutomationProperties.SetAccessibilityView(_richTextView, AccessibilityView.Raw);

        Children.Add(_richTextView);
        Children.Add(InputBox);

        InputBox.TextChanged += OnInputTextChanged;
        RenderBody();
    }

    internal TextBox InputBox { get; }

    internal TextBlock RichTextView => _richTextView;

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
            _richTextView.Inlines!.Clear();
            var body = _buffer.Body;
            for (int paragraphIndex = 0; paragraphIndex < body.Paragraphs.Count; paragraphIndex++)
            {
                if (paragraphIndex > 0)
                    _richTextView.Inlines.Add(new LineBreak());

                foreach (var run in body.Paragraphs[paragraphIndex].Runs)
                    _richTextView.Inlines.Add(CreateInline(run));
            }
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private static AvaloniaRun CreateInline(ModelRun run)
    {
        var inline = new AvaloniaRun
        {
            Text = run.Text,
            FontWeight = run.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = run.Italic ? FontStyle.Italic : FontStyle.Normal,
            BaselineAlignment = run.BaselineOffset switch
            {
                > 0 => BaselineAlignment.Superscript,
                < 0 => BaselineAlignment.Subscript,
                _ => BaselineAlignment.Baseline,
            },
        };

        if (!string.IsNullOrWhiteSpace(run.FontFamily))
            inline.FontFamily = new FontFamily(run.FontFamily);
        if (run.FontSizePt is { } fontSizePt)
            inline.FontSize = fontSizePt;
        if (run.Color is { } color)
        {
            inline.Foreground = new SolidColorBrush(Color.FromRgb(
                color.Resolved.R,
                color.Resolved.G,
                color.Resolved.B));
        }

        if (run.Underline || run.Strikethrough)
        {
            var decorations = new TextDecorationCollection();
            if (run.Underline)
                decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
            if (run.Strikethrough)
                decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
            inline.TextDecorations = decorations;
        }

        return inline;
    }

    private void ApplyInputMetrics(InCanvasEditorTextStyleState style)
    {
        if (!string.IsNullOrWhiteSpace(style.FontFamily))
            InputBox.FontFamily = new FontFamily(style.FontFamily);
        if (style.FontSizePt is { } fontSizePt)
            InputBox.FontSize = fontSizePt;
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
