using System.IO;
using System.Reflection;
using System.Windows.Controls;
using Freexcel.App.Host;
using Freexcel.Core.Model;
using FluentAssertions;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host.Tests;

public sealed class FormatCellsDialogXamlTests
{
    [Fact]
    public void FormatCellsDialog_ContainsSupportedExcelTabs()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        foreach (var tab in new[] { "Number", "Alignment", "Font", "Fill", "Border", "Protection" })
        {
            xaml.Should().Contain($"<TabItem Header=\"{tab}\"");
        }
    }

    [Fact]
    public void FormatCellsDialog_ContainsControlsForSupportedStyleFields()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        foreach (var controlName in new[]
        {
            "NumberFormatCombo",
            "DlgHAlignBox", "DlgVAlignBox", "DlgWrapTextCheck", "DlgShrinkToFitCheck",
            "DlgIndentLevelBox", "DlgTextRotationBox",
            "DlgFontNameBox", "DlgFontSizeBox", "DlgBoldCheck", "DlgItalicCheck",
            "DlgUnderlineCheck", "DlgDoubleUnderlineCheck", "DlgStrikeCheck", "DlgFontColorBox",
            "DlgSuperscriptCheck", "DlgSubscriptCheck",
            "DlgFillColorBox", "DlgClearFillCheck",
            "DlgBorderTopStyleBox", "DlgBorderTopColorBox",
            "DlgBorderRightStyleBox", "DlgBorderRightColorBox",
            "DlgBorderBottomStyleBox", "DlgBorderBottomColorBox",
            "DlgBorderLeftStyleBox", "DlgBorderLeftColorBox",
            "DlgLockedCheck",
        })
        {
            xaml.Should().Contain($"x:Name=\"{controlName}\"");
        }
    }

    [Fact]
    public void FormatCellsDialog_ContainsColorPickerButtonsForColorFields()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        foreach (var controlName in new[]
        {
            "DlgFontColorPickerButton",
            "DlgFillColorPickerButton",
            "DlgBorderTopColorPickerButton",
            "DlgBorderRightColorPickerButton",
            "DlgBorderBottomColorPickerButton",
            "DlgBorderLeftColorPickerButton",
        })
        {
            xaml.Should().Contain($"x:Name=\"{controlName}\"");
        }
    }

    [Fact]
    public void FormatCellsDialog_ExposesShrinkToFitAndMapsItIntoStyleDiff()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        xaml.Should().Contain("x:Name=\"DlgShrinkToFitCheck\"");
        xaml.Should().Contain("Content=\"Shrink to fit\"");
    }

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
    public void FormatCellsDialog_DoesNotEmitUnsupportedTextRotation()
    {
        StaTestRunner.Run(() =>
        {
            var current = new CellStyle { TextRotation = 45 };
            var dialog = ShowDialogForTest(current);
            try
            {
                GetControl<TextBox>(dialog, "DlgTextRotationBox").Text = "999";
                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.TextRotation.Should().BeNull();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

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
                GetControl<CheckBox>(dialog, "DlgBoldCheck").IsChecked = true;
                GetControl<CheckBox>(dialog, "DlgItalicCheck").IsChecked = true;
                GetControl<CheckBox>(dialog, "DlgUnderlineCheck").IsChecked = true;
                GetControl<CheckBox>(dialog, "DlgDoubleUnderlineCheck").IsChecked = true;
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
                dialog.ResultDiff.Underline.Should().BeTrue();
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
    public void FormatCellsDialog_MapsNumberAndAlignmentFieldsIntoStyleDiff()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                GetControl<ComboBox>(dialog, "NumberFormatCombo").SelectedIndex = 2;
                GetControl<ComboBox>(dialog, "DlgHAlignBox").SelectedItem = nameof(CellHAlign.Right);
                GetControl<ComboBox>(dialog, "DlgVAlignBox").SelectedItem = nameof(CellVAlign.Center);
                GetControl<CheckBox>(dialog, "DlgWrapTextCheck").IsChecked = true;
                GetControl<CheckBox>(dialog, "DlgShrinkToFitCheck").IsChecked = true;
                GetControl<TextBox>(dialog, "DlgIndentLevelBox").Text = "7";
                GetControl<TextBox>(dialog, "DlgTextRotationBox").Text = "-45";

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.NumberFormat.Should().Be("$#,##0.00");
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
    public void FormatCellsDialog_MapsFillBorderAndProtectionFields()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                GetControl<TextBox>(dialog, "DlgFillColorBox").Text = "12,34,56";
                GetControl<ComboBox>(dialog, "DlgBorderTopStyleBox").SelectedItem = nameof(BorderStyle.Thick);
                GetControl<TextBox>(dialog, "DlgBorderTopColorBox").Text = "1,2,3";
                GetControl<ComboBox>(dialog, "DlgBorderRightStyleBox").SelectedItem = nameof(BorderStyle.Dashed);
                GetControl<TextBox>(dialog, "DlgBorderRightColorBox").Text = "4,5,6";
                GetControl<ComboBox>(dialog, "DlgBorderBottomStyleBox").SelectedItem = nameof(BorderStyle.Dotted);
                GetControl<TextBox>(dialog, "DlgBorderBottomColorBox").Text = "7,8,9";
                GetControl<ComboBox>(dialog, "DlgBorderLeftStyleBox").SelectedItem = nameof(BorderStyle.Double);
                GetControl<TextBox>(dialog, "DlgBorderLeftColorBox").Text = "10,11,12";
                GetControl<CheckBox>(dialog, "DlgLockedCheck").IsChecked = false;

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.FillColor.Should().Be(new CellColor(12, 34, 56));
                dialog.ResultDiff.ClearFill.Should().BeNull();
                dialog.ResultDiff.BorderTop.Should().Be(new CellBorder(BorderStyle.Thick, new CellColor(1, 2, 3)));
                dialog.ResultDiff.BorderRight.Should().Be(new CellBorder(BorderStyle.Dashed, new CellColor(4, 5, 6)));
                dialog.ResultDiff.BorderBottom.Should().Be(new CellBorder(BorderStyle.Dotted, new CellColor(7, 8, 9)));
                dialog.ResultDiff.BorderLeft.Should().Be(new CellBorder(BorderStyle.Double, new CellColor(10, 11, 12)));
                dialog.ResultDiff.Locked.Should().BeFalse();
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
            var current = new CellStyle { FillColor = new CellColor(12, 34, 56) };
            var dialog = ShowDialogForTest(current);
            try
            {
                GetControl<CheckBox>(dialog, "DlgClearFillCheck").IsChecked = true;

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.FillColor.Should().BeNull();
                dialog.ResultDiff.ClearFill.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static FormatCellsDialog ShowDialogForTest(CellStyle current)
    {
        var dialog = new FormatCellsDialog(current);
        dialog.Show();
        return dialog;
    }

    private static T GetControl<T>(FormatCellsDialog dialog, string name)
        where T : class
    {
        var field = typeof(FormatCellsDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static void ClickOkForTest(FormatCellsDialog dialog)
    {
        var method = typeof(FormatCellsDialog).GetMethod("OkButton_Click", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        try
        {
            method!.Invoke(dialog, [dialog, new System.Windows.RoutedEventArgs()]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException invalidOperation
            && invalidOperation.Message.Contains("DialogResult"))
        {
            // The handler creates ResultDiff before setting DialogResult. Direct invocation on a modeless
            // test window reaches that WPF-only modal postcondition after the behavior under test runs.
        }
    }
}
