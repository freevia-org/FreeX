using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    public void FormatCellsDialog_BorderTab_UsesExcelLikePresetLineColorAndPreviewLayout()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var text in new[]
        {
            "Text=\"Presets\"",
            "Text=\"Line\"",
            "Content=\"_Color:\"",
            "Text=\"Border\""
        })
            xaml.Should().Contain(text);

        foreach (var controlName in new[]
        {
            "DlgBorderPresetNoneButton",
            "DlgBorderPresetOutlineButton",
            "DlgBorderPresetInsideButton",
            "DlgBorderLineStyleBox",
            "DlgBorderLineColorBox",
            "DlgBorderPreviewArea",
            "DlgBorderPreviewTopButton",
            "DlgBorderPreviewRightButton",
            "DlgBorderPreviewBottomButton",
            "DlgBorderPreviewLeftButton",
            "DlgBorderPreviewInsideVertical",
            "DlgBorderPreviewInsideHorizontal"
        })
            xaml.Should().Contain($"x:Name=\"{controlName}\"");
    }

    [Fact]
    public void FormatCellsDialog_BorderTab_ExposesAccessKeysForPresetPreviewAndDetailsControls()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var content in new[]
        {
            "Content=\"_None\"",
            "Content=\"_Outline\"",
            "Content=\"_Inside\"",
            "Content=\"To_p\"",
            "Content=\"L_eft\"",
            "Content=\"Ri_ght\"",
            "Content=\"Botto_m\"",
            "Header=\"Individual border _details\""
        })
            xaml.Should().Contain(content);
    }

    [Fact]
    public void FormatCellsDialog_BorderTab_LabelsLineControlsWithAccessKeyTargets()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var target in new[]
        {
            "Content=\"_Style:\" Target=\"{Binding ElementName=DlgBorderLineStyleList}\"",
            "Content=\"_Color:\" Target=\"{Binding ElementName=DlgBorderLineColorBox}\""
        })
            xaml.Should().Contain(target);
    }

    [Fact]
    public void FormatCellsDialog_BorderTab_LabelsIndividualSideStyleControlsWithAccessKeyTargets()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var target in new[]
        {
            "Content=\"_Top:\" Target=\"{Binding ElementName=DlgBorderTopStyleBox}\"",
            "Content=\"_Right:\" Target=\"{Binding ElementName=DlgBorderRightStyleBox}\"",
            "Content=\"_Bottom:\" Target=\"{Binding ElementName=DlgBorderBottomStyleBox}\"",
            "Content=\"_Left:\" Target=\"{Binding ElementName=DlgBorderLeftStyleBox}\""
        })
            xaml.Should().Contain(target);
    }

    [Fact]
    public void FormatCellsDialog_BorderTab_NamesIndividualSideColorInputsForAccessibility()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var name in new[]
        {
            "x:Name=\"DlgBorderTopColorBox\" Height=\"24\" AutomationProperties.Name=\"Top border color (R,G,B)\"",
            "x:Name=\"DlgBorderRightColorBox\" Height=\"24\" AutomationProperties.Name=\"Right border color (R,G,B)\"",
            "x:Name=\"DlgBorderBottomColorBox\" Height=\"24\" AutomationProperties.Name=\"Bottom border color (R,G,B)\"",
            "x:Name=\"DlgBorderLeftColorBox\" Height=\"24\" AutomationProperties.Name=\"Left border color (R,G,B)\""
        })
            xaml.Should().Contain(name);
    }

    [Fact]
    public void FormatCellsDialog_FillTab_ExposesBackgroundPatternControlsAndSamplePreview()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");
        var source = ReadFormatCellsDialogSource();

        foreach (var text in new[]
        {
            "Content=\"_Background Color:\"",
            "Content=\"Pattern _Color:\"",
            "Content=\"Pattern _Style:\"",
            "Text=\"Sample\""
        })
            xaml.Should().Contain(text);

        foreach (var controlName in new[]
        {
            "DlgFillBackgroundPreview",
            "DlgFillSamplePreview",
            "DlgFillPalettePanel",
            "DlgFillPatternColorBox",
            "DlgFillPatternStyleBox"
        })
            xaml.Should().Contain($"x:Name=\"{controlName}\"");

        source.Should().Contain("FillPatternChoices");
        source.Should().Contain("FillPatternStyle:");
    }

    [Fact]
    public void FormatCellsDialog_FillAndBorderTabs_ExposeExcelLikeColorSwatchesAndPreviews()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var expected in new[]
        {
            "Text=\"Background Color:\"",
            "x:Name=\"DlgBorderLineColorPreview\"",
            "ToolTip=\"Black border\"",
            "ToolTip=\"Red border\"",
            "ToolTip=\"Blue border\""
        })
            xaml.Should().Contain(expected);
    }

    [Fact]
    public void FormatCellsDialog_FillTab_UsesExcelLikePalettePatternAndSampleAreas()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var expected in new[]
        {
            "Title=\"Format Cells\" Width=\"620\" Height=\"540\"",
            "x:Name=\"DlgFillPalettePanel\" Columns=\"10\" Rows=\"3\"",
            "x:Name=\"DlgFillPatternColorPalettePanel\" Columns=\"8\" Rows=\"1\"",
            "x:Name=\"DlgFillPatternSamplePreview\"",
            "Text=\"Pattern Color:\"",
            "Text=\"Pattern Style:\""
        })
            xaml.Should().Contain(expected);
    }

    [Fact]
    public void FormatCellsDialog_BorderTab_UsesExcelLikeLineListPaletteAndUnclippedPreview()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");
        var source = ReadFormatCellsDialogSource();

        foreach (var expected in new[]
        {
            "x:Name=\"DlgBorderLineStyleList\"",
            "Height=\"124\"",
            "x:Name=\"DlgBorderLinePalettePanel\" Columns=\"8\" Rows=\"2\"",
            "Width=\"244\" Height=\"164\"",
            "MinWidth=\"48\"",
            "MinHeight=\"30\"",
            "ToolTip=\"Apply top border\"",
            "ToolTip=\"Apply right border\"",
            "ToolTip=\"Apply bottom border\"",
            "ToolTip=\"Apply left border\""
        })
            xaml.Should().Contain(expected);

        source.Should().Contain("FormatCellsBorderPalettePlanner.StyleChoices");
        source.Should().Contain("FormatCellsBorderPalettePlanner.ColorEntries");
        source.Should().NotContain("Tag = color.ToString");
    }

    [Fact]
    public void FormatCellsDialog_BorderPalette_RendersCanonicalTypedEntries()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new FormatCellsDialog(CellStyle.Default, FormatCellsDialogTab.Border);
            dialog.Show();
            try
            {
                var panel = DialogSourceTestSupport.GetPrivateField<UniformGrid>(dialog, "DlgBorderLinePalettePanel");
                panel.Children.Cast<Button>()
                    .Select(button => button.Tag)
                    .Should().Equal(FormatCellsBorderPalettePlanner.ColorEntries);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_FillTab_LabelsEditableControlsWithAccessKeyTargets()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var target in new[]
        {
            "Content=\"_Background Color:\" Target=\"{Binding ElementName=DlgFillColorBox}\"",
            "Content=\"Pattern _Color:\" Target=\"{Binding ElementName=DlgFillPatternColorBox}\"",
            "Content=\"Pattern _Style:\" Target=\"{Binding ElementName=DlgFillPatternStyleBox}\""
        })
            xaml.Should().Contain(target);
    }
}
