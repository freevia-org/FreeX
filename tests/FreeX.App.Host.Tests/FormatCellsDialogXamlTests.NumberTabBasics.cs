using System.IO;
using System.Reflection;
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
    public void FormatCellsDialog_PreservesCustomNumberFormatWhenAcceptedUnchanged()
    {
        StaTestRunner.Run(() =>
        {
            var current = new CellStyle { NumberFormat = "#,##0.0000" };
            var dialog = ShowDialogForTest(current);
            try
            {
                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.NumberFormat.Should().Be("#,##0.0000");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_ExposesExpandedExcelLikeFormatFamilies()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                var categories = GetControl<ListBox>(dialog, "NumberCategoryList");
                var combo = GetControl<ComboBox>(dialog, "NumberFormatCombo");
                var labels = new HashSet<string>();

                foreach (var category in new[] { "General", "Number", "Currency", "Accounting", "Percentage", "Fraction", "Scientific", "Text" })
                {
                    categories.SelectedItem = category;
                    foreach (var label in combo.Items.Cast<string>())
                        labels.Add(label);
                }

                labels.Should().Contain(new[]
                {
                    "General",
                    "Number (#,##0.00)",
                    "Currency ($#,##0.00)",
                    "Accounting ($#,##0.00)",
                    "Percentage (0.00%)",
                    "Fraction (# ?/?)",
                    "Scientific (0.00E+00)",
                    "Text (@)"
                });

                FormatCellsDialog.ResolveNumberFormat("Accounting ($#,##0.00)", 3)
                    .Should().Be("_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)");
                FormatCellsDialog.ResolveNumberFormat("Fraction (# ?/?)", 8)
                    .Should().Be("# ?/?");
                FormatCellsDialog.ResolveNumberFormat("Long date ([$-F800])", 0)
                    .Should().Be("[$-F800]");
                FormatCellsDialog.ResolveNumberFormat("Long time ([$-F400])", 0)
                    .Should().Be("[$-F400]");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_SwitchesTypeListForEachFormatCategory()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                var categories = GetControl<ListBox>(dialog, "NumberCategoryList");
                var types = GetControl<ComboBox>(dialog, "NumberFormatCombo");

                categories.SelectedItem = "Number";
                types.Items.Cast<string>().Should().Contain(new[]
                {
                    "0",
                    "0.00",
                    "#,##0",
                    "#,##0.00"
                });

                categories.SelectedItem = "Currency";
                types.Items.Cast<string>().Should().Contain(new[]
                {
                    "$#,##0",
                    "$#,##0.00",
                    "$#,##0;[Red]($#,##0)",
                    "$#,##0.00;[Red]($#,##0.00)"
                });

                categories.SelectedItem = "Date";
                types.Items.Cast<string>().Should().Contain(new[]
                {
                    "m/d/yyyy",
                    "mmmm d, yyyy",
                    "d-mmm-yy",
                    "Long date ([$-F800])"
                });

                categories.SelectedItem = "Time";
                types.Items.Cast<string>().Should().Contain(new[]
                {
                    "h:mm AM/PM",
                    "h:mm:ss",
                    "Long time ([$-F400])"
                });

                categories.SelectedItem = "Custom";
                types.Items.Cast<string>().Should().Contain(new[]
                {
                    "General",
                    "#,##0.00",
                    "$#,##0.00",
                    "0.00%",
                    "m/d/yyyy",
                    "h:mm AM/PM"
                });

                categories.SelectedItem = "Special";
                types.Items.Cast<string>().Should().Contain(new[]
                {
                    "Zip Code",
                    "Zip Code + 4",
                    "Social Security Number",
                    "Phone Number"
                });
                FormatCellsDialog.ResolveNumberFormat("Zip Code", 0)
                    .Should().Be("00000");
                FormatCellsDialog.ResolveNumberFormat("Zip Code + 4", 0)
                    .Should().Be("00000-0000");
                FormatCellsDialog.ResolveNumberFormat("Social Security Number", 0)
                    .Should().Be("000-00-0000");
                FormatCellsDialog.ResolveNumberFormat("Phone Number", 0)
                    .Should().Be("[<=9999999]###-####;(###) ###-####");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_UsesExcelLikeCategoryAndSampleLayout()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        xaml.Should().Contain("x:Name=\"NumberCategoryList\"");
        xaml.ShouldContainLocalizedAttribute("Content", "_Category:");
        xaml.Should().Contain("Target=\"{Binding ElementName=NumberCategoryList}\"");
        xaml.Should().Contain("Text=\"Sample\"");
        xaml.Should().Contain("x:Name=\"NumberDecimalPlacesBox\"");
        xaml.Should().Contain("x:Name=\"NumberNegativeNumbersList\"");
        xaml.Should().Contain("x:Name=\"NumberSymbolCombo\"");
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_LabelsEditableControlsWithAccessKeyTargets()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var content in new[]
        {
            "Content=\"_Type:\" Target=\"{Binding ElementName=NumberFormatCombo}\"",
            "Content=\"_Decimal places:\" Target=\"{Binding ElementName=NumberDecimalPlacesBox}\"",
            "Content=\"_Symbol:\" Target=\"{Binding ElementName=NumberSymbolCombo}\"",
            "Content=\"_Negative numbers:\" Target=\"{Binding ElementName=NumberNegativeNumbersList}\""
        })
            xaml.Should().Contain(content);
    }

    [Theory]
    [InlineData("Number", "#,##0.00", "0", "None", 0, "#,##0")]
    [InlineData("Currency", "$#,##0.00", "3", "EUR", 2, "EUR#,##0.000;(EUR#,##0.000)")]
    [InlineData("Accounting", "$#,##0.00", "1", "GBP", 0, "_(GBP* #,##0.0_);_(GBP* (#,##0.0);_(GBP* \"-\"?_);_(@_)")]
    [InlineData("Accounting", "$#,##0.00", "2", "GBP", 0, "_(GBP* #,##0.00_);_(GBP* (#,##0.00);_(GBP* \"-\"??_);_(@_)")]
    [InlineData("Percentage", "0.00%", "1", "None", 0, "0.0%")]
    public void FormatCellsDialog_NumberTab_ComposesFormatFromCategoryControls(
        string category,
        string selectedFormat,
        string decimalPlaces,
        string symbol,
        int negativeIndex,
        string expected)
    {
        FormatCellsDialog.ResolveNumberFormat(selectedFormat, 0, category, decimalPlaces, symbol, negativeIndex)
            .Should()
            .Be(expected);
    }

}
