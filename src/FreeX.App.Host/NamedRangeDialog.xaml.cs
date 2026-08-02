using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.NamedRanges;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// Named Range Manager dialog.
/// Allows the user to define, view, and delete named ranges in the workbook.
/// </summary>
public sealed partial class NamedRangeDialog : Window
{
    private readonly Workbook _workbook;
    private readonly ICommandBus _commandBus;
    private readonly Action<NamedRangeSelectionRequest>? _requestRangeSelection;
    private readonly ObservableCollection<NamedRangeViewModel> _items = [];
    private readonly string _initialRefersTo;
    private NameDefinitionDialog? _activeDefinitionDialog;

    public NamedRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    /// <param name="workbook">The active workbook.</param>
    /// <param name="commandBus">Command bus for dispatching define/delete commands.</param>
    /// <param name="initialRange">
    ///   Optional initial range (e.g. the current selection). If provided, pre-fills
    ///   the Range text box in Sheet!A1:B10 notation.
    /// </param>
    public NamedRangeDialog(
        Workbook workbook,
        ICommandBus commandBus,
        GridRange? initialRange = null,
        Action<NamedRangeSelectionRequest>? requestRangeSelection = null)
    {
        _workbook = workbook;
        _commandBus = commandBus;
        _requestRangeSelection = requestRangeSelection;
        InitializeComponent();
        RefreshList();
        UpdateSelectionCommands();

        _initialRefersTo = initialRange.HasValue ? FormatRange(initialRange.Value, workbook) : "";
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    // ── List management ───────────────────────────────────────────────────────

    private void RefreshList()
    {
        _items.Clear();
        foreach (var (name, range) in _workbook.NamedRanges)
        {
            var metadata = _workbook.TryGetNamedRangeMetadata(name, out var savedMetadata)
                ? savedMetadata
                : NamedRangeMetadata.WorkbookScope;
            _items.Add(new NamedRangeViewModel(
                name,
                FormatValue(range, _workbook),
                FormatRange(range, _workbook),
                metadata.Scope,
                metadata.Comment,
                scopeSheetId: null));
        }

        // Sheet-scoped names (Excel "localSheetId") are stored separately from the workbook-global
        // NamedRanges dictionary and must also be listed, or they're invisible and unreachable
        // through the Name Manager's Edit/Delete actions.
        foreach (var ((name, scopeSheetId), range) in _workbook.ScopedNamedRanges)
        {
            _workbook.TryGetScopedNamedRangeMetadata(name, scopeSheetId, out var metadata);
            var scopeLabel = _workbook.GetSheet(scopeSheetId)?.Name ?? metadata.Scope;
            _items.Add(new NamedRangeViewModel(
                name,
                FormatValue(range, _workbook),
                FormatRange(range, _workbook),
                scopeLabel,
                metadata.Comment,
                scopeSheetId));
        }

        // Named formulas/constants (Excel names whose "Refers To" is a formula expression, e.g.
        // "=1.05" or "=SUM(Sheet1!A:A)", rather than a plain cell range) live in a separate
        // dictionary from NamedRanges and must also be listed, or a whole class of commonly-used
        // Excel defined names is invisible in the Name Manager and unreachable for Edit/Delete.
        foreach (var (name, formulaText) in _workbook.NamedFormulas)
        {
            var metadata = _workbook.TryGetNamedRangeMetadata(name, out var savedMetadata)
                ? savedMetadata
                : NamedRangeMetadata.WorkbookScope;
            _items.Add(new NamedRangeViewModel(
                name, FormatNamedFormulaValue(_workbook, formulaText, scopeSheetId: null), formulaText, metadata.Scope, metadata.Comment,
                scopeSheetId: null));
        }

        foreach (var ((name, scopeSheetId), formulaText) in _workbook.ScopedNamedFormulas)
        {
            _workbook.TryGetScopedNamedRangeMetadata(name, scopeSheetId, out var metadata);
            var scopeLabel = _workbook.GetSheet(scopeSheetId)?.Name ?? metadata.Scope;
            _items.Add(new NamedRangeViewModel(
                name, FormatNamedFormulaValue(_workbook, formulaText, scopeSheetId), formulaText, scopeLabel, metadata.Comment,
                scopeSheetId));
        }

        ApplyFilter();
    }

    private static string FormatRange(GridRange range, Workbook wb)
    {
        var sheet = wb.GetSheet(range.Start.Sheet);
        var sheetName = sheet?.Name ?? "Sheet1";
        var start = range.Start.ToA1();
        var end = range.End.ToA1();
        return $"{sheetName}!{start}:{end}";
    }

    /// <summary>
    /// R88-app-name-manager-ui-5-2: the Name Manager's Value column must show the name's actual
    /// live computed value/preview (real Excel: e.g. "1.05" for a named constant, "{100;200}" for a
    /// range name), not just repeat the Refers To text -- <see cref="FormatRange"/> above only
    /// formats the RANGE REFERENCE, never any cell content. A single-cell name shows that cell's own
    /// computed value; a multi-cell name shows a small array-literal preview (row-major, columns
    /// comma-separated, rows semicolon-separated, capped like Excel's own Name Manager bounds how
    /// much of a huge range's content it tries to render) and falls back to the plain range
    /// reference beyond that cap.
    /// </summary>
    private static string FormatValue(GridRange range, Workbook wb)
    {
        if (wb.GetSheet(range.Start.Sheet) is not { } sheet)
            return FormatRange(range, wb);

        var rowCount = checked((int)(range.End.Row - range.Start.Row + 1));
        var colCount = checked((int)(range.End.Col - range.Start.Col + 1));

        if (rowCount == 1 && colCount == 1)
            return FormatScalarValuePreview(sheet.GetCell(range.Start)?.Value);

        const int maxPreviewCells = 25;
        if ((long)rowCount * colCount > maxPreviewCells)
            return FormatRange(range, wb);

        var rowTexts = new List<string>(rowCount);
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            var cellTexts = new List<string>(colCount);
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                cellTexts.Add(FormatScalarValuePreview(sheet.GetCell(row, col)?.Value));
            rowTexts.Add(string.Join(",", cellTexts));
        }

        return "{" + string.Join(";", rowTexts) + "}";
    }

