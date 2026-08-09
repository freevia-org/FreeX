using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class FindReplaceDialog : Window
{
    public static GridLength FindReplaceFieldLabelColumnWidth => new(FindReplaceDialogPlanner.FieldLabelColumnWidth);
    public static DataGridLength FindReplaceResultBookColumnWidth => new(FindReplaceDialogPlanner.ResultBookColumnWidth);
    public static DataGridLength FindReplaceResultSheetColumnWidth => new(FindReplaceDialogPlanner.ResultSheetColumnWidth);
    public static DataGridLength FindReplaceResultNameColumnWidth => new(FindReplaceDialogPlanner.ResultNameColumnWidth);
    public static DataGridLength FindReplaceResultCellColumnWidth => new(FindReplaceDialogPlanner.ResultCellColumnWidth);

    private readonly Func<Workbook> _getWorkbook;
    private readonly Func<SheetId?> _getCurrentSheetId;
    private readonly ICommandBus _commandBus;
    private readonly Action<CellAddress> _navigateTo;
    private readonly Action _onWorkbookChanged;
    private readonly Func<CellAddress?> _getActiveSelectionCell;
    private IReadOnlyList<FindResult> _results = [];
    private int _currentIndex = -1;
    private string _lastSearch = string.Empty;
    // Tracks every option Find Next's "same search" detection needs, not just the search text --
    // Match Case/Match Entire Cell/Look In/Within/Search Order all change which results are found
    // and in what order, so any of them changing must be treated exactly like a brand-new search
    // (R60-commands-find-replace-6-3).
    private FindOptions? _lastFindOptions;
    private bool _lastMatchCase;
    private bool _lastMatchEntireCell;
    private StyleDiff? _findFormatDiff;
    private StyleDiff? _replaceFormatDiff;
    private bool _syncingSearchText;
    private IReadOnlyList<GridRange>? _selectionScopeAtOpen;

    public FindReplaceDialog(
        Func<Workbook> getWorkbook,
        ICommandBus commandBus,
        Action<CellAddress> navigateTo,
        bool replaceMode = false,
        Func<SheetId?>? getCurrentSheetId = null,
        Func<CellAddress?>? getActiveSelectionCell = null,
        Action? onWorkbookChanged = null)
    {
        _getWorkbook = getWorkbook;
        _getCurrentSheetId = getCurrentSheetId ?? (() => null);
        _commandBus = commandBus;
        _navigateTo = navigateTo;
        _onWorkbookChanged = onWorkbookChanged ?? (() => { });
        _getActiveSelectionCell = getActiveSelectionCell ?? (() => null);
        InitializeComponent();
        if (replaceMode)
        {
            FindReplaceTabs.SelectedItem = ReplaceTab;
        }
        UpdateReplaceButtonVisibility();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
        Loaded += (_, _) => CaptureSelectionScopeAtOpen();
    }

    /// <summary>
    /// Excel: when more than one cell is selected before Find &amp; Replace is opened, Replace
    /// All/Find All is automatically restricted to that selection. Captured once when the dialog
    /// finishes loading (by which point <see cref="Window.Owner"/> is set) so subsequent grid
    /// selection changes made while this modeless dialog stays open don't retroactively change the
    /// scope, matching Excel's "at open time" semantics.
    /// A multi-area (Ctrl+click) selection must be honored as a whole -- SheetGrid.SelectedRange
    /// alone only ever holds the single most-recently-drawn area, while SheetGrid.SelectedRanges
    /// holds every disjoint area (R127-findreplace-selectionscope-multiarea-1). Resolved through
    /// the same SelectionStyleCommandPlanner.ResolveRanges choke point MainWindow.CommandExecution.cs
    /// already uses for exactly this SelectedRange/SelectedRanges duality.
    /// </summary>
    private void CaptureSelectionScopeAtOpen()
    {
        if (Owner is not MainWindow mainWindow)
            return;

        var ranges = SelectionStyleCommandPlanner.ResolveRanges(
            mainWindow.SheetGrid.SelectedRange,
            mainWindow.SheetGrid.SelectedRanges);

        if (ranges.Count == 0)
            return;

        // A scope of a single, degenerate one-cell range means nothing was really selected
        // (Excel only restricts the search when more than one cell was selected); anything
        // covering more than one cell -- whether a single contiguous block or several disjoint
        // Ctrl+click areas -- must be captured.
        if (ranges.Count == 1 && ranges[0].Start == ranges[0].End)
            return;

        _selectionScopeAtOpen = ranges;
    }

    /// <summary>
    /// Switches the dialog to Find or Replace tab when the window is already open.
    /// Called when the host wants to reuse the live dialog instead of opening a second one.
    /// </summary>
    public void SwitchMode(bool replaceMode)
    {
        var target = replaceMode ? ReplaceTab : FindTab;
        if (!ReferenceEquals(FindReplaceTabs.SelectedItem, target))
            FindReplaceTabs.SelectedItem = target;
        FocusSearchBox();
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusSearchBox();
    }

    private void FocusSearchBox()
    {
        DialogFocus.FocusAndSelect(ResolveSearchBox());
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext();
    private void FindAll_Click(object sender, RoutedEventArgs e) => FindAll();
    private void Replace_Click(object sender, RoutedEventArgs e) => ReplaceOne();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void FindReplaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateReplaceButtonVisibility();
        if (IsLoaded)
            FocusSearchBox();
    }

    private void UpdateReplaceButtonVisibility()
    {
        var visibility = FindReplaceTabs.SelectedItem == ReplaceTab ? Visibility.Visible : Visibility.Collapsed;
        ReplaceBtn.Visibility = visibility;
        ReplaceAllBtn.Visibility = visibility;
    }

    private void FindResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FindResultsGrid.SelectedItem is FindResultRow row)
            _navigateTo(row.Address);
    }

    private void OptionsExpander_Expanded(object sender, RoutedEventArgs e) => OptionsExpander.Header = UiText.Get("FindReplace_OptionsExpanded");
    private void OptionsExpander_Collapsed(object sender, RoutedEventArgs e) => OptionsExpander.Header = UiText.Get("FindReplace_Options");
    private void FindFormatButton_Click(object sender, RoutedEventArgs e) => PickFormat(ref _findFormatDiff, FindFormatButton, ReplaceFindFormatButton);
    private void ReplaceWithFormatButton_Click(object sender, RoutedEventArgs e) => PickFormat(ref _replaceFormatDiff, ReplaceWithFormatButton);
    private void ChooseFindFormatFromCellButton_Click(object sender, RoutedEventArgs e) => PickFormatFromCell(ref _findFormatDiff);
    private void ChooseReplaceWithFormatFromCellButton_Click(object sender, RoutedEventArgs e) => PickFormatFromCell(ref _replaceFormatDiff);
    private void FindClearFormatButton_Click(object sender, RoutedEventArgs e)
    {
        _findFormatDiff = null;
        UpdateFormatStateButtons();
    }

    private void ReplaceWithClearFormatButton_Click(object sender, RoutedEventArgs e)
    {
        _replaceFormatDiff = null;
        UpdateFormatStateButtons();
    }

    private void FindBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) FindNext();
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSearchText)
            return;

        _syncingSearchText = true;
        try
        {
            if (ReferenceEquals(sender, FindBox))
                ReplaceFindBox.Text = FindBox.Text;
            else if (ReferenceEquals(sender, ReplaceFindBox))
                FindBox.Text = ReplaceFindBox.Text;
        }
        finally
        {
            _syncingSearchText = false;
        }
    }

    private void FindNext()
    {
        var search = SearchText;
        // Excel allows a blank "Find what" as long as a Format criterion is set (Find All by
        // format only); only warn/block on an empty search when there is ALSO no format criterion
        // (R64-commands-find-replace-6-1).
        if (string.IsNullOrEmpty(search) && _findFormatDiff is null && ShowBlankSearchWarning()) return;

        var options = CreateFindOptions();
        var matchCase = MatchCaseBox.IsChecked == true;
        var matchEntireCell = MatchEntireBox.IsChecked == true;

        // "Same search" means the text AND every option that affects which results are found/their
        // order are unchanged since the previous Find Next -- comparing only the text (as before)
        // let a stale _currentIndex from a differently-filtered/ordered result set silently carry
        // over whenever an option was toggled without retyping the query (R60-commands-find-replace-6-3).
        var sameSearch = search == _lastSearch &&
            options == _lastFindOptions &&
            matchCase == _lastMatchCase &&
            matchEntireCell == _lastMatchEntireCell;
        if (!sameSearch)
        {
            _currentIndex = -1;
            _lastSearch = search;
            _lastFindOptions = options;
            _lastMatchCase = matchCase;
            _lastMatchEntireCell = matchEntireCell;
        }

        _results = FindReplaceService.Find(
            _getWorkbook(), search,
            options,
            matchCase: matchCase,
            matchEntireCell: matchEntireCell);

        UpdateResultsGrid();

        if (_results.Count == 0)
        {
            SetStatusText(UiText.Get("FindReplace_NoMatchesFound"));
            _currentIndex = -1;
            return;
        }

        // On a brand-new search (fresh text or a changed option, both reset _currentIndex to -1
        // above) Excel starts searching forward from the ACTIVE cell, wrapping around -- it never
        // restarts at the first match in sheet order regardless of where the user currently is
        // (R60-commands-find-replace-6-1). Continuing an unchanged search just advances to the next
        // result as before.
        _currentIndex = _currentIndex < 0
            ? FindFirstResultIndexAfterActiveCell(_results, options.SearchOrder)
            : (_currentIndex + 1) % _results.Count;
        var result = _results[_currentIndex];
        SetStatusText(UiText.Format("FindReplace_MatchStatus", _currentIndex + 1, _results.Count));
        _navigateTo(result.Address);
    }

    /// <summary>
    /// Mirrors the Avalonia shell's WorkbookSession.FindFirstResultAfterActiveCell: the first result
    /// (in the given search order) that sorts strictly after the active cell, or index 0 (wrap to
    /// the first sheet-order match) when none do / no active cell is available.
    /// </summary>
    private int FindFirstResultIndexAfterActiveCell(IReadOnlyList<FindResult> results, FindSearchOrder searchOrder)
    {
        var activeCell = _getActiveSelectionCell();
        if (activeCell is null)
            return 0;

        return FindFirstResultIndexAfterAddress(results, activeCell.Value, searchOrder);
    }

    /// <summary>
    /// Mirrors the Avalonia shell's WorkbookSession.FindNextResultIndexAtSameAddress /
    /// GetReplaceTargetIndex fallback: the first result (in the given search order) that sorts
    /// strictly after <paramref name="address"/>, or index 0 (wrap to the first sheet-order match)
    /// when none do. Used after a successful single Replace to advance past the just-replaced cell
    /// instead of unconditionally jumping back to match #1 -- otherwise a replacement whose result
    /// still matches the search (e.g. "Report" -> "Report_v2") would re-edit the same cell on every
    /// Replace click instead of advancing to the next distinct match (R71-commands-find-replace-4-2).
    /// </summary>
    private int FindFirstResultIndexAfterAddress(IReadOnlyList<FindResult> results, CellAddress address, FindSearchOrder searchOrder)
    {
        var workbook = _getWorkbook();
        for (var index = 0; index < results.Count; index++)
        {
            if (CompareFindOrder(workbook, results[index].Address, address, searchOrder) > 0)
                return index;
        }

        return 0;
    }

    private static int CompareFindOrder(Workbook workbook, CellAddress left, CellAddress right, FindSearchOrder searchOrder)
    {
        var leftSheetIndex = FindSheetIndex(workbook, left.Sheet);
        var rightSheetIndex = FindSheetIndex(workbook, right.Sheet);
        var sheetComparison = leftSheetIndex.CompareTo(rightSheetIndex);
        if (sheetComparison != 0)
            return sheetComparison;

        if (searchOrder == FindSearchOrder.ByColumns)
        {
            var colComparison = left.Col.CompareTo(right.Col);
            return colComparison != 0 ? colComparison : left.Row.CompareTo(right.Row);
        }

        var rowComparison = left.Row.CompareTo(right.Row);
        return rowComparison != 0 ? rowComparison : left.Col.CompareTo(right.Col);
    }

    private static int FindSheetIndex(Workbook workbook, SheetId sheetId)
    {
        for (var index = 0; index < workbook.Sheets.Count; index++)
        {
            if (workbook.Sheets[index].Id.Equals(sheetId))
                return index;
        }

        return int.MaxValue;
    }

    private void FindAll()
    {
        var search = SearchText;
        // See FindNext: a blank search is allowed when a Format criterion narrows the results.
        if (string.IsNullOrEmpty(search) && _findFormatDiff is null && ShowBlankSearchWarning()) return;

        _lastSearch = search;
        _currentIndex = -1;
        _results = FindReplaceService.Find(
            _getWorkbook(), search,
            CreateFindOptions(),
            matchCase: MatchCaseBox.IsChecked == true,
            matchEntireCell: MatchEntireBox.IsChecked == true);

        UpdateResultsGrid();
        SetStatusText(_results.Count == 0
            ? UiText.Get("FindReplace_NoMatchesFound")
            : UiText.Format("FindReplace_CellsFoundStatus", _results.Count));
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        var search = SearchText;
        // See FindNext: a blank search is allowed when a Format criterion narrows the results.
        if (string.IsNullOrEmpty(search) && _findFormatDiff is null && ShowBlankSearchWarning()) return;

        var result = FindReplaceService.TryReplaceAll(
            _getWorkbook(), _commandBus, search, ReplaceBox.Text,
            CreateFindOptions(),
            matchCase: MatchCaseBox.IsChecked == true,
            matchEntireCell: MatchEntireBox.IsChecked == true,
            replacementFormat: _replaceFormatDiff);

        if (ShowReplaceFailureWarning(result.Failure))
            return;

        if (result.ReplacedCount > 0)
            _onWorkbookChanged();

        SetStatusText(result.ReplacedCount == 0
            ? UiText.Get("FindReplace_NoMatchesFound")
            : UiText.Format("FindReplace_ReplacedCellsStatus", result.ReplacedCount));
        _results = FindReplaceService.Find(
            _getWorkbook(), search,
            CreateFindOptions(),
            matchCase: MatchCaseBox.IsChecked == true,
            matchEntireCell: MatchEntireBox.IsChecked == true);
        _currentIndex = -1;
        UpdateResultsGrid();
    }

    private void ReplaceOne()
    {
        var search = SearchText;
        if (string.IsNullOrEmpty(search) && ShowBlankSearchWarning()) return;

        if (_results.Count == 0 || _currentIndex < 0 || search != _lastSearch)
            FindNext();

        if (_results.Count == 0 || _currentIndex < 0)
            return;

        var options = CreateFindOptions();

        // A match can be non-replaceable without being a hard failure (e.g. Look-in=Values finds a
        // formula cell whose displayed result matches, but the formula itself can't be replaced).
        // Advance through the remaining matches (bounded by _results.Count so an all-non-replaceable
        // result set still terminates) instead of getting permanently stuck retrying the same match
        // forever — matching the Avalonia shell's WorkbookSession.ReplaceNextValue, which always
        // moves the active cell past a skipped match before the next Replace click.
        for (var attempt = 0; attempt < _results.Count; attempt++)
        {
            var match = _results[_currentIndex];
            var result = FindReplaceDialogPlanner.TryReplaceSingleMatch(
                _getWorkbook(),
                _commandBus,
                match,
                search,
                ReplaceBox.Text,
                matchCase: MatchCaseBox.IsChecked == true,
                matchEntireCell: MatchEntireBox.IsChecked == true,
                lookIn: options.LookIn,
                replacementFormat: _replaceFormatDiff);

            if (ShowReplaceFailureWarning(result.Failure))
                return;

            if (result.Replaced)
            {
                var replacedAddress = match.Address;
                SetStatusText(UiText.Get("FindReplace_ReplacedOneCell"));
                _onWorkbookChanged();
                _results = FindReplaceService.Find(
                    _getWorkbook(), search,
                    options,
                    matchCase: MatchCaseBox.IsChecked == true,
                    matchEntireCell: MatchEntireBox.IsChecked == true);
                _currentIndex = -1;
                UpdateResultsGrid();
                if (_results.Count > 0)
                {
                    // Advance past the cell we just replaced (which may still match the search,
                    // e.g. "Report" -> "Report_v2") instead of unconditionally jumping back to
                    // match #1, which would re-replace the same cell forever
                    // (R71-commands-find-replace-4-2).
                    _currentIndex = FindFirstResultIndexAfterAddress(_results, replacedAddress, options.SearchOrder);
                    _navigateTo(_results[_currentIndex].Address);
                    SetStatusText(UiText.Format("FindReplace_MatchStatus", _currentIndex + 1, _results.Count));
                }
                return;
            }

            // Not replaceable: advance past this match so the next attempt (or the next Replace
            // click, if this was the last attempt) tries a different one instead of repeating it.
            _currentIndex = (_currentIndex + 1) % _results.Count;
            _navigateTo(_results[_currentIndex].Address);
        }

        SetStatusText(UiText.Get("FindReplace_NoReplaceableMatchFound"));
    }

    private bool ShowReplaceFailureWarning(CommandOutcome? failure)
    {
        if (failure is null)
            return false;

        DialogMessageHelper.ShowWarning(this, failure.ErrorMessage ?? UiText.Get("FindReplace_ReplacementFailed"), Title);
        FocusSearchBox();
        return true;
    }

    private string SearchText => FindReplaceTabs.SelectedItem == ReplaceTab ? ReplaceFindBox.Text : FindBox.Text;

    private bool ShowBlankSearchWarning()
    {
        DialogFocus.ShowWarningAndFocus(this, UiText.Get("FindReplace_FindWhatRequired"), Title, ResolveSearchBox());
        return true;
    }

    private TextBox ResolveSearchBox() => FindReplaceTabs.SelectedItem == ReplaceTab ? ReplaceFindBox : FindBox;

    private FindOptions CreateFindOptions() =>
        new(
            Within: WithinCombo.SelectedIndex == 1 ? FindWithin.Workbook : FindWithin.Sheet,
            CurrentSheetId: _getCurrentSheetId(),
            SearchOrder: SearchCombo.SelectedIndex == 1 ? FindSearchOrder.ByColumns : FindSearchOrder.ByRows,
            LookIn: LookInCombo.SelectedIndex switch
            {
                0 => FindLookIn.Formulas,
                2 => FindLookIn.Notes,
                3 => FindLookIn.Comments,
                _ => FindLookIn.Values
            },
            RequiredFormat: _findFormatDiff,
            SelectionScope: _selectionScopeAtOpen);

    private void PickFormat(ref StyleDiff? target, params Button[] buttons)
    {
        var baseStyle = target?.ApplyTo(CellStyle.Default) ?? CellStyle.Default;
        var dialog = new FormatCellsDialog(baseStyle, FormatCellsDialogTab.Font) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultDiff is null)
            return;

        target = dialog.ResultDiff;
        UpdateFormatStateButtons();
    }

    private void PickFormatFromCell(ref StyleDiff? target)
    {
        var address = FindResultsGrid.SelectedItem is FindResultRow row
            ? row.Address
            : _getActiveSelectionCell();
        if (address is null)
        {
            SetStatusText(UiText.Get("FindReplace_SelectFormatSourceStatus"));
            return;
        }

        var diff = FindReplaceDialogPlanner.CreateFormatDiffFromCell(_getWorkbook(), address.Value);
        if (diff is null)
        {
            SetStatusText(UiText.Get("FindReplace_NoCellFormatFoundStatus"));
            return;
        }

        target = diff;
        SetStatusText(FindResultsGrid.SelectedItem is FindResultRow
            ? UiText.Get("FindReplace_FormatChosenFromResultStatus")
            : UiText.Get("FindReplace_FormatChosenFromWorksheetStatus"));
        UpdateFormatStateButtons();
    }

    private void UpdateFormatStateButtons()
    {
        SetFormatState(_findFormatDiff is not null, UiText.Get("FindReplace_FindFormatSetToolTip"), FindFormatButton, FindClearFormatButton);
        SetFormatState(_findFormatDiff is not null, UiText.Get("FindReplace_FindFormatSetToolTip"), ReplaceFindFormatButton, ReplaceFindClearFormatButton);
        SetFormatState(_replaceFormatDiff is not null, UiText.Get("FindReplace_ReplaceFormatSetToolTip"), ReplaceWithFormatButton, ReplaceWithClearFormatButton);
    }

    private static void SetFormatState(bool isSet, string toolTip, Button formatButton, Button clearButton)
    {
        formatButton.Content = isSet ? UiText.Get("FindReplace_FormatSetButton") : UiText.Get("FindReplace_Format");
        formatButton.ToolTip = isSet ? toolTip : null;
        clearButton.Visibility = isSet ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateResultsGrid()
    {
        FindResultsGrid.ItemsSource = FindReplaceDialogPlanner.BuildFindResultRows(_getWorkbook(), _results);
    }

    /// <summary>
    /// Sets the Find/Replace status text and, since <see cref="StatusLabel"/> is declared as a
    /// polite UIA live region, raises the automation notifications needed for screen readers to
    /// actually announce it (WPF live regions are not announced purely by a Text change; the
    /// AutomationPeer's Name must change and a LiveRegionChanged event must be raised). Mirrors
    /// the status-bar convention in MainWindow.GridStatus.cs's NotifyStatusStatisticAutomationChanged.
    /// </summary>
    private void SetStatusText(string text)
    {
        if (StatusLabel.Text == text)
            return;

        StatusLabel.Text = text;

        var previousName = AutomationProperties.GetName(StatusLabel);
        AutomationProperties.SetName(StatusLabel, text);

        if (!StatusLabel.IsLoaded)
            return;

        try
        {
            var peer = UIElementAutomationPeer.FromElement(StatusLabel) ??
                       UIElementAutomationPeer.CreatePeerForElement(StatusLabel);
            peer?.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, previousName, text);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
