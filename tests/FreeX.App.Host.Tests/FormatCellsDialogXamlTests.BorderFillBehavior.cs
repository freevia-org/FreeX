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
    public void FormatCellsDialog_MapsFillBorderAndProtectionFields()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                GetControl<TextBox>(dialog, "DlgFillColorBox").Text = "12,34,56";
                GetControl<TextBox>(dialog, "DlgFillPatternColorBox").Text = "90,80,70";
                GetControl<ComboBox>(dialog, "DlgFillPatternStyleBox").SelectedItem = "Diagonal Crosshatch";
                GetControl<ComboBox>(dialog, "DlgBorderTopStyleBox").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.Thick);
                GetControl<TextBox>(dialog, "DlgBorderTopColorBox").Text = "1,2,3";
                GetControl<ComboBox>(dialog, "DlgBorderRightStyleBox").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.Dashed);
                GetControl<TextBox>(dialog, "DlgBorderRightColorBox").Text = "4,5,6";
                GetControl<ComboBox>(dialog, "DlgBorderBottomStyleBox").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.Dotted);
                GetControl<TextBox>(dialog, "DlgBorderBottomColorBox").Text = "7,8,9";
                GetControl<ComboBox>(dialog, "DlgBorderLeftStyleBox").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.Double);
                GetControl<TextBox>(dialog, "DlgBorderLeftColorBox").Text = "10,11,12";
                GetControl<CheckBox>(dialog, "DlgLockedCheck").IsChecked = false;
                GetControl<CheckBox>(dialog, "DlgHiddenCheck").IsChecked = true;

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.FillColor.Should().Be(new CellColor(12, 34, 56));
                dialog.ResultDiff.FillPatternColor.Should().Be(new CellColor(90, 80, 70));
                dialog.ResultDiff.FillPatternStyle.Should().Be(CellFillPatternStyle.DarkGrid);
                dialog.ResultDiff.ClearFill.Should().BeNull();
                dialog.ResultDiff.BorderTop.Should().Be(new CellBorder(BorderStyle.Thick, new CellColor(1, 2, 3)));
                dialog.ResultDiff.BorderRight.Should().Be(new CellBorder(BorderStyle.Dashed, new CellColor(4, 5, 6)));
                dialog.ResultDiff.BorderBottom.Should().Be(new CellBorder(BorderStyle.Dotted, new CellColor(7, 8, 9)));
                dialog.ResultDiff.BorderLeft.Should().Be(new CellBorder(BorderStyle.Double, new CellColor(10, 11, 12)));
                dialog.ResultDiff.Locked.Should().BeFalse();
                dialog.ResultDiff.Hidden.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_BorderPresetsExposeRangeBorderSelection()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                GetControl<ListBox>(dialog, "DlgBorderLineStyleList").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.Dashed);
                GetControl<TextBox>(dialog, "DlgBorderLineColorBox").Text = "20,30,40";
                InvokeDialogHandler(dialog, "DlgBorderPresetInsideButton_Click");
                ClickOkForTest(dialog);

                dialog.ResultBorderSelection.Clear.Should().BeFalse();
                dialog.ResultBorderSelection.Outline.Should().BeNull();
                dialog.ResultBorderSelection.Inside.Should().Be(new CellBorder(BorderStyle.Dashed, new CellColor(20, 30, 40)));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_OutlineAndNonePresetsExposeRangeBorderSelection()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                GetControl<ListBox>(dialog, "DlgBorderLineStyleList").SelectedItem = FormatCellsBorderPalettePlanner.ChoiceFor(BorderStyle.Thick);
                GetControl<TextBox>(dialog, "DlgBorderLineColorBox").Text = "1,2,3";
                InvokeDialogHandler(dialog, "DlgBorderPresetOutlineButton_Click");
                ClickOkForTest(dialog);

                dialog.ResultBorderSelection.Outline.Should().Be(new CellBorder(BorderStyle.Thick, new CellColor(1, 2, 3)));
                dialog.ResultBorderSelection.Inside.Should().BeNull();
                dialog.ResultBorderSelection.Clear.Should().BeFalse();

                InvokeDialogHandler(dialog, "DlgBorderPresetNoneButton_Click");
                ClickOkForTest(dialog);
                dialog.ResultBorderSelection.Clear.Should().BeTrue();
                dialog.ResultBorderSelection.Outline.Should().BeNull();
                dialog.ResultBorderSelection.Inside.Should().BeNull();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_BorderPreviewButtonsToggleExistingSidesOff()
    {
        StaTestRunner.Run(() =>
        {
            var current = new CellStyle
            {
                BorderTop = new CellBorder(BorderStyle.Thin, CellColor.Black)
            };
            var dialog = ShowDialogForTest(current);
            try
            {
                InvokeDialogHandler(dialog, "DlgBorderPreviewTopButton_Click");
                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.BorderTop.Should().Be(new CellBorder(BorderStyle.None, CellColor.Black));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_MapsClearFillIntoStyleDiff()
    {
        StaTestRunner.Run(() =>
        {
            var current = new CellStyle
            {
                FillColor = new CellColor(12, 34, 56),
                FillPatternStyle = CellFillPatternStyle.DarkGrid,
                FillPatternColor = new CellColor(90, 80, 70)
            };
            var dialog = ShowDialogForTest(current);
            try
            {
                GetControl<CheckBox>(dialog, "DlgClearFillCheck").IsChecked = true;

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.FillColor.Should().BeNull();
                dialog.ResultDiff.FillPatternStyle.Should().Be(CellFillPatternStyle.None);
                dialog.ResultDiff.FillPatternColor.Should().BeNull();
                dialog.ResultDiff.ClearFill.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
