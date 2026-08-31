using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using FreeX.App.Host;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

public sealed partial class FormatCellsDialogXamlTests
{
    [Fact]
    public void FormatCellsDialog_NumberTab_AppliesDecimalSymbolAndNegativeControlsToNumberFormats()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                var categories = GetControl<ListBox>(dialog, "NumberCategoryList");
                var decimals = GetControl<TextBox>(dialog, "NumberDecimalPlacesBox");
                var symbols = GetControl<ComboBox>(dialog, "NumberSymbolCombo");
                var negatives = GetControl<ListBox>(dialog, "NumberNegativeNumbersList");

                categories.SelectedItem = "Number";
                decimals.Text = "3";
                negatives.SelectedIndex = 2;
                ClickOkForTest(dialog);
                dialog.ResultDiff!.NumberFormat.Should().Be("#,##0.000;(#,##0.000)");

                categories.SelectedItem = "Currency";
                decimals.Text = "1";
                symbols.SelectedItem = "€";
                negatives.SelectedIndex = 3;
                ClickOkForTest(dialog);
                dialog.ResultDiff!.NumberFormat.Should().Be("€#,##0.0;[Red](€#,##0.0)");

                categories.SelectedItem = "Accounting";
                decimals.Text = "0";
                symbols.SelectedItem = "£";
                ClickOkForTest(dialog);
                dialog.ResultDiff!.NumberFormat.Should().Be("_(£* #,##0_);_(£* (#,##0);_(£* \"-\"_);_(@_)");

                decimals.Text = "1";
                symbols.SelectedItem = "GBP";
                ClickOkForTest(dialog);
                dialog.ResultDiff!.NumberFormat.Should().Be("_(GBP* #,##0.0_);_(GBP* (#,##0.0);_(GBP* \"-\"?_);_(@_)");

                categories.SelectedItem = "Percentage";
                decimals.Text = "4";
                ClickOkForTest(dialog);
                dialog.ResultDiff!.NumberFormat.Should().Be("0.0000%");

                categories.SelectedItem = "Scientific";
                decimals.Text = "1";
                ClickOkForTest(dialog);
                dialog.ResultDiff!.NumberFormat.Should().Be("0.0E+00");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_ListsLocalizedAccountingCurrencyLabels()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                var categories = GetControl<ListBox>(dialog, "NumberCategoryList");
                var decimals = GetControl<TextBox>(dialog, "NumberDecimalPlacesBox");
                var symbols = GetControl<ComboBox>(dialog, "NumberSymbolCombo");
                var usRegion = new RegionInfo("en-US");
                var usCulture = CultureInfo.GetCultureInfo("en-US");
                var frRegion = new RegionInfo("fr-FR");
                var frCulture = CultureInfo.GetCultureInfo("fr-FR");
                var usDollarLabel = $"{usRegion.CurrencySymbol} {usRegion.CurrencyNativeName}";
                var usCultureLabel = $"{usRegion.CurrencySymbol} {usCulture.EnglishName}";
                var frCultureLabel = $"{frRegion.CurrencySymbol} {frCulture.EnglishName}";

                symbols.Items.Cast<string>().Should().Contain(usDollarLabel);
                symbols.Items.Cast<string>().Should().Contain(usCultureLabel);
                symbols.Items.Cast<string>().Should().Contain(frCultureLabel);

                categories.SelectedItem = "Accounting";
                decimals.Text = "2";
                symbols.SelectedItem = usCultureLabel;

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.NumberFormat.Should().Be($"_({usRegion.CurrencySymbol}* #,##0.00_);_({usRegion.CurrencySymbol}* (#,##0.00);_({usRegion.CurrencySymbol}* \"-\"??_);_(@_)");

                decimals.Text = "1";
                symbols.SelectedItem = frCultureLabel;

                ClickOkForTest(dialog);

