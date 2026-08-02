using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Rich in-canvas editor for Avalonia. A native TextBox owns input, IME, clipboard, and local
/// text undo while a synchronized layout surface renders mixed runs, selection, and caret.
/// </summary>
internal sealed class AvaloniaRichTextEditor : Grid
{
    internal static readonly DataFormat<byte[]> RichTextFormat =
        DataFormat.CreateBytesApplicationFormat(PresentationClipboardFormats.RichText);
    internal static readonly DataFormat<byte[]> RichTextPlatformFormat =
        DataFormat.CreateBytesPlatformFormat(PresentationClipboardFormats.RichText);
    internal static readonly DataFormat<byte[]> ExternalRtfWindowsFormat =
        DataFormat.CreateBytesPlatformFormat(PresentationClipboardFormats.WindowsRtf);
    internal static readonly DataFormat<byte[]> ExternalRtfLinuxFormat =
        DataFormat.CreateBytesPlatformFormat(PresentationClipboardFormats.LinuxRtf);
    internal static readonly DataFormat<byte[]> ExternalXamlPackageWindowsFormat =
        DataFormat.CreateBytesPlatformFormat(PresentationClipboardFormats.WindowsXamlPackage);
    internal static readonly DataFormat<byte[]> ExternalXamlPackageLinuxFormat =
        DataFormat.CreateBytesPlatformFormat(PresentationClipboardFormats.LinuxXamlPackage);

    private readonly InCanvasRichTextEditBuffer _buffer;
    private readonly AvaloniaRichTextEditingSurface _richTextView;
    private readonly string _fallbackFontFamily;
    private readonly double _fallbackFontSizePt;
    private bool _synchronizing;
    private int _pointerSelectionAnchor;
    private int? _keyboardSelectionAnchor;
    private int? _keyboardSelectionCaret;
    private double? _preferredVerticalX;
    private int? _preferredVerticalLineIndex;
    private readonly DispatcherTimer _pointerAutoScrollTimer;
    private Point _lastPointerPosition;
    private bool _pointerDragActive;
    private AvaloniaRichTextEditor? _activeInlineTableCellEditor;
    private AvaloniaRichTextEditingSurface.InlineTableCellHit? _activeInlineTableCellHit;
    private readonly Func<bool, bool>? _navigateInlineTableCell;
    private readonly List<PendingInlineTableRows> _pendingInlineTableRows = new();

