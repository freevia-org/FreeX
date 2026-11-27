using Free.Shared.AppServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Ribbon.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW Avalonia Find &amp; Replace dialog: a modeless <see cref="Window"/> (non-blocking) with Find /
/// Replace fields, one checkbox per option in <see cref="FindReplaceDialogPlanner.Surface"/>, Find Next / Replace / Replace All
/// buttons, and a Go To section (headings via <see cref="DocumentOutline"/> + document start/end).
///
/// Find/replace option policy, validation, request composition, and result text live in
/// <see cref="FindReplaceDialogPlanner"/>. Navigation (Find Next, Go To) uses the editor's
/// <see cref="DocumentView.FindNext"/> / <see cref="DocumentView.GetBlockTop"/> surface so the editor
/// controls the caret and scroll.
///
/// Options: the same seven as the WPF dialog (Match case, Whole word, Use wildcards, Match prefix,
/// Match suffix, Ignore punctuation, Ignore white space). "Use wildcards" disables every option but
/// Match case through the presentation planner policy.
///
/// The inline find bar in MainWindow continues to work; the dialog is opened via a separate
/// <c>freew.find-replace-dialog</c> ribbon command (Home → Editing group) or Ctrl+H.
/// </summary>
public sealed partial class FindReplaceDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;
    private static readonly FindReplaceDialogSurfaceSpec Surface = FindReplaceDialogPlanner.Surface;

    // ── Editor reference ──────────────────────────────────────────────────────

    private readonly DocumentView _editor;

    // ── Controls ──────────────────────────────────────────────────────────────

    private readonly TextBox _findBox = new()
    {
        MinWidth = Surface.Metrics.FieldMinWidth,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly TextBox _replaceBox = new()
    {
        MinWidth = Surface.Metrics.FieldMinWidth,
        Margin = new Thickness(0, 6, 0, 0),
    };

    // One check box per Surface option, in Surface order; rendered, read and enabled from the session.
    private readonly Dictionary<FindReplaceOptionKind, CheckBox> _options = [];
    private readonly FindReplaceDialogText _text = new(UiText.Get);

    private readonly ComboBox _goToTarget = new()
    {
        MinWidth = Surface.Metrics.FieldMinWidth,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly TextBlock _status = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
        Margin = new Thickness(0, 6, 0, 0),
    };

    // The "Format: ..." lines under Find what / Replace with; hidden while no formatting is set.
    private readonly TextBlock _findFormatSummary = NewFormatSummary();
    private readonly TextBlock _replaceFormatSummary = NewFormatSummary();

    private TextBox _lastFocusedBox = null!;
    private readonly FindReplaceDialogSession _session;

    // The clipboard is read asynchronously, but the shared replace path is synchronous: an action whose
    // Replace with box uses ^c (Clipboard Contents) reads the clipboard first and then runs against this snapshot.
    private IPlatformClipboard _platformClipboard;
    private string? _clipboardText;

    // Shows the Format dialog; null means cancelled. A seam so tests can answer without a modal window.
    private Func<FindReplaceDialogFieldKind, FindFormat?, Task<FindFormat?>> _chooseFormat;

    // ── Construction ──────────────────────────────────────────────────────────

    public FindReplaceDialog(
        DocumentView editor,
        FindReplaceOpenMode openMode = FindReplaceOpenMode.Find)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _session = new FindReplaceDialogSession(
            new AvaloniaFindReplaceCommandHost(_editor),
            openMode,
            FindReplaceDialogPlanner.ResolvePolicyText(UiText.Get),
            _text,
            () => _editor.Document,
            () => _clipboardText);
        _platformClipboard = new AvaloniaPlatformClipboard(() => TopLevel.GetTopLevel(this)?.Clipboard);
        _chooseFormat = ChooseFormatWithDialogAsync;

        Title = Surface.Title;
        Width = Surface.Metrics.WindowWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        foreach (var choice in Surface.Options)
        {
            var box = new CheckBox
            {
                Content = FindReplaceDialogPlanner.LabelFor(choice.Kind, _text),
                Margin = new Thickness(0, _options.Count == 0 ? 6 : 4, 0, 0),
            };
            AutomationProperties.SetAutomationId(box, choice.AutomationId);
            AvaloniaCompactDialogChrome.ApplyCompactCheckBox(box, DialogChromeStyle);
            _options[choice.Kind] = box;
        }

        // "Use wildcards", "Sounds like" and "Find all word forms" grey out the other options
        // (incompatible, mirrors Word); see FindReplaceDialogPlanner.IsOptionEnabled.
        foreach (var exclusive in new[]
                 {
                     FindReplaceOptionKind.UseWildcards,
                     FindReplaceOptionKind.SoundsLike,
                     FindReplaceOptionKind.FindAllWordForms,
                 })
        {
            _options[exclusive].IsCheckedChanged += (_, _) => ApplyOptionPolicy();
        }

        ApplyOptionPolicy();
        AutomationProperties.SetAutomationId(_findBox, Surface.Field(FindReplaceDialogFieldKind.Find).AutomationId);
        AutomationProperties.SetAutomationId(_replaceBox, Surface.Field(FindReplaceDialogFieldKind.Replace).AutomationId);
        AutomationProperties.SetAutomationId(_goToTarget, Surface.GoToTargetAutomationId);
        AutomationProperties.SetAutomationId(_findFormatSummary, FindReplaceDialogPlanner.FindFormatSummaryAutomationId);
        AutomationProperties.SetAutomationId(_replaceFormatSummary, FindReplaceDialogPlanner.ReplaceFormatSummaryAutomationId);
        AvaloniaCompactDialogChrome.ApplyTextBox(_findBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_replaceBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_goToTarget, DialogChromeStyle);

        // --- Main grid (Find label | Find box, Replace label | Replace box) ------
        var grid = new Grid { Margin = new Thickness(Surface.Metrics.OuterMargin, Surface.Metrics.OuterMargin, Surface.Metrics.OuterMargin, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Find: + its format summary, Replace: + its format summary.
        AddLabeledRow(grid, Surface.Field(FindReplaceDialogFieldKind.Find).Label, _findBox);
        AddInFieldColumn(grid, _findFormatSummary);
        AddLabeledRow(grid, Surface.Field(FindReplaceDialogFieldKind.Replace).Label, _replaceBox);
        AddInFieldColumn(grid, _replaceFormatSummary);

        _lastFocusedBox = _findBox;
        _findBox.GotFocus += (_, _) => _lastFocusedBox = _findBox;
        _replaceBox.GotFocus += (_, _) => _lastFocusedBox = _replaceBox;

        // Option rows (one per Surface option) in the field column.
        foreach (var chk in _options.Values)
            AddInFieldColumn(grid, chk);

        // Special / Format / No Formatting act on whichever box last had focus, as in Word.
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        tools.Children.Add(BuildSpecialButton());
        tools.Children.Add(MakeToolButton(
            _text.Get(FindReplaceTextId.FormatButton),
            FindReplaceDialogPlanner.FormatButtonAutomationId,
            async (_, _) => await ChooseFormatAsync()));
        tools.Children.Add(MakeToolButton(
            _text.Get(FindReplaceTextId.NoFormattingButton),
            FindReplaceDialogPlanner.NoFormattingButtonAutomationId,
            (_, _) => ShowFormatState(_session.ClearFormat(LastFocusedField))));
        AddInFieldColumn(grid, tools);

        // --- Action buttons ---------------------------------------------------
        var actionButtons = new[]
        {
            MakeButton(Surface.Actions[0], (_, _) => Execute(Surface.Actions[0].Kind)),
            MakeButton(Surface.Actions[1], (_, _) => Execute(Surface.Actions[1].Kind)),
            MakeButton(Surface.Actions[2], (_, _) => Execute(Surface.Actions[2].Kind)),
            MakeButton(Surface.Actions[3], (_, _) => Close()),
        };
        var btnRow = AvaloniaCompactDialogChrome.CreateActionRow(
            actionButtons,
            new Thickness(Surface.Metrics.OuterMargin, Surface.Metrics.ActionTopMargin, Surface.Metrics.OuterMargin, Surface.Metrics.OuterMargin));

        // --- Go To section ---------------------------------------------------
        var goToSection = BuildGoToSection();

        // --- Status bar -------------------------------------------------------
        var statusHost = new Border { Margin = new Thickness(Surface.Metrics.OuterMargin, 0, Surface.Metrics.OuterMargin, 12), Child = _status };

        // --- Outer stack ------------------------------------------------------
        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(btnRow);
        outer.Children.Add(goToSection);
        outer.Children.Add(statusHost);

        Content = outer;

        // Keyboard: Enter = Find Next in find box, Escape = close dialog.
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Execute(FindReplaceDialogActionKind.FindNext);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        _replaceBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };

        Opened += (_, _) => ActivateFor(_session.State.OpenMode);
    }

    internal void ActivateFor(FindReplaceOpenMode openMode)
    {
        var state = _session.ActivateFor(openMode);
        AvaloniaCompactDialogChrome.FocusAndSelect(
            state.OpenMode == FindReplaceOpenMode.Replace ? _replaceBox : _findBox);
    }

    private Button BuildSpecialButton()
    {
        var button = MakeButton(Surface.SpecialButtonLabel, (_, _) => { }, Surface.SpecialButtonAutomationId);
        button.HorizontalAlignment = HorizontalAlignment.Left;

        button.Click += (_, _) =>
        {
            // Word offers a different list per box, and wildcard operators when wildcards are on.
            SyncSessionInput();
            var items = _session.BuildSpecialItems(LastFocusedField);
            var menu = AvaloniaContextMenuRenderer.BuildContextMenu(
                FreeWContextMenuPlanner.BuildFindSpecial(items),
                commandId =>
                {
                    if (FreeWContextMenuPlanner.TryParseIndex(commandId, FreeWContextMenuPlanner.FindSpecialPrefix, out var index)
                        && index < items.Count)
                    {
                        InsertSpecial(items[index].Insert);
                    }
                });
            button.ContextMenu = menu;
            menu.Open(button);
        };
        return button;
    }

    private void InsertSpecial(string text)
    {
        var box = _lastFocusedBox ?? _findBox;
        var plan = _session.PlanSpecialInsertion(box.Text, box.CaretIndex, text);
        box.Text = plan.Text;
        box.CaretIndex = plan.CaretIndex;
        box.Focus();
    }

    // ── Find / Replace formatting ─────────────────────────────────────────────

    private FindReplaceDialogFieldKind LastFocusedField =>
        ReferenceEquals(_lastFocusedBox, _replaceBox)
            ? FindReplaceDialogFieldKind.Replace
            : FindReplaceDialogFieldKind.Find;

    private async Task ChooseFormatAsync()
    {
        var field = LastFocusedField;
        if (await _chooseFormat(field, _session.FormatFor(field)) is { } format)
            ShowFormatState(_session.SetFormat(field, format));
    }

    private Task<FindFormat?> ChooseFormatWithDialogAsync(FindReplaceDialogFieldKind field, FindFormat? current) =>
        FindFormatDialog.ChooseAsync(this, field, current, _editor.Document, _text);

    private void ShowFormatState(FindReplaceDialogState state)
    {
        ShowSummary(_findFormatSummary, state.FindFormatSummary);
        ShowSummary(_replaceFormatSummary, state.ReplaceFormatSummary);
    }

    private static void ShowSummary(TextBlock summary, string text)
    {
        summary.Text = text;
        summary.IsVisible = text.Length > 0;
    }

    private static TextBlock NewFormatSummary() => new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 0),
        IsVisible = false,
    };

    // ── Go To section ─────────────────────────────────────────────────────────

    private Panel BuildGoToSection()
    {
        var panel = new StackPanel { Margin = new Thickness(Surface.Metrics.OuterMargin, 0, Surface.Metrics.OuterMargin, 0) };

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            Margin = new Thickness(0, 0, 0, 6),
        });

        panel.Children.Add(new TextBlock
        {
            Text = Surface.GoToSectionLabel,
            FontWeight = FontWeight.SemiBold,
        });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(_goToTarget, 0);
        row.Children.Add(_goToTarget);

        var goBtn = MakeButton(Surface.GoToButtonLabel, (_, _) => GoTo(), Surface.GoToButtonAutomationId);
        Grid.SetColumn(goBtn, 2);
        row.Children.Add(goBtn);

        row.Margin = new Thickness(0, 0, 0, 2);

        panel.Children.Add(row);

        // Populate initially and again each time the drop-down opens (document may have changed).
        PopulateGoToTargets();
        _goToTarget.DropDownOpened += (_, _) => PopulateGoToTargets();

        return panel;
    }

    private void PopulateGoToTargets()
    {
        var plan = _session.BuildGoToTargets(_editor.Document, _goToTarget.SelectedIndex);
        _goToTarget.ItemsSource = plan.Targets;
        _goToTarget.SelectedIndex = plan.SelectedIndex;
    }

    private void GoTo()
    {
        var plan = _session.PlanGoTo(
            _goToTarget.SelectedItem as FindReplaceGoToTarget,
            _editor.Document.Blocks.Count);
        if (plan is null)
            return;

        // Document start/end move the caret like the WPF host does; headings only scroll the block into view.
        if (plan.Kind is FindReplaceGoToTargetKind.DocumentStart or FindReplaceGoToTargetKind.DocumentEnd)
        {
            if (!_editor.MoveCaretToDocumentEdge(plan.Kind == FindReplaceGoToTargetKind.DocumentEnd))
                ScrollEditorToBlock(plan.BlockIndex);
        }
        else
        {
            ScrollEditorToBlock(plan.BlockIndex);
        }

        _editor.Focus();
        _status.Text = _session.State.StatusText;
    }

    // ── Find / Replace logic ──────────────────────────────────────────────────

    private void Execute(FindReplaceDialogActionKind action)
    {
        if (action != FindReplaceDialogActionKind.FindNext
            && (_replaceBox.Text ?? string.Empty).Contains("^c", StringComparison.OrdinalIgnoreCase))
        {
            _ = ExecuteWithClipboardAsync(action);
            return;
        }

        ExecuteNow(action);
    }

    private void ExecuteNow(FindReplaceDialogActionKind action) =>
        _status.Text = _session.Execute(action, ReadInput()).StatusText;

    private async Task ExecuteWithClipboardAsync(FindReplaceDialogActionKind action)
    {
        _clipboardText = await ReadClipboardTextAsync();
        ExecuteNow(action);
    }

    private async Task<string?> ReadClipboardTextAsync()
    {
        try
        {
            var read = await _platformClipboard.ReadTextAsync();
            return read.IsSuccess ? read.Value : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An unavailable clipboard reads as an empty one, as in Word.
            return null;
        }
    }

    private FindReplaceDialogState SyncSessionInput() =>
        _session.SetInput(ReadInput());

    private FindReplaceDialogInput ReadInput() =>
        new(
            _findBox.Text,
            _replaceBox.Text,
            IsChecked(FindReplaceOptionKind.MatchCase),
            IsChecked(FindReplaceOptionKind.WholeWord),
            IsChecked(FindReplaceOptionKind.UseWildcards),
            IsChecked(FindReplaceOptionKind.MatchPrefix),
            IsChecked(FindReplaceOptionKind.MatchSuffix),
            IsChecked(FindReplaceOptionKind.IgnorePunctuation),
            IsChecked(FindReplaceOptionKind.IgnoreWhiteSpace),
            IsChecked(FindReplaceOptionKind.SoundsLike),
            IsChecked(FindReplaceOptionKind.FindAllWordForms));

    private bool IsChecked(FindReplaceOptionKind kind) => _options[kind].IsChecked == true;

    private void ApplyOptionPolicy()
    {
        var state = SyncSessionInput();
        foreach (var (kind, box) in _options)
        {
            box.IsEnabled = state.IsOptionEnabled(kind);
            if (box.IsChecked == true && !state.IsOptionChecked(kind))
                box.IsChecked = false;
        }
    }

    // ── Scroll helper (mirrors NavigationPane.ScrollEditorToBlock) ────────────

    /// <summary>
    /// Scrolls the <see cref="ScrollViewer"/> that wraps the editor so that
    /// <paramref name="blockIndex"/> is visible near the top of the viewport. Set via
    /// <see cref="ScrollerRef"/> after construction (wired by MainWindow).
    /// </summary>
    public ScrollViewer? ScrollerRef { get; set; }

    private void ScrollEditorToBlock(int blockIndex)
    {
        if (ScrollerRef is not { } scroller)
            return;
        var y = _editor.GetBlockTop(blockIndex);
        if (y < 0)
            return;
        scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, y - 40));
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private static void AddInFieldColumn(Grid grid, Control element)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(element, row);
        Grid.SetColumn(element, 1);
        grid.Children.Add(element);
    }

    private static void AddLabeledRow(Grid grid, string label, Control field)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, Surface.Metrics.RowTopMargin, 8, 0),
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static Button MakeButton(string content, EventHandler<RoutedEventArgs> onClick, string automationId)
    {
        var btn = new Button
        {
            Content = content,
        };
        AutomationProperties.SetAutomationId(btn, automationId);
        AvaloniaCompactDialogChrome.ApplyButton(btn, DialogChromeStyle, minWidth: Surface.Metrics.ButtonMinWidth);
        btn.Click += onClick;
        return btn;
    }

    private static Button MakeToolButton(string content, string automationId, EventHandler<RoutedEventArgs> onClick)
    {
        var btn = new Button { Content = content };
        AutomationProperties.SetAutomationId(btn, automationId);
        AvaloniaCompactDialogChrome.ApplyButton(btn, DialogChromeStyle, minWidth: 0);
        btn.Click += onClick;
        return btn;
    }

    private static Button MakeButton(FindReplaceDialogActionSpec action, EventHandler<RoutedEventArgs> onClick) =>
        MakeButton(action.Label, onClick, action.AutomationId);

}
