using System.Windows;
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
        // Parse Set Cell
        var setCellText = SetCellBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(setCellText))
        {
            MessageBox.Show("Please enter the Set cell address.", "Goal Seek",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!CellAddress.TryParse(setCellText, _sheetId, out var setCell))
        {
            MessageBox.Show($"'{setCellText}' is not a valid cell address.", "Goal Seek",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Parse Target Value
        var toValueText = ToValueBox.Text.Trim();
        if (!double.TryParse(toValueText, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.CurrentCulture, out var targetValue) &&
            !double.TryParse(toValueText, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out targetValue))
        {
            MessageBox.Show($"'{toValueText}' is not a valid number.", "Goal Seek",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Parse Changing Cell
        var changingCellText = ChangingCellBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(changingCellText))
        {
            MessageBox.Show("Please enter the By changing cell address.", "Goal Seek",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!CellAddress.TryParse(changingCellText, _sheetId, out var changingCell))
        {
            MessageBox.Show($"'{changingCellText}' is not a valid cell address.", "Goal Seek",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Validate: set cell must differ from changing cell
        if (setCell == changingCell)
        {
            MessageBox.Show("The Set cell and the By changing cell must be different.", "Goal Seek",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetCell = setCell;
        TargetValue = targetValue;
        ChangingCell = changingCell;
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
