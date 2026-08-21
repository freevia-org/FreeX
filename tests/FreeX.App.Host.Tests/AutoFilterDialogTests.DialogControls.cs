using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDialogTests
{
    [Fact]
    public void DialogLayout_PinsActionButtonsBelowScrollableDropdownContent()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AutoFilterDialog.cs");

        source.Should().Contain("SizeToContent = SizeToContent.Height");
        source.Should().Contain("var root = new DockPanel { Margin = new Thickness(10), LastChildFill = true };");
        source.Should().Contain("var scrollViewer = new ScrollViewer");
        source.Should().Contain("VerticalScrollBarVisibility = ScrollBarVisibility.Auto");
        source.Should().Contain("DockPanel.SetDock(buttons, Dock.Bottom)");
        source.Should().Contain("root.Children.Add(buttons)");
        source.Should().Contain("scrollViewer.Content = stack");
        source.IndexOf("root.Children.Add(buttons)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(source.IndexOf("root.Children.Add(scrollViewer)", StringComparison.Ordinal));
        source.Should().NotContain("Height = 540");
    }

    [Fact]
    public void DialogLayout_ActionButtonsKeepNaturalHeightAtRuntime()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AutoFilterDialog(
            [
                new AutoFilterDialogItem("Apple", "Apple", true),
                new AutoFilterDialogItem("Banana", "Banana", true)
            ]);
            dialog.Show();
            try
            {
                dialog.UpdateLayout();
                var buttons = WpfTestTree.FindVisualDescendants<Button>(dialog).ToList();
                var ok = buttons.Single(button => button.IsDefault);
                var cancel = buttons.Single(button => button.IsCancel);

                ok.ActualHeight.Should().BeLessThan(40);
                cancel.ActualHeight.Should().BeLessThan(40);
                dialog.ActualWidth.Should().BeLessThan(340);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogSearch_NarrowsChecklistWithoutDroppingHiddenSelections()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("_searchBox.TextChanged");
        source.Should().Contain("FilterItems(_allItems, _searchBox.Text)");
        source.Should().Contain("UpdateSelectAllBoxState()");
        source.Should().Contain("_allItems");
        source.Should().Contain("_addCurrentSelectionToFilterBox.IsChecked == true");
        source.Should().Contain("GetResultItemsForSearchMode");
    }

    [Fact]
    public void DialogControls_ExposeExcelStyleKeyboardAccessKeys()
    {
        var source = ReadAutoFilterDialogSources();

        foreach (var key in new[]
        {
            "AutoFilter_SortAToZ",
            "AutoFilter_SortZToA",
            "AutoFilter_ClearFilterFrom2",
            "AutoFilter_TextFilters",
            "AutoFilter_NumberFilters",
            "AutoFilter_DateFilters",
            "AutoFilter_SelectAll",
            "AutoFilter_AddCurrentSelectionToFilter"
        })
            source.Should().Contain($"UiText.Get(\"{key}\")");

        source.Should().Contain("Content = UiText.Ok");
        source.Should().Contain("Content = UiText.Cancel");
        source.Should().Contain("UiText.Get(\"AutoFilter_CriteriaText\")");
        source.Should().Contain("AutomationProperties.SetName(_searchBox, UiText.Get(\"AutoFilter_Search3\"));");
        source.Should().Contain("AutomationProperties.SetAccessKey(_searchBox, \"S\");");
        source.Should().NotContain("Content = UiText.Get(\"AutoFilter_Search2\")");
        source.Should().NotContain("RadioButton _sortNone");
    }

    [Fact]
    public void DialogControls_FilterValueChecklistExposesAutomationName()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("AutomationProperties.SetName(_checklistBox, UiText.Get(\"AutoFilter_FilterValues\"));");
    }

    [Fact]
    public void DialogControls_MenuCommandRowsUseSharedIconColumn()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("CreateMenuCommandButton(UiText.Get(\"AutoFilter_SortAToZ\"), RibbonCommandIconKind.SortAscending)");
        source.Should().Contain("CreateMenuCommandButton(UiText.Get(\"AutoFilter_SortZToA\"), RibbonCommandIconKind.SortDescending)");
        source.Should().Contain("CreateMenuCommandButton(UiText.Get(\"AutoFilter_ClearFilterFrom2\"), RibbonCommandIconKind.Clear)");
        source.Should().Contain("SetMenuCommandButtonContent(_textFiltersButton, FormatCascadeMenuHeader(UiText.Get(\"AutoFilter_TextFilters\")), RibbonCommandIconKind.Filter)");
        source.Should().Contain("RibbonIconFactory.CreateIcon(new RibbonCommandIcon(iconKind), 14, Brushes.Black)");
    }

    [Fact]
    public void DialogControls_SearchBoxHasExcelStyleWatermarkWithoutStandaloneLabel()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AutoFilterDialog(
            [
                new AutoFilterDialogItem("Apple", "Apple", true),
                new AutoFilterDialogItem("Banana", "Banana", true)
            ]);
            dialog.Show();
            try
            {
                dialog.UpdateLayout();
                var searchBox = WpfTestTree.FindVisualDescendants<TextBox>(dialog)
                    .Single(textBox => AutomationProperties.GetName(textBox) == "Search");
                var labels = WpfTestTree.FindVisualDescendants<Label>(dialog)
                    .Select(label => label.Content?.ToString() ?? string.Empty)
                    .ToList();

                AutomationProperties.GetAccessKey(searchBox).Should().Be("S");
                labels.Should().NotContain("_Search");

                var watermark = WpfTestTree.FindVisualDescendants<TextBlock>(dialog)
                    .Single(textBlock => textBlock.Text == "Search");
                watermark.Visibility.Should().Be(Visibility.Visible);

                searchBox.Text = "Apple";
                watermark.Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesFirstSortCommand()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_sortAscendingButton.Focus();");
        source.Should().Contain("Keyboard.Focus(_sortAscendingButton);");
    }

    [Fact]
    public void DialogControls_UseExcelStyleCommandSortAndSelectAllCheckbox()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("private readonly Button _sortAscendingButton");
        source.Should().Contain("private readonly Button _sortDescendingButton");
        source.Should().Contain("ApplySortCommand(AutoFilterSortDirection.Ascending)");
        source.Should().Contain("ApplySortCommand(AutoFilterSortDirection.Descending)");
        source.Should().Contain("private readonly CheckBox _selectAllBox");
        source.Should().Contain("Content = UiText.Get(\"AutoFilter_SelectAll\")");
        source.Should().Contain("IsThreeState = true");
        source.Should().Contain("AutoFilterDialogCriteriaPlanner.SetSelectionForSearch");
        source.Should().Contain("UpdateSelectAllBoxState");
        source.Should().Contain("ApplySearchTextChange");
        source.Should().Contain("AutoFilterMenuPlanner.PlanChecklistState(_allItems, _searchBox.Text)");
        source.Should().Contain("_addCurrentSelectionToFilterBox.Visibility = state.IsAddCurrentSelectionVisible");
        source.Should().NotContain("private readonly RadioButton _sortNone");
        source.Should().NotContain("var selectionRow = new StackPanel");
        source.Should().NotContain("UiText.Get(\"AutoFilter_ClearAll\")");
    }

    [Fact]
    public void DialogControls_ClearFilterButtonHonorsMenuPlanEnabledState()
    {
        StaTestRunner.Run(() =>
        {
            var menuPlan = new AutoFilterMenuPlan(
                "Status",
                AutoFilterMenuFilterKind.Text,
                [
                    new AutoFilterMenuEntry("Sort A to Z", AutoFilterMenuEntryKind.SortAscending),
                    new AutoFilterMenuEntry(
                        UiText.Format("AutoFilter_ClearFilterFrom", "Status"),
                        AutoFilterMenuEntryKind.ClearFilter,
                        isEnabled: false),
                    new AutoFilterMenuEntry(new AutoFilterChecklistItem("Open", "Open"))
                ]);
            var dialog = new AutoFilterDialog(menuPlan);
            dialog.Show();
            try
            {
                var clearButton = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Single(button => WpfTestTree.FindVisualDescendants<TextBlock>(button)
                        .Any(text => text.Text.Contains("Clear Filter From", StringComparison.Ordinal)));

                clearButton.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogControls_ConsumeSharedMenuPlanRowsWithoutOwningPopupPolicy()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("public AutoFilterDialog(AutoFilterMenuPlan menuPlan)");
        source.Should().Contain("FindClearFilterEntry(menuPlan)");
        source.Should().Contain("SetSortLabels(menuPlan)");
        source.Should().Contain("ShowFilterFamilyButton(menuPlan.FilterKind)");
        source.Should().Contain("AutoFilterDialogCriteriaPlanner.GetCriteriaSuggestions(menuPlan)");
        source.Should().Contain("AutoFilterMenuPlanner.CreateCriteriaOptions(menuPlan.FilterKind");
        source.Should().Contain("ConfigureFilterFamilySubmenu(menuPlan)");
        source.Should().Contain("PopulateColorChoices(colorOptions)");
        source.Should().NotContain("AutoFilterMenuEntryPresentation.ForKind");
    }

    [Fact]
    public void DialogControls_MenuPlanChecklistStateSeedsDialogSelections()
    {
        StaTestRunner.Run(() =>
        {
            var menuPlan = new AutoFilterMenuPlan(
                "Status",
                AutoFilterMenuFilterKind.Text,
                [
                    new AutoFilterMenuEntry("Sort A to Z", AutoFilterMenuEntryKind.SortAscending),
                    new AutoFilterMenuEntry("Select All", AutoFilterMenuEntryKind.SelectAll, isChecked: null),
                    new AutoFilterMenuEntry(new AutoFilterChecklistItem("Open", "Open")),
                    new AutoFilterMenuEntry(new AutoFilterChecklistItem("Closed", "Closed", IsChecked: false))
                ]);
            var dialog = new AutoFilterDialog(menuPlan);
            dialog.Show();
            try
            {
                dialog.UpdateLayout();
                var listBox = WpfTestTree.FindVisualDescendants<ListBox>(dialog).Single();
                var items = listBox.Items
                    .Cast<AutoFilterDialogItem>()
                    .ToDictionary(item => item.Value, item => item.IsSelected);
                var selectAll = WpfTestTree.FindVisualDescendants<CheckBox>(dialog)
                    .Single(checkBox => checkBox.Content?.ToString() == UiText.Get("AutoFilter_SelectAll"));

                items.Should().BeEquivalentTo(new Dictionary<string, bool>
                {
                    ["Open"] = true,
                    ["Closed"] = false
                });
                selectAll.IsChecked.Should().BeNull();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogControls_SearchWithNoMatchesDisablesSelectAllAndChecklist()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AutoFilterDialog(
            [
                new AutoFilterDialogItem("Apple", "Apple", true),
                new AutoFilterDialogItem("Banana", "Banana", true)
            ]);
            dialog.Show();
            try
            {
                dialog.UpdateLayout();
                var searchBox = WpfTestTree.FindVisualDescendants<TextBox>(dialog)
                    .Single(textBox => AutomationProperties.GetName(textBox) == "Search");
                var selectAll = WpfTestTree.FindVisualDescendants<CheckBox>(dialog)
                    .Single(checkBox => checkBox.Content?.ToString() == UiText.Get("AutoFilter_SelectAll"));
                var addCurrentSelection = WpfTestTree.FindVisualDescendants<CheckBox>(dialog)
                    .Single(checkBox => checkBox.Content?.ToString() == UiText.Get("AutoFilter_AddCurrentSelectionToFilter"));
                var checklist = WpfTestTree.FindVisualDescendants<ListBox>(dialog).Single();

                addCurrentSelection.Visibility.Should().Be(System.Windows.Visibility.Collapsed);

                searchBox.Text = "zzz";
                dialog.UpdateLayout();

                selectAll.IsEnabled.Should().BeFalse();
                checklist.IsEnabled.Should().BeFalse();
                addCurrentSelection.Visibility.Should().Be(System.Windows.Visibility.Visible);
                addCurrentSelection.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogControls_ExposeFilterByColorPickerWhenMenuPlanSupportsIt()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("_filterByColorGroup");
        source.Should().Contain("Header = UiText.Get(\"AutoFilter_FilterByColor2\")");
        source.Should().Contain("colorOptions.Count > 0 && AutoFilterDialogCriteriaPlanner.HasFilterByColorEntry(menuPlan)");
        source.Should().Contain("PopulateColorChoices");
        source.Should().Contain("UiText.Get(\"AutoFilter_CellColor\")");
        source.Should().Contain("UiText.Get(\"AutoFilter_FontColor\")");
        source.Should().Contain("CreateColorSwatch");
        source.Should().NotContain("new ColorPickerDialog(_selectedColorFilter, allowNoColor: true)");
        source.Should().Contain("HasFilterByColorEntry");
    }

    [Fact]
    public void DialogControls_ColorSwatchActivationAppliesFilterAndClosesDialog()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("private readonly List<Button> _colorChoiceButtons");
        source.Should().Contain("_colorChoiceButtons.Clear();");
        source.Should().Contain("_colorChoiceButtons.Add(button);");
        source.Should().Contain("KeyboardNavigation.SetDirectionalNavigation(swatches, KeyboardNavigationMode.Contained);");
        source.Should().Contain("button.PreviewKeyDown += ColorChoiceButton_PreviewKeyDown;");
        source.Should().Contain("private void ColorChoiceButton_PreviewKeyDown(object sender, KeyEventArgs e)");
        source.Should().Contain("Key.Left or Key.Up => currentIndex - 1");
        source.Should().Contain("Key.Right or Key.Down => currentIndex + 1");
        source.Should().Contain("Key.Home => 0");
        source.Should().Contain("Key.End => _colorChoiceButtons.Count - 1");
        source.Should().Contain("private void FocusColorChoiceButton(int index)");
        source.Should().Contain("Keyboard.Focus(button);");
        source.Should().Contain("button.Click += (_, _) => ApplyColorChoice(colorFilter);");
        source.Should().Contain("private void ApplyColorChoice(AutoFilterColorFilter colorFilter)");
        source.Should().Contain("CommitResult(AutoFilterDialogCriteriaPlanner.BuildResult(");
        source.Should().Contain("colorFilter,");
        source.Should().Contain("DialogResult = true;");
        source.Should().NotContain("button.Click += (_, _) => _selectedColorFilter = colorFilter;");
    }

    [Fact]
    public void DialogControls_ColorChoiceButtonsExposeUiaMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var menuPlan = new AutoFilterMenuPlan(
                "Status",
                AutoFilterMenuFilterKind.Text,
                [
                    new AutoFilterMenuEntry(UiText.Get("AutoFilter_FilterByColor"), AutoFilterMenuEntryKind.FilterByColor),
                    new AutoFilterMenuEntry(new AutoFilterChecklistItem("Open", "Open"))
                ],
                [
                    new AutoFilterColorOption("#00B050", AutoFilterColorFilterKind.CellFillColor, new CellColor(0, 176, 80)),
                    new AutoFilterColorOption("No Fill", AutoFilterColorFilterKind.NoFill, null),
                    new AutoFilterColorOption("#C00000", AutoFilterColorFilterKind.FontColor, new CellColor(192, 0, 0))
                ]);
            var dialog = new AutoFilterDialog(menuPlan);
            dialog.Show();
            try
            {
                var colorButtons = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Where(button => AutomationProperties.GetName(button).StartsWith("Filter by ", StringComparison.Ordinal))
                    .ToDictionary(AutomationProperties.GetName);

                colorButtons.Keys.Should().BeEquivalentTo(
                    "Filter by cell color #00B050",
                    "Filter by no fill",
                    "Filter by font color #C00000");
                colorButtons.Values.Should().OnlyContain(button =>
                    AutomationProperties.GetHelpText(button) == "Apply this color filter.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogControls_ChecklistSupportsKeyboardToggleAndBoundaryNavigation()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("private readonly ListBox _checklistBox = new();");
        source.Should().Contain("AutomationProperties.SetName(_checklistBox, UiText.Get(\"AutoFilter_FilterValues\"));");
        source.Should().Contain("_checklistBox.PreviewKeyDown += ChecklistBox_PreviewKeyDown;");
        source.Should().Contain("private void ChecklistBox_PreviewKeyDown(object sender, KeyEventArgs e)");
        source.Should().Contain("Key.Space => ToggleFocusedChecklistItem()");
        source.Should().Contain("Key.Home => FocusChecklistItem(0)");
        source.Should().Contain("Key.End => FocusChecklistItem(_items.Count - 1)");
        source.Should().Contain("private bool ToggleFocusedChecklistItem()");
        source.Should().Contain("item.IsSelected = !item.IsSelected;");
        source.Should().Contain("_checklistBox.Items.Refresh();");
        source.Should().Contain("private bool FocusChecklistItem(int index)");
        source.Should().Contain("_checklistBox.ScrollIntoView(item);");
    }

    [Fact]
    public void DialogControls_UseTypedCriteriaControlsInsteadOfFocusOnlyFilterButtons()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("_criteriaOperatorBox");
        source.Should().Contain("_criteriaValueBox");
        source.Should().Contain("_betweenCriteriaPanel");
        source.Should().Contain("_betweenMinBox");
        source.Should().Contain("_betweenMaxBox");
        source.Should().Contain("_topBottomCriteriaPanel");
        source.Should().Contain("_topBottomCountBox");
        source.Should().Contain("_datePresetBox");
        source.Should().Contain("UiText.Get(\"AutoFilter_DatePresetThisWeek\")");
        source.Should().Contain("UiText.Get(\"AutoFilter_DatePresetLastWeek\")");
        source.Should().Contain("UiText.Get(\"AutoFilter_DatePresetNextWeek\")");
        source.Should().Contain("UiText.Get(\"AutoFilter_DatePresetThisYear\")");
        source.Should().Contain("UiText.Get(\"AutoFilter_DatePresetLastYear\")");
        source.Should().Contain("UiText.Get(\"AutoFilter_DatePresetNextYear\")");
        source.Should().Contain("UiText.Get(\"AutoFilter_DatePreset\")");
        source.Should().Contain("_criteriaConnectorBox");
        source.Should().Contain("_criteriaOperatorBox2");
        source.Should().Contain("_criteriaValueBox2");
        source.Should().Contain("_customFilterGroup");
        source.Should().Contain("Header = UiText.Get(\"AutoFilter_CustomFilter\")");
        source.Should().Contain("IsReadOnly = true");
        source.Should().Contain("private void ShowCustomFilterPanel()");
        source.Should().Contain("_customFilterGroup.Visibility = Visibility.Visible;");
        source.Should().Contain("_criteriaSuggestionLabel.Visibility = Visibility.Visible");
        source.Should().Contain("BuildCriteriaText");
        source.Should().Contain("BuildBetweenCriteriaText");
        source.Should().Contain("BuildTopBottomCriteriaText");
        source.Should().Contain("BuildDatePresetCriteriaText");
        source.Should().Contain("BuildCompositeCriteriaText");
        source.Should().Contain("RefreshSpecialCriteriaPanels");
        source.Should().Contain("SelectedDatePresetCriteria");
        source.Should().Contain("!string.IsNullOrWhiteSpace(_criteriaValueBox2.Text)");
        source.Should().NotContain("filterButton.Click += (_, _) => _criteriaBox.Focus()");
    }

    [Fact]
    public void DialogControls_BetweenAndTopBottomCriteriaLabelsTargetInputs()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("new Label { Content = UiText.Get(\"AutoFilter_MinimumLabel\"), Target = _betweenMinBox");
        source.Should().Contain("new Label { Content = UiText.Get(\"AutoFilter_MaximumLabel\"), Target = _betweenMaxBox");
        source.Should().Contain("new Label { Content = UiText.Get(\"AutoFilter_ShowLabel\"), Target = _topBottomCountBox");
        source.Should().NotContain("new TextBlock { Text = \"_Minimum:\"");
        source.Should().NotContain("new TextBlock { Text = \"And _maximum:\"");
        source.Should().NotContain("new TextBlock { Text = \"_Show:\"");
    }

    [Fact]
    public void DialogControls_RenderFilterFamilyAsNestedMenuCommands()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("ConfigureFilterFamilySubmenu(menuPlan);");
        source.Should().Contain("private void ConfigureFilterFamilySubmenu(AutoFilterMenuPlan menuPlan)");
        source.Should().Contain("new ContextMenu()");
        source.Should().Contain("var usedAccessKeys = new HashSet<char>();");
        source.Should().Contain("Header = AddUniqueAccessKey(child.Header, usedAccessKeys),");
        source.Should().Contain("private static string AddUniqueAccessKey(string header, HashSet<char> usedAccessKeys)");
        source.Should().Contain("usedAccessKeys.Add(char.ToUpperInvariant(ch))");
        source.Should().Contain("parentButton.ContextMenu = submenu;");
        source.Should().Contain("menuItem.Click += (_, _) => ApplyFilterFamilyChild(child);");
        source.Should().Contain("private void ApplyFilterFamilyChild(AutoFilterMenuEntry child)");
        source.Should().Contain("AutoFilterMenuEntryKind.FilterFamilyCommand");
    }

    [Fact]
    public void DialogControls_FilterFamilyContinuationKeyOpensVisibleSubmenu()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("PreviewKeyDown += AutoFilterDialog_PreviewKeyDown;");
        source.Should().Contain("private void AutoFilterDialog_PreviewKeyDown(object sender, KeyEventArgs e)");
        source.Should().Contain("e.Key != Key.F");
        source.Should().Contain("TryOpenVisibleFilterFamilySubmenu()");
        source.Should().Contain("private bool TryOpenVisibleFilterFamilySubmenu()");
        source.Should().Contain("_textFiltersButton, _numberFiltersButton, _dateFiltersButton");
        source.Should().Contain("private Button? FindFirstVisibleFilterFamilyButton()");
        source.Should().Contain("_textFiltersButton.Visibility == Visibility.Visible");
        source.Should().Contain("_numberFiltersButton.Visibility == Visibility.Visible");
        source.Should().Contain("_dateFiltersButton.Visibility == Visibility.Visible");
        source.Should().Contain("private bool TryOpenFilterFamilySubmenu(Button filterButton)");
        source.Should().Contain("submenu.IsOpen = true;");
        source.Should().Contain("Keyboard.Focus(firstItem);");
        source.Should().Contain("filterButton.Click += (_, _) => TryOpenFilterFamilySubmenu(filterButton);");
    }

    [Fact]
    public void DialogControls_FilterFamilyContinuationKeyDoesNotHijackTextEntry()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("if (IsTextInputElement(e.OriginalSource))");
        source.Should().Contain("private static bool IsTextInputElement(object? originalSource)");
        source.Should().Contain("originalSource is TextBox");
        source.Should().Contain("originalSource is ComboBox { IsEditable: true }");
        source.Should().Contain("return;");
    }

    [Fact]
    public void DialogControls_InvalidTypedCriteriaWarnsAndRefocusesRequiredField()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("if (!ValidateTypedCriteriaInputs())");
        source.Should().Contain("if (_customFilterGroup.Visibility != Visibility.Visible)");
        source.Should().Contain("private string GetCommittedCriteriaText()");
        source.Should().Contain("? _criteriaBox.Text");
        source.Should().Contain(": string.Empty");
        source.Should().Contain("ShowInvalidCriteriaWarning(UiText.Get(\"AutoFilter_EnterFilterValue\"), _criteriaValueBox);");
        source.Should().Contain("ShowInvalidCriteriaWarning(UiText.Get(\"AutoFilter_EnterFirstBetweenValue\"), _betweenMinBox);");
        source.Should().Contain("ShowInvalidCriteriaWarning(UiText.Get(\"AutoFilter_EnterSecondBetweenValue\"), _betweenMaxBox);");
        source.Should().Contain("ShowInvalidCriteriaWarning(UiText.Get(\"AutoFilter_EnterValidTopOrBottomCount\"), _topBottomCountBox);");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
    }

    [Fact]
    public void DialogLayout_UsesSeparatorsBetweenExcelFilterMenuSections()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("AddFilterMenuSeparator(stack)");
        source.Should().Contain("new Separator");
    }
}
