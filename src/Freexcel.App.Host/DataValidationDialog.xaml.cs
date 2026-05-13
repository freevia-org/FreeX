using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

/// <summary>
/// Dialog for creating or editing a data validation rule.
/// </summary>
public partial class DataValidationDialog : Window
{
    /// <summary>Set to the resulting rule when the user clicks OK.</summary>
    public DataValidation? Result { get; private set; }

    public DataValidationDialog()
    {
        InitializeComponent();
        TypeCombo.SelectedIndex = 0;
        OperatorCombo.SelectedIndex = 0;
        UpdateVisibility();
    }

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (TypeCombo == null) return;

        var tag = (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Any";

        bool isAny  = tag == "Any";
        bool isList = tag == "List";

        // Operator row: hide for Any and List
        var operatorRowVis = (!isAny && !isList) ? Visibility.Visible : Visibility.Collapsed;
        OperatorLabel.Visibility  = operatorRowVis;
        OperatorCombo.Visibility  = operatorRowVis;

        // Formula1 label changes by type
        if (isList)
            Formula1Label.Text = "List items (comma-separated):";
        else
        {
            var opTag = (OperatorCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "Between";
            Formula1Label.Text = (opTag == "Between" || opTag == "NotBetween") ? "Minimum:" : "Value:";
        }

        Formula1Label.Visibility = isAny ? Visibility.Collapsed : Visibility.Visible;
        Formula1Box.Visibility   = isAny ? Visibility.Collapsed : Visibility.Visible;

        // Formula2 only for Between / NotBetween
        bool showFormula2 = !isAny && !isList &&
                            (OperatorCombo?.SelectedItem as ComboBoxItem)?.Tag is "Between" or "NotBetween";
        Formula2Label.Visibility = showFormula2 ? Visibility.Visible : Visibility.Collapsed;
        Formula2Box.Visibility   = showFormula2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var typeTag = (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Any";
        var opTag   = (OperatorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Between";

        var dvType = typeTag switch
        {
            "List"        => DvType.List,
            "WholeNumber" => DvType.WholeNumber,
            "Decimal"     => DvType.Decimal,
            "Date"        => DvType.Date,
            "TextLength"  => DvType.TextLength,
            _             => DvType.Any
        };

        var dvOp = opTag switch
        {
            "NotBetween"         => DvOperator.NotBetween,
            "Equal"              => DvOperator.Equal,
            "NotEqual"           => DvOperator.NotEqual,
            "GreaterThan"        => DvOperator.GreaterThan,
            "LessThan"           => DvOperator.LessThan,
            "GreaterThanOrEqual" => DvOperator.GreaterThanOrEqual,
            "LessThanOrEqual"    => DvOperator.LessThanOrEqual,
            _                    => DvOperator.Between
        };

        Result = new DataValidation
        {
            Type         = dvType,
            Operator     = dvOp,
            Formula1     = Formula1Box.Text.Trim(),
            Formula2     = Formula2Box.Text.Trim(),
            AllowBlank   = AllowBlankBox.IsChecked == true,
            ShowDropdown = true,
            ErrorMessage = ErrorMessageBox.Text.Trim(),
        };

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
