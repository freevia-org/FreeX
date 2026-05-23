using System.Windows;
using System.Windows.Controls;
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

    public CellAddress? SetCell { get; private set; }
    public double TargetValue { get; private set; }
    public CellAddress? ChangingCell { get; private set; }

    /// <param name="sheetId">The active sheet ID, used when parsing bare A1 references.</param>
    /// <param name="selectedCell">Optional pre-selected cell to pre-populate the Set Cell box.</param>
    public GoalSeekDialog(SheetId sheetId, CellAddress? selectedCell)
    {
        _sheetId = sheetId;
        InitializeComponent();

        if (selectedCell.HasValue)
            SetCellBox.Text = selectedCell.Value.ToA1();

        SetCellBox.Focus();
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
            MessageBox.Show(error, "Goal Seek", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetCell = input.SetCell;
        TargetValue = input.TargetValue;
        ChangingCell = input.ChangingCell;
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void RangePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string targetName })
            return;

        var target = targetName == nameof(SetCellBox) ? SetCellBox : ChangingCellBox;
        target.Focus();
        target.SelectAll();
    }
}
