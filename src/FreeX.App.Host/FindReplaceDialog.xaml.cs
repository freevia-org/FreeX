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
    private readonly Action<CellAddress> _navigateTo;
    private readonly Action _onWorkbookChanged;
    private readonly Func<CellAddress?> _getActiveSelectionCell;
    private readonly FindReplaceWorkflowSession _workflow;
    private IReadOnlyList<FindResult> _results = [];
    private int _currentIndex = -1;
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
        _navigateTo = navigateTo;
        _onWorkbookChanged = onWorkbookChanged ?? (() => { });
        _getActiveSelectionCell = getActiveSelectionCell ?? (() => null);
        _workflow = new FindReplaceWorkflowSession(
            getWorkbook,
            _getActiveSelectionCell,
            address =>
            {
                navigateTo(address);
                return WorkbookNavigationResult.Selected(new GridRange(address, address));
            },
            command =>
            {
                var outcome = commandBus.Execute(getWorkbook().Id, command);
                return new WorkbookCellEditResult(
                    outcome.Success,
                    outcome.ErrorMessage,
                    outcome.AffectedCells ?? [],
                    RecalcReport: null,
                    IsNoOp: outcome.IsNoOp);
            });
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
    /// </summary>
    private void CaptureSelectionScopeAtOpen()
    {
        if (Owner is MainWindow mainWindow &&
            mainWindow.SheetGrid.SelectedRange is { } range &&
            range.Start != range.End)
        {
            _selectionScopeAtOpen = [range];
        }
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
        var result = _workflow.FindNext(search, options, matchCase, matchEntireCell);
        _results = result.Matches;
        _currentIndex = result.SelectedIndex;
        UpdateResultsGrid();
        if (!result.Success)
        {
            SetStatusText(UiText.Get("FindReplace_NoMatchesFound"));
            _currentIndex = -1;
            return;
        }
        SetStatusText(UiText.Format("FindReplace_MatchStatus", _currentIndex + 1, _results.Count));
    }

    private void FindAll()
    {
        var search = SearchText;
        // See FindNext: a blank search is allowed when a Format criterion narrows the results.
        if (string.IsNullOrEmpty(search) && _findFormatDiff is null && ShowBlankSearchWarning()) return;

        var result = _workflow.FindAll(
            search,
            CreateFindOptions(),
            MatchCaseBox.IsChecked == true,
            MatchEntireBox.IsChecked == true);
        _results = result.Matches;
        _currentIndex = -1;

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

        var result = _workflow.ReplaceAll(
            search,
            ReplaceBox.Text,
            CreateFindOptions(),
            MatchCaseBox.IsChecked == true,
            MatchEntireBox.IsChecked == true,
            replacementFormat: _replaceFormatDiff);

        if (!result.Success && ShowReplaceFailureWarning(result.ErrorMessage))
            return;

        if (result.ReplacedCount > 0)
            _onWorkbookChanged();

        SetStatusText(result.ReplacedCount == 0
            ? UiText.Get("FindReplace_NoMatchesFound")
            : UiText.Format("FindReplace_ReplacedCellsStatus", result.ReplacedCount));
        _results = result.CurrentMatches;
        _currentIndex = -1;
        UpdateResultsGrid();
    }

    private void ReplaceOne()
    {
        var search = SearchText;
        if (string.IsNullOrEmpty(search) && ShowBlankSearchWarning()) return;

        var result = _workflow.ReplaceNext(
            search,
            ReplaceBox.Text,
            CreateFindOptions(),
            MatchCaseBox.IsChecked == true,
            MatchEntireBox.IsChecked == true,
            replacementFormat: _replaceFormatDiff,
            behavior: FindReplaceNextBehavior.SubmittedDialogStyle);
        if (!result.Success)
        {
            ShowReplaceFailureWarning(result.ErrorMessage);
            return;
        }

        _results = result.CurrentMatches;
        _currentIndex = result.CurrentIndex;
        UpdateResultsGrid();
        if (result.ReplacedCount == 0)
        {
            SetStatusText(_results.Count == 0
                ? UiText.Get("FindReplace_NoMatchesFound")
                : UiText.Get("FindReplace_NoReplaceableMatchFound"));
            return;
        }

        SetStatusText(UiText.Get("FindReplace_ReplacedOneCell"));
        _onWorkbookChanged();
        if (_currentIndex >= 0)
            SetStatusText(UiText.Format("FindReplace_MatchStatus", _currentIndex + 1, _results.Count));
    }

    private bool ShowReplaceFailureWarning(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return false;

        DialogMessageHelper.ShowWarning(this, errorMessage, Title);
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
