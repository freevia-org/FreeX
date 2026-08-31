using FreeX.App.Services;
using System.Windows;

namespace FreeX.App.Host;

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

    internal static string PreviewForFormat(string? text) =>
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

        NumberGeneralDescription.Visibility = availability.ShowsGeneralDescription ? Visibility.Visible : Visibility.Collapsed;
        NumberTypePanel.Visibility = availability.ShowsType ? Visibility.Visible : Visibility.Collapsed;
        NumberDecimalPlacesPanel.Visibility = availability.UsesDecimals ? Visibility.Visible : Visibility.Collapsed;
        NumberSymbolPanel.Visibility = availability.UsesSymbol ? Visibility.Visible : Visibility.Collapsed;
        NumberNegativeNumbersPanel.Visibility = availability.UsesNegativeOptions ? Visibility.Visible : Visibility.Collapsed;

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

        if (NumberCategoryList.SelectedItem is "General" && _numberPreviewText is not null)
        {
            NumberPreview.Text = _numberPreviewText;
            return;
        }

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
}
