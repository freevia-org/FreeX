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
    public void FormatCellsDialog_FontTab_ExposesStyleUnderlineEffectsAndSamplePreview()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var text in new[]
        {
            "Content=\"Font _style:\"",
            "Content=\"_Underline:\"",
            "Text=\"Effects\"",
            "Text=\"Sample\""
        })
            xaml.Should().Contain(text);

        foreach (var controlName in new[]
        {
            "DlgFontStyleList",
            "DlgUnderlineStyleBox",
            "DlgFontEffectsGroup",
            "DlgFontSamplePreview"
        })
            xaml.Should().Contain($"x:Name=\"{controlName}\"");
    }

    [Fact]
    public void FormatCellsDialog_FontTab_ExposesFontColorSwatchesAndPreviewUpdate()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var expected in new[]
        {
            "x:Name=\"DlgFontColorPalettePanel\"",
            "Columns=\"8\" Rows=\"2\"",
            "ToolTip=\"Automatic font color\"",
            "ToolTip=\"Red font\"",
            "ToolTip=\"Blue font\"",
            "Click=\"DlgFontColorSwatchButton_Click\""
        })
            xaml.Should().Contain(expected);

        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                var colorBox = GetControl<TextBox>(dialog, "DlgFontColorBox");
                var preview = GetControl<TextBlock>(dialog, "DlgFontSamplePreview");
                var swatch = new Button { Tag = "192,0,0" };

                InvokeDialogHandler(dialog, "DlgFontColorSwatchButton_Click", swatch);

                colorBox.Text.Should().Be("192,0,0");
                preview.Foreground.Should().BeOfType<SolidColorBrush>()
                    .Which.Color.Should().Be(Color.FromRgb(192, 0, 0));

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.FontColor.Should().Be(new CellColor(192, 0, 0));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_FontTab_NormalFontResetsModeledFontFields()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");
        xaml.Should().Contain("x:Name=\"DlgNormalFontCheck\" Content=\"_Normal font\"");
        xaml.Should().Contain("Checked=\"DlgNormalFontCheck_Checked\"");

        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle
            {
                FontName = "Verdana",
                FontSize = 18,
                Bold = true,
                Italic = true,
                Underline = true,
                DoubleUnderline = true,
                Strikethrough = true,
                Superscript = true,
                Subscript = true,
                FontColor = new CellColor(192, 0, 0)
            });
            try
            {
                InvokeDialogHandler(dialog, "DlgNormalFontCheck_Checked", GetControl<CheckBox>(dialog, "DlgNormalFontCheck"));

                GetControl<ComboBox>(dialog, "DlgFontNameBox").Text.Should().Be(CellStyle.Default.FontName);
                GetControl<ComboBox>(dialog, "DlgFontSizeBox").Text.Should().Be("11");
                GetControl<ListBox>(dialog, "DlgFontStyleList").SelectedItem.Should().Be("Regular");
                GetControl<ComboBox>(dialog, "DlgUnderlineStyleBox").SelectedItem.Should().Be("None");
                GetControl<CheckBox>(dialog, "DlgDoubleUnderlineCheck").IsChecked.Should().BeFalse();
                GetControl<CheckBox>(dialog, "DlgStrikeCheck").IsChecked.Should().BeFalse();
                GetControl<CheckBox>(dialog, "DlgSuperscriptCheck").IsChecked.Should().BeFalse();
                GetControl<CheckBox>(dialog, "DlgSubscriptCheck").IsChecked.Should().BeFalse();
                GetControl<TextBox>(dialog, "DlgFontColorBox").Text.Should().Be("0,0,0");

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.FontName.Should().Be(CellStyle.Default.FontName);
                dialog.ResultDiff.FontSize.Should().Be(CellStyle.Default.FontSize);
                dialog.ResultDiff.Bold.Should().BeFalse();
                dialog.ResultDiff.Italic.Should().BeFalse();
                dialog.ResultDiff.Underline.Should().BeFalse();
                dialog.ResultDiff.DoubleUnderline.Should().BeFalse();
                dialog.ResultDiff.Strikethrough.Should().BeFalse();
                dialog.ResultDiff.Superscript.Should().BeFalse();
                dialog.ResultDiff.Subscript.Should().BeFalse();
                dialog.ResultDiff.FontColor.Should().Be(CellColor.Black);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_FontTab_DoesNotDuplicateFontStyleAndUnderlineControlsAsEffects()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        xaml.Should().NotContain("Content=\"_Bold\"");
        xaml.Should().NotContain("Content=\"_Italic\"");
        xaml.Should().NotContain("Content=\"_Underline\"");
        xaml.Should().NotContain("x:Name=\"DlgBoldCheck\"");
        xaml.Should().NotContain("x:Name=\"DlgItalicCheck\"");
        xaml.Should().NotContain("x:Name=\"DlgUnderlineCheck\"");
        xaml.Should().Contain("x:Name=\"DlgFontStyleList\"");
        xaml.Should().Contain("x:Name=\"DlgUnderlineStyleBox\"");
    }

    [Fact]
    public void FormatCellsDialog_ProtectionTab_ExposesLockedHiddenAndExcelProtectionExplanation()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        xaml.Should().Contain("x:Name=\"DlgLockedCheck\" Content=\"_Locked\"");
        xaml.Should().Contain("x:Name=\"DlgHiddenCheck\" Content=\"_Hidden\"");
        xaml.Should().Contain("Locking cells or hiding formulas has no effect until you protect the worksheet.");
    }

    [Fact]
    public void FormatCellsDialog_FontTab_LabelsEditableControlsWithAccessKeyTargets()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var content in new[]
        {
            "Content=\"_Font:\" Target=\"{Binding ElementName=DlgFontNameBox}\"",
            "Content=\"Font _style:\" Target=\"{Binding ElementName=DlgFontStyleList}\"",
            "Content=\"_Size:\" Target=\"{Binding ElementName=DlgFontSizeBox}\"",
            "Content=\"_Underline:\" Target=\"{Binding ElementName=DlgUnderlineStyleBox}\"",
            "Content=\"_Color:\" Target=\"{Binding ElementName=DlgFontColorBox}\""
        })
            xaml.Should().Contain(content);
    }

    [Fact]
    public void FormatCellsDialog_FontTab_UsesEditableFontNameCombo()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        xaml.Should().Contain("x:Name=\"DlgFontNameBox\"");
        xaml.Should().Contain("IsEditable=\"True\"");
        xaml.Should().Contain("TextBoxBase.TextChanged=\"FontPreviewInput_Changed\"");
    }

    [Fact]
    public void FormatCellsDialog_FontTab_PopulatesInstalledFontsAndKeepsCustomCurrentFont()
    {
        StaTestRunner.Run(() =>
        {
            const string customFont = "FreeX Test Font Not Installed";
            var dialog = ShowDialogForTest(new CellStyle { FontName = customFont });
            try
            {
                var fontBox = GetControl<ComboBox>(dialog, "DlgFontNameBox");
                var availableFonts = fontBox.Items.Cast<string>().ToArray();

                availableFonts.Should().Contain(customFont);
                fontBox.SelectedItem.Should().Be(customFont);
                availableFonts.Should().Contain(Fonts.SystemFontFamilies.Select(f => f.Source));
                availableFonts.Should().HaveCountGreaterThan(6);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_FontTab_AppliesTypedFontName()
    {
        StaTestRunner.Run(() =>
        {
            const string typedFont = "FreeX Typed Font";
            var dialog = ShowDialogForTest(new CellStyle());
            try
            {
                var fontBox = GetControl<ComboBox>(dialog, "DlgFontNameBox");
                fontBox.SelectedItem = null;
                fontBox.Text = $"  {typedFont}  ";

                ClickOkForTest(dialog);

                dialog.ResultDiff.Should().NotBeNull();
                dialog.ResultDiff!.FontName.Should().Be(typedFont);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

}