    /// <summary>
    /// R88-app-name-manager-ui-5-2: evaluates a named FORMULA/constant's own text (e.g. "1.05" for
    /// TaxRate, no leading "=" -- matching how Workbook.NamedFormulas/ScopedNamedFormulas store it,
    /// see <see cref="DefineOrUpdateNamedFormula"/>) so the Value column shows its live computed
    /// result rather than the formula source text again. Uses a fresh, throwaway
    /// <see cref="FormulaEvaluator"/> (the same one-off pattern used elsewhere for ad-hoc,
    /// non-cell-anchored evaluation, e.g. DataValidationService), evaluated against the name's own
    /// scope sheet when it has one, or the workbook's first sheet for a workbook-scoped name (a
    /// constant like "=1.05" has no sheet dependency, but FormulaEvaluator still needs a sheet
    /// context to resolve any unqualified references the formula text might contain).
    /// </summary>
    private static string FormatNamedFormulaValue(Workbook wb, string formulaText, SheetId? scopeSheetId)
    {
        var sheet = (scopeSheetId is { } sheetId ? wb.GetSheet(sheetId) : null) ?? wb.Sheets.FirstOrDefault();
        if (sheet is null)
            return formulaText;

        var result = new FormulaEvaluator().Evaluate(formulaText, sheet, wb);
        return FormatScalarValuePreview(result);
    }

    private static string FormatScalarValuePreview(ScalarValue? value) =>
        value switch
        {
            null or BlankValue => "",
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue dateTime => dateTime.Value.ToString(CultureInfo.InvariantCulture),
            ErrorValue error => error.Code,
            RangeValue rangeValue => FormatRangeValuePreview(rangeValue),
            _ => value.ToString() ?? ""
        };

    private static string FormatRangeValuePreview(RangeValue range)
    {
        var rowTexts = new List<string>(range.RowCount);
        for (var row = 1; row <= range.RowCount; row++)
        {
            var cellTexts = new List<string>(range.ColCount);
            for (var col = 1; col <= range.ColCount; col++)
                cellTexts.Add(FormatScalarValuePreview(range.At(row, col)));
            rowTexts.Add(string.Join(",", cellTexts));
        }

        return "{" + string.Join(";", rowTexts) + "}";
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void NamesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (NamesList.SelectedItem is NamedRangeViewModel vm)
        {
            RefersToBox.Text = vm.RefersTo;
        }

        UpdateSelectionCommands();
    }

