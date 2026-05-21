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
    public bool ClearRequested { get; private set; }
    public bool ApplyToSameSettings { get; private set; }
    public string? SelectionSource { get; set; }

    public DataValidationDialog()
    {
        InitializeComponent();
        TypeCombo.SelectedIndex = 0;
        OperatorCombo.SelectedIndex = 0;
        AlertStyleCombo.SelectedIndex = 0;
        UpdateVisibility();
    }

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateVisibility();
    }

    private void OperatorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (TypeCombo == null) return;

        var tag = (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Any";

        bool isAny    = tag == "Any";
        bool isList   = tag == "List";
        bool isCustom = tag == "Custom";

        // Operator row: hide for Any and List
        var operatorRowVis = (!isAny && !isList && !isCustom) ? Visibility.Visible : Visibility.Collapsed;
        OperatorLabel.Visibility  = operatorRowVis;
        OperatorCombo.Visibility  = operatorRowVis;

        // Formula1 label changes by type
        if (isList)
            Formula1Label.Content = "_Source:";
        else if (isCustom)
            Formula1Label.Content = "_Formula:";
        else
        {
            var opTag = (OperatorCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "Between";
            Formula1Label.Content = (opTag == "Between" || opTag == "NotBetween") ? "_Minimum:" : "_Value:";
        }

        Formula1Label.Visibility = isAny ? Visibility.Collapsed : Visibility.Visible;
        Formula1Box.Visibility   = isAny ? Visibility.Collapsed : Visibility.Visible;
        SourcePickerButton.Visibility = isList && !string.IsNullOrWhiteSpace(SelectionSource)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UseSelectionButton.Visibility = isList && !string.IsNullOrWhiteSpace(SelectionSource)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Formula2 only for Between / NotBetween
        bool showFormula2 = !isAny && !isList && !isCustom &&
                            (OperatorCombo?.SelectedItem as ComboBoxItem)?.Tag is "Between" or "NotBetween";
        Formula2Label.Visibility = showFormula2 ? Visibility.Visible : Visibility.Collapsed;
        Formula2Box.Visibility   = showFormula2 ? Visibility.Visible : Visibility.Collapsed;
        UseSelection2Button.Visibility = showFormula2 && !string.IsNullOrWhiteSpace(SelectionSource)
            ? Visibility.Visible
            : Visibility.Collapsed;

        ShowDropdownBox.Visibility = isList ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var typeTag = (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Any";
        var opTag   = (OperatorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Between";
        var alertTag = (AlertStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Stop";

        var dvType = typeTag switch
        {
            "List"        => DvType.List,
            "WholeNumber" => DvType.WholeNumber,
            "Decimal"     => DvType.Decimal,
            "Date"        => DvType.Date,
            "Time"        => DvType.Time,
            "TextLength"  => DvType.TextLength,
            "Custom"      => DvType.Custom,
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

        var alertStyle = alertTag switch
        {
            "Warning" => DvAlertStyle.Warning,
            "Information" => DvAlertStyle.Information,
            _ => DvAlertStyle.Stop
        };

        Result = new DataValidation
        {
            Type         = dvType,
            Operator     = dvOp,
            Formula1     = Formula1Box.Text.Trim(),
            Formula2     = Formula2Box.Text.Trim(),
            AllowBlank   = AllowBlankBox.IsChecked == true,
            ShowDropdown = dvType == DvType.List && ShowDropdownBox.IsChecked == true,
            AlertStyle   = alertStyle,
            ShowInputMessage = ShowInputMessageBox.IsChecked == true,
            ShowErrorMessage = ShowErrorMessageBox.IsChecked == true,
            ErrorTitle = ErrorTitleBox.Text.Trim(),
            PromptTitle = PromptTitleBox.Text.Trim(),
            PromptMessage = PromptMessageBox.Text.Trim(),
            ErrorMessage = ErrorMessageBox.Text.Trim(),
        };
        ApplyToSameSettings = SameSettingsBox.IsChecked == true;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        ClearRequested = true;
        Result = null;
        DialogResult = true;
        Close();
    }

    private void UseSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SelectionSource))
            Formula1Box.Text = SelectionSource;
    }

    private void SourcePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SelectionSource))
            Formula1Box.Text = SelectionSource;

        Formula1Box.Focus();
        Formula1Box.SelectAll();
    }

    private void UseSelection2Button_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SelectionSource))
            Formula2Box.Text = SelectionSource;
    }
}
