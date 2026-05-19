using System.IO;
using System.Reflection;
using System.Windows.Controls;
using Freexcel.App.Host;
using Freexcel.Core.Model;
using FluentAssertions;

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
    public void FormatCellsDialog_MapsAllSupportedStyleDiffFields()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml.cs"));

        foreach (var field in new[]
        {
            "Bold", "Italic", "Underline", "Strikethrough", "FontName", "FontSize",
            "FontColor", "FillColor", "HAlign", "VAlign", "WrapText", "ShrinkToFit",
            "NumberFormat", "DoubleUnderline", "IndentLevel", "TextRotation",
            "BorderTop", "BorderRight", "BorderBottom", "BorderLeft", "Locked", "ClearFill",
        })
        {
            source.Should().Contain($"{field}:");
        }

        source.Should().Contain("s.DoubleUnderline");
        source.Should().Contain("s.IndentLevel");
        source.Should().Contain("s.TextRotation");
        source.Should().Contain("s.BorderTop");
        source.Should().Contain("s.BorderRight");
        source.Should().Contain("s.BorderBottom");
        source.Should().Contain("s.BorderLeft");
        source.Should().Contain("s.Locked");
    }

    [Fact]
    public void FormatCellsDialog_ExposesShrinkToFitAndMapsItIntoStyleDiff()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml.cs"));

        xaml.Should().Contain("x:Name=\"DlgShrinkToFitCheck\"");
        xaml.Should().Contain("Content=\"Shrink to fit\"");
        source.Should().Contain("DlgShrinkToFitCheck.IsChecked = s.ShrinkToFit;");
        source.Should().Contain("ShrinkToFit:");
        source.Should().Contain("DlgShrinkToFitCheck.IsChecked");
        source.Should().Contain("Enum.GetNames(typeof(CellHAlign))");
        source.Should().Contain("Enum.GetNames(typeof(CellVAlign))");
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
