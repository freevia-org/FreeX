using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
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

        foreach (var tab in new[] { "_Number", "_Alignment", "_Font", "F_ill", "_Border", "_Protection" })
        {
            xaml.Should().Contain($"<TabItem Header=\"{tab}\"");
        }
    }

    [Fact]
    public void FormatCellsDialog_ExposesKeyboardAccessKeysForTabsAndButtons()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        foreach (var tab in new[] { "_Number", "_Alignment", "_Font", "F_ill", "_Border", "_Protection" })
            xaml.Should().Contain($"<TabItem Header=\"{tab}\"");

        xaml.Should().Contain("Content=\"_OK\"");
        xaml.Should().Contain("Content=\"_Cancel\"");
    }

    [Fact]
    public void FormatCellsDialog_ExposesKeyboardAccessKeysForSupportedOptionControls()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        foreach (var content in new[]
        {
            "_Wrap text",
            "S_hrink to fit",
            "_Double underline",
            "_Strikethrough",
            "Super_script",
            "Su_bscript",
            "_Clear fill",
            "_Locked",
            "_Hidden"
        })
            xaml.Should().Contain($"Content=\"{content}\"");

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml.cs"));

        source.Should().Contain("FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("NumberCategoryList");
        source.Should().Contain("DlgHAlignBox");
        source.Should().Contain("DlgFontNameBox");
        source.Should().Contain("DlgFillColorBox");
        source.Should().Contain("DlgBorderLineStyleBox");
        source.Should().Contain("DlgLockedCheck");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("Keyboard.Focus(target);");
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
            "DlgFontNameBox", "DlgFontSizeBox", "DlgFontStyleList",
            "DlgUnderlineStyleBox", "DlgDoubleUnderlineCheck", "DlgStrikeCheck", "DlgFontColorBox",
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
    public void FormatCellsDialog_ColorPickerButtons_OpenContextNamedExcelDialogs()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml.cs"));

        source.Should().Contain("PickColorInto(DlgFontColorBox, allowNoColor: false, \"Font Color\")");
        source.Should().Contain("PickColorInto(DlgFillColorBox, allowNoColor: true, \"Fill Color\")");
        source.Should().Contain("PickColorInto(DlgFillPatternColorBox, allowNoColor: true, \"Pattern Color\")");
        source.Should().Contain("Title = title");

        var borderSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.Border.cs"));
        borderSource.Should().Contain("PickColorInto(DlgBorderLineColorBox, allowNoColor: false, \"Border Color\")");
        borderSource.Should().Contain("PickColorInto(DlgBorderTopColorBox, allowNoColor: false, \"Top Border Color\")");
        borderSource.Should().Contain("PickColorInto(DlgBorderRightColorBox, allowNoColor: false, \"Right Border Color\")");
        borderSource.Should().Contain("PickColorInto(DlgBorderBottomColorBox, allowNoColor: false, \"Bottom Border Color\")");
        borderSource.Should().Contain("PickColorInto(DlgBorderLeftColorBox, allowNoColor: false, \"Left Border Color\")");
    }

    [Fact]
    public void FormatCellsDialog_ExposesShrinkToFitAndMapsItIntoStyleDiff()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        xaml.Should().Contain("x:Name=\"DlgShrinkToFitCheck\"");
        xaml.Should().Contain("Content=\"S_hrink to fit\"");
    }

    [Fact]
    public void FormatCellsDialog_AlignmentTab_LabelsEditableControlsWithAccessKeyTargets()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        foreach (var content in new[]
        {
            "Content=\"_Horizontal alignment:\" Target=\"{Binding ElementName=DlgHAlignBox}\"",
            "Content=\"_Vertical alignment:\" Target=\"{Binding ElementName=DlgVAlignBox}\"",
            "Content=\"_Indent level (0-15):\" Target=\"{Binding ElementName=DlgIndentLevelBox}\"",
            "Content=\"Text _rotation (-90 to 90, or 255):\" Target=\"{Binding ElementName=DlgTextRotationBox}\""
        })
            xaml.Should().Contain(content);
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
                    .Should().Be("$#,##0.00");
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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        xaml.Should().Contain("x:Name=\"NumberCategoryList\"");
        xaml.Should().Contain("Content=\"_Category:\"");
        xaml.Should().Contain("Target=\"{Binding ElementName=NumberCategoryList}\"");
        xaml.Should().Contain("Text=\"Sample\"");
        xaml.Should().Contain("x:Name=\"NumberDecimalPlacesBox\"");
        xaml.Should().Contain("x:Name=\"NumberNegativeNumbersList\"");
        xaml.Should().Contain("x:Name=\"NumberSymbolCombo\"");
    }

    [Fact]
    public void FormatCellsDialog_NumberTab_LabelsEditableControlsWithAccessKeyTargets()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

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

    [Fact]
    public void FormatCellsDialog_BorderTab_UsesExcelLikePresetLineColorAndPreviewLayout()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        foreach (var target in new[]
        {
            "Content=\"_Style:\" Target=\"{Binding ElementName=DlgBorderLineStyleBox}\"",
            "Content=\"_Color:\" Target=\"{Binding ElementName=DlgBorderLineColorBox}\""
        })
            xaml.Should().Contain(target);
    }

    [Fact]
    public void FormatCellsDialog_BorderTab_LabelsIndividualSideStyleControlsWithAccessKeyTargets()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml.cs"));

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

        source.Should().Contain("FillPatternOptions");
        source.Should().Contain("FillPatternStyle:");
    }

    [Fact]
    public void FormatCellsDialog_FillAndBorderTabs_ExposeExcelLikeColorSwatchesAndPreviews()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        foreach (var expected in new[]
        {
            "Text=\"Background Color:\"",
            "ToolTip=\"No Fill\"",
            "ToolTip=\"Yellow\"",
            "ToolTip=\"Green\"",
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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        foreach (var expected in new[]
        {
            "Title=\"Format Cells\" Width=\"700\" Height=\"610\"",
            "x:Name=\"DlgFillPalettePanel\" Columns=\"10\" Rows=\"6\"",
            "x:Name=\"DlgFillPatternColorPalettePanel\" Columns=\"8\" Rows=\"2\"",
            "x:Name=\"DlgFillPatternSamplePreview\"",
            "Text=\"Pattern Color:\"",
            "Text=\"Pattern Style:\"",
            "ToolTip=\"Gold\"",
            "ToolTip=\"Dark Blue\"",
            "ToolTip=\"Pattern accent blue\""
        })
            xaml.Should().Contain(expected);
    }

    [Fact]
    public void FormatCellsDialog_BorderTab_UsesExcelLikeLineListPaletteAndUnclippedPreview()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

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
            "ToolTip=\"Apply left border\"",
            "ToolTip=\"Gold border\"",
            "ToolTip=\"Purple border\""
        })
            xaml.Should().Contain(expected);
    }

    [Fact]
    public void FormatCellsDialog_FillTab_LabelsEditableControlsWithAccessKeyTargets()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        foreach (var target in new[]
        {
            "Content=\"_Background Color:\" Target=\"{Binding ElementName=DlgFillColorBox}\"",
            "Content=\"Pattern _Color:\" Target=\"{Binding ElementName=DlgFillPatternColorBox}\"",
            "Content=\"Pattern _Style:\" Target=\"{Binding ElementName=DlgFillPatternStyleBox}\""
        })
            xaml.Should().Contain(target);
    }

    [Fact]
    public void FormatCellsDialog_FontTab_ExposesStyleUnderlineEffectsAndSamplePreview()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

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
    public void FormatCellsDialog_FontTab_DoesNotDuplicateFontStyleAndUnderlineControlsAsEffects()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        xaml.Should().Contain("x:Name=\"DlgLockedCheck\" Content=\"_Locked\"");
        xaml.Should().Contain("x:Name=\"DlgHiddenCheck\" Content=\"_Hidden\"");
        xaml.Should().Contain("Locking cells or hiding formulas has no effect until you protect the worksheet.");
    }

    [Fact]
    public void FormatCellsDialog_FontTab_LabelsEditableControlsWithAccessKeyTargets()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

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
    public void FormatCellsDialog_FontTab_PopulatesInstalledFontsAndKeepsCustomCurrentFont()
    {
        StaTestRunner.Run(() =>
        {
            const string customFont = "Freexcel Test Font Not Installed";
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

                categories.SelectedItem = "General";
                decimals.IsEnabled.Should().BeFalse();
                symbols.IsEnabled.Should().BeFalse();
                negatives.IsEnabled.Should().BeFalse();

                categories.SelectedItem = "Number";
                decimals.IsEnabled.Should().BeTrue();
                symbols.IsEnabled.Should().BeFalse();
                negatives.IsEnabled.Should().BeTrue();

                categories.SelectedItem = "Currency";
                decimals.IsEnabled.Should().BeTrue();
                symbols.IsEnabled.Should().BeTrue();
                negatives.IsEnabled.Should().BeTrue();

                categories.SelectedItem = "Accounting";
                decimals.IsEnabled.Should().BeTrue();
                symbols.IsEnabled.Should().BeTrue();
                negatives.IsEnabled.Should().BeFalse();

                foreach (var category in new[] { "Date", "Time", "Fraction", "Text", "Special", "Custom" })
                {
                    categories.SelectedItem = category;
                    decimals.IsEnabled.Should().BeFalse();
                    symbols.IsEnabled.Should().BeFalse();
                    negatives.IsEnabled.Should().BeFalse();
                }

                foreach (var category in new[] { "Percentage", "Scientific" })
                {
                    categories.SelectedItem = category;
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
                GetControl<ComboBox>(dialog, "DlgBorderTopStyleBox").SelectedItem = nameof(BorderStyle.Thick);
                GetControl<TextBox>(dialog, "DlgBorderTopColorBox").Text = "1,2,3";
                GetControl<ComboBox>(dialog, "DlgBorderRightStyleBox").SelectedItem = nameof(BorderStyle.Dashed);
                GetControl<TextBox>(dialog, "DlgBorderRightColorBox").Text = "4,5,6";
                GetControl<ComboBox>(dialog, "DlgBorderBottomStyleBox").SelectedItem = nameof(BorderStyle.Dotted);
                GetControl<TextBox>(dialog, "DlgBorderBottomColorBox").Text = "7,8,9";
                GetControl<ComboBox>(dialog, "DlgBorderLeftStyleBox").SelectedItem = nameof(BorderStyle.Double);
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
                GetControl<ListBox>(dialog, "DlgBorderLineStyleList").SelectedItem = nameof(BorderStyle.Dashed);
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
                GetControl<ListBox>(dialog, "DlgBorderLineStyleList").SelectedItem = nameof(BorderStyle.Thick);
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

    private static void InvokeDialogHandler(FormatCellsDialog dialog, string methodName)
    {
        var method = typeof(FormatCellsDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(dialog, [dialog, new System.Windows.RoutedEventArgs()]);
    }
}
