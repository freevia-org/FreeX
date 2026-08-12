using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class DialogVisualParitySourceTests
{
    [Fact]
    public void FindReplaceDialog_UsesWpfColumnAndResultSurfaceMetrics()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog);");
        source.Should().Contain("var findBox = new TextBox { Text = _session.LastFindText, MinWidth = FindReplaceDialogPlanner.FieldMinWidth };");
        source.Should().Contain("findFormatButton.Margin = new Thickness(FindReplaceDialogPlanner.FormatButtonMargin, 0, 0, 0);");
        source.Should().Contain("findChooseFormatButton.Margin = new Thickness(FindReplaceDialogPlanner.AdjacentFormatButtonMargin, 0, 0, 0);");
        source.Should().Contain("Height = replaceMode ? FindReplaceDialogPlanner.ReplaceTabHeight : FindReplaceDialogPlanner.FindTabHeight,");
        source.Should().Contain("tabs.Height = tabHeight;");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions($\"{FindReplaceDialogPlanner.FieldLabelColumnWidth},*,Auto,Auto,Auto\")");
        source.Should().Contain("new Thickness(6, 1)");
        source.Should().Contain("BorderBrush = Brush(68, 114, 196)");
        source.Should().Contain("Background = Brush(242, 242, 242)");
        source.Should().Contain("optionsHeader");
        source.Should().Contain("resultsHeader.Height = FindReplaceDialogPlanner.ResultsHeaderHeight;");
        source.Should().Contain("FindReplaceDialogPlanner.FieldMinWidth");
        source.Should().Contain("FindReplaceDialogPlanner.ResultBookColumnWidth");
        source.Should().Contain("FindReplaceDialogPlanner.ActionButtonSpacing");
        source.Should().Contain("FindReplaceDialogPlanner.AvaloniaRootRightMargin");
        source.Should().Contain("button.CornerRadius = new CornerRadius(0);");
        source.Should().Contain("textBox.CornerRadius = new CornerRadius(0);");
        source.Should().Contain("optionsHeader.Width = FindReplaceDialogPlanner.OptionsHeaderMinimumWidth;");
        source.Should().Contain("optionsHeader.HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left;");
        source.Should().Contain("optionsHeader.Background = Brushes.White;");
        source.Should().Contain("? Fr(FindReplaceDialogText.OptionsExpanded)");
        source.Should().Contain(": Fr(FindReplaceDialogText.Options)");
        source.Should().Contain("AutomationProperties.SetName(optionsHeader, optionsHeaderText.Text);");
        source.Should().Contain("dialog.Opened += (_, _) => resultsList.Background = Brush(242, 242, 242);");
    }

    [Fact]
    public void GoToSpecialDialog_UsesCompactRowsAndBottomDockedActions()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("numbersBox.Opacity = enabled ? 1 : 0.7;");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCompactRadioButton(button, AvaloniaCompactDialogChrome.WindowsStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCompactCheckBox(numbersBox, AvaloniaCompactDialogChrome.WindowsStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyGroupBox(availableGroup, borderBrush: Brush(213, 223, 229));");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyGroupBox(valueTypeGroup, borderBrush: Brush(213, 223, 229));");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaChoiceGroupTopMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaChoiceGroupBottomMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaChoiceGroupHorizontalPadding");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaChoiceGroupBottomPadding");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaValueTypeGroupBottomPadding");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaValueTypeSpacing");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaChoiceButtonRightMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaChoiceButtonBottomMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaContentLeftMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaContentTopMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaContentRightMargin");
        source.Should().Contain("Margin = new Thickness(0, 0, 0, 7),");
        source.Should().Contain("GoToSpecialDialogPlanner.ActionRowTopMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.ActionRowRightMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.ActionRowBottomMargin");
        source.Should().Contain("ApplyGoToSpecialButtonSize(okButton);");
        source.Should().Contain("ApplyGoToSpecialButtonSize(cancelButton);");
        source.Should().Contain("var root = new DockPanel { Margin = new Thickness(0) };");
        source.Should().Contain("DockPanel.SetDock(buttonRow, Dock.Bottom);");

        var wpfSource = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "GoToSpecialDialog.cs"));
        wpfSource.Should().Contain("GoToSpecialDialogPlanner.ContentMargin");

        var chrome = File.ReadAllText(RepoFile("shared", "Free.Shared.Shell.Avalonia", "AvaloniaCompactDialogChrome.cs"));
        chrome.Should().Contain("IBrush? borderBrush = null");
        chrome.Should().Contain("Color.FromRgb(198, 215, 232)");
        chrome.Should().Contain("groupBox.BorderBrush = borderBrush ?? GroupBoxBorderBrush;");
    }

    [Fact]
    public void GoToDialog_UsesSharedChromeAndWpfHistoryGridMetrics()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog, dialogChrome);");
        source.Should().Contain("ButtonHeight = 24,");
        source.Should().Contain("MinHeight = 130,");
        source.Should().Contain("Margin = new Thickness(0, 24, 0, 0),");
        source.Should().Contain("new GridLength(1, GridUnitType.Star)");
        source.Should().Contain("Grid.SetRow(historyList, 1);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(specialButton, dialogChrome, minWidth: 86);");
        source.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow(");
        source.Should().NotContain("var historyBorder = new Border");
        source.Should().Contain("AutomationProperties.SetAutomationId(historyList, \"GoToHistoryList\");");
        source.Should().Contain("AvaloniaCompactDialogChrome.FocusAndSelect(inputBox);");
    }

    [Fact]
    public void ConditionalFormatNewRule_UsesCurrentWpfDescriptionChrome_WithoutChangingRuleBehavior()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ConditionalFormat.cs"));

        source.Should().Contain("ConditionalFormatDialog_FormatOnlyCellsWithLabel");
        source.Should().NotContain("ConditionalFormat_AppliesToFormat");
        source.Should().Contain("ApplyCfButtonChrome(formatButton, 84);");
        source.Should().Contain("Children = { highlightBox, formatButton },");
        source.Should().Contain("control.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;");
        source.Should().Contain("Width = 218,");
        source.Should().Contain("Margin = new Thickness(16, 16, 29, 29),");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions(\"244,*\"),");
        source.Should().Contain("Margin = new Thickness(0, 12, 0, 0),");
        source.Should().Contain("Spacing = 8,");
        source.Should().Contain("formatButton.Height = 21;");
        source.Should().Contain("okButton.Height = 21;");
        source.Should().Contain("cancelButton.Height = 21;");
        source.Should().Contain("AutomationProperties.SetAutomationId(formatButton, \"ConditionalFormatFormatButton\")");
        source.Should().Contain("ConditionalFormatRuleSchema.ForRuleType(ruleType)");
        source.Should().Contain("ConditionalFormatRuleBuilder.TryBuildApplyCommand(");
        source.Should().Contain("ConfigureDialogTabCycle(dialog, root);");
    }

    [Fact]
    public void SortOptionsDialog_UsesSharedLocalizationAndReferenceCaptureState()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var captureSource = File.ReadAllText(RepoFile("tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ParityCapture.cs"));

        source.Should().Contain("SortOptionsDialogCatalog.Create(UiText.Get)");
        source.Should().Contain("Title = presentation.Title");
        source.Should().Contain("Content = presentation.CaseSensitive");
        source.Should().Contain("ItemsSource = presentation.FirstKeySortOrders");
        source.Should().Contain("Content = presentation.SortTopToBottom");
        source.Should().Contain("Content = presentation.SortLeftToRight");
        source.Should().Contain("Header = StripDisplayMnemonic(presentation.Orientation)");
        source.Should().Contain("SortOptionsPolicy.CreateResult(");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"SortOptionsDialog\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(firstKeyBox, \"SortOptionsFirstKeySortOrderBox\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(leftToRightButton, \"SortOptionsLeftToRightRadio\")");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog)");
        source.Should().Contain("StripContentMnemonic(caseSensitiveBox)");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCompactCheckBox(caseSensitiveBox");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCompactRadioButton(topToBottomButton");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(firstKeyBox");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyGroupBox(");
        source.Should().Contain("LastChildFill = false");
        source.Should().Contain("DockPanel.SetDock(buttons, Dock.Bottom)");
        source.Should().Contain("okButton.Height = okButton.MinHeight = okButton.MaxHeight = 52");
        source.Should().Contain("cancelButton.Height = cancelButton.MinHeight = cancelButton.MaxHeight = 52");
        source.Should().NotContain("SortOptionsCheckBoxTemplate");
        source.Should().NotContain("SortOptionsRadioButtonTemplate");

        captureSource.Should().Contain("CaseSensitive: true");
        captureSource.Should().Contain("LeftToRight: true");
        captureSource.Should().Contain("FirstKeySortOrder: \"Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec\"");
    }

    [Fact]
    public void InsertHyperlinkDialog_UsesInactiveWpfSelectionForFocusedAddressEditor()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyListBox(");
        source.Should().Contain("ListBoxItemPadding = new Thickness(4, 0)");
        source.Should().Contain("var inactiveLinkTypeSelection = Brush(246, 246, 246);");
        source.Should().Contain("selector.Class(\":selected\").Class(\":focus\")");
        source.Should().Contain("selector.Class(\":selected\").Class(\":pointerover\")");
        source.Should().Contain("selector.Class(\":selected\").Class(\":focus\").Class(\":pointerover\")");
        source.Should().Contain("new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent)");
        source.Should().Contain("targetBox.Focus();");
        source.Should().Contain("targetBox.SelectAll();");
    }

    [Fact]
    public void InsertHyperlinkParityCapture_UsesFixtureWithoutChangingProductionPrefill()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var captureSource = File.ReadAllText(RepoFile("tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ParityCapture.cs"));

        source.Should().Contain("var prefill = _session.GetSelectedRangeHyperlinkDialogPrefill();");
        captureSource.Should().Contain("HyperlinkDialogParityFixture.Seed(_session.ActiveSheet, address);");
        captureSource.Should().Contain("await ShowInsertHyperlinkInputDialogAsync();");
    }

    [Fact]
    public void SubtotalDialog_UsesSharedFixtureStateAndLocalizedAccessKeyControls()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var captureSource = File.ReadAllText(RepoFile("tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ParityCapture.cs"));

        source.Should().Contain("initialPlan?.SummaryBelowData ?? _session.ActiveSheet.OutlineSummaryBelow ?? true");
        source.Should().NotContain("SubtotalDialogCaptureState");
        source.Should().Contain("CreateSubtotalAccessText(label)");
        source.Should().Contain("Content = UiText.Get(\"Subtotal_ReplaceCurrentSubtotals\")");
        source.Should().Contain("Content = UiText.Get(\"Subtotal_PageBreakBetweenGroups\")");
        source.Should().Contain("Content = UiText.Get(\"Subtotal_SummaryBelowData\")");
        source.Should().Contain("IsDefault = true");
        source.Should().Contain("IsCancel = true");
        source.Should().Contain("supportsRecycling: false");
        source.Should().NotContain("Text = $\"Range: {FormatRangeReference(range)}\"");

        captureSource.Should().Contain("var fixture = SubtotalParityFixture.CreateState(_session.ActiveSheet);");
        captureSource.Should().Contain("SubtotalParityFixture.ApplySheetState(_session.ActiveSheet);");
        captureSource.Should().Contain("fixture.CreatePlan()");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
