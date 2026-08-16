using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
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

    // The portable session owns the InCanvasRichTextEditBuffer and all model mutations.
    private readonly InCanvasRichTextEditSession _session;
    private readonly IPlatformClipboard _clipboard;
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
    private readonly Action? _cancelInlineTableCellEdit;
    private readonly List<PendingInlineTableRows> _pendingInlineTableRows = new();
    private readonly MenuItem _copyContextMenuItem;
    private readonly MenuItem _cutContextMenuItem;
    private readonly MenuItem _pasteContextMenuItem;
    private string? _lastWriteFailureMessage;

    internal AvaloniaRichTextEditor(
        TextBody? body,
        byte backgroundAlpha,
        string fallbackFontFamily = InCanvasRichTextEditorDefaults.FallbackFontFamily,
        double fallbackFontSizePt = InCanvasRichTextEditorDefaults.ShapeFallbackFontSizePt,
        Func<bool, bool>? navigateInlineTableCell = null,
        Action? cancelInlineTableCellEdit = null,
        IPlatformClipboard? clipboard = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackFontFamily);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fallbackFontSizePt);

        _session = InCanvasRichTextEditSession.Create(body);
        _fallbackFontFamily = fallbackFontFamily;
        _fallbackFontSizePt = fallbackFontSizePt;
        _navigateInlineTableCell = navigateInlineTableCell;
        _cancelInlineTableCellEdit = cancelInlineTableCellEdit;
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
            Text = _session.PlainText,
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
        _clipboard = clipboard ?? new AvaloniaPlatformClipboard(
            () => TopLevel.GetTopLevel(InputBox)?.Clipboard);
        AutomationProperties.SetAutomationId(
            InputBox,
            PresentationSemanticIdentityCatalog.RichTextEditorInputAutomationId);

        _copyContextMenuItem = new MenuItem
        {
            Header = PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCopyCommand),
        };
        _copyContextMenuItem.Click += async (_, _) => { _ = await CopySelectionAsync(); };
        _cutContextMenuItem = new MenuItem
        {
            Header = PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCutCommand),
        };
        _cutContextMenuItem.Click += async (_, _) => { _ = await CutSelectionAsync(); };
        _pasteContextMenuItem = new MenuItem
        {
            Header = PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditPasteCommand),
        };
        _pasteContextMenuItem.Click += async (_, _) => { _ = await PasteClipboardAsync(); };
        var selectAllContextMenuItem = new MenuItem
        {
            Header = PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditSelectAllCommand),
        };
        selectAllContextMenuItem.Click += (_, _) =>
        {
            SelectionStart = 0;
            SelectionEnd = Text.Length;
            FocusEditor();
        };
        var clipboardContextMenu = new ContextMenu();
        clipboardContextMenu.Items.Add(_cutContextMenuItem);
        clipboardContextMenu.Items.Add(_copyContextMenuItem);
        clipboardContextMenu.Items.Add(_pasteContextMenuItem);
        clipboardContextMenu.Items.Add(new Separator());
        clipboardContextMenu.Items.Add(selectAllContextMenuItem);
        clipboardContextMenu.Opening += (_, _) => UpdateClipboardContextMenuState();
        InputBox.ContextMenu = clipboardContextMenu;
        UpdateClipboardContextMenuState();

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
                UpdateClipboardContextMenuState();
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
            var body = _session.Body;
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
        set
        {
            EditingTarget.InputBox.Text = value;
            // Assigning InputBox.Text alone leaves the rich body on its previous content until the
            // TextChanged handler runs, so anything reading the body in the same turn (a paste, for
            // instance) operated on stale text and spliced the new content into the OLD string.
            // Resync eagerly; ReplacePlainText is idempotent when the handler follows.
            EditingTarget.SynchronizeText();
        }
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

    /// <summary>
    /// The message from the most recent failed OS-clipboard write by <see
    /// cref="CopySelectionAsync"/> or <see cref="CutSelectionAsync"/>, or null if the most recent
    /// write succeeded (or none has run yet). In-place shape/table-cell text editing used to
    /// swallow this failure silently; callers now read it after a false result so it reaches the
    /// user instead of the user believing the copy/cut succeeded.
    /// </summary>
    internal string? LastWriteFailureMessage =>
        !ReferenceEquals(EditingTarget, this) ? EditingTarget.LastWriteFailureMessage : _lastWriteFailureMessage;

    internal bool FocusEditor() => EditingTarget.InputBox.Focus();

    internal InCanvasRichClipboardPayload CreateClipboardPayload()
    {
        var target = EditingTarget;
        if (!ReferenceEquals(target, this))
            return target.CreateClipboardPayload();
        SynchronizeText();
        return _session.CreateClipboardPayload(Selection);
    }

    internal async Task<bool> CopySelectionAsync(CancellationToken cancellationToken = default) =>
        !ReferenceEquals(EditingTarget, this)
            ? await EditingTarget.CopySelectionAsync(cancellationToken)
            : await WriteRichClipboardAsync(CreateClipboardPayload(), cancellationToken);

    internal async Task<bool> CutSelectionAsync(CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(EditingTarget, this))
            return await EditingTarget.CutSelectionAsync(cancellationToken);

        if (Selection.IsCollapsed)
        {
            // Nothing selected to cut; not a write failure, so clear any stale error from an
            // earlier call rather than letting it resurface on an unrelated empty-selection cut.
            _lastWriteFailureMessage = null;
            return false;
        }

        if (!await WriteRichClipboardAsync(CreateClipboardPayload(), cancellationToken))
            return false;
        int caret;
        _session.ReplaceSelectionWithPlainText(Selection, string.Empty, out caret);
        ApplyBufferText(caret);
        return true;
    }

    internal async Task<bool> PasteClipboardAsync(CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(EditingTarget, this))
            return await EditingTarget.PasteClipboardAsync(cancellationToken);

        var read = await PresentationRichTextClipboardWorkflow.ReadAsync(
            _clipboard,
            cancellationToken);
        if (!read.IsSuccess || read.Value is null)
            return false;

        return ApplyClipboardContent(read.Value);
    }

    internal async Task<bool> PasteDataTransferAsync(IAsyncDataTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        if (!ReferenceEquals(EditingTarget, this))
            return await EditingTarget.PasteDataTransferAsync(transfer);

        var read = await AvaloniaPlatformClipboard.ReadDataTransferAsync(
            transfer,
            PresentationClipboardPlatformMapper.RichTextReadRequest);
        var content = read.IsSuccess && read.Value is not null
            ? PresentationClipboardPlatformMapper.FromPlatformContent(read.Value)
            : new PresentationClipboardContent();
        return ApplyClipboardContent(content);
    }

    private bool ApplyClipboardContent(PresentationClipboardContent content)
    {
        var resolution = InCanvasRichClipboardFormatResolver.Resolve(content);
        if (resolution.Payload is null)
            return false;

        int caret;
        if (resolution.Source == PresentationClipboardPasteSource.Text)
            _session.ReplaceSelectionWithPlainText(Selection, content.Text!, out caret);
        else
            _session.ApplyClipboardPayload(resolution.Payload, Selection, out caret);
        ApplyBufferText(caret);
        return true;
    }

    internal InCanvasTableCellRichTextEditPlan CurrentPlan()
    {
        if (!ReferenceEquals(EditingTarget, this))
            return EditingTarget.CurrentPlan();

        SynchronizeText();
        return _session.Plan(Selection);
    }

    internal Hyperlink? SelectedRunHyperlink()
    {
        if (!ReferenceEquals(EditingTarget, this))
            return EditingTarget.SelectedRunHyperlink();

        SynchronizeText();
        return _session.GetSelectedRunHyperlink(Selection);
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

    internal bool TryGetInlineOleHit(
        int logicalPosition,
        out AvaloniaInlineOleHostRequest hit)
    {
        foreach (var candidate in _richTextView.GetInlineOleHits())
        {
            if (candidate.LogicalPosition == logicalPosition)
            {
                hit = candidate;
                return true;
            }
        }

        hit = null!;
        return false;
    }

    internal bool UpdateInlineOleObjectAt(
        int logicalPosition,
        IReadOnlyList<byte> embeddedBytes) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.UpdateInlineOleObjectAt(logicalPosition, embeddedBytes)
            : _session.UpdateInlineOleObjectAt(logicalPosition, embeddedBytes);

    internal bool ApplyHyperlink(Hyperlink? hyperlink) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyHyperlink(hyperlink)
            : ApplyMutation(() => _session.ApplyHyperlink(hyperlink, Selection));

    internal bool ToggleTextFormat(TableCellTextFormatKind kind) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ToggleTextFormat(kind)
            : ApplyMutation(() => _session.ToggleTextFormat(kind, Selection));

    internal bool ApplyFontFamily(string? fontFamily) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyFontFamily(fontFamily)
            : ApplyMutation(() => _session.ApplyValueFormat(
            TableCellTextValueFormatKind.FontFamily,
            fontFamily,
            Selection));

    internal bool ApplyFontSize(double? sizePt) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyFontSize(sizePt)
            : ApplyMutation(() => _session.ApplyValueFormat(
            TableCellTextValueFormatKind.FontSize,
            sizePt,
            Selection));

    internal bool ApplyColor(ThemeAwareColor? color) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyColor(color)
            : ApplyMutation(() => _session.ApplyValueFormat(
            TableCellTextValueFormatKind.Color,
            color,
            Selection));

    internal bool ApplyParagraphAlignment(TextAlign alignment) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyParagraphAlignment(alignment)
            : ApplyMutation(() => _session.ApplyParagraphAlignment(alignment, Selection));

    internal bool ToggleParagraphBullets() =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ToggleParagraphBullets()
            : ApplyMutation(() => _session.ToggleParagraphBullets(Selection));

    internal bool ToggleParagraphNumbering() =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ToggleParagraphNumbering()
            : ApplyMutation(() => _session.ToggleParagraphNumbering(Selection));

    internal bool ApplyParagraphListPreset(TableCellListPresetDescriptor preset) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyParagraphListPreset(preset)
            : ApplyMutation(() => _session.ApplyParagraphListPreset(preset, Selection));

    internal bool ApplyParagraphPictureBullet(PresentationPictureBulletPayload payload) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyParagraphPictureBullet(payload)
            : ApplyMutation(() => _session.ApplyParagraphPictureBullet(payload, Selection));

    internal bool ApplyParagraphIndent(bool increase) =>
        !ReferenceEquals(EditingTarget, this)
            ? EditingTarget.ApplyParagraphIndent(increase)
            : ApplyMutation(() => _session.ApplyParagraphIndent(increase, Selection));

    internal bool InsertSoftBreak()
    {
        if (!ReferenceEquals(EditingTarget, this))
            return EditingTarget.InsertSoftBreak();

        ResetVerticalNavigation();
        SynchronizeText();
        int start = Math.Min(SelectionStart, SelectionEnd);
        int end = Math.Max(SelectionStart, SelectionEnd);
        if (!_session.InsertSoftBreak(Selection))
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
        _session.ReplacePlainText(InputBox.Text);
        UpdateClipboardContextMenuState();
        RenderBody();
    }

    private void UpdateClipboardContextMenuState()
    {
        var selection = Selection;
        bool hasSelection = !selection.IsCollapsed;
        _copyContextMenuItem.IsEnabled = hasSelection;
        _cutContextMenuItem.IsEnabled = hasSelection;
        _pasteContextMenuItem.IsEnabled = _clipboard.IsAvailable;
    }

    private void SynchronizeText() =>
        _session.ReplacePlainText(InputBox.Text);

    private void RenderBody()
    {
        _synchronizing = true;
        try
        {
            var body = _session.Body;
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
            _session.UpdateInlineTableCellAt(
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

    private void CancelInlineTableCellEdit()
    {
        var cellEditor = _activeInlineTableCellEditor;
        if (cellEditor is null)
            return;

        _activeInlineTableCellEditor = null;
        _activeInlineTableCellHit = null;
        cellEditor.InputBox.LostFocus -= OnInlineTableCellEditorLostFocus;
        Children.Remove(cellEditor);
        RenderBody();
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
            navigateInlineTableCell: NavigateInlineTableCell,
            cancelInlineTableCellEdit: CancelInlineTableCellEdit,
            clipboard: _clipboard)
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
        var initialSelection = TableCellEditPlanner.PlanInitialSelection(body);
        cellEditor.SelectionStart = initialSelection.Start;
        cellEditor.SelectionEnd = initialSelection.End;
        cellEditor.ApplyPlanMetadata(
            cellEditor.CurrentPlan(),
            "freep-table-cell-rich-editor",
            "freep-table-cell-mixed-formatting");
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

                table.Rows.Add(pending.Rows[index].Clone());
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

    private sealed class PendingInlineTableRows(int logicalPosition, int firstRowIndex)
    {
        internal int LogicalPosition { get; } = logicalPosition;
        internal int FirstRowIndex { get; } = firstRowIndex;
        internal List<TableRow> Rows { get; } = new();
    }

    private bool TryActivateInlineOleAt(int logicalPosition)
    {
        SynchronizeText();
        if (!_session.TryGetInlineOleObjectAt(logicalPosition, out var inlineObject)
            && (logicalPosition <= 0
                || !_session.TryGetInlineOleObjectAt(logicalPosition - 1, out inlineObject)))
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
        bool clampedAtExtent = false;
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
            {
                _pointerAutoScrollTimer.Start();
            }
            else
            {
                _pointerAutoScrollTimer.Stop();
                // Only when there was scrolling to exhaust. A document that fits the viewport never
                // advances either, and treating that as "dragged past the end" would clamp ordinary
                // drags inside short documents to the document end.
                clampedAtExtent =
                    _richTextView.ContentExtentHeight - _richTextView.Bounds.Height > 0.01;
            }
        }

        int logicalPosition = _richTextView.HitTestLogicalPosition(_lastPointerPosition);
        if (clampedAtExtent)
        {
            // The pointer is held past an edge with nothing left to scroll, so hit-testing it keeps
            // returning the last visible line rather than the content beyond. Dragging below the
            // document selects to its end (and above it, to the start), which is what the edge band
            // means once the offset can no longer advance.
            logicalPosition = direction > 0 ? Text.Length : 0;
        }

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

        if (e.Key == Key.Escape && _cancelInlineTableCellEdit is not null)
        {
            var keyboardPlan = TableCellEditPlanner.PlanKeyboard(
                TableCellEditKeyboardKey.Escape,
                ToTableCellEditKeyboardModifiers(e.KeyModifiers));
            if (keyboardPlan.Action == TableCellEditKeyboardAction.Cancel)
            {
                _cancelInlineTableCellEdit();
                e.Handled = true;
                return;
            }
        }

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
                case Key.B:
                    e.Handled = ToggleTextFormat(TableCellTextFormatKind.Bold);
                    return;
                case Key.I:
                    e.Handled = ToggleTextFormat(TableCellTextFormatKind.Italic);
                    return;
                case Key.U:
                    e.Handled = ToggleTextFormat(TableCellTextFormatKind.Underline);
                    return;
                case Key.D5:
                    e.Handled = ToggleTextFormat(TableCellTextFormatKind.Strikethrough);
                    return;
                case Key.OemPlus:
                case Key.Add:
                    e.Handled = ToggleTextFormat(
                        shift ? TableCellTextFormatKind.Superscript : TableCellTextFormatKind.Subscript);
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
            // The caret must follow the moving end of the selection. Assigning SelectionEnd alone
            // leaves CaretIndex where it was, and every navigation above reads CaretIndex as its
            // origin -- so a second Shift+Arrow re-navigated from the stale position and collapsed
            // the selection instead of extending it. Set the caret first: assigning CaretIndex can
            // itself collapse the selection, so the range assignment has to come after it.
            InputBox.CaretIndex = target;
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
            InputBox.Text = _session.PlainText;
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

    private async Task<bool> WriteRichClipboardAsync(
        InCanvasRichClipboardPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.PlainText.Length == 0)
        {
            // Nothing selected to copy/cut; not a write failure, so clear any stale error from an
            // earlier call rather than letting it resurface on an unrelated empty-selection copy.
            _lastWriteFailureMessage = null;
            return false;
        }

        var result = await PresentationRichTextClipboardWorkflow.WriteAsync(
            _clipboard,
            CreateClipboardContent(payload),
            NativeRichTextScope,
            NativeXamlPackageFormat,
            NativeRtfFormat,
            cancellationToken);
        _lastWriteFailureMessage = result.IsSuccess ? null : result.ErrorMessage;
        return result.IsSuccess;
    }

    /// <summary>
    /// Builds the actual rich-editor clipboard transfer. The private FreeP payload preserves
    /// editor-only resources, while standard RTF gives WPF, Office, and Linux rich editors a
    /// truthful interoperable text/run projection.
    /// </summary>
    internal static DataTransfer BuildRichTextDataTransfer(InCanvasRichClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return AvaloniaPlatformClipboard.BuildDataTransfer(
            PresentationClipboardPlatformMapper.ToPlatformContent(
                CreateClipboardContent(payload),
                NativeRichTextScope,
                NativeXamlPackageFormat,
                NativeRtfFormat),
            out _);
    }

    private static PresentationClipboardContent CreateClipboardContent(
        InCanvasRichClipboardPayload payload) =>
        PresentationRichTextClipboardWorkflow.CreateWriteContent(
            payload,
            ExternalXamlClipboardPlanner.SerializeXamlPackage(payload),
            ExternalRichTextClipboardPlanner.SerializeRtf(payload));

    private static PlatformClipboardFormatScope NativeRichTextScope =>
        OperatingSystem.IsWindows()
            ? PlatformClipboardFormatScope.Platform
            : PlatformClipboardFormatScope.Application;

    private static string NativeXamlPackageFormat => OperatingSystem.IsWindows()
        ? PresentationClipboardFormats.WindowsXamlPackage
        : PresentationClipboardFormats.LinuxXamlPackage;

    private static string NativeRtfFormat => OperatingSystem.IsWindows()
        ? PresentationClipboardFormats.WindowsRtf
        : PresentationClipboardFormats.LinuxRtf;

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

    private static TableCellEditKeyboardModifiers ToTableCellEditKeyboardModifiers(
        KeyModifiers modifiers)
    {
        var result = TableCellEditKeyboardModifiers.None;
        if ((modifiers & KeyModifiers.Control) != 0)
            result |= TableCellEditKeyboardModifiers.Control;
        if ((modifiers & KeyModifiers.Shift) != 0)
            result |= TableCellEditKeyboardModifiers.Shift;
        if ((modifiers & KeyModifiers.Alt) != 0)
            result |= TableCellEditKeyboardModifiers.Alt;
        if ((modifiers & KeyModifiers.Meta) != 0)
            result |= TableCellEditKeyboardModifiers.Platform;
        return result;
    }
}