                dialog.ResultDiff!.NumberFormat.Should().Be($"_({frRegion.CurrencySymbol}* #,##0.0_);_({frRegion.CurrencySymbol}* (#,##0.0);_({frRegion.CurrencySymbol}* \"-\"?_);_(@_)");
                FormatCellsDialog.ResolveNumberFormat("$#,##0.00", 0, "Accounting", "2", usCultureLabel, 0)
                    .Should().Be($"_({usRegion.CurrencySymbol}* #,##0.00_);_({usRegion.CurrencySymbol}* (#,##0.00);_({usRegion.CurrencySymbol}* \"-\"??_);_(@_)");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_UpdatesSamplePreviewFromResolvedNumberFormat()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                var categories = GetControl<ListBox>(dialog, "NumberCategoryList");
                var decimals = GetControl<TextBox>(dialog, "NumberDecimalPlacesBox");
                var symbols = GetControl<ComboBox>(dialog, "NumberSymbolCombo");
                var preview = GetControl<TextBlock>(dialog, "NumberPreview");
                var type = GetControl<ComboBox>(dialog, "NumberFormatCombo");

                categories.SelectedItem = "Currency";
                decimals.Text = "3";
                symbols.SelectedItem = "EUR";
                preview.Text.Should().Be("EUR1,234.560");

                categories.SelectedItem = "Percentage";
                decimals.Text = "1";
                preview.Text.Should().Be("123456.0%");

                categories.SelectedItem = "Custom";
                type.Text = "m/d/yyyy";
                preview.Text.Should().Be("5/21/2026");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_UsesWidthAwareAccountingPreview()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                var categories = GetControl<ListBox>(dialog, "NumberCategoryList");
                var decimals = GetControl<TextBox>(dialog, "NumberDecimalPlacesBox");
                var symbols = GetControl<ComboBox>(dialog, "NumberSymbolCombo");
                var preview = GetControl<TextBlock>(dialog, "NumberPreview");
                var usRegion = new RegionInfo("en-US");
                var usCulture = CultureInfo.GetCultureInfo("en-US");
                var usCultureLabel = $"{usRegion.CurrencySymbol} {usCulture.EnglishName}";

                categories.SelectedItem = "Accounting";
                decimals.Text = "2";
                symbols.SelectedItem = "GBP";
                preview.Text.Should().Be("GBP   1,234.56");

                symbols.SelectedItem = usCultureLabel;
                preview.Text.Should().Be("$     1,234.56");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_TextFormatWithLayoutDirectivePreviewsSampleText()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                var categories = GetControl<ListBox>(dialog, "NumberCategoryList");
                var preview = GetControl<TextBlock>(dialog, "NumberPreview");
                var type = GetControl<ComboBox>(dialog, "NumberFormatCombo");

                categories.SelectedItem = "Custom";
                type.Text = "@_* ";
                preview.Text.Should().Be("Sample");

                type.Text = ";;;@_* ";
                preview.Text.Should().Be("Sample");

                PreviewForFormat("@ 0_* ").Should().Be("Sample 0 ");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_EscapedOrQuotedLayoutCharactersDoNotForceAccountingPreview()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                var categories = GetControl<ListBox>(dialog, "NumberCategoryList");
                var preview = GetControl<TextBlock>(dialog, "NumberPreview");
                var type = GetControl<ComboBox>(dialog, "NumberFormatCombo");

                categories.SelectedItem = "Custom";
                type.Text = @"@\*";
                preview.Text.Should().Be("Sample*");
                PreviewForFormat("\"*_\"@").Should().Be("*_Sample");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_EnablesOnlyControlsThatAffectSelectedCategory()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                var categories = GetControl<ListBox>(dialog, "NumberCategoryList");
                var decimals = GetControl<TextBox>(dialog, "NumberDecimalPlacesBox");
                var symbols = GetControl<ComboBox>(dialog, "NumberSymbolCombo");
                var negatives = GetControl<ListBox>(dialog, "NumberNegativeNumbersList");
                var generalDescription = GetControl<TextBlock>(dialog, "NumberGeneralDescription");
                var typePanel = GetControl<StackPanel>(dialog, "NumberTypePanel");
                var decimalPanel = GetControl<StackPanel>(dialog, "NumberDecimalPlacesPanel");
                var symbolPanel = GetControl<StackPanel>(dialog, "NumberSymbolPanel");
                var negativePanel = GetControl<StackPanel>(dialog, "NumberNegativeNumbersPanel");

