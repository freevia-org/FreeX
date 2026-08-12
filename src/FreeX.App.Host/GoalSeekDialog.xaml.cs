using System.Windows;
using System.Windows.Controls;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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
        DialogFocus.FocusAndSelect(SetCellBox);
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!GoalSeekRequestParser.TryParse(
                _sheetId,
                SetCellBox.Text,
                ToValueBox.Text,
                ChangingCellBox.Text,
                out var input,
                out var parseResult))
        {
            var validation = GoalSeekStatusDialogPlanner.DescribeValidationError(
                parseResult,
                GoalSeekPresentationProfile.Wpf);
            DialogMessageHelper.ShowWarning(
                this,
                validation.Message.Resolve(UiText.Get, UiText.Format),
                UiText.Get("GoalSeek_GoalSeek"));
            FocusInvalidInput(validation.FocusTarget);
            return;
        }

        SetCell = input.SetCell;
        TargetValue = input.TargetValue;
        ChangingCell = input.ChangingCell;
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void FocusInvalidInput(GoalSeekValidationFocusTarget focusTarget)
    {
        var target = focusTarget switch
        {
            GoalSeekValidationFocusTarget.TargetValue => ToValueBox,
            GoalSeekValidationFocusTarget.ChangingCell => ChangingCellBox,
            _ => SetCellBox
        };
        DialogFocus.FocusAndSelect(target);
    }

    private void RangePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string targetName })
            return;

        var target = targetName == nameof(SetCellBox) ? SetCellBox : ChangingCellBox;
        RangeSelectionRequest = CreateRangeSelectionRequest(GetRangeSelectionTarget(targetName), target.Text);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        DialogFocus.FocusAndSelect(target);
    }

    public void ApplyRangeSelection(GoalSeekRangeSelectionTarget target, CellAddress address)
    {
        var textBox = target == GoalSeekRangeSelectionTarget.SetCell
            ? SetCellBox
            : ChangingCellBox;

        textBox.Text = address.ToA1();
        DialogFocus.FocusAndSelect(textBox);
    }

    public void ApplyInputValues(
        CellAddress setCell,
        string targetValueText,
        CellAddress changingCell)
    {
        SetCellBox.Text = setCell.ToA1();
        ToValueBox.Text = targetValueText;
        ChangingCellBox.Text = changingCell.ToA1();
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
