using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using FreeX.Core.Commands;
using FluentAssertions;
using FreeX.Core.Model;

using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Host.Tests;

public sealed class PageSetupDialogXamlTests
{
    [Fact]
    public void PageSetupDialog_ExposesKeyboardAccessKeysForTabsOptionsAndButtons()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("PageSetupDialog.xaml");

        foreach (var header in new[]
        {
            "_Page",
            "_Margins",
            "_Header/Footer",
            "_Sheet"
        })
            xaml.ShouldContainLocalizedAttribute("Header", header);

        foreach (var content in new[]
        {
            "_Orientation:",
            "_Paper size:",
            "First _page number:",
            "Print _quality:",
            "_Left:",
            "_Right:",
            "_Top:",
            "_Bottom:",
            "_Header:",
            "_Footer:",
            "_Header preset:",
            "_Footer preset:",
            "Custom _Header...",
            "Custom _Footer...",
            "_Different first page",
            "Different _odd and even pages",
            "_Scale with document",
            "_Align with page margins",
            "Print _area:",
            "_Rows to repeat at top:",
            "_Columns to repeat at left:",
            "_Center horizontally",
            "Center _vertically",
            "_Print gridlines",
            "Print row and column _headings",
            "Pa_ge order:",
            "_Black and white",
            "_Draft quality",
            "Cell _errors as:",
            "Co_mments:",
            "_OK",
            "_Cancel"
        })
            xaml.Should().Contain(content);
    }

    [Fact]
    public void PageSetupDialogOpenedFromKeyboard_FocusesOrientationBox()
    {
        var source = ReadPageSetupDialogSource();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("PageSetupDialogPlanner.PlanInitialFocus(");
        source.Should().Contain("FocusDialogTarget(plan);");
        source.Should().Contain("_ => OrientationBox");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void PrintTitlesCommand_OpensPageSetupSheetTabWithRowsRepeatFocus()
    {
        var source = ReadPageSetupDialogSource();
        var handlerSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PageLayout.cs");

        source.Should().Contain("PageSetupDialogPlanner.PlanInitialFocus(");
        source.Should().Contain("PageSetupDialogTab.Sheet => SheetTab");
        source.Should().Contain("PageSetupDialogFocusTarget.RepeatRows => RowsRepeatBox");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
        handlerSource.Should().Contain("PrintTitlesBtn_Click");
        handlerSource.Should().Contain("OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.PrintTitles);");
        handlerSource.Should().Contain("OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.DialogButton);");
        handlerSource.Should().Contain("PageSetupDialogPlanner.PlanOpen(source)");
        handlerSource.Should().Contain("openPlan.InitialFocusTarget) { Owner = this }");
    }

    [Fact]
    public void ScaleToFitCommand_OpensPageSetupPageTabWithActiveScalingInputFocus()
    {
        var source = ReadPageSetupDialogSource();
        var handlerSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PageLayout.cs");

        source.Should().Contain("PageSetupDialogPlanner.PlanInitialFocus(");
        source.Should().Contain("_ => PageTab");
        source.Should().Contain("PageSetupDialogPlanner.PlanInitialFocus(");
        source.Should().Contain("PageSetupScalingMode.FitToPages");
        source.Should().Contain("PageSetupDialogFocusTarget.ScalePercent => ScalePercentBox");
        source.Should().Contain("PageSetupDialogFocusTarget.FitPagesWide => FitPagesWideBox");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
        handlerSource.Should().Contain("ScaleToFitBtn_Click");
        handlerSource.Should().Contain("OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.ScaleToFit);");
    }

    [Fact]
    public void MarginsAndExtendedPaperSizeCommands_UseSharedPageSetupOpenPlan()
    {
        var source = ReadPageSetupDialogSource();
        var handlerSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PageLayout.cs");

        source.Should().Contain("PageSetupDialogPlanner.PlanOpen(_initialFocusTarget)");
        source.Should().Contain("PageSetupDialogPlanner.PlanInitialFocus(");
        source.Should().Contain("PageSetupDialogTab.Margins => MarginsTab");
        source.Should().Contain("PageSetupDialogFocusTarget.LeftMargin => LeftMarginBox");
        source.Should().Contain("PageSetupDialogFocusTarget.PaperSize => PaperSizeBox");
        handlerSource.Should().Contain("OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.CustomMargins);");
        handlerSource.Should().Contain("OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.ExtendedPaperSize);");
        handlerSource.Should().Contain("ShowPageSetupDialog(PageSetupDialogPlanner.PlanOpen(source));");
    }

    [Fact]
    public void PageTab_UsesExcelLikeScalingChoices()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("PageSetupDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        document.Descendants(presentation + "GroupBox")
            .Single(element => element.Attribute("Header")?.Value == "Scaling")
            .Descendants(presentation + "RadioButton")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_Adjust to:", "_Fit to:"]);

        foreach (var name in new[] { "ScalePercentBox", "FitPagesWideBox", "FitPagesTallBox" })
        {
            document.Descendants()
                .Any(element => element.Attribute(x + "Name")?.Value == name)
                .Should().BeTrue($"{name} should exist for Excel-style scaling input");
        }
    }

    [Fact]
    public void PageTab_DisablesInactiveScalingInputsByMode()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("PageSetupDialog.xaml");
        var source = ReadPageSetupDialogSource();

        xaml.Should().Contain("Checked=\"ScalingMode_Changed\"");
        source.Should().Contain("UpdateScalingInputState");
        source.Should().Contain("ScalePercentBox.IsEnabled = adjustTo");
        source.Should().Contain("FitPagesWideBox.IsEnabled = fitTo");
        source.Should().Contain("FitPagesTallBox.IsEnabled = fitTo");
    }

    [Fact]
    public void PageSetupDialog_DelegatesChoiceMappingAndValidationRoutesToSharedModel()
    {
        var source = ReadPageSetupDialogSource();

        source.Should().Contain("PopulateChoiceBox(OrientationBox, PageSetupDialogPlanner.OrientationChoices)");
        source.Should().Contain("PageSetupDialogPlanner.PlanSurface(_sourceSheet, Fields)");
        source.Should().Contain("surface.ChoiceIndexes.Orientation");
        source.Should().Contain("PageSetupDialogPlanner.BuildFields(Fields, new PageSetupDialogSurfaceInput");
        source.Should().Contain("PageSetupDialogPlanner.PlanValidationFocus(");
        source.Should().Contain("PageSetupDialogModel.HeaderPresetChoices");
        source.Should().Contain("PageSetupDialogModel.FooterPresetChoices");
        source.Should().Contain("PageSetupDialogModel.BuildHeaderFooterPreview(");
        source.Should().Contain("PageSetupDialogPlanner.ApplyHeaderPreset(");
        source.Should().Contain("PageSetupDialogPlanner.ResolveHeaderPresetIndex(");
        source.Should().NotContain("PageSetupDialogModel.ChoiceIndex(");
        source.Should().NotContain("PageSetupDialogModel.ChoiceValue(");
        source.Should().NotContain("PageSetupDialogModel.GetValidationRoute(");
        source.Should().NotContain("PageSetupDialogPlanner.OrientationChoices.ValueAt(OrientationBox.SelectedIndex)");
        source.Should().NotContain("((PageOrderBox.SelectedItem as ComboBoxItem)?.Tag as string)");
        source.Should().NotContain("WorksheetPrintErrorValue SelectedPrintErrorValue() =>\r\n        ((PrintErrorValueBox.SelectedItem as ComboBoxItem)");
    }

    [Fact]
    public void PageSetupDialog_XamlDefersSharedComboChoicesToPlanner()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("PageSetupDialog.xaml");
        var source = ReadPageSetupDialogSource();

        foreach (var comboBoxName in new[]
        {
            "OrientationBox",
            "PaperSizeBox",
            "PageOrderBox",
            "PrintErrorValueBox",
            "PrintCommentsBox"
        })
        {
            ComboItemTags(document, comboBoxName)
                .Should()
                .BeEmpty($"{comboBoxName} should be populated from PageSetupDialogPlanner instead of XAML literals");
        }

        source.Should().Contain("PopulateChoiceBox(PaperSizeBox, PageSetupDialogPlanner.PaperSizeChoices)");
        source.Should().Contain("PopulateChoiceBox(PageOrderBox, PageSetupDialogPlanner.PageOrderChoices)");
        source.Should().Contain("PopulateChoiceBox(PrintErrorValueBox, PageSetupDialogPlanner.PrintErrorValueChoices)");
        source.Should().Contain("PopulateChoiceBox(PrintCommentsBox, PageSetupDialogPlanner.PrintCommentChoices)");
    }

    [Fact]
    public void PageSetupDialog_RuntimeComboOrderMatchesSharedPlanner()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book1").AddSheet("Sheet1");
            var dialog = new PageSetupDialog(sheet);
            try
            {
                ComboItemTags((ComboBox)dialog.FindName("OrientationBox"))
                    .Should()
                    .Equal(PageSetupDialogPlanner.OrientationChoices.Choices.Select(choice => choice.Value.ToString()));
                ComboItemTags((ComboBox)dialog.FindName("PaperSizeBox"))
                    .Should()
                    .Equal(PageSetupDialogPlanner.PaperSizeChoices.Choices.Select(choice => choice.Value.ToString()));
                ComboItemTags((ComboBox)dialog.FindName("PageOrderBox"))
                    .Should()
                    .Equal(PageSetupDialogPlanner.PageOrderChoices.Choices.Select(choice => choice.Value.ToString()));
                ComboItemTags((ComboBox)dialog.FindName("PrintErrorValueBox"))
                    .Should()
                    .Equal(PageSetupDialogPlanner.PrintErrorValueChoices.Choices.Select(choice => choice.Value.ToString()));
                ComboItemTags((ComboBox)dialog.FindName("PrintCommentsBox"))
                    .Should()
                    .Equal(PageSetupDialogPlanner.PrintCommentChoices.Choices.Select(choice => choice.Value.ToString()));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void HeaderFooterTab_ReusesSupportedPresetAndCustomDialogConcepts()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("PageSetupDialog.xaml");
        var source = ReadPageSetupDialogSource();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var tab = document.Descendants(presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "_Header/Footer");

        foreach (var name in new[]
        {
            "HeaderPresetBox",
            "FooterPresetBox",
            "CustomHeaderButton",
            "CustomFooterButton",
            "DifferentFirstPageBox",
            "DifferentOddEvenBox",
            "ScaleWithDocumentBox",
            "AlignWithMarginsBox"
        })
        {
            tab.Descendants()
                .Any(element => element.Attribute(x + "Name")?.Value == name)
                .Should().BeTrue($"{name} should exist on the Page Setup Header/Footer tab");
        }

        tab.Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "HeaderPresetBox")
            .Elements(presentation + "ComboBoxItem")
            .Should()
            .BeEmpty("Page Setup header presets should be populated from the shared presentation catalog");
        tab.Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FooterPresetBox")
            .Elements(presentation + "ComboBoxItem")
            .Should()
            .BeEmpty("Page Setup footer presets should be populated from the shared presentation catalog");

        source.Should().Contain("PopulatePresetBox(HeaderPresetBox, PageSetupDialogModel.HeaderPresetChoices)");
        source.Should().Contain("PopulatePresetBox(FooterPresetBox, PageSetupDialogModel.FooterPresetChoices)");
        source.Should().Contain("PageSetupDialogPlanner.ResolveChoiceLabels(choices, UiText.Get)");
        source.Should().Contain("PageSetupDialogPlanner.ApplyHeaderPreset(Header, HeaderPresetBox.SelectedIndex)");
        source.Should().Contain("PageSetupDialogPlanner.ApplyFooterPreset(Footer, FooterPresetBox.SelectedIndex)");
        source.Should().Contain("PageSetupDialogPlanner.ResolveHeaderPresetIndex(Header)");
        source.Should().Contain("PageSetupDialogPlanner.ResolveFooterPresetIndex(Footer)");
        source.Should().NotContain("PageSetupDialogModel.HeaderFooterPresetValue(choices, comboBox.SelectedIndex)");
        source.Should().NotContain("PageSetupPresetComboItem");
    }

    [Fact]
    public void SheetTab_ExposesCurrentSelectionRangePickerButtonsForPrintRanges()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("PageSetupDialog.xaml");
        var source = ReadPageSetupDialogSource();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var (buttonName, targetName, automationName, automationId, helpText) in new[]
        {
            ("PrintAreaPickerButton", "PrintAreaBox", "Select print area", "PageSetupPrintAreaPickerButton", "Collapse the Page Setup dialog and select the print area from the worksheet."),
            ("RowsRepeatPickerButton", "RowsRepeatBox", "Select rows to repeat", "PageSetupRowsRepeatPickerButton", "Collapse the Page Setup dialog and select rows to repeat at the top of each printed page."),
            ("ColumnsRepeatPickerButton", "ColumnsRepeatBox", "Select columns to repeat", "PageSetupColumnsRepeatPickerButton", "Collapse the Page Setup dialog and select columns to repeat at the left of each printed page.")
        })
        {
            var button = document.Descendants(presentation + "Button")
                .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == buttonName);

            button.Should().NotBeNull($"{buttonName} should expose Excel-like picker affordance");
            button!.Attribute("Content")?.Value.Should().Be("...");
            button.Attribute("Click")?.Value.Should().Be("RangePickerButton_Click");
            button.Attribute("ToolTip")?.Value.Should().Contain("Collapse dialog");
            button.Attribute("Tag")?.Value.Should().Be(targetName);
            button.Attribute(x + "Name")?.Value.Should().Be(buttonName);
            button.Attribute("AutomationProperties.Name")?.Value.Should().Be(automationName);
            button.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be(automationId);
            button.Attribute("AutomationProperties.HelpText")?.Value.Should().Be(helpText);
        }

        source.Should().Contain("RangePickerButton_Click");
        source.Should().Contain("private readonly GridRange? _currentSelection");
        source.Should().Contain("PageSetupRangeSelectionRequest");
        source.Should().Contain("RangeSelectionRequest = CreateRangeSelectionRequest");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("target.Text = PageSetupRangeSelectionFormatter.Format(");
        source.Should().Contain("GetRangeSelectionTarget(targetName)");
        var pickerHandlerSource = source[
            source.IndexOf("private void RangePickerButton_Click", StringComparison.Ordinal)..
            source.IndexOf("public static PageSetupRangeSelectionRequest", StringComparison.Ordinal)];
        pickerHandlerSource.Should().Contain("DialogFocus.FocusAndSelect(target)");
    }

    [Fact]
    public void PageSetupRangeSelectionRequest_UsesExcelCollapseIntent()
    {
        PageSetupDialog.CreateRangeSelectionRequest(PageSetupRangeSelectionTarget.PrintArea, " A1:C10 ")
            .Should()
            .Be(new PageSetupRangeSelectionRequest(PageSetupRangeSelectionTarget.PrintArea, "A1:C10", CollapseDialog: true));
    }

    [Fact]
    public void PageSetupDialogApplyRangeSelection_UpdatesRequestedSheetTabBox()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book1").AddSheet("Sheet1");
            var dialog = new PageSetupDialog(sheet);
            try
            {
                dialog.ApplyRangeSelection(PageSetupRangeSelectionTarget.PrintArea, "B2:D8");
                dialog.ApplyRangeSelection(PageSetupRangeSelectionTarget.RepeatRows, "2:4");
                dialog.ApplyRangeSelection(PageSetupRangeSelectionTarget.RepeatColumns, "B:D");

                ((TextBox)dialog.FindName("PrintAreaBox")).Text.Should().Be("B2:D8");
                ((TextBox)dialog.FindName("RowsRepeatBox")).Text.Should().Be("2:4");
                ((TextBox)dialog.FindName("ColumnsRepeatBox")).Text.Should().Be("B:D");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void PageSetupHandler_WiresRangePickersToCurrentSelection()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.PageLayout.cs");

        source.Should().Contain("new PageSetupDialog(");
        source.Should().Contain("request => ApplyPageSetupRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyPageSetupRangeSelection(");
        source.Should().Contain("PageSetupRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("PageSetupRangeSelectionFormatter.Format(");
        source.Should().Contain("dialog.ApplyRangeSelection(request.Target, rangeText);");
    }

    [Theory]
    [InlineData(PageSetupRangeSelectionTarget.PrintArea, false, "$B$2:$D$8")]
    [InlineData(PageSetupRangeSelectionTarget.RepeatRows, false, "$2:$8")]
    [InlineData(PageSetupRangeSelectionTarget.RepeatColumns, false, "$B:$D")]
    [InlineData(PageSetupRangeSelectionTarget.RepeatRows, true, "R2:R8")]
    [InlineData(PageSetupRangeSelectionTarget.PrintArea, true, "R2C2:R8C4")]
    [InlineData(PageSetupRangeSelectionTarget.RepeatColumns, true, "C2:C4")]
    public void PageSetupRangeSelectionFormatter_FormatsPickerSelectionForTarget(
        PageSetupRangeSelectionTarget target,
        bool useR1C1ReferenceStyle,
        string expected)
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 4));

        PageSetupRangeSelectionFormatter.Format(target, range, useR1C1ReferenceStyle)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void PageSetupRangeSelectionFormatter_FormatsSingleCellPrintAreaWithoutRangeSeparator()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 4, 3);
        var range = new GridRange(address, address);

        PageSetupRangeSelectionFormatter.Format(
                PageSetupRangeSelectionTarget.PrintArea,
                range,
                useR1C1ReferenceStyle: false)
            .Should()
            .Be("$C$4");
    }

    [Fact]
    public void PageSetupDialogInvalidPrintArea_SelectsSheetTabPrintAreaBox()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("PageSetupDialog.xaml");
        var source = ReadPageSetupDialogSource();

        xaml.Should().Contain("x:Name=\"PageSetupTabs\"");
        xaml.Should().Contain("x:Name=\"SheetTab\"");
        source.Should().Contain("PageSetupDialogPlanner.PlanValidationFocus(");
        source.Should().Contain("PageSetupDialogFocusTarget.PrintArea => PrintAreaBox");
        source.Should().Contain("PageSetupDialogTab.Sheet => SheetTab");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void PageSetupDialogInvalidPrintTitles_SelectsSheetTabInvalidTitleBox()
    {
        var source = ReadPageSetupDialogSource();

        source.Should().Contain("PageSetupDialogPlanner.PlanValidationFocus(");
        source.Should().Contain("RepeatRowsText = RowsRepeatBox.Text");
        source.Should().Contain("PageSetupDialogFocusTarget.RepeatRows => RowsRepeatBox");
        source.Should().Contain("PageSetupDialogFocusTarget.RepeatColumns => ColumnsRepeatBox");
        source.Should().Contain("PageSetupDialogTab.Sheet => SheetTab");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void PageSetupDialogInvalidPageTabNumber_SelectsPageTabInvalidBox()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("PageSetupDialog.xaml");
        var source = ReadPageSetupDialogSource();

        xaml.Should().Contain("x:Name=\"PageTab\"");
        source.Should().Contain("PageSetupDialogFocusTarget.FirstPageNumber => FirstPageNumberBox");
        source.Should().Contain("PageSetupDialogFocusTarget.PrintQuality => PrintQualityBox");
        source.Should().Contain("private void FocusDialogTarget(PageSetupDialogFocusPlan plan)");
        source.Should().Contain("_ => PageTab");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void PageSetupDialogInvalidMargin_SelectsMarginsTabInvalidBox()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("PageSetupDialog.xaml");
        var source = ReadPageSetupDialogSource();

        xaml.Should().Contain("x:Name=\"MarginsTab\"");
        source.Should().Contain("HasSeparateMarginFields = true");
        source.Should().Contain("PageSetupDialogFocusTarget.LeftMargin => LeftMarginBox");
        source.Should().Contain("PageSetupDialogFocusTarget.RightMargin => RightMarginBox");
        source.Should().Contain("PageSetupDialogFocusTarget.HeaderMargin => HeaderMarginBox");
        source.Should().Contain("PageSetupDialogFocusTarget.FooterMargin => FooterMarginBox");
        source.Should().Contain("PageSetupDialogTab.Margins => MarginsTab");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void PageSetupDialogInvalidScaling_SelectsPageTabActiveScalingBox()
    {
        var source = ReadPageSetupDialogSource();

        source.Should().Contain("PageSetupDialogPlanner.PlanValidationFocus(");
        source.Should().Contain("_ => PageTab");
        source.Should().Contain("PageSetupDialogFocusTarget.ScalePercent => ScalePercentBox");
        source.Should().Contain("PageSetupDialogFocusTarget.FitPagesWide => FitPagesWideBox");
        source.Should().Contain("PageSetupDialogFocusTarget.FitPagesTall => FitPagesTallBox");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void Footer_ExposesExcelPrintActionsAndPrinterOptionsAction()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("PageSetupDialog.xaml");
        var source = ReadPageSetupDialogSource();
        var handlerSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PageLayout.cs");

        foreach (var content in new[] { "Print Pre_view", "_Print...", "_Options..." })
            xaml.ShouldContainLocalizedAttribute("Content", content);

        xaml.Should().Contain("Click=\"OptionsButton_Click\"");
        xaml.Should().NotContain("IsEnabled=\"False\"");
        xaml.Should().NotContain("not available yet");
        source.Should().Contain("PageSetupDialogAction.Options");
        source.Should().Contain("PageSetupDialogAction.PrintPreview");
        source.Should().Contain("PageSetupDialogAction.Print");
        handlerSource.Should().Contain("PageSetupDialogFollowUpAction.ShowPrinterOptions");
        handlerSource.Should().Contain("PageSetupDialogFollowUpAction.Print");
        handlerSource.Should().Contain("PageSetupDialogFollowUpAction.PrintPreview");
        handlerSource.Should().Contain("ShowPageSetupPrinterOptions()");
        handlerSource.Should().Contain("PrintButton_Click(this, new RoutedEventArgs())");
    }

    [Fact]
    public void FooterActionButtons_ExposeAutomationMetadata()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("PageSetupDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        foreach (var (content, name, automationId, helpText) in new[]
        {
            (UiText.Get("PageSetup_Print"), UiText.Get("PageSetup_PrintAutomationName"), "PageSetupPrintButton", UiText.Get("PageSetup_PrintHelpText")),
            (UiText.Get("PageSetup_PrintPreview"), UiText.Get("PageSetup_PrintPreviewAutomationName"), "PageSetupPrintPreviewButton", UiText.Get("PageSetup_PrintPreviewHelpText")),
            (UiText.Get("PageSetup_Options"), UiText.Get("PageSetup_OptionsAutomationName"), "PageSetupOptionsButton", UiText.Get("PageSetup_OptionsHelpText")),
            (UiText.Get("Common_Ok"), UiText.Get("PageSetup_OkAutomationName"), "PageSetupOkButton", UiText.Get("PageSetup_OkHelpText")),
            (UiText.Get("Common_Cancel"), UiText.Get("PageSetup_CancelAutomationName"), "PageSetupCancelButton", UiText.Get("PageSetup_CancelHelpText"))
        })
        {
            var button = document
                .Descendants(presentation + "Button")
                .Single(element => element.Attribute("Content")?.Value == content);

            button.Attribute("AutomationProperties.Name")?.Value.Should().Be(name);
            button.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be(automationId);
            button.Attribute("AutomationProperties.HelpText")?.Value.Should().Be(helpText);
        }
    }

    [Fact]
    public void PageSetupHandler_AppliesHeaderFooterValuesReturnedByDialog()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.PageLayout.cs",
            "PageSetupDialog.xaml.cs");

        source.Should().Contain("CreatePageLayoutCommandSession().TryPlanPageSetup(");
        source.Should().Contain("dialog.Fields");
        source.Should().NotContain("PageSetupSubmissionPlanner.TryBuild(sheet, fields, dialog.RequestedAction)");
        source.Should().Contain("new PageSetupDialog(");
        source.Should().Contain("SheetGrid.SelectedRange");
        source.Should().Contain("Header = Header");
        source.Should().Contain("FirstPageHeader = FirstPageHeader");
        source.Should().Contain("EvenPageFooter = EvenPageFooter");
        source.Should().Contain("HeaderPictures = HeaderPictures.DeepClone()");
        source.Should().Contain("ScaleHeaderFooterWithDocument = ScaleWithDocumentBox.IsChecked == true");
        source.Should().Contain("AlignHeaderFooterWithMargins = AlignWithMarginsBox.IsChecked == true");
    }

    [Fact]
    public void PageSetupHandler_AppliesPrintAreaReturnedByDialog()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.PageLayout.cs",
            "PageSetupDialog.xaml.cs");

        source.Should().Contain("dialog.Fields");
        source.Should().Contain("PrintAreaText = PrintAreaBox.Text");
        source.Should().Contain("CreatePageLayoutCommandSession().TryPlanPageSetup(");
    }

    [Fact]
    public void PageSetupDialogCommand_AppliesCenterOnPageAndPageOrderSelections()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var dialog = new PageSetupDialog(sheet);
            try
            {
                ((CheckBox)dialog.FindName("CenterHorizontallyBox")).IsChecked = true;
                ((CheckBox)dialog.FindName("CenterVerticallyBox")).IsChecked = true;
                SelectComboItemByTag((ComboBox)dialog.FindName("PageOrderBox"), "OverThenDown");

                InvokePrivateAllowingNonModalDialogResult(dialog, "OkButton_Click");
                var build = PageSetupSubmissionPlanner.TryBuild(sheet, dialog.Fields);
                build.Success.Should().BeTrue(build.Validation?.Message.FallbackText);
                var outcome = build.Submission!.CommandPlan.ToComposite().Apply(new TestCommandContext(workbook));

                outcome.Success.Should().BeTrue(outcome.ErrorMessage);
                sheet.CenterHorizontallyOnPage.Should().BeTrue();
                sheet.CenterVerticallyOnPage.Should().BeTrue();
                sheet.PageOrder.Should().Be(WorksheetPageOrder.OverThenDown);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void UiTestCatalog_PageSetupRowNoLongerListsCenterAndPageOrderProofAsRemaining()
    {
        var catalog = WorkspaceFileLocator.ReadAllLines("docs", "testing/ui-test-catalog.md");
        var pageSetupRow = catalog.Single(line => line.StartsWith("| UI-CMD-PAGE-003 |", StringComparison.Ordinal));

        pageSetupRow.Should().Contain("Center on Page and Page Order dialog choices flow through the command builder into the worksheet model");
        pageSetupRow.Should().NotContain("Remaining work is Center on Page, Page Order");
    }

    private static string ReadPageSetupDialogSource() =>
        DialogSourceTestSupport.ReadHostSources(
            "PageSetupDialog.xaml.cs",
            "PageSetupDialog.HeaderFooter.cs",
            "PageSetupDialog.Population.cs",
            "PageSetupDialog.RangeSelection.cs",
            "PageSetupDialog.ValidationFocus.cs");

    private static IReadOnlyList<string> ComboItemTags(XDocument document, string comboBoxName)
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        return document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == comboBoxName)
            .Elements(presentation + "ComboBoxItem")
            .Select(element => element.Attribute("Tag")?.Value ?? "")
            .ToList();
    }

    private static IReadOnlyList<string> ComboItemTags(ComboBox comboBox) =>
        comboBox.Items
            .OfType<ComboBoxItem>()
            .Select(item => item.Tag as string ?? "")
            .ToList();

    private static void SelectComboItemByTag(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .Single(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
    }

    private static void InvokePrivateAllowingNonModalDialogResult(PageSetupDialog dialog, string methodName)
        => DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, methodName);

}
