using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using FreeX.App.Host;
using FreeX.App.Services;
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
    public void FormatCellsDialog_MapsFontFieldsIntoStyleDiff()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                GetControl<ComboBox>(dialog, "DlgFontNameBox").SelectedItem = "Verdana";
                GetControl<ComboBox>(dialog, "DlgFontSizeBox").Text = "13.5";
                GetControl<ListBox>(dialog, "DlgFontStyleList").SelectedItem = "Bold Italic";
                GetControl<ComboBox>(dialog, "DlgUnderlineStyleBox").SelectedItem = "Double";
                GetControl<CheckBox>(dialog, "DlgStrikeCheck").IsChecked = true;
                GetControl<CheckBox>(dialog, "DlgSuperscriptCheck").IsChecked = true;
                GetControl<CheckBox>(dialog, "DlgSubscriptCheck").IsChecked = false;
                GetControl<TextBox>(dialog, "DlgFontColorBox").Text = "20,40,60";

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.FontName.Should().Be("Verdana");
                dialog.ResultDiff.FontSize.Should().Be(13.5);
                dialog.ResultDiff.Bold.Should().BeTrue();
                dialog.ResultDiff.Italic.Should().BeTrue();
                dialog.ResultDiff.Underline.Should().BeFalse();
                dialog.ResultDiff.DoubleUnderline.Should().BeTrue();
                dialog.ResultDiff.Strikethrough.Should().BeTrue();
                dialog.ResultDiff.Superscript.Should().BeTrue();
                dialog.ResultDiff.Subscript.Should().BeFalse();
                dialog.ResultDiff.FontColor.Should().Be(new CellColor(20, 40, 60));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_AcceptsHexColorTextForFontFillPatternAndBorders()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                GetControl<TextBox>(dialog, "DlgFontColorBox").Text = "#C00000";
                GetControl<TextBox>(dialog, "DlgFillColorBox").Text = "#00B050";
                GetControl<TextBox>(dialog, "DlgFillPatternColorBox").Text = "#5B9BD5";
                GetControl<ComboBox>(dialog, "DlgFillPatternStyleBox").SelectedItem = "Diagonal Crosshatch";
                GetControl<TextBox>(dialog, "DlgBorderLineColorBox").Text = "#7030A0";
                GetControl<ComboBox>(dialog, "DlgBorderTopStyleBox").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.Thin);
                GetControl<TextBox>(dialog, "DlgBorderTopColorBox").Text = "#FFC000";
                GetControl<ComboBox>(dialog, "DlgBorderRightStyleBox").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.Medium);
                GetControl<TextBox>(dialog, "DlgBorderRightColorBox").Text = "#4472C4";
                GetControl<ComboBox>(dialog, "DlgBorderBottomStyleBox").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.Dashed);
                GetControl<TextBox>(dialog, "DlgBorderBottomColorBox").Text = "#70AD47";
                GetControl<ComboBox>(dialog, "DlgBorderLeftStyleBox").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.Double);
                GetControl<TextBox>(dialog, "DlgBorderLeftColorBox").Text = "#ED7D31";

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.FontColor.Should().Be(new CellColor(192, 0, 0));
                dialog.ResultDiff.FillColor.Should().Be(new CellColor(0, 176, 80));
                dialog.ResultDiff.FillPatternColor.Should().Be(new CellColor(91, 155, 213));
                dialog.ResultDiff.FillPatternStyle.Should().Be(CellFillPatternStyle.DarkGrid);
                dialog.ResultDiff.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, new CellColor(255, 192, 0)));
                dialog.ResultDiff.BorderRight.Should().Be(new CellBorder(BorderStyle.Medium, new CellColor(68, 114, 196)));
                dialog.ResultDiff.BorderBottom.Should().Be(new CellBorder(BorderStyle.Dashed, new CellColor(112, 173, 71)));
                dialog.ResultDiff.BorderLeft.Should().Be(new CellBorder(BorderStyle.Double, new CellColor(237, 125, 49)));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_AllowsBlankSideColorsWhenBorderSidesAreNone()
    {
        StaTestRunner.Run(() =>
        {
            var current = new CellStyle
            {
                BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(1, 2, 3)),
                BorderRight = new CellBorder(BorderStyle.Thin, new CellColor(4, 5, 6)),
                BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(7, 8, 9)),
                BorderLeft = new CellBorder(BorderStyle.Thin, new CellColor(10, 11, 12))
            };
            var dialog = ShowDialogForTest(current);
            try
            {
                GetControl<ComboBox>(dialog, "DlgBorderTopStyleBox").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.None);
                GetControl<TextBox>(dialog, "DlgBorderTopColorBox").Text = "";
                GetControl<ComboBox>(dialog, "DlgBorderRightStyleBox").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.None);
                GetControl<TextBox>(dialog, "DlgBorderRightColorBox").Text = "";
                GetControl<ComboBox>(dialog, "DlgBorderBottomStyleBox").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.None);
                GetControl<TextBox>(dialog, "DlgBorderBottomColorBox").Text = "";
                GetControl<ComboBox>(dialog, "DlgBorderLeftStyleBox").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.None);
                GetControl<TextBox>(dialog, "DlgBorderLeftColorBox").Text = "";

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.BorderTop.Should().Be(new CellBorder(BorderStyle.None, new CellColor(1, 2, 3)));
                dialog.ResultDiff.BorderRight.Should().Be(new CellBorder(BorderStyle.None, new CellColor(4, 5, 6)));
                dialog.ResultDiff.BorderBottom.Should().Be(new CellBorder(BorderStyle.None, new CellColor(7, 8, 9)));
                dialog.ResultDiff.BorderLeft.Should().Be(new CellBorder(BorderStyle.None, new CellColor(10, 11, 12)));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_FontTab_KeepsSuperscriptAndSubscriptMutuallyExclusive()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        xaml.Should().Contain("Checked=\"DlgSuperscriptCheck_Checked\"");
        xaml.Should().Contain("Checked=\"DlgSubscriptCheck_Checked\"");

        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                var superscript = GetControl<CheckBox>(dialog, "DlgSuperscriptCheck");
                var subscript = GetControl<CheckBox>(dialog, "DlgSubscriptCheck");

                superscript.IsChecked = true;
                InvokeDialogHandler(dialog, "DlgSuperscriptCheck_Checked", superscript);
                subscript.IsChecked = true;
                InvokeDialogHandler(dialog, "DlgSubscriptCheck_Checked", subscript);

                superscript.IsChecked.Should().BeFalse();
                subscript.IsChecked.Should().BeTrue();

                superscript.IsChecked = true;
                InvokeDialogHandler(dialog, "DlgSuperscriptCheck_Checked", superscript);

                superscript.IsChecked.Should().BeTrue();
                subscript.IsChecked.Should().BeFalse();

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.Superscript.Should().BeTrue();
                dialog.ResultDiff.Subscript.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_FontTab_ListsExcelUnderlineOptions()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("UiText.Get(\"FormatCells_UnderlineNone\")");
        source.Should().Contain("UiText.Get(\"FormatCells_UnderlineSingle\")");
        source.Should().Contain("UiText.Get(\"FormatCells_UnderlineDouble\")");
        source.Should().Contain("UiText.Get(\"FormatCells_UnderlineSingleAccounting\")");
        source.Should().Contain("UiText.Get(\"FormatCells_UnderlineDoubleAccounting\")");
    }

    [Theory]
    [InlineData("Single Accounting", true, false)]
    [InlineData("Double Accounting", false, true)]
    public void FormatCellsDialog_MapsAccountingUnderlineOptionsIntoCurrentStyleModel(
        string underlineOption,
        bool expectedUnderline,
        bool expectedDoubleUnderline)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                GetControl<ComboBox>(dialog, "DlgUnderlineStyleBox").SelectedItem = underlineOption;

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.Underline.Should().Be(expectedUnderline);
                dialog.ResultDiff.DoubleUnderline.Should().Be(expectedDoubleUnderline);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_MapsNumberAndAlignmentFieldsIntoStyleDiff()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                GetControl<ListBox>(dialog, "NumberCategoryList").SelectedItem = "Currency";
                GetControl<ComboBox>(dialog, "NumberFormatCombo").SelectedItem = "Currency ($#,##0.00)";
                GetControl<TextBox>(dialog, "NumberDecimalPlacesBox").Text = "3";
                GetControl<ComboBox>(dialog, "NumberSymbolCombo").SelectedItem = "EUR";
                GetControl<ListBox>(dialog, "NumberNegativeNumbersList").SelectedIndex = 2;
                GetControl<ComboBox>(dialog, "DlgHAlignBox").SelectedItem = nameof(CellHAlign.Right);
                GetControl<ComboBox>(dialog, "DlgVAlignBox").SelectedItem = nameof(CellVAlign.Center);
                GetControl<CheckBox>(dialog, "DlgWrapTextCheck").IsChecked = true;
                GetControl<CheckBox>(dialog, "DlgShrinkToFitCheck").IsChecked = true;
                GetControl<TextBox>(dialog, "DlgIndentLevelBox").Text = "7";
                GetControl<TextBox>(dialog, "DlgTextRotationBox").Text = "-45";

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.NumberFormat.Should().Be("EUR#,##0.000;(EUR#,##0.000)");
                dialog.ResultDiff.HAlign.Should().Be(CellHAlign.Right);
                dialog.ResultDiff.VAlign.Should().Be(CellVAlign.Center);
                dialog.ResultDiff.WrapText.Should().BeTrue();
                dialog.ResultDiff.ShrinkToFit.Should().BeTrue();
                dialog.ResultDiff.IndentLevel.Should().Be(7);
                dialog.ResultDiff.TextRotation.Should().Be(-45);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_MapsMergeCellsOnlyWhenChanged()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new FormatCellsDialog(new CellStyle(), FormatCellsDialogTab.Alignment, mergeCells: false);
            dialog.Show();
            try
            {
                GetControl<CheckBox>(dialog, "DlgMergeCellsCheck").IsChecked.Should().BeFalse();

                ClickOkForTest(dialog);
                dialog.ResultMergeCells.Should().BeNull();

                GetControl<CheckBox>(dialog, "DlgMergeCellsCheck").IsChecked = true;
                ClickOkForTest(dialog);
                dialog.ResultMergeCells.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_MapsUnmergeWhenExistingMergeIsUnchecked()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new FormatCellsDialog(new CellStyle(), FormatCellsDialogTab.Alignment, mergeCells: true);
            dialog.Show();
            try
            {
                GetControl<CheckBox>(dialog, "DlgMergeCellsCheck").IsChecked.Should().BeTrue();
                GetControl<CheckBox>(dialog, "DlgMergeCellsCheck").IsChecked = false;

                ClickOkForTest(dialog);

                dialog.ResultMergeCells.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void CellMergePlanner_CreatesFormatCellsMergeAndUnmergeCommandsForSelection()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));

        var mergeCommands = CellMergePlanner.CreateFormatCellsMergeCommands(sheet, sheet.Id, range, mergeCells: true);

        mergeCommands.Should().ContainSingle().Which.Should().BeOfType<MergeCellsCommand>();

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("One"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Two"));
        var concatenateCommands = CellMergePlanner.CreateFormatCellsMergeCommands(
            sheet,
            sheet.Id,
            range,
            mergeCells: true,
            MergeCellContentResolution.ConcatenateAllCells);

        concatenateCommands.Should().HaveCount(2);
        concatenateCommands[0].Should().BeOfType<EditCellsCommand>();
        concatenateCommands[1].Should().BeOfType<MergeCellsCommand>();

        sheet.AddMergedRegion(range);
        CellMergePlanner.IsSelectionMerged(sheet, new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)))
            .Should()
            .BeTrue();

        var unmergeCommands = CellMergePlanner.CreateFormatCellsMergeCommands(
            sheet,
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            mergeCells: false);

        unmergeCommands.Should().ContainSingle().Which.Should().BeOfType<UnmergeCellsCommand>();
    }
}
