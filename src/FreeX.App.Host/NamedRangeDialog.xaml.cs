using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// Named Range Manager dialog.
/// Allows the user to define, view, and delete named ranges in the workbook.
/// </summary>
public sealed partial class NamedRangeDialog : Window
{
    private readonly Workbook _workbook;
    private readonly Func<IWorkbookCommand, CommandOutcome> _executeCommand;
    private readonly DefinedNamesSession _definedNames;
    private readonly Action<NamedRangeSelectionRequest>? _requestRangeSelection;
    private readonly List<DefinedNameRow> _items = [];
    private readonly string _initialRefersTo;
    private NameDefinitionDialog? _activeDefinitionDialog;
    private DefinedNameIdentity? _activeDefinitionOriginal;

    public NamedRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    /// <param name="workbook">The active workbook.</param>
    /// <param name="executeCommand">Session-owned executor for define/delete commands.</param>
    /// <param name="initialRange">
    ///   Optional initial range (e.g. the current selection). If provided, pre-fills
    ///   the Range text box in Sheet!A1:B10 notation.
    /// </param>
    public NamedRangeDialog(
        Workbook workbook,
        Func<IWorkbookCommand, CommandOutcome> executeCommand,
        GridRange? initialRange = null,
        Action<NamedRangeSelectionRequest>? requestRangeSelection = null)
    {
        _workbook = workbook;
        _executeCommand = executeCommand;
        _definedNames = new DefinedNamesSession(workbook, initialRange?.Start.Sheet);
        _requestRangeSelection = requestRangeSelection;
        InitializeComponent();
        FilterBox.ItemsSource = DefinedNameUiPolicy.Filters
            .Select(descriptor => UiText.Get(descriptor.LabelResourceKey))
            .ToList();
        FilterBox.SelectedIndex = 0;
        RefreshList();
        UpdateSelectionCommands();

        _initialRefersTo = initialRange.HasValue ? _definedNames.FormatRefersTo(initialRange.Value) : "";
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    // ── List management ───────────────────────────────────────────────────────

    private void RefreshList()
    {
        _items.Clear();
        _items.AddRange(_definedNames.BuildRows());

        ApplyFilter();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void NamesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var plan = DefinedNameUiPolicy.PlanManagerSelection(
            NamesList.SelectedItem as DefinedNameRow,
            DefinedNameUiProfile.Wpf);
        if (plan.ShouldUpdateRefersTo)
            RefersToBox.Text = plan.RefersToText;
        UpdateSelectionCommands();
    }

    private void NamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (NamesList.SelectedItem is not DefinedNameRow)
            return;

        EditButton_Click(sender, e);
        e.Handled = true;
    }

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (NamesList is null)
            return;

        var selected = DefinedNameUiPolicy.ResolveFilter(FilterBox.SelectedIndex);

