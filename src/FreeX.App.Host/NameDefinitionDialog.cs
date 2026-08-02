using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <param name="ScopeSheetId">
///   The actual scope identity to route Define/Delete against: null for workbook-global, or the
///   target sheet's <see cref="SheetId"/> for a sheet-scoped name (Excel "localSheetId"). Tracked
///   separately from <paramref name="Scope"/> (the display label shown in the Scope combo/column)
///   because a worksheet can legally be named exactly "Workbook" -- nothing in
///   <see cref="FreeX.Core.Model.Workbook.ValidateSheetNameStructure"/> reserves that text -- which
///   would otherwise make the label alone ambiguous with the workbook-global scope sentinel.
/// </param>
public sealed record NameDefinitionDialogResult(string Name, string Scope, string Comment, string RefersTo, SheetId? ScopeSheetId = null);

/// <summary>
/// A choice offered by the Name Manager's Scope combo: <see cref="Label"/> is the text shown to the
/// user (matching Excel, which always displays "Workbook" for the global scope regardless of any
/// sheet's own name), while <see cref="SheetId"/> is the real, non-collidable identity used to route
/// Define/Delete commands. Two options may legitimately share the same <see cref="Label"/> (the
/// workbook-global sentinel and a worksheet literally named "Workbook"); they are still distinct
/// entries here because they carry different <see cref="SheetId"/> values.
/// </summary>
internal readonly record struct NamedRangeScopeOption(string Label, SheetId? SheetId)
{
    public override string ToString() => Label;

    /// <summary>Lets call sites/tests still write a plain scope-label string for the common (no
    /// same-named-sheet-collision) case; always maps to the workbook-global scope.</summary>
    public static implicit operator NamedRangeScopeOption(string label) => new(label, null);
}

internal sealed class NameDefinitionDialog : Window
{
    private readonly TextBox _nameBox = new();
    private readonly ComboBox _scopeBox = new();
    private readonly TextBox _commentBox = new();
    private readonly TextBox _refersToBox = new();
    private readonly Button _rangePickerButton = new() { Content = "...", Width = 26 };
    private readonly IReadOnlyList<NamedRangeScopeOption> _scopeOptions;
    private readonly Action<NamedRangeSelectionRequest>? _requestRangeSelection;
    private readonly Func<string, bool> _isValidRange;
    private readonly Func<string, string?> _validateName;

