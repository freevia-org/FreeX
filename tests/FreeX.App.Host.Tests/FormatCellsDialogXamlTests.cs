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
    public void FormatCellsDialog_ContainsSupportedExcelTabs()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var tab in new[] { "_Number", "_Alignment", "_Font", "F_ill", "_Border", "_Protection" })
        {
            xaml.Should().Contain($"<TabItem Header=\"{tab}\"");
        }
    }

    [Fact]
    public void FormatCellsDialog_ExposesKeyboardAccessKeysForTabsAndButtons()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var tab in new[] { "_Number", "_Alignment", "_Font", "F_ill", "_Border", "_Protection" })
            xaml.Should().Contain($"<TabItem Header=\"{tab}\"");

        xaml.ShouldContainLocalizedAttribute("Content", "_OK");
        xaml.ShouldContainLocalizedAttribute("Content", "_Cancel");
    }

    [Fact]
    public void FormatCellsDialog_ExposesKeyboardAccessKeysForSupportedOptionControls()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var content in new[]
        {
            "_Wrap text",
            "S_hrink to fit",
            "_Merge cells",
            "_Normal font",
            "_Double underline",
            "_Strikethrough",
            "Super_script",
            "Su_bscript",
            "_Clear fill",
            "_Locked",
            "_Hidden"
        })
            xaml.ShouldContainLocalizedAttribute("Content", content);

        foreach (var picker in new[]
        {
            "Content=\"_Pick\"",
            "Content=\"P_ick\""
        })
            xaml.Should().Contain(picker);
    }

    [Fact]
    public void FormatCellsDialogOpenedFromKeyboard_FocusesActiveTabFirstControl()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatCellsDialog.xaml.cs"));

        source.Should().Contain("FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("NumberCategoryList");
        source.Should().Contain("DlgHAlignBox");
        source.Should().Contain("DlgFontNameBox");
        source.Should().Contain("DlgFillColorBox");
        source.Should().Contain("DlgBorderLineStyleList");
        source.Should().Contain("DlgLockedCheck");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void FormatCellsDialogOpenedOnBorderTab_FocusesVisibleLineStyleList()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new CellStyle(), FormatCellsDialogTab.Border);
            try
            {
                var lineStyleList = GetControl<ListBox>(dialog, "DlgBorderLineStyleList");
                var hiddenLineStyleBox = GetControl<ComboBox>(dialog, "DlgBorderLineStyleBox");

                lineStyleList.IsVisible.Should().BeTrue();
                hiddenLineStyleBox.IsVisible.Should().BeFalse();
                FocusManager.GetFocusedElement(dialog).Should().BeSameAs(lineStyleList);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FormatCellsDialog_ContainsControlsForSupportedStyleFields()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var controlName in new[]
        {
            "NumberFormatCombo",
            "DlgHAlignBox", "DlgVAlignBox", "DlgWrapTextCheck", "DlgShrinkToFitCheck", "DlgMergeCellsCheck",
            "DlgIndentLevelBox", "DlgTextRotationBox",
            "DlgFontNameBox", "DlgFontSizeBox", "DlgFontStyleList",
            "DlgUnderlineStyleBox", "DlgNormalFontCheck", "DlgDoubleUnderlineCheck", "DlgStrikeCheck", "DlgFontColorBox",
            "DlgSuperscriptCheck", "DlgSubscriptCheck",
            "DlgFillColorBox", "DlgClearFillCheck", "DlgFillPalettePanel",
            "DlgBorderTopStyleBox", "DlgBorderTopColorBox",
            "DlgBorderRightStyleBox", "DlgBorderRightColorBox",
            "DlgBorderBottomStyleBox", "DlgBorderBottomColorBox",
            "DlgBorderLeftStyleBox", "DlgBorderLeftColorBox",
            "DlgLockedCheck", "DlgHiddenCheck",
        })
        {
            xaml.Should().Contain($"x:Name=\"{controlName}\"");
        }
    }

    [Fact]
    public void FormatCellsDialog_ContainsColorPickerButtonsForColorFields()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

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
    public void FormatCellsDialog_ColorPickerButtons_OpenContextNamedExcelDialogs()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("PickColorInto(DlgFontColorBox, allowNoColor: false, UiText.Get(\"FormatCells_FontColorTitle\"))");
        source.Should().Contain("PickColorInto(DlgFillColorBox, allowNoColor: true, UiText.Get(\"FormatCells_FillColorTitle\"))");
        source.Should().Contain("PickColorInto(DlgFillPatternColorBox, allowNoColor: true, UiText.Get(\"FormatCells_PatternColorTitle\"))");
        source.Should().Contain("Title = title");

        var borderSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatCellsDialog.Border.cs"));
        borderSource.Should().Contain("PickColorInto(DlgBorderLineColorBox, allowNoColor: false, UiText.Get(\"FormatCells_BorderColorTitle\"))");
        borderSource.Should().Contain("PickColorInto(DlgBorderTopColorBox, allowNoColor: false, UiText.Get(\"FormatCells_TopBorderColorTitle\"))");
        borderSource.Should().Contain("PickColorInto(DlgBorderRightColorBox, allowNoColor: false, UiText.Get(\"FormatCells_RightBorderColorTitle\"))");
        borderSource.Should().Contain("PickColorInto(DlgBorderBottomColorBox, allowNoColor: false, UiText.Get(\"FormatCells_BottomBorderColorTitle\"))");
        borderSource.Should().Contain("PickColorInto(DlgBorderLeftColorBox, allowNoColor: false, UiText.Get(\"FormatCells_LeftBorderColorTitle\"))");
    }

    [Fact]
    public void FormatCellsDialog_ExposesShrinkToFitAndMapsItIntoStyleDiff()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        xaml.Should().Contain("x:Name=\"DlgShrinkToFitCheck\"");
        xaml.ShouldContainLocalizedAttribute("Content", "S_hrink to fit");
        xaml.Should().Contain("x:Name=\"DlgMergeCellsCheck\"");
        xaml.ShouldContainLocalizedAttribute("Content", "_Merge cells");
    }

    [Fact]
    public void FormatCellsDialog_AlignmentTab_LabelsEditableControlsWithAccessKeyTargets()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var content in new[]
        {
            "Content=\"_Horizontal alignment:\" Target=\"{Binding ElementName=DlgHAlignBox}\"",
            "Content=\"_Vertical alignment:\" Target=\"{Binding ElementName=DlgVAlignBox}\"",
            "Content=\"_Indent level (0-15):\" Target=\"{Binding ElementName=DlgIndentLevelBox}\"",
            "Content=\"Text _rotation (-90 to 90, or 255):\" Target=\"{Binding ElementName=DlgTextRotationBox}\""
        })
            xaml.Should().Contain(content);
    }

}