    private void NamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (NamesList.SelectedItem is not NamedRangeViewModel)
            return;

        EditButton_Click(sender, e);
        e.Handled = true;
    }

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (NamesList is null)
            return;

        var selected = FilterBox.SelectedIndex switch
        {
            1 => NamedRangeFilterOption.Workbook,
            2 => NamedRangeFilterOption.Worksheet,
            3 => NamedRangeFilterOption.Errors,
            4 => NamedRangeFilterOption.NoErrors,
            _ => NamedRangeFilterOption.All
        };

        NamesList.ItemsSource = NamedRangeDialogPlanner.FilterItems(_items, selected).ToList();
        if (NamesList.SelectedItem is not NamedRangeViewModel)
        {
            RefersToBox.Clear();
            UpdateSelectionCommands();
        }
    }

    private void UpdateSelectionCommands()
    {
        var hasSelection = NamesList.SelectedItem is NamedRangeViewModel;
        EditButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
        RefersToPickerButton.IsEnabled = hasSelection;
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
            new NameDefinitionDialogResult("", "Workbook", "", _initialRefersTo),
            GetScopeOptions(),
            RequestRangeSelection,
            isValidRange: rangeText => NamedRangeInputParser.TryParseRange(_workbook, rangeText, out _) || IsPossibleNamedFormulaText(rangeText),
            validateName: _workbook.ValidateNamedRangeName) { Owner = this };
        ShowNameDefinitionDialog(dialog, originalName: null, originalScope: null, originalScopeSheetId: null);
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (NamesList.SelectedItem is not NamedRangeViewModel vm)
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("NamedRange_SelectEditMessage"), UiText.Get("NamedRange_NamedRangeTitle"));
            FocusNamesListOrNewButton();
            return;
        }

        var dialog = new NameDefinitionDialog(
            new NameDefinitionDialogResult(vm.Name, vm.Scope, vm.Comment, vm.RefersTo, vm.ScopeSheetId),
            GetScopeOptions(),
            RequestRangeSelection,
            isValidRange: rangeText => NamedRangeInputParser.TryParseRange(_workbook, rangeText, out _) || IsPossibleNamedFormulaText(rangeText),
            validateName: _workbook.ValidateNamedRangeName)
        {
            Owner = this
        };

        ShowNameDefinitionDialog(dialog, originalName: vm.Name, originalScope: vm.Scope, originalScopeSheetId: vm.ScopeSheetId);
    }

    private void ShowNameDefinitionDialog(
        NameDefinitionDialog dialog,
        string? originalName,
        string? originalScope,
        SheetId? originalScopeSheetId)
    {
        _activeDefinitionDialog = dialog;
        try
        {
            if (dialog.ShowDialog() == true)
                DefineOrUpdateName(dialog.Result, originalName, originalScope, originalScopeSheetId);
        }
        finally
        {
            _activeDefinitionDialog = null;
        }
    }

    /// <summary>
    /// R114-app-name-manager-workbook-sentinel-3-2: the target scope identity is now threaded
    /// end-to-end from the Scope combo's actual selection (<see cref="NameDefinitionDialogResult.ScopeSheetId"/>,
    /// populated by <see cref="NameDefinitionDialog"/> from the chosen <see cref="NamedRangeScopeOption"/>)
    /// rather than re-derived here from the display label. A worksheet can legally be named exactly
    /// "Workbook" (nothing in <see cref="Workbook.ValidateSheetNameStructure"/> reserves that text),
    /// which would make a label-based lookup here indistinguishable from the workbook-global scope
    /// sentinel -- silently misrouting Define/Delete to the wrong scope (or the wrong pre-existing
    /// name of the same text) for any name actually scoped to that sheet. There is deliberately no
    /// "ResolveScopeSheetId(string)" helper here any more: every caller that needs a scope identity
    /// already has the real one in hand (from the dialog result or from a row's own
    /// <see cref="NamedRangeViewModel.ScopeSheetId"/>) and must use that directly.
    /// </summary>
    private void DefineOrUpdateName(
        NameDefinitionDialogResult definition,
        string? originalName,
        string? originalScope,
        SheetId? originalScopeSheetId)
    {
        var name = definition.Name.Trim();
        var rangeText = definition.RefersTo.Trim();
        var scope = definition.Scope.Trim();
        var scopeSheetId = definition.ScopeSheetId;

        if (string.IsNullOrWhiteSpace(name))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("NamedRange_NameRequiredMessage"), UiText.Get("NamedRange_NamedRangeTitle"));
            FocusNamesListOrNewButton();
            return;
        }

        // Editing the exact same (name, scope) pair replaces that entry; anything else — a brand
        // new name, or an edit that changed the name and/or scope — must not silently clobber an
        // unrelated existing name already occupying that scope (Excel's New Name dialog rejects
        // this with "already exists"; cross-scope same-text names are fine and simply coexist).
        // Scope sameness is compared by actual identity (SheetId?), not by display label, so a
        // worksheet literally named "Workbook" can't be confused with the global scope sentinel here.
        var isEditingExisting = originalName is not null && originalScope is not null;
        var isSameEntry =
            isEditingExisting &&
            string.Equals(originalName, name, StringComparison.OrdinalIgnoreCase) &&
            Nullable.Equals(originalScopeSheetId, scopeSheetId);

        if (!NamedRangeInputParser.TryParseRange(_workbook, rangeText, out var range))
        {
            // Not a parseable cell range — Excel's Name Manager also supports named
            // FORMULAS/constants (Refers To can be any formula expression, e.g. "=1.05" or
            // "=SUM(Sheet1!A:A)", not just a range), so fall back to the formula counterpart of
            // DefineNamedRangeCommand below instead of rejecting outright.
            DefineOrUpdateNamedFormula(name, rangeText, scopeSheetId, isSameEntry);
            return;
        }

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

        var cmd = new DefineNamedRangeCommand(
            name,
            range,
            new NamedRangeMetadata(scope, definition.Comment.Trim()),
            scopeSheetId,
            allowRedefine: isSameEntry);
        var outcome = _commandBus.Execute(_workbook.Id, cmd);
        if (!outcome.Success)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get("NamedRange_DefineFailedMessage"), UiText.Get("NamedRange_NamedRangeTitle"));
            FocusNamesListOrNewButton();
            return;
        }

        RefreshList();
        if (FindItemByName(name) is { } updated)
        {
            ApplyFilter();
            NamesList.SelectedItem = updated;
            RefersToBox.Text = updated.RefersTo;
        }
    }

    /// <summary>
    /// Defines or updates a named FORMULA/constant (Refers To text that isn't a parseable cell
    /// range) via <see cref="DefineNamedFormulaCommand"/> — the formula counterpart of
    /// <see cref="DefineNamedRangeCommand"/> used by <see cref="DefineOrUpdateName"/> for ranges.
    /// Unlike DefineNamedRangeCommand, DefineNamedFormulaCommand always overwrites any existing
    /// formula of the same name/scope (it has no allowRedefine guard), so New/Edit's own-scope
    /// duplicate rejection is performed here first, mirroring the range branch's behavior. Takes the
    /// already-resolved <paramref name="scopeSheetId"/> identity directly (see
    /// <see cref="DefineOrUpdateName"/>'s doc comment for why no scope-label lookup happens here).
    /// </summary>
    private void DefineOrUpdateNamedFormula(string name, string rangeText, SheetId? scopeSheetId, bool isSameEntry)
    {
        var formulaText = rangeText.StartsWith('=') ? rangeText[1..].Trim() : rangeText;
        if (string.IsNullOrWhiteSpace(formulaText))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("NamedRange_InvalidRangeFormatMessage"), UiText.Get("NamedRange_NamedRangeTitle"));
            FocusRefersToSummary();
            return;
        }

        if (!isSameEntry && NameAlreadyExistsInScope(name, scopeSheetId))
        {
            DialogMessageHelper.ShowWarning(this, $"The name '{name}' already exists in this scope.", UiText.Get("NamedRange_NamedRangeTitle"));
            FocusNamesListOrNewButton();
            return;
        }

        var cmd = new DefineNamedFormulaCommand(name, formulaText, scopeSheetId);
        var outcome = _commandBus.Execute(_workbook.Id, cmd);
        if (!outcome.Success)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get("NamedRange_DefineFailedMessage"), UiText.Get("NamedRange_NamedRangeTitle"));
            FocusNamesListOrNewButton();
            return;
        }

        RefreshList();
        if (FindItemByName(name) is { } updated)
        {
            ApplyFilter();
            NamesList.SelectedItem = updated;
            RefersToBox.Text = updated.RefersTo;
        }
    }

    /// <summary>
    /// Whether <paramref name="name"/> is already defined (as either a range or a formula) in the
    /// exact target scope — used to reject a brand-new named-formula create/rename that would
    /// otherwise silently overwrite an unrelated existing definition, since
    /// <see cref="DefineNamedFormulaCommand"/> itself has no such guard.
    /// </summary>
    private bool NameAlreadyExistsInScope(string name, SheetId? scopeSheetId) =>
        scopeSheetId is { } sheetId
            ? _workbook.ScopedNamedRanges.ContainsKey((name, sheetId)) || _workbook.ScopedNamedFormulas.ContainsKey((name, sheetId))
            : _workbook.NamedRanges.ContainsKey(name) || _workbook.NamedFormulas.ContainsKey(name);

    /// <summary>
    /// Accepts any non-blank Refers To text as a potential named formula/constant (Excel's New
    /// Name dialog allows any formula expression there, not just ranges); actual validity is
    /// resolved on demand by the formula engine, matching Excel's own lazy behavior.
    /// </summary>
    private static bool IsPossibleNamedFormulaText(string rangeText) => !string.IsNullOrWhiteSpace(rangeText);

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (NamesList.SelectedItem is not NamedRangeViewModel vm)
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("NamedRange_SelectDeleteMessage"), UiText.Get("NamedRange_NamedRangeTitle"));
            FocusNamesListOrNewButton();
            return;
        }

        if (!DialogMessageHelper.AskYesNo(this, UiText.Format("NamedRange_DeleteConfirmation", vm.Name), UiText.Get("NamedRange_NameManager")))
        {
            return;
        }

        // Uses the row's own tracked scope identity directly (not a re-resolution of vm.Scope's
        // display label) -- see the R114-app-name-manager-workbook-sentinel-3-2 doc comment on
        // DefineOrUpdateName for why: a worksheet can legally be named exactly "Workbook", which
        // would otherwise make Delete indistinguishable from the workbook-global scope and either
        // fail outright or silently remove an unrelated pre-existing global name of the same text.
        var cmd = new RemoveNamedRangeCommand(vm.Name, vm.ScopeSheetId);
        var outcome = _commandBus.Execute(_workbook.Id, cmd);
        if (!outcome.Success)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get("NamedRange_DeleteFailedMessage"), UiText.Get("NamedRange_NamedRangeTitle"));
            FocusNamesListOrNewButton();
        }
        else
            RefreshList();
    }

    private NamedRangeViewModel? FindItemByName(string name)
    {
        foreach (var item in _items)
        {
            if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
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
    /// <see cref="NamedRangeScopeOption.SheetId"/>) or that sheet's own scope could never be
    /// selected/preselected at all (see the R114-app-name-manager-workbook-sentinel-3-2 doc comment
    /// on <see cref="DefineOrUpdateName"/>).
    /// </summary>
    private IReadOnlyList<NamedRangeScopeOption> GetScopeOptions()
    {
        var options = new List<NamedRangeScopeOption> { new("Workbook", null) };
        options.AddRange(_workbook.Sheets.Select(sheet => new NamedRangeScopeOption(sheet.Name, sheet.Id)));
        return options;
    }

    public static NamedRangeSelectionRequest CreateRangeSelectionRequest(
        NamedRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

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

public enum NamedRangeSelectionTarget
{
    SelectedNameRefersTo,
    DefinitionRefersTo
}

public sealed record NamedRangeSelectionRequest(
    NamedRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