        NamesList.ItemsSource = _definedNames.ProjectRows(_items, selected).ToList();
        if (NamesList.SelectedItem is not DefinedNameRow)
        {
            RefersToBox.Clear();
            UpdateSelectionCommands();
        }
    }

    private void UpdateSelectionCommands()
    {
        var plan = DefinedNameUiPolicy.PlanManagerSelection(
            NamesList.SelectedItem as DefinedNameRow,
            DefinedNameUiProfile.Wpf);
        EditButton.IsEnabled = plan.CanEdit;
        DeleteButton.IsEnabled = plan.CanDelete;
        RefersToPickerButton.IsEnabled = plan.CanSelectRefersTo;
    }

    private void RefersToPickerButton_Click(object sender, RoutedEventArgs e)
    {
        RangeSelectionRequest = CreateRangeSelectionRequest(
            NamedRangeSelectionTarget.SelectedNameRefersTo,
            RefersToBox.Text);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        RefersToBox.Focus();
        RefersToBox.SelectAll();
        Keyboard.Focus(RefersToBox);
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NameDefinitionDialog(
            new NameDefinitionDialogResult("", DefinedNameScope.WorkbookLabel, "", _initialRefersTo),
            GetScopeOptions(),
            RequestRangeSelection,
            isValidRange: rangeText => _definedNames.ValidateRefersTo(rangeText).IsValid,
            validateName: ValidateNameForNativeDialog) { Owner = this };
        ShowNameDefinitionDialog(dialog, originalName: null, originalScope: null, originalScopeSheetId: null);
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (NamesList.SelectedItem is not DefinedNameRow vm)
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("NamedRange_SelectEditMessage"), UiText.Get("NamedRange_NamedRangeTitle"));
            FocusNamesListOrNewButton();
            return;
        }

        var dialog = new NameDefinitionDialog(
            new NameDefinitionDialogResult(vm.Name, vm.ScopeLabel, vm.Comment, vm.RefersTo, vm.Scope.SheetId),
            GetScopeOptions(),
            RequestRangeSelection,
            isValidRange: rangeText => _definedNames.ValidateRefersTo(rangeText).IsValid,
            validateName: ValidateNameForNativeDialog)
        {
            Owner = this
        };

        ShowNameDefinitionDialog(
            dialog,
            originalName: vm.Name,
            originalScope: vm.ScopeLabel,
            originalScopeSheetId: vm.Scope.SheetId);
    }

    private void ShowNameDefinitionDialog(
        NameDefinitionDialog dialog,
        string? originalName,
        string? originalScope,
        SheetId? originalScopeSheetId)
    {
        _activeDefinitionDialog = dialog;
        // Mirrors the identity DefineOrUpdateName itself excludes from its post-close duplicate
        // check (see below): null for a brand-new name, or the entry being edited so re-saving it
        // unchanged (or under the same name/scope) never flags itself as a duplicate of itself.
        _activeDefinitionOriginal = originalName is null
            ? null
            : new DefinedNameIdentity(originalName, _definedNames.GetScope(originalScopeSheetId));
        try
        {
            if (dialog.ShowDialog() == true)
                DefineOrUpdateName(dialog.Result, originalName, originalScope, originalScopeSheetId);
        }
        finally
        {
            _activeDefinitionDialog = null;
            _activeDefinitionOriginal = null;
        }
    }

    /// <summary>
    /// R114-app-name-manager-workbook-sentinel-3-2: the target scope identity is now threaded
    /// end-to-end from the Scope combo's actual selection (<see cref="NameDefinitionDialogResult.ScopeSheetId"/>,
    /// populated by <see cref="NameDefinitionDialog"/> from the chosen <see cref="DefinedNameScopeOption"/>)
    /// rather than re-derived here from the display label. A worksheet can legally be named exactly
    /// "Workbook" (nothing in <see cref="Workbook.ValidateSheetNameStructure"/> reserves that text),
    /// which would make a label-based lookup here indistinguishable from the workbook-global scope
    /// sentinel -- silently misrouting Define/Delete to the wrong scope (or the wrong pre-existing
    /// name of the same text) for any name actually scoped to that sheet. There is deliberately no
    /// "ResolveScopeSheetId(string)" helper here any more: every caller that needs a scope identity
    /// already has the real one in hand (from the dialog result or from a row's own
    /// <see cref="DefinedNameRow.Scope"/>) and must use that directly.
    /// </summary>
    private void DefineOrUpdateName(
        NameDefinitionDialogResult definition,
        string? originalName,
        string? originalScope,
        SheetId? originalScopeSheetId)
    {
        var draft = DefinedNameUiPolicy.CreateDraft(
            definition.Name,
            _definedNames.GetScope(definition.ScopeSheetId),
            definition.RefersTo,
            definition.Comment);
        DefinedNameIdentity? original = originalName is null
            ? null
            : new DefinedNameIdentity(originalName, _definedNames.GetScope(originalScopeSheetId));
        var plan = _definedNames.PlanSave(draft, original);
        if (!plan.Validation.Name.IsValid)
        {
            DialogMessageHelper.ShowWarning(
                this,
                DescribeDraftNameError(plan.Validation.Name.Error, plan.Draft.Name),
                UiText.Get("NamedRange_NamedRangeTitle"));
            FocusNamesListOrNewButton();
            return;
        }

        if (!plan.Validation.RefersTo.IsValid)
        {
            DialogMessageHelper.ShowWarning(
                this,
                DescribeRefersToError(plan.Validation.RefersTo.Error),
                UiText.Get("NamedRange_NamedRangeTitle"));
            FocusRefersToSummary();
            return;
        }

        _ = originalScope;

        // NOTE: renaming an existing name (or moving it to a different scope) intentionally does
        // NOT remove the old entry first. FreeX resolves names in formulas by literal text (e.g.
        // =SUM(Revenue)), and nothing rewrites referencing formulas old-name -> new-name on rename;
        // removing the old entry here would turn every such formula into #NAME? the instant the
        // rename is applied. Leaving the old entry in place means a rename creates a second,
        // orphaned name alongside the new one (visible/deletable from Name Manager like any other
        // name) - a lesser, cosmetic bug compared to silently breaking live formulas. The correct
        // fix is a dedicated rename command that updates the name and rewrites every referencing
        // formula via a FormulaRewriter (the same way a sheet rename does); that is deferred pending
        // that plumbing.

        var outcome = _executeCommand(plan.Command!);
        if (!outcome.Success)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get("NamedRange_DefineFailedMessage"), UiText.Get("NamedRange_NamedRangeTitle"));
            FocusNamesListOrNewButton();
            return;
        }

        RefreshList();
        if (DefinedNameUiPolicy.FindRow(_items, plan.Draft.Name, plan.Draft.Scope) is { } updated)
        {
            ApplyFilter();
            NamesList.SelectedItem = updated;
            RefersToBox.Text = updated.RefersTo;
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (NamesList.SelectedItem is not DefinedNameRow vm)
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("NamedRange_SelectDeleteMessage"), UiText.Get("NamedRange_NamedRangeTitle"));
            FocusNamesListOrNewButton();
            return;
        }

        if (!DialogMessageHelper.AskYesNo(this, UiText.Format("NamedRange_DeleteConfirmation", vm.Name), UiText.Get("NamedRange_NameManager")))
        {
            return;
        }

        var cmd = _definedNames.BuildDeleteCommand(vm);
        var outcome = _executeCommand(cmd);
        if (!outcome.Success)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get("NamedRange_DeleteFailedMessage"), UiText.Get("NamedRange_NamedRangeTitle"));
            FocusNamesListOrNewButton();
        }
        else
            RefreshList();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void FocusInitialKeyboardTarget()
    {
        FocusNamesListOrNewButton();
    }

    private void FocusNamesListOrNewButton()
    {
        if (NamesList.Items.Count > 0)
        {
            NamesList.Focus();
            Keyboard.Focus(NamesList);
            return;
        }

        NewButton.Focus();
        Keyboard.Focus(NewButton);
    }

    private void FocusRefersToSummary()
    {
        RefersToBox.Focus();
        RefersToBox.SelectAll();
        Keyboard.Focus(RefersToBox);
    }

    /// <summary>
    /// Builds the Scope combo's choices: the workbook-global sentinel first, then every sheet in
    /// workbook order. Deliberately does NOT de-duplicate by display label: sheet names are already
    /// guaranteed unique among themselves (<see cref="Workbook.ValidateSheetName"/>), so the only
    /// possible label collision is between the global sentinel and a sheet literally named
    /// "Workbook" -- and those two must remain two distinct entries (different
    /// <see cref="DefinedNameScopeOption.SheetId"/>) or that sheet's own scope could never be
    /// selected/preselected at all (see the R114-app-name-manager-workbook-sentinel-3-2 doc comment
    /// on <see cref="DefineOrUpdateName"/>).
    /// </summary>
    private IReadOnlyList<DefinedNameScopeOption> GetScopeOptions() =>
        DefinedNameUiPolicy.BuildScopeOptions(_definedNames.ScopeChoices);

    /// <summary>
    /// R162-name-manager-f1: this used to route through <see cref="DefinedNamesSession.ValidateNameStructure"/>,
    /// which only checks structural validity plus collisions with structured-table names -- it never
    /// consulted the workbook's own defined names/formulas at all, so the New/Edit Name dialog would
    /// close successfully on a real duplicate (only the later <see cref="DefinedNamesSession.PlanSave"/>
    /// call in <see cref="DefineOrUpdateName"/> caught it, by which point the dialog -- and everything
    /// the user typed into it -- was already gone). <see cref="DefinedNamesSession.ValidateName"/> is the
    /// full scope-aware duplicate check (the same one Avalonia's live validation and this dialog's own
    /// post-close <see cref="DefineOrUpdateName"/> already use), with <see cref="_activeDefinitionOriginal"/>
    /// excluded so editing a name in place without renaming it does not flag itself.
    /// </summary>
    private string? ValidateNameForNativeDialog(string name, DefinedNameScope scope)
    {
        var result = _definedNames.ValidateName(name, scope, _activeDefinitionOriginal);
        return result.IsValid ? null : DescribeNameError(result.Error);
    }

    private static string DescribeNameError(DefinedNameError error) =>
        DefinedNameValidationMessages.Describe(error).Resolve(UiText.Get);

    private static string DescribeDraftNameError(DefinedNameError error, string name) =>
        error == DefinedNameError.Duplicate
            ? UiText.Format("NamedRange_NameAlreadyExistsInScopeMessage", name)
            : DescribeNameError(error);

    private static string DescribeRefersToError(RefersToError error) =>
        RefersToValidationMessages.Describe(error).Resolve(UiText.Get);

    public static NamedRangeSelectionRequest CreateRangeSelectionRequest(
        NamedRangeSelectionTarget target,
        string currentText) =>
        DefinedNameUiPolicy.CreateRangeSelectionRequest(target, currentText);

    private void RequestRangeSelection(NamedRangeSelectionRequest request)
    {
        RangeSelectionRequest = request;
        _requestRangeSelection?.Invoke(request);
    }

    public void ApplyRangeSelection(NamedRangeSelectionTarget target, string rangeText)
    {
        if (target == NamedRangeSelectionTarget.DefinitionRefersTo && _activeDefinitionDialog is { } definitionDialog)
        {
            definitionDialog.ApplyRangeSelection(rangeText);
            return;
        }

        RefersToBox.Text = rangeText;
        FocusRefersToSummary();
    }
}

