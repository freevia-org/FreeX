namespace Freexcel.App.Host;

public partial class FormatCellsDialog
{
    public static string? ResolveNumberFormat(string text, int selectedIndex) =>
        FormatCellsNumberFormatPlanner.ResolveNumberFormat(text, selectedIndex);

    public static string? ResolveNumberFormat(
        string text,
        int selectedIndex,
        string? category,
        string? decimalPlacesText,
        string? symbol,
        int negativeIndex) =>
        FormatCellsNumberFormatPlanner.ResolveNumberFormat(
            text,
            selectedIndex,
            category,
            decimalPlacesText,
            symbol,
            negativeIndex);

    private static FormatCellsNumberFormatOption? FindNumberFormatOption(string? text) =>
        FormatCellsNumberFormatPlanner.FindOption(text);

    private static int DecimalPlacesForFormat(string? format) =>
        FormatCellsNumberFormatPlanner.DecimalPlacesForFormat(format);

    private static string PreviewForFormat(string? text) =>
        FormatCellsNumberFormatPlanner.PreviewForFormat(text);

    private void SelectNumberFormatOption(FormatCellsNumberFormatOption option)
    {
        NumberFormatCombo.SelectedItem = option.Label;
        if (!string.Equals(NumberFormatCombo.SelectedItem as string, option.Label, StringComparison.Ordinal))
            NumberFormatCombo.Text = option.Label;
    }

    private string? ResolveSelectedNumberFormat() =>
        FormatCellsNumberFormatPlanner.ResolveSelectedNumberFormat(
            NumberCategoryList.SelectedItem as string,
            NumberFormatCombo.Text,
            NumberFormatCombo.SelectedIndex,
            NumberDecimalPlacesBox.Text,
            NumberSymbolCombo.SelectedItem as string ?? NumberSymbolCombo.Text,
            NumberNegativeNumbersList.SelectedIndex);

    private void UpdateNumberControlAvailability()
    {
        if (NumberCategoryList?.SelectedItem is not string category)
            return;

        var availability = FormatCellsNumberControlPlanner.Plan(category);

        NumberDecimalPlacesBox.IsEnabled = availability.UsesDecimals;
        NumberSymbolCombo.IsEnabled = availability.UsesSymbol;
        NumberNegativeNumbersList.IsEnabled = availability.UsesNegativeOptions;
    }

    private void UpdateNumberPreview()
    {
        if (NumberPreview is null
            || NumberCategoryList is null
            || NumberFormatCombo is null
            || NumberDecimalPlacesBox is null
            || NumberSymbolCombo is null
            || NumberNegativeNumbersList is null)
            return;

        NumberPreview.Text = ResolveSelectedNumberFormat() is { } generatedFormat
            ? PreviewForFormat(generatedFormat)
            : PreviewForFormat(NumberFormatCombo.SelectedItem as string ?? NumberFormatCombo.Text);
    }

    private void SyncDecimalPlacesFromSelectedNumberFormat()
    {
        if (_syncingNumberControls || NumberDecimalPlacesBox is null)
            return;

        var selectedFormat = ResolveNumberFormat(NumberFormatCombo.SelectedItem as string ?? NumberFormatCombo.Text, NumberFormatCombo.SelectedIndex);
        if (selectedFormat is null)
            return;

        _syncingNumberControls = true;
        NumberDecimalPlacesBox.Text = DecimalPlacesForFormat(selectedFormat).ToString();
        _syncingNumberControls = false;
    }

    private bool ValidateNumberInputs()
    {
        if (NumberDecimalPlacesBox.IsEnabled
            && (!int.TryParse(NumberDecimalPlacesBox.Text.Trim(), out var decimals) || decimals is < 0 or > 30))
        {
            Tabs.SelectedIndex = (int)FormatCellsDialogTab.Number;
            ShowInvalidInputWarning("Enter decimal places from 0 to 30.", NumberDecimalPlacesBox);
            return false;
        }

        if (!IsGeneratedNumberFormatCategory(NumberCategoryList.SelectedItem as string)
            && !FormatCellsInputParser.IsSupportedCustomNumberFormat(NumberFormatCombo.Text))
        {
            Tabs.SelectedIndex = (int)FormatCellsDialogTab.Number;
            ShowInvalidInputWarning("Enter a valid custom number format.", NumberFormatCombo);
            return false;
        }

        return true;
    }

    private static bool IsGeneratedNumberFormatCategory(string? category) =>
        FormatCellsNumberControlPlanner.Plan(category).GeneratesFormat;
}