    public NameDefinitionDialogResult Result { get; private set; }
    public NamedRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public NameDefinitionDialog(
        NameDefinitionDialogResult initial,
        IReadOnlyList<NamedRangeScopeOption> scopeOptions,
        Action<NamedRangeSelectionRequest>? requestRangeSelection = null,
        Func<string, bool>? isValidRange = null,
        Func<string, string?>? validateName = null)
    {
        Result = initial;
        _scopeOptions = scopeOptions.Count > 0 ? scopeOptions : [new NamedRangeScopeOption("Workbook", null)];
        _requestRangeSelection = requestRangeSelection;
        _isValidRange = isValidRange ?? (rangeText => !string.IsNullOrWhiteSpace(rangeText));
        _validateName = validateName ?? (_ => null);
        Title = string.IsNullOrWhiteSpace(initial.Name)
            ? UiText.Get("NameDefinition_NewNameTitle")
            : UiText.Get("NameDefinition_EditNameTitle");
        Width = 460;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _nameBox.Text = initial.Name;
        AutomationProperties.SetName(_nameBox, UiText.Get("NameDefinition_NameAutomationName"));
        foreach (var scope in _scopeOptions)
            _scopeBox.Items.Add(scope);
        _scopeBox.SelectedItem = FindScopeOption(initial.Scope, initial.ScopeSheetId) ?? _scopeOptions[0];
        AutomationProperties.SetName(_scopeBox, UiText.Get("NameDefinition_ScopeAutomationName"));
        _commentBox.Text = initial.Comment;
        AutomationProperties.SetName(_commentBox, UiText.Get("NameDefinition_CommentAutomationName"));
        _refersToBox.Text = initial.RefersTo;
        AutomationProperties.SetName(_refersToBox, UiText.Get("NameDefinition_RefersToAutomationName"));
        _rangePickerButton.ToolTip = UiText.Get("NameDefinition_RangePickerToolTip");
        AutomationProperties.SetName(_rangePickerButton, UiText.Get("NameDefinition_RangePickerAutomationName"));
        AutomationProperties.SetHelpText(_rangePickerButton, UiText.Get("NameDefinition_RangePickerHelpText"));
        _rangePickerButton.Click += (_, _) =>
        {
            RangeSelectionRequest = NamedRangeDialog.CreateRangeSelectionRequest(
                NamedRangeSelectionTarget.DefinitionRefersTo,
                _refersToBox.Text);
            _requestRangeSelection?.Invoke(RangeSelectionRequest);
            _refersToBox.Focus();
            _refersToBox.SelectAll();
            Keyboard.Focus(_refersToBox);
        };

        Content = CreateContent();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    /// <summary>
    /// Resolves the combo entry that matches the original scope. Prefers an exact identity match
    /// (<paramref name="scopeSheetId"/>) so a worksheet literally named "Workbook" (see
    /// <see cref="NamedRangeScopeOption"/>) is preselected correctly even though its label collides
    /// with the workbook-global sentinel; falls back to a label match only when no identity was
    /// supplied (e.g. a caller that only ever deals in workbook-global names).
    /// </summary>
    private NamedRangeScopeOption? FindScopeOption(string scopeName, SheetId? scopeSheetId)
    {
        foreach (var scope in _scopeOptions)
        {
            if (Nullable.Equals(scope.SheetId, scopeSheetId))
                return scope;
        }

        foreach (var scope in _scopeOptions)
        {
            if (string.Equals(scope.Label, scopeName, StringComparison.OrdinalIgnoreCase))
                return scope;
        }

        return null;
    }

    private Grid CreateContent()
    {
        var grid = new Grid { Margin = new Thickness(16) };
        for (var row = 0; row < 5; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddTextRow(grid, 0, UiText.Get("NameDefinition_NameLabel"), _nameBox);
        AddComboRow(grid, 1, UiText.Get("NameDefinition_ScopeLabel"), _scopeBox);
        AddTextRow(grid, 2, UiText.Get("NameDefinition_CommentLabel"), _commentBox);
        AddRefersToRow(grid, 3);

        var buttons = DialogButtonRowFactory.Create(Accept, 72);
        buttons.Margin = new Thickness(0, 8, 0, 0);
        grid.Children.Add(buttons);
        Grid.SetRow(buttons, 4);
        Grid.SetColumnSpan(buttons, 3);
        return grid;
    }

    private static void AddTextRow(Grid grid, int row, string label, TextBox box)
    {
        grid.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);
        box.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        Grid.SetColumnSpan(box, 2);
    }

    private static void AddComboRow(Grid grid, int row, string label, ComboBox box)
    {
        grid.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);
        box.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        Grid.SetColumnSpan(box, 2);
    }

    private void AddRefersToRow(Grid grid, int row)
    {
        grid.Children.Add(new Label { Content = UiText.Get("NameDefinition_RefersToLabel"), Target = _refersToBox, Padding = new Thickness(0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);
        _refersToBox.Margin = new Thickness(0, 0, 4, 8);
        grid.Children.Add(_refersToBox);
        Grid.SetRow(_refersToBox, row);
        Grid.SetColumn(_refersToBox, 1);
        _rangePickerButton.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(_rangePickerButton);
        Grid.SetRow(_rangePickerButton, row);
        Grid.SetColumn(_rangePickerButton, 2);
    }

    private void Accept()
    {
        var name = _nameBox.Text.Trim();
        var nameError = ValidateNameInput(name, _validateName);
        if (nameError is not null)
        {
            DialogMessageHelper.ShowWarning(this, nameError, Title);
            FocusNameInput();
            return;
        }

        if (!_isValidRange(_refersToBox.Text.Trim()))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("NameDefinition_InvalidRangeFormatMessage"), Title);
            FocusRefersToInput();
            return;
        }

        var selectedScope = _scopeBox.SelectedItem as NamedRangeScopeOption? ?? _scopeOptions[0];
        Result = new NameDefinitionDialogResult(
            name,
            selectedScope.Label.Trim(),
            _commentBox.Text.Trim(),
            _refersToBox.Text.Trim(),
            selectedScope.SheetId);
        DialogResult = true;
    }

    internal static string? ValidateNameInput(string name, Func<string, string?> validateName)
    {
        if (string.IsNullOrWhiteSpace(name))
            return UiText.Get("NameDefinition_PleaseEnterNameMessage");

        return validateName(name.Trim());
    }

    private void FocusNameInput()
    {
        _nameBox.Focus();
        _nameBox.SelectAll();
        Keyboard.Focus(_nameBox);
    }

    private void FocusRefersToInput()
    {
        _refersToBox.Focus();
        _refersToBox.SelectAll();
        Keyboard.Focus(_refersToBox);
    }

    public void ApplyRangeSelection(string rangeText)
    {
        _refersToBox.Text = rangeText;
        FocusRefersToInput();
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusNameInput();
    }
}