                categories.SelectedItem = "General";
                generalDescription.Visibility.Should().Be(Visibility.Visible);
                typePanel.Visibility.Should().Be(Visibility.Collapsed);
                decimalPanel.Visibility.Should().Be(Visibility.Collapsed);
                symbolPanel.Visibility.Should().Be(Visibility.Collapsed);
                negativePanel.Visibility.Should().Be(Visibility.Collapsed);
                decimals.IsEnabled.Should().BeFalse();
                symbols.IsEnabled.Should().BeFalse();
                negatives.IsEnabled.Should().BeFalse();

                categories.SelectedItem = "Number";
                generalDescription.Visibility.Should().Be(Visibility.Collapsed);
                typePanel.Visibility.Should().Be(Visibility.Visible);
                decimalPanel.Visibility.Should().Be(Visibility.Visible);
                symbolPanel.Visibility.Should().Be(Visibility.Collapsed);
                negativePanel.Visibility.Should().Be(Visibility.Visible);
                decimals.IsEnabled.Should().BeTrue();
                symbols.IsEnabled.Should().BeFalse();
                negatives.IsEnabled.Should().BeTrue();

                categories.SelectedItem = "Currency";
                generalDescription.Visibility.Should().Be(Visibility.Collapsed);
                typePanel.Visibility.Should().Be(Visibility.Visible);
                decimalPanel.Visibility.Should().Be(Visibility.Visible);
                symbolPanel.Visibility.Should().Be(Visibility.Visible);
                negativePanel.Visibility.Should().Be(Visibility.Visible);
                decimals.IsEnabled.Should().BeTrue();
                symbols.IsEnabled.Should().BeTrue();
                negatives.IsEnabled.Should().BeTrue();

                categories.SelectedItem = "Accounting";
                generalDescription.Visibility.Should().Be(Visibility.Collapsed);
                typePanel.Visibility.Should().Be(Visibility.Visible);
                decimalPanel.Visibility.Should().Be(Visibility.Visible);
                symbolPanel.Visibility.Should().Be(Visibility.Visible);
                negativePanel.Visibility.Should().Be(Visibility.Collapsed);
                decimals.IsEnabled.Should().BeTrue();
                symbols.IsEnabled.Should().BeTrue();
                negatives.IsEnabled.Should().BeFalse();

                foreach (var category in new[] { "Date", "Time", "Fraction", "Text", "Special", "Custom" })
                {
                    categories.SelectedItem = category;
                    generalDescription.Visibility.Should().Be(Visibility.Collapsed);
                    typePanel.Visibility.Should().Be(Visibility.Visible);
                    decimalPanel.Visibility.Should().Be(Visibility.Collapsed);
                    symbolPanel.Visibility.Should().Be(Visibility.Collapsed);
                    negativePanel.Visibility.Should().Be(Visibility.Collapsed);
                    decimals.IsEnabled.Should().BeFalse();
                    symbols.IsEnabled.Should().BeFalse();
                    negatives.IsEnabled.Should().BeFalse();
                }

                foreach (var category in new[] { "Percentage", "Scientific" })
                {
                    categories.SelectedItem = category;
                    generalDescription.Visibility.Should().Be(Visibility.Collapsed);
                    typePanel.Visibility.Should().Be(Visibility.Visible);
                    decimalPanel.Visibility.Should().Be(Visibility.Visible);
                    symbolPanel.Visibility.Should().Be(Visibility.Collapsed);
                    negativePanel.Visibility.Should().Be(Visibility.Collapsed);
                    decimals.IsEnabled.Should().BeTrue();
                    symbols.IsEnabled.Should().BeFalse();
                    negatives.IsEnabled.Should().BeFalse();
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_UsesProvidedCellDisplayTextForGeneralSample()
    {
        StaTestRunner.Run(() =>
        {
            const string selectedCellText = "FreeX / Excel UX parity corpus";
            var dialog = ShowDialogForTest(new CellStyle(), numberPreviewText: selectedCellText);
            try
            {
                var categories = GetControl<ListBox>(dialog, "NumberCategoryList");
                var preview = GetControl<TextBlock>(dialog, "NumberPreview");
                var previewBorder = GetControl<Border>(dialog, "NumberPreviewBorder");

                categories.SelectedItem = "General";
                preview.Text.Should().Be(selectedCellText);
                previewBorder.ActualWidth.Should().Be(330);
                preview.ActualWidth.Should().BeGreaterThan(250);

                categories.SelectedItem = "Currency";
                preview.Text.Should().Be("$1,234.56");
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
