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

        var orderedTabs = new[] { "_Number", "_Alignment", "_Font", "_Border", "F_ill", "_Protection" };
        var previousIndex = -1;
        foreach (var tab in orderedTabs)
        {
            var index = xaml.IndexOf($"<TabItem Header=\"{tab}\"", StringComparison.Ordinal);
            index.Should().BeGreaterThan(previousIndex);
            previousIndex = index;
        }
    }

    [Fact]
    public void FormatCellsParityCapture_UsesSameTabOrderAsDialog()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("\"dialog.FormatCells\"");
        source.Should().Contain("[\"Number\", \"Alignment\", \"Font\", \"Border\", \"Fill\", \"Protection\"]");
        source.Should().NotContain("[\"Number\", \"Alignment\", \"Font\", \"Fill\", \"Border\", \"Protection\"]");
    }

    [Fact]
    public void FormatCellsDialog_ExposesKeyboardAccessKeysForTabsAndButtons()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        foreach (var tab in new[] { "_Number", "_Alignment", "_Font", "_Border", "F_ill", "_Protection" })
            xaml.Should().Contain($"<TabItem Header=\"{tab}\"");

        xaml.ShouldContainLocalizedAttribute("Content", "_OK");
        xaml.ShouldContainLocalizedAttribute("Content", "_Cancel");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"FormatCellsOkButton\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"FormatCellsCancelButton\"");
        xaml.Should().Contain("IsDefault=\"True\"");
        xaml.Should().Contain("IsCancel=\"True\"");
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_UsesExcelLikePaneDensityAndSelectionChrome()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        xaml.Should().Contain("x:Name=\"NumberCategoryList\" Height=\"278\"");
        xaml.Should().Contain("FocusVisualStyle=\"{x:Null}\"");
        xaml.Should().Contain("<Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/>");
        xaml.Should().Contain("<ControlTemplate TargetType=\"{x:Type ListBoxItem}\">");
        xaml.Should().Contain("SystemColors.HighlightBrushKey");
        xaml.Should().Contain("SystemColors.HighlightTextBrushKey");
        xaml.Should().Contain("x:Name=\"NumberPreview\" FontWeight=\"Bold\"");
        xaml.Should().Contain("<StackPanel Grid.Column=\"1\" Width=\"330\" HorizontalAlignment=\"Left\">");
        xaml.Should().Contain("Width=\"94\"");
        xaml.Should().Contain("Height=\"36\"");
        xaml.Should().Contain("x:Name=\"NumberGeneralDescription\"");
    }

    [Fact]
    public void FormatCellsDialog_ActionButtons_UseCompactExcelLikeSpacing()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");

        xaml.Should().Contain("Width=\"74\" Height=\"24\" Margin=\"5,0,0,0\" IsDefault=\"True\"");
        xaml.Should().Contain("Width=\"74\" Height=\"24\" Margin=\"8,0,0,0\" IsCancel=\"True\"");
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
        var source = DialogSourceTestSupport.ReadHostSources("FormatCellsDialog.xaml.cs");

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

        var borderSource = DialogSourceTestSupport.ReadHostSources("FormatCellsDialog.Border.cs");
        borderSource.Should().Contain("PickColorInto(DlgBorderLineColorBox, allowNoColor: false, UiText.Get(\"FormatCells_BorderColorTitle\"))");
        borderSource.Should().Contain("PickColorInto(DlgBorderTopColorBox, allowNoColor: false, UiText.Get(\"FormatCells_TopBorderColorTitle\"))");
        borderSource.Should().Contain("PickColorInto(DlgBorderRightColorBox, allowNoColor: false, UiText.Get(\"FormatCells_RightBorderColorTitle\"))");
        borderSource.Should().Contain("PickColorInto(DlgBorderBottomColorBox, allowNoColor: false, UiText.Get(\"FormatCells_BottomBorderColorTitle\"))");
        borderSource.Should().Contain("PickColorInto(DlgBorderLeftColorBox, allowNoColor: false, UiText.Get(\"FormatCells_LeftBorderColorTitle\"))");
    }

    [Fact]
    public void FormatCellsDialog_UsesCanonicalBorderSelectionContract()
    {
        var dialogSource = DialogSourceTestSupport.ReadHostSources("FormatCellsDialog.xaml.cs");
        var borderSource = DialogSourceTestSupport.ReadHostSources("FormatCellsDialog.Border.cs");

        dialogSource.Should().Contain("FormatCellsDialogBorderSelection ResultBorderSelection");
        dialogSource.Should().Contain("new FormatCellsDialogBorderSelection(");
        borderSource.Should().NotContain("record FormatCellsBorderSelection");
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
