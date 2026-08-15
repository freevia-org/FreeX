using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Dialogs;
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
        Func<IWorkbookCommand, CommandOutcome> executeCommand,
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
                var outcome = executeCommand(command);
                return new WorkbookCellEditResult(
                    outcome.Success,
                    outcome.ErrorMessage,
                    outcome.AffectedCells ?? [],
                    RecalcReport: null,
                    IsNoOp: outcome.IsNoOp);
            },
            FindReplaceDialogSchema.ResolvePolicyText(UiText.Get));
        InitializeComponent();
        ApplySharedDialogSchema();
        if (FindReplaceDialogPlanner.ShowsReplaceCommands(replaceMode))
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

        _selectionScopeAtOpen = FindReplaceDialogPlanner.ResolveSelectionScopeAtOpen(
            mainWindow.SheetGrid.SelectedRange,
            mainWindow.SheetGrid.SelectedRanges);
    }

    /// <summary>
    /// Switches the dialog to Find or Replace tab when the window is already open.
    /// Called when the host wants to reuse the live dialog instead of opening a second one.
    /// </summary>
    public void SwitchMode(bool replaceMode)
    {
        var target = FindReplaceDialogPlanner.ShowsReplaceCommands(replaceMode) ? ReplaceTab : FindTab;
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

    /// <summary>
    /// The dialog's current Find/Replace open mode, expressed with the cross-app
    /// <see cref="FindReplaceOpenMode"/> owned by <c>Free.Shared.AppServices</c>. The selected
    /// TabItem is the WPF rendering of that mode; every mode-dependent decision reads this.
    /// </summary>
    internal FindReplaceOpenMode OpenMode =>
        FindReplaceDialogPlanner.OpenModeFor(FindReplaceTabs.SelectedItem == ReplaceTab);

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
        var visibility = FindReplaceDialogPlanner.ShowsReplaceCommands(OpenMode)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReplaceBtn.Visibility = visibility;
        ReplaceAllBtn.Visibility = visibility;
    }

    private void FindResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FindResultsGrid.SelectedItem is FindResultRow row)
            _navigateTo(row.Address);
    }

    private void OptionsExpander_Expanded(object sender, RoutedEventArgs e) =>
        OptionsExpander.Header = DialogText(FindReplaceDialogText.OptionsExpanded);

    private void OptionsExpander_Collapsed(object sender, RoutedEventArgs e) =>
        OptionsExpander.Header = DialogText(FindReplaceDialogText.Options);
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
            SetStatusText(DialogText(FindReplaceDialogText.NoMatchesFound));
            _currentIndex = -1;
            return;
        }
        SetStatusText(DialogText(FindReplaceDialogText.MatchStatus, _currentIndex + 1, _results.Count));
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
            ? DialogText(FindReplaceDialogText.NoMatchesFound)
            : DialogText(FindReplaceDialogText.CellsFoundStatus, _results.Count));
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
            ? DialogText(FindReplaceDialogText.NoMatchesFound)
            : DialogText(FindReplaceDialogText.ReplacedCellsStatus, result.ReplacedCount));
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
                ? DialogText(FindReplaceDialogText.NoMatchesFound)
                : DialogText(FindReplaceDialogText.NoReplaceableMatchFound));
            return;
        }

        SetStatusText(DialogText(FindReplaceDialogText.ReplacedOneCell));
        _onWorkbookChanged();
        if (_currentIndex >= 0)
            SetStatusText(DialogText(FindReplaceDialogText.MatchStatus, _currentIndex + 1, _results.Count));
    }

    private bool ShowReplaceFailureWarning(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return false;

        DialogMessageHelper.ShowWarning(this, errorMessage, Title);
        FocusSearchBox();
        return true;
    }

    private string SearchText => ResolveSearchBox().Text;

    private bool ShowBlankSearchWarning()
    {
        DialogFocus.ShowWarningAndFocus(
            this,
            DialogText(FindReplaceDialogText.FindWhatRequired),
            Title,
            ResolveSearchBox());
        return true;
    }

    // FreeX focuses the ACTIVE TAB's "Find what" box in both modes; FreeW instead focuses its
    // replacement field in Replace mode. The focus target is therefore app-specific and stays here
    // even though the FindReplaceOpenMode it is resolved from is shared.
    private TextBox ResolveSearchBox() =>
        FindReplaceDialogPlanner.ShowsReplaceCommands(OpenMode) ? ReplaceFindBox : FindBox;

    private FindOptions CreateFindOptions() =>
        FindReplaceDialogPlanner.CreateFindOptions(
            currentSheetId: _getCurrentSheetId(),
            withinSelectedIndex: WithinCombo.SelectedIndex,
            searchOrderSelectedIndex: SearchCombo.SelectedIndex,
            lookInSelectedIndex: LookInCombo.SelectedIndex,
            requiredFormat: _findFormatDiff,
            selectionScope: _selectionScopeAtOpen);

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
            SetStatusText(DialogText(FindReplaceDialogText.SelectFormatSourceStatus));
            return;
        }

        var diff = FindReplaceDialogPlanner.CreateFormatDiffFromCell(_getWorkbook(), address.Value);
        if (diff is null)
        {
            SetStatusText(DialogText(FindReplaceDialogText.NoCellFormatFoundStatus));
            return;
        }

        target = diff;
        SetStatusText(FindResultsGrid.SelectedItem is FindResultRow
            ? DialogText(FindReplaceDialogText.FormatChosenFromResultStatus)
            : DialogText(FindReplaceDialogText.FormatChosenFromWorksheetStatus));
        UpdateFormatStateButtons();
    }

    private void UpdateFormatStateButtons()
    {
        SetFormatState(_findFormatDiff is not null, DialogText(FindReplaceDialogText.FindFormatSetToolTip), FindFormatButton, FindClearFormatButton);
        SetFormatState(_findFormatDiff is not null, DialogText(FindReplaceDialogText.FindFormatSetToolTip), ReplaceFindFormatButton, ReplaceFindClearFormatButton);
        SetFormatState(_replaceFormatDiff is not null, DialogText(FindReplaceDialogText.ReplaceFormatSetToolTip), ReplaceWithFormatButton, ReplaceWithClearFormatButton);
    }

    private static void SetFormatState(bool isSet, string toolTip, Button formatButton, Button clearButton)
    {
        formatButton.Content = isSet
            ? DialogText(FindReplaceDialogText.FormatSetButton)
            : DialogText(FindReplaceDialogText.Format);
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

    private void ApplySharedDialogSchema()
    {
        Title = DialogText(FindReplaceDialogText.Title);
        FindTab.Header = DialogText(FindReplaceDialogText.Find);
        ReplaceTab.Header = DialogText(FindReplaceDialogText.Replace);
        FindWhatLabel.Content = DialogText(FindReplaceDialogText.FindWhat);
        ReplaceFindWhatLabel.Content = DialogText(FindReplaceDialogText.FindWhat);
        ReplaceWithLabel.Content = DialogText(FindReplaceDialogText.ReplaceWith);

        foreach (var button in new[] { FindFormatButton, ReplaceFindFormatButton, ReplaceWithFormatButton })
            button.Content = DialogText(FindReplaceDialogText.Format);
        foreach (var button in new[] { FindClearFormatButton, ReplaceFindClearFormatButton, ReplaceWithClearFormatButton })
            button.Content = DialogText(FindReplaceDialogText.Clear);
        foreach (var button in new[] { FindChooseFormatFromCellButton, ReplaceFindChooseFormatFromCellButton, ReplaceWithChooseFormatFromCellButton })
            button.Content = DialogText(FindReplaceDialogText.ChooseFromCell);

        OptionsExpander.Header = DialogText(FindReplaceDialogText.Options);
        WithinLabel.Content = DialogText(FindReplaceDialogText.Within);
        SearchLabel.Content = DialogText(FindReplaceDialogText.Search);
        LookInLabel.Content = DialogText(FindReplaceDialogText.LookIn);
        WithinCombo.ItemsSource = FindReplaceDialogSchema.WithinChoices
            .Select(choice => DialogText(choice.Text))
            .ToArray();
        SearchCombo.ItemsSource = FindReplaceDialogSchema.SearchChoices
            .Select(choice => DialogText(choice.Text))
            .ToArray();
        LookInCombo.ItemsSource = FindReplaceDialogSchema.LookInChoices
            .Select(choice => DialogText(choice.Text))
            .ToArray();
        WithinCombo.SelectedIndex = 0;
        SearchCombo.SelectedIndex = 0;
        LookInCombo.SelectedIndex = 0;
        MatchCaseBox.Content = DialogText(FindReplaceDialogText.MatchCase);
        MatchEntireBox.Content = DialogText(FindReplaceDialogText.MatchEntireCellContents);

        var resultHeaders = new[]
        {
            FindReplaceDialogText.Book,
            FindReplaceDialogText.Sheet,
            FindReplaceDialogText.Name,
            FindReplaceDialogText.Cell,
            FindReplaceDialogText.Value,
            FindReplaceDialogText.Formula,
        };
        for (var index = 0; index < resultHeaders.Length; index++)
            FindResultsGrid.Columns[index].Header = DialogText(resultHeaders[index]);

        FindAllBtn.Content = DialogText(FindReplaceDialogText.FindAll);
        FindNextBtn.Content = DialogText(FindReplaceDialogText.FindNext);
        ReplaceBtn.Content = DialogText(FindReplaceDialogText.Replace);
        ReplaceAllBtn.Content = DialogText(FindReplaceDialogText.ReplaceAll);
        CloseBtn.Content = DialogText(FindReplaceDialogText.Close);
    }

    private static string DialogText(FindReplaceDialogText text, params object?[] arguments) =>
        FindReplaceDialogSchema.Resolve(text, UiText.Get, UiText.Format, arguments: arguments);
}
