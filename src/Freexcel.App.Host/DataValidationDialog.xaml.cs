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
    private readonly Guid _resultId = Guid.NewGuid();

    public DataValidationDialog()
    {
        InitializeComponent();
        ResetToDefaults(markClearRequested: false);
    }

    public DataValidationDialog(DataValidation? existing)
        : this()
    {
        if (existing is null)
            return;

        _resultId = existing.Id;
        SelectComboItemByTag(TypeCombo, TypeTag(existing.Type));
        SelectComboItemByTag(OperatorCombo, OperatorTag(existing.Operator));
        SelectComboItemByTag(AlertStyleCombo, AlertStyleTag(existing.AlertStyle));
        Formula1Box.Text = existing.Formula1 ?? "";
        Formula2Box.Text = existing.Formula2 ?? "";
        AllowBlankBox.IsChecked = existing.AllowBlank;
        ShowDropdownBox.IsChecked = existing.ShowDropdown;
        ShowInputMessageBox.IsChecked = existing.ShowInputMessage;
        ShowErrorMessageBox.IsChecked = existing.ShowErrorMessage;
        ErrorTitleBox.Text = existing.ErrorTitle ?? "";
        PromptTitleBox.Text = existing.PromptTitle ?? "";
        PromptMessageBox.Text = existing.PromptMessage ?? "";
        ErrorMessageBox.Text = existing.ErrorMessage ?? "";
        UpdateVisibility();
    }

    private void ResetToDefaults(bool markClearRequested)
    {
        TypeCombo.SelectedIndex = 0;
        OperatorCombo.SelectedIndex = 0;
        AlertStyleCombo.SelectedIndex = 0;
        Formula1Box.Text = "";
        Formula2Box.Text = "";
        AllowBlankBox.IsChecked = true;
        ShowDropdownBox.IsChecked = true;
        SameSettingsBox.IsChecked = false;
        ShowInputMessageBox.IsChecked = true;
        ShowErrorMessageBox.IsChecked = true;
        ErrorTitleBox.Text = "";
        PromptTitleBox.Text = "";
        PromptMessageBox.Text = "";
        ErrorMessageBox.Text = "";
        ClearRequested = markClearRequested;
        Result = null;
        ApplyToSameSettings = false;
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
        SourcePickerButton.Visibility = !isAny && !string.IsNullOrWhiteSpace(SelectionSource)
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
        SourcePicker2Button.Visibility = showFormula2 && !string.IsNullOrWhiteSpace(SelectionSource)
            ? Visibility.Visible
            : Visibility.Collapsed;
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
            Id = _resultId,
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
        ClearRequested = ClearRequested && IsClearAllState(typeTag, opTag, alertTag);

        DialogResult = true;
        Close();
    }

    private bool IsClearAllState(string typeTag, string opTag, string alertTag) =>
        typeTag == "Any"
        && opTag == "Between"
        && alertTag == "Stop"
        && string.IsNullOrWhiteSpace(Formula1Box.Text)
        && string.IsNullOrWhiteSpace(Formula2Box.Text)
        && AllowBlankBox.IsChecked == true
        && ShowDropdownBox.IsChecked == true
        && SameSettingsBox.IsChecked != true
        && ShowInputMessageBox.IsChecked == true
        && ShowErrorMessageBox.IsChecked == true
        && string.IsNullOrWhiteSpace(ErrorTitleBox.Text)
        && string.IsNullOrWhiteSpace(PromptTitleBox.Text)
        && string.IsNullOrWhiteSpace(PromptMessageBox.Text)
        && string.IsNullOrWhiteSpace(ErrorMessageBox.Text);

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaults(markClearRequested: true);
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

    private void SourcePicker2Button_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SelectionSource))
            Formula2Box.Text = SelectionSource;

        Formula2Box.Focus();
        Formula2Box.SelectAll();
    }

    private void UseSelection2Button_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SelectionSource))
            Formula2Box.Text = SelectionSource;
    }

    private static void SelectComboItemByTag(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
            ?? comboBox.Items[0];
    }

    private static string TypeTag(DvType type) => type switch
    {
        DvType.List => "List",
        DvType.WholeNumber => "WholeNumber",
        DvType.Decimal => "Decimal",
        DvType.Date => "Date",
        DvType.Time => "Time",
        DvType.TextLength => "TextLength",
        DvType.Custom => "Custom",
        _ => "Any"
    };

    private static string OperatorTag(DvOperator op) => op switch
    {
        DvOperator.NotBetween => "NotBetween",
        DvOperator.Equal => "Equal",
        DvOperator.NotEqual => "NotEqual",
        DvOperator.GreaterThan => "GreaterThan",
        DvOperator.LessThan => "LessThan",
        DvOperator.GreaterThanOrEqual => "GreaterThanOrEqual",
        DvOperator.LessThanOrEqual => "LessThanOrEqual",
        _ => "Between"
    };

    private static string AlertStyleTag(DvAlertStyle style) => style switch
    {
        DvAlertStyle.Warning => "Warning",
        DvAlertStyle.Information => "Information",
        _ => "Stop"
    };
}