    internal AvaloniaRichTextEditor(
        TextBody? body,
        byte backgroundAlpha,
        string fallbackFontFamily = InCanvasRichTextEditorDefaults.FallbackFontFamily,
        double fallbackFontSizePt = InCanvasRichTextEditorDefaults.ShapeFallbackFontSizePt,
        Func<bool, bool>? navigateInlineTableCell = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackFontFamily);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fallbackFontSizePt);

        _buffer = new InCanvasRichTextEditBuffer(body);
        _fallbackFontFamily = fallbackFontFamily;
        _fallbackFontSizePt = fallbackFontSizePt;
        _navigateInlineTableCell = navigateInlineTableCell;
        _richTextView = new AvaloniaRichTextEditingSurface();
        var textWrapping = body?.Wrap == false
            ? TextWrapping.NoWrap
            : TextWrapping.Wrap;
        _pointerAutoScrollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(30),
            IsEnabled = false,
        };
        _pointerAutoScrollTimer.Tick += (_, _) => ApplyPointerSelectionAtLastPosition();
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.FromArgb(backgroundAlpha, 0xFF, 0xFF, 0xFF));

        InputBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = textWrapping,
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
        InputBox.PointerCaptureLost += OnInputPointerCaptureLost;
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
            CommitInlineTableCellEdit(focusParent: false);
            SynchronizeText();
            var body = _buffer.Body;
            ApplyPendingInlineTableRows(body);
            return body;
        }
    }

    /// <summary>
    /// Returns the focused nested editor when a cell is being edited. Ribbon and host
    /// adapters call the outer editor, so this keeps those commands on the same target
    /// as keyboard input without exposing the temporary child control.
    /// </summary>
    internal AvaloniaRichTextEditor EditingTarget =>
        _activeInlineTableCellEditor?.EditingTarget ?? this;

    internal string Text
    {
        get => EditingTarget.InputBox.Text ?? string.Empty;
        set => EditingTarget.InputBox.Text = value;
    }

    internal int SelectionStart
    {
        get => EditingTarget.InputBox.SelectionStart;
        set => EditingTarget.InputBox.SelectionStart = value;
    }

    internal int SelectionEnd
    {
        get => EditingTarget.InputBox.SelectionEnd;
        set => EditingTarget.InputBox.SelectionEnd = value;
    }

    internal InCanvasEditorTextSelection Selection =>
        new(SelectionStart, SelectionEnd);

    internal double? PreferredVerticalCaretX => _preferredVerticalX;

    internal bool FocusEditor() => EditingTarget.InputBox.Focus();

    internal InCanvasRichClipboardPayload CreateClipboardPayload()
    {
        var target = EditingTarget;
        if (!ReferenceEquals(target, this))
            return target.CreateClipboardPayload();
        SynchronizeText();
        return _buffer.CreateClipboardPayload(Selection);
    }

    internal async Task<bool> CopySelectionAsync() =>
        !ReferenceEquals(EditingTarget, this)
            ? await EditingTarget.CopySelectionAsync()
            : await WriteRichClipboardAsync(CreateClipboardPayload());

    internal async Task<bool> CutSelectionAsync()
    {
        if (!ReferenceEquals(EditingTarget, this))
            return await EditingTarget.CutSelectionAsync();

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
        if (!ReferenceEquals(EditingTarget, this))
            return await EditingTarget.PasteClipboardAsync();

        var clipboard = TopLevel.GetTopLevel(InputBox)?.Clipboard;
        if (clipboard is null)
            return false;

        using var transfer = await clipboard.TryGetDataAsync();
        if (transfer is null)
            return false;

        return await PasteDataTransferAsync(transfer);
    }

    internal async Task<bool> PasteDataTransferAsync(IAsyncDataTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        if (!ReferenceEquals(EditingTarget, this))
            return await EditingTarget.PasteDataTransferAsync(transfer);

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

        byte[]? xamlBytes = await TryGetValueAsync(
            transfer,
            OperatingSystem.IsWindows()
                ? ExternalXamlPackageWindowsFormat
                : ExternalXamlPackageLinuxFormat);
        xamlBytes ??= await TryGetValueAsync(
            transfer,
            OperatingSystem.IsWindows()
                ? ExternalXamlPackageLinuxFormat
                : ExternalXamlPackageWindowsFormat);
        var xamlPayload = ExternalXamlClipboardPlanner.TryParseXamlPackage(xamlBytes);
        if (xamlPayload is not null)
        {
            _buffer.ApplyClipboardPayload(xamlPayload, Selection, out var xamlCaret);
            ApplyBufferText(xamlCaret);
            return true;
        }

        byte[]? rtfBytes = await TryGetValueAsync(
            transfer,
            OperatingSystem.IsWindows() ? ExternalRtfWindowsFormat : ExternalRtfLinuxFormat);
        rtfBytes ??= await TryGetValueAsync(
            transfer,
            OperatingSystem.IsWindows() ? ExternalRtfLinuxFormat : ExternalRtfWindowsFormat);
        var externalPayload = ExternalRichTextClipboardPlanner.TryParseRtf(rtfBytes);
        if (externalPayload is not null)
        {
            _buffer.ApplyClipboardPayload(externalPayload, Selection, out var rtfCaret);
            ApplyBufferText(rtfCaret);
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
        if (!ReferenceEquals(EditingTarget, this))
            return EditingTarget.CurrentPlan();

        SynchronizeText();
        return _buffer.Plan(Selection);
    }

    internal Hyperlink? SelectedRunHyperlink()
    {
        if (!ReferenceEquals(EditingTarget, this))
            return EditingTarget.SelectedRunHyperlink();

        SynchronizeText();
        return _buffer.GetSelectedRunHyperlink(Selection);
    }

    internal bool TryActivateInlineOleObject(Func<int, bool> tryActivateAt)
    {
        ArgumentNullException.ThrowIfNull(tryActivateAt);
        if (!ReferenceEquals(EditingTarget, this))
            return EditingTarget.TryActivateInlineOleObject(tryActivateAt);

        SynchronizeText();
        int position = Math.Min(SelectionStart, SelectionEnd);
        return tryActivateAt(position)
            || (position > 0 && tryActivateAt(position - 1));
    }

    internal bool UpdateInlineOleObjectAt(
        int logicalPosition,
        IReadOnlyList<byte> embeddedBytes) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.UpdateInlineOleObjectAt(logicalPosition, embeddedBytes)
            : _buffer.UpdateInlineOleObjectAt(logicalPosition, embeddedBytes);

    internal bool ApplyHyperlink(Hyperlink? hyperlink) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyHyperlink(hyperlink)
            : ApplyMutation(() => _buffer.ApplyHyperlink(hyperlink, Selection));

    internal bool ToggleTextFormat(TableCellTextFormatKind kind) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ToggleTextFormat(kind)
            : ApplyMutation(() => _buffer.ToggleTextFormat(kind, Selection));

    internal bool ApplyFontFamily(string? fontFamily) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyFontFamily(fontFamily)
            : ApplyMutation(() => _buffer.ApplyValueFormat(
            TableCellTextValueFormatKind.FontFamily,
            fontFamily,
            Selection));

    internal bool ApplyFontSize(double? sizePt) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyFontSize(sizePt)
            : ApplyMutation(() => _buffer.ApplyValueFormat(
            TableCellTextValueFormatKind.FontSize,
            sizePt,
            Selection));

    internal bool ApplyColor(ThemeAwareColor? color) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyColor(color)
            : ApplyMutation(() => _buffer.ApplyValueFormat(
            TableCellTextValueFormatKind.Color,
            color,
            Selection));

    internal bool ApplyParagraphAlignment(TextAlign alignment) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyParagraphAlignment(alignment)
            : ApplyMutation(() => _buffer.ApplyParagraphAlignment(alignment, Selection));

    internal bool ToggleParagraphBullets() =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ToggleParagraphBullets()
            : ApplyMutation(() => _buffer.ToggleParagraphBullets(Selection));

    internal bool ToggleParagraphNumbering() =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ToggleParagraphNumbering()
            : ApplyMutation(() => _buffer.ToggleParagraphNumbering(Selection));

    internal bool ApplyParagraphListPreset(TableCellListPresetDescriptor preset) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyParagraphListPreset(preset)
            : ApplyMutation(() => _buffer.ApplyParagraphListPreset(preset, Selection));

    internal bool ApplyParagraphPictureBullet(PresentationPictureBulletPayload payload) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyParagraphPictureBullet(payload)
            : ApplyMutation(() => _buffer.ApplyParagraphPictureBullet(payload, Selection));

    internal bool ApplyParagraphIndent(bool increase) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyParagraphIndent(increase)
            : ApplyMutation(() => _buffer.ApplyParagraphIndent(increase, Selection));

    internal bool InsertSoftBreak()
    {
        if (!ReferenceEquals(EditingTarget, this))
            return EditingTarget.InsertSoftBreak();

        ResetVerticalNavigation();
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
        if (!ReferenceEquals(EditingTarget, this))
        {
            EditingTarget.ApplyPlanMetadata(plan, richClass, mixedClass);
            return;
        }

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
        ResetVerticalNavigation();
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

        ResetVerticalNavigation();
        _keyboardSelectionAnchor = null;
        _keyboardSelectionCaret = null;
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
            ApplyPendingInlineTableRows(body);
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

        ResetVerticalNavigation();
        InputBox.Focus();
        var point = e.GetPosition(_richTextView);
        if (e.ClickCount >= 2 && TryBeginInlineTableCellEdit(point))
        {
            e.Handled = true;
            return;
        }

        int logicalPosition = _richTextView.HitTestLogicalPosition(point);
        if (e.ClickCount >= 2 && TryActivateInlineOleAt(logicalPosition))
        {
            e.Handled = true;
            return;
        }

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
            _keyboardSelectionAnchor = _pointerSelectionAnchor;
            _keyboardSelectionCaret = logicalPosition;
            ApplyPointerSelection(_pointerSelectionAnchor, logicalPosition);
        }
        else
        {
            _pointerSelectionAnchor = logicalPosition;
            _keyboardSelectionAnchor = logicalPosition;
            _keyboardSelectionCaret = logicalPosition;
            ApplyPointerSelection(logicalPosition, logicalPosition);
        }

        _lastPointerPosition = e.GetPosition(_richTextView);
        _pointerDragActive = true;
        _pointerAutoScrollTimer.Stop();
        e.Pointer.Capture(InputBox);
        e.Handled = true;
        UpdateSurfaceSelection();
    }

    private bool TryBeginInlineTableCellEdit(Point point)
    {
        if (!_richTextView.TryHitTestInlineTableCell(point, out var hit))
            return false;

        return BeginInlineTableCellEdit(hit);
    }

    private bool NavigateInlineTableCell(bool backwards)
    {
        if (_activeInlineTableCellHit is not { } current)
            return false;

        if (_richTextView.TryFindAdjacentInlineTableCell(
            current,
            backwards,
            out var next))
            return BeginInlineTableCellEdit(next);

        return backwards || AppendInlineTableRow(current);
    }

    private bool AppendInlineTableRow(
        AvaloniaRichTextEditingSurface.InlineTableCellHit current)
    {
        CommitInlineTableCellEdit(focusParent: false);
        var table = current.Table.Table;
        var row = InlineTableLogicalGridPlan.CreateAppendRow(table);

        var pending = _pendingInlineTableRows.FirstOrDefault(item =>
            item.LogicalPosition == current.LogicalPosition);
        if (pending is null)
        {
            pending = new PendingInlineTableRows(
                current.LogicalPosition,
                table.Rows.Count);
            _pendingInlineTableRows.Add(pending);
        }
        pending.Rows.Add(row);
        RenderBody();

        int targetRow = pending.FirstRowIndex + pending.Rows.Count - 1;
        return _richTextView.TryFindInlineTableCell(
                current,
                targetRow,
                0,
                out var next)
            && BeginInlineTableCellEdit(next);
    }

    private void OnInlineTableCellEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        CommitInlineTableCellEdit(focusParent: false);
    }

    private void CommitInlineTableCellEdit(bool focusParent)
    {
        var cellEditor = _activeInlineTableCellEditor;
        var hit = _activeInlineTableCellHit;
        if (cellEditor is null || hit is null)
            return;

        _activeInlineTableCellEditor = null;
        _activeInlineTableCellHit = null;
        cellEditor.InputBox.LostFocus -= OnInlineTableCellEditorLostFocus;
        var editedBody = cellEditor.EditedBody;
        var pending = _pendingInlineTableRows.FirstOrDefault(item =>
            item.LogicalPosition == hit.Value.LogicalPosition
            && hit.Value.RowIndex >= item.FirstRowIndex);
        if (pending is not null)
        {
            int pendingRowIndex = hit.Value.RowIndex - pending.FirstRowIndex;
            if (pending.Rows.ElementAtOrDefault(pendingRowIndex)?
                    .Cells.ElementAtOrDefault(hit.Value.SourceCellIndex) is { } cell)
                cell.TextBody = editedBody;
        }
        else
        {
            _buffer.UpdateInlineTableCellAt(
                hit.Value.LogicalPosition,
                hit.Value.RowIndex,
                hit.Value.SourceCellIndex,
                editedBody);
        }
        Children.Remove(cellEditor);
        RenderBody();
        if (focusParent)
            FocusEditor();
    }

    private bool BeginInlineTableCellEdit(
        AvaloniaRichTextEditingSurface.InlineTableCellHit hit)
    {
        if (hit.Table.Table.Rows.ElementAtOrDefault(hit.RowIndex)?
                .Cells.ElementAtOrDefault(hit.SourceCellIndex)?.TextBody is not { } body)
        {
            return false;
        }

        CommitInlineTableCellEdit(focusParent: false);
        var cellEditor = new AvaloniaRichTextEditor(
            body,
            backgroundAlpha: 0,
            fallbackFontFamily: _fallbackFontFamily,
            fallbackFontSizePt: _fallbackFontSizePt,
            navigateInlineTableCell: NavigateInlineTableCell)
        {
            Width = Math.Max(1, hit.Bounds.Width),
            Height = Math.Max(1, hit.Bounds.Height),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(
                hit.Bounds.X - _richTextView.ScrollOffsetX,
                hit.Bounds.Y - _richTextView.ScrollOffsetY,
                0,
                0),
        };
        cellEditor.SetValue(Panel.ZIndexProperty, 2);
        cellEditor.InputBox.LostFocus += OnInlineTableCellEditorLostFocus;
        _activeInlineTableCellEditor = cellEditor;
        _activeInlineTableCellHit = hit;
        Children.Add(cellEditor);
        cellEditor.FocusEditor();
        return true;
    }

    private void ApplyPendingInlineTableRows(TextBody body)
    {
        foreach (var pending in _pendingInlineTableRows)
        {
            if (!TryFindInlineTable(body, pending.LogicalPosition, out var table))
                continue;

            for (int index = 0; index < pending.Rows.Count; index++)
            {
                int rowIndex = pending.FirstRowIndex + index;
                if (rowIndex < table.Rows.Count)
                    continue;

                table.Rows.Add(CloneTableRow(pending.Rows[index]));
            }
        }
    }

    private static bool TryFindInlineTable(
        TextBody body,
        int logicalPosition,
        out TableShape table)
    {
        int position = 0;
        foreach (var paragraph in body.Paragraphs)
        {
            foreach (var run in paragraph.Runs)
            {
                int length = Math.Max(1, run.Text?.Length ?? 0);
                if (run.InlineTable is { } inlineTable
                    && logicalPosition >= position
                    && logicalPosition < position + length)
                {
                    table = inlineTable.Table;
                    return true;
                }

                position += run.Text?.Length ?? 0;
            }

            position++;
        }

        table = null!;
        return false;
    }

    private static TableRow CloneTableRow(TableRow source)
    {
        var row = new TableRow
        {
            HeightEmu = source.HeightEmu,
            HeightRule = source.HeightRule,
            HorizontalAlignment = source.HorizontalAlignment,
        };
        foreach (var sourceCell in source.Cells)
        {
            row.Cells.Add(new TableCell
            {
                TextBody = sourceCell.TextBody is null
                    ? null
                    : new InCanvasRichTextEditBuffer(sourceCell.TextBody).Body,
                Fill = sourceCell.Fill,
                Borders = sourceCell.Borders,
                GridSpan = sourceCell.GridSpan,
                RowSpan = sourceCell.RowSpan,
                HMerge = sourceCell.HMerge,
                VMerge = sourceCell.VMerge,
                InsetLeftPt = sourceCell.InsetLeftPt,
                InsetRightPt = sourceCell.InsetRightPt,
                InsetTopPt = sourceCell.InsetTopPt,
                InsetBottomPt = sourceCell.InsetBottomPt,
                Anchor = sourceCell.Anchor,
            });
        }

        return row;
    }

    private sealed class PendingInlineTableRows(int logicalPosition, int firstRowIndex)
    {
        internal int LogicalPosition { get; } = logicalPosition;
        internal int FirstRowIndex { get; } = firstRowIndex;
        internal List<TableRow> Rows { get; } = new();
    }

    private bool TryActivateInlineOleAt(int logicalPosition)
    {
        SynchronizeText();
        if (!_buffer.TryGetInlineOleObjectAt(logicalPosition, out var inlineObject)
            && (logicalPosition <= 0
                || !_buffer.TryGetInlineOleObjectAt(logicalPosition - 1, out inlineObject)))
        {
            return false;
        }

        return OleActivationService.TryActivate(inlineObject);
    }

    private void OnInputPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Captured != InputBox
            || !e.GetCurrentPoint(InputBox).Properties.IsLeftButtonPressed)
            return;

        _lastPointerPosition = e.GetPosition(_richTextView);
        ApplyPointerSelectionAtLastPosition();
        e.Handled = true;
        UpdateSurfaceSelection();
    }

    private void ApplyPointerSelectionAtLastPosition()
    {
        if (!_pointerDragActive)
            return;

        int direction = InCanvasRichTextPointerSelectionPlanner.ResolveVerticalEdgeDirection(
            _lastPointerPosition.Y,
            _richTextView.Bounds.Height);
        if (direction == 0)
        {
            _pointerAutoScrollTimer.Stop();
        }
        else
        {
            double previousOffset = _richTextView.ScrollOffsetY;
            double offset = InCanvasRichTextPointerSelectionPlanner.AdvanceVerticalScroll(
                previousOffset,
                _richTextView.ContentExtentHeight,
                _richTextView.Bounds.Height,
                direction);
            _richTextView.SetScrollOffset(offset);
            if (Math.Abs(offset - previousOffset) >= 0.01)
                _pointerAutoScrollTimer.Start();
            else
                _pointerAutoScrollTimer.Stop();
        }

        int logicalPosition = _richTextView.HitTestLogicalPosition(_lastPointerPosition);
        ApplyPointerSelection(_pointerSelectionAnchor, logicalPosition);
        _keyboardSelectionAnchor = _pointerSelectionAnchor;
        _keyboardSelectionCaret = logicalPosition;
        UpdateSurfaceSelection();
    }

    private void ApplyPointerSelection(int anchor, int caret)
    {
        var selection = InCanvasRichTextPointerSelectionPlanner.Plan(
            anchor,
            caret,
            Text.Length);
        InputBox.SelectionStart = selection.Start;
        InputBox.SelectionEnd = selection.End;
    }

    private void OnInputPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured != InputBox)
            return;
        _pointerDragActive = false;
        _pointerAutoScrollTimer.Stop();
        e.Pointer.Capture(null);
        e.Handled = true;
        UpdateSurfaceSelection();
    }

    private void OnInputPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _pointerDragActive = false;
        _pointerAutoScrollTimer.Stop();
    }

    private async void OnInputNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        bool control = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (e.Key == Key.Tab && _navigateInlineTableCell is not null)
        {
            e.Handled = _navigateInlineTableCell(shift);
            return;
        }

        if (control)
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
            && shift
            && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
        {
            e.Handled = InsertSoftBreak();
            return;
        }

        if ((e.KeyModifiers & (KeyModifiers.Alt | KeyModifiers.Meta)) != 0)
            return;

        if (e.Key is not (Key.Up or Key.Down))
            ResetVerticalNavigation();

        int target = e.Key switch
        {
            Key.Up => MoveCaretVertically(-1),
            Key.Down => MoveCaretVertically(1),
            Key.Left => InCanvasRichTextNavigationPlanner.MoveCaret(
                Text,
                InputBox.CaretIndex,
                InCanvasTextNavigationKey.Left,
                control),
            Key.Right => InCanvasRichTextNavigationPlanner.MoveCaret(
                Text,
                InputBox.CaretIndex,
                InCanvasTextNavigationKey.Right,
                control),
            Key.Home when control => InCanvasRichTextNavigationPlanner.MoveCaret(
                Text,
                InputBox.CaretIndex,
                InCanvasTextNavigationKey.Home,
                control: true),
            Key.End when control => InCanvasRichTextNavigationPlanner.MoveCaret(
                Text,
                InputBox.CaretIndex,
                InCanvasTextNavigationKey.End,
                control: true),
            Key.Home => _richTextView.MoveCaretToVisualLineBoundary(InputBox.CaretIndex, end: false),
            Key.End => _richTextView.MoveCaretToVisualLineBoundary(InputBox.CaretIndex, end: true),
            _ => -1,
        };
        if (target < 0)
            return;

        if (shift)
        {
            if (!_keyboardSelectionAnchor.HasValue
                || !_keyboardSelectionCaret.HasValue
                || !SelectionMatches(
                    _keyboardSelectionAnchor.Value,
                    _keyboardSelectionCaret.Value))
            {
                _keyboardSelectionAnchor =
                    InCanvasRichTextNavigationPlanner.ResolveSelectionAnchor(
                        SelectionStart,
                        SelectionEnd,
                        InputBox.CaretIndex);
            }

            _pointerSelectionAnchor = _keyboardSelectionAnchor.Value;
            InputBox.SelectionStart = _keyboardSelectionAnchor.Value;
            InputBox.SelectionEnd = target;
            _keyboardSelectionCaret = target;
        }
        else
        {
            _pointerSelectionAnchor = target;
            _keyboardSelectionAnchor = target;
            _keyboardSelectionCaret = target;
            InputBox.SelectionStart = target;
            InputBox.SelectionEnd = target;
        }

        e.Handled = true;
        UpdateSurfaceSelection();
    }

    private int MoveCaretVertically(int lineDelta)
    {
        var move = _richTextView.MoveCaretVertically(
            InputBox.CaretIndex,
            lineDelta,
            _preferredVerticalX,
            _preferredVerticalLineIndex);
        _preferredVerticalX = move.PreferredX;
        _preferredVerticalLineIndex = move.VisualLineIndex;
        return move.LogicalPosition;
    }

    private void ResetVerticalNavigation()
    {
        _preferredVerticalX = null;
        _preferredVerticalLineIndex = null;
    }

    private void ApplyBufferText(int caret)
    {
        ResetVerticalNavigation();
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
            _keyboardSelectionAnchor = index;
            _keyboardSelectionCaret = index + 1;
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
        _keyboardSelectionAnchor = start;
        _keyboardSelectionCaret = end;
    }

    private void SelectParagraph(int logicalPosition)
    {
        string text = Text;
        var selection = InCanvasRichTextPointerSelectionPlanner.PlanParagraph(
            text,
            logicalPosition);
        InputBox.SelectionStart = selection.Start;
        InputBox.SelectionEnd = selection.End;
        _pointerSelectionAnchor = selection.Start;
        _keyboardSelectionAnchor = selection.Start;
        _keyboardSelectionCaret = selection.End;
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
        return InCanvasRichTextNavigationPlanner.ResolveSelectionAnchor(
            InputBox.SelectionStart,
            InputBox.SelectionEnd,
            InputBox.CaretIndex);
    }

    private bool SelectionMatches(int anchor, int caret)
    {
        int start = Math.Min(anchor, caret);
        int end = Math.Max(anchor, caret);
        return SelectionStart == start
            && SelectionEnd == end
            && InputBox.CaretIndex == caret;
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
