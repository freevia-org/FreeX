using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

/// <summary>
/// Goal Seek dialog — lets the user specify a set cell, a target value,
/// and a changing cell. The owning window runs GoalSeekService and applies
/// the result via GoalSeekCommand if the user confirms.
/// </summary>
public partial class GoalSeekDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly Action<GoalSeekRangeSelectionRequest>? _requestRangeSelection;

    public CellAddress? SetCell { get; private set; }
    public double TargetValue { get; private set; }
    public CellAddress? ChangingCell { get; private set; }
    public GoalSeekRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    /// <param name="sheetId">The active sheet ID, used when parsing bare A1 references.</param>
    /// <param name="selectedCell">Optional pre-selected cell to pre-populate the Set Cell box.</param>
    public GoalSeekDialog(
        SheetId sheetId,
        CellAddress? selectedCell,
        Action<GoalSeekRangeSelectionRequest>? requestRangeSelection = null)
    {
        _sheetId = sheetId;
        _requestRangeSelection = requestRangeSelection;
        InitializeComponent();

        if (selectedCell.HasValue)
            SetCellBox.Text = selectedCell.Value.ToA1();

        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        SetCellBox.Focus();
        SetCellBox.SelectAll();
        Keyboard.Focus(SetCellBox);
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!GoalSeekInputParser.TryParse(
                _sheetId,
                SetCellBox.Text,
                ToValueBox.Text,
                ChangingCellBox.Text,
                out var input,
                out var error))
        {
            MessageBox.Show(this, error, "Goal Seek", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidInput(error);
            return;
        }

        SetCell = input.SetCell;
        TargetValue = input.TargetValue;
        ChangingCell = input.ChangingCell;
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void FocusInvalidInput(string error)
    {
        var target = ResolveInvalidInputTarget(error);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }

    private TextBox ResolveInvalidInputTarget(string error)
    {
        if (string.Equals(error, "Please enter the Set cell address.", StringComparison.Ordinal) ||
            !CellAddress.TryParse(SetCellBox.Text.Trim(), _sheetId, out _))
            return SetCellBox;

        if (error.Contains("valid number", StringComparison.Ordinal))
            return ToValueBox;

        return ChangingCellBox;
    }

    private void RangePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string targetName })
            return;

        var target = targetName == nameof(SetCellBox) ? SetCellBox : ChangingCellBox;
        RangeSelectionRequest = CreateRangeSelectionRequest(GetRangeSelectionTarget(targetName), target.Text);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }

    public static GoalSeekRangeSelectionRequest CreateRangeSelectionRequest(
        GoalSeekRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    private static GoalSeekRangeSelectionTarget GetRangeSelectionTarget(string targetName) =>
        targetName == nameof(SetCellBox)
            ? GoalSeekRangeSelectionTarget.SetCell
            : GoalSeekRangeSelectionTarget.ChangingCell;
}

public enum GoalSeekRangeSelectionTarget
{
    SetCell,
    ChangingCell
}

public sealed record GoalSeekRangeSelectionRequest(
    GoalSeekRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);
