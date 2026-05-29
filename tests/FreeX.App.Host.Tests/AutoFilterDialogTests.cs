using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class AutoFilterDialogTests
{
    [Fact]
    public void DialogLayout_ScrollsWhenTypedFilterControlsAreVisible()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFilterDialog.cs"));

        source.Should().Contain("var scrollViewer = new ScrollViewer");
        source.Should().Contain("VerticalScrollBarVisibility = ScrollBarVisibility.Auto");
        source.Should().Contain("DockPanel.SetDock(buttons, Dock.Bottom)");
        source.Should().Contain("root.Children.Add(buttons)");
        source.Should().Contain("scrollViewer.Content = stack");
    }

    [Fact]
    public void FilterItems_ReturnsSearchMatchesWithoutChangingSelection()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", true),
            new AutoFilterDialogItem("Banana", "Banana", false),
            new AutoFilterDialogItem("(Blanks)", "", true)
        };

        var filtered = AutoFilterDialog.FilterItems(items, "app");

        filtered.Should().Equal(new AutoFilterDialogItem("Apple", "Apple", true));
        items.Should().Contain(new AutoFilterDialogItem("Banana", "Banana", false));
    }

    [Fact]
    public void SelectAllAndClearAll_UpdateChecklistSelections()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", false),
            new AutoFilterDialogItem("Banana", "Banana", true)
        };

        AutoFilterDialog.SelectAll(items).Should().OnlyContain(item => item.IsSelected);
        AutoFilterDialog.ClearAll(items).Should().OnlyContain(item => !item.IsSelected);
    }

    [Fact]
    public void SetSelectionForSearch_UpdatesVisibleMatchesAndPreservesHiddenSelections()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", false),
            new AutoFilterDialogItem("Apricot", "Apricot", false),
            new AutoFilterDialogItem("Banana", "Banana", true)
        };

        var updated = AutoFilterDialog.SetSelectionForSearch(items, "ap", isSelected: true);

        updated.Should().Equal(
            new AutoFilterDialogItem("Apple", "Apple", true),
            new AutoFilterDialogItem("Apricot", "Apricot", true),
            new AutoFilterDialogItem("Banana", "Banana", true));
    }

    [Theory]
    [InlineData(AutoFilterMenuFilterKind.Text, "Text Filters")]
    [InlineData(AutoFilterMenuFilterKind.Number, "Number Filters")]
    [InlineData(AutoFilterMenuFilterKind.Date, "Date Filters")]
    public void GetFilterFamilyHeader_ReturnsExcelTypedFilterAffordance(AutoFilterMenuFilterKind filterKind, string expected)
    {
        AutoFilterDialog.GetFilterFamilyHeader(filterKind).Should().Be(expected);
    }

    [Theory]
    [InlineData(AutoFilterMenuFilterKind.Text, "Sort _A to Z", "Sort _Z to A")]
    [InlineData(AutoFilterMenuFilterKind.Number, "Sort _Smallest to Largest", "Sort _Largest to Smallest")]
    [InlineData(AutoFilterMenuFilterKind.Date, "Sort _Oldest to Newest", "Sort _Newest to Oldest")]
    public void GetSortLabels_ReturnsExcelLabelsForDetectedFilterValueType(
        AutoFilterMenuFilterKind filterKind,
        string expectedAscending,
        string expectedDescending)
    {
        AutoFilterDialog.GetSortLabels(filterKind)
            .Should()
            .Be((expectedAscending, expectedDescending));
    }

    [Fact]
    public void DialogItems_AreMutableForChecklistBinding()
    {
        var item = new AutoFilterDialogItem("Apple", "Apple", true);

        item.IsSelected = false;

        AutoFilterDialog.BuildResult(AutoFilterSortDirection.None, [item], "", "")
            .SelectedValues.Should().BeEmpty();
    }

    [Fact]
    public void BuildResult_IncludesSortDirectionChecklistValuesSearchAndCriteriaText()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", true),
            new AutoFilterDialogItem("Banana", "Banana", false),
            new AutoFilterDialogItem("(Blanks)", "", true)
        };

        var result = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.Descending,
            items,
            "a",
            "contains: App");

        result.SortDirection.Should().Be(AutoFilterSortDirection.Descending);
        result.SelectedValues.Should().Equal("Apple", "");
        result.SearchText.Should().Be("a");
        result.CriteriaText.Should().Be("contains: App");
        result.ColorFilter.Should().BeNull();
    }

    [Fact]
    public void BuildResult_WithSearchUsesVisibleMatchesUnlessAddingCurrentSelection()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", true),
            new AutoFilterDialogItem("Apricot", "Apricot", false),
            new AutoFilterDialogItem("Banana", "Banana", true)
        };

        var searchOnly = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.None,
            items,
            "ap",
            "",
            addCurrentSelectionToFilter: false);
        var addCurrentSelection = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.None,
            items,
            "ap",
            "",
            addCurrentSelectionToFilter: true);

        searchOnly.SelectedValues.Should().Equal("Apple");
        searchOnly.CriteriaText.Should().Be("Apple");
        addCurrentSelection.SelectedValues.Should().Equal("Apple", "Banana");
        addCurrentSelection.CriteriaText.Should().Be("Apple, Banana");
    }

    [Fact]
    public void BuildResult_CarriesOptionalColorFilter()
    {
        var color = new CellColor(33, 115, 70);

        var result = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.None,
            [new AutoFilterDialogItem("Apple", "Apple", true)],
            "",
            "",
            new AutoFilterColorFilter(AutoFilterColorFilterKind.CellFillColor, color));

        result.ColorFilter.Should().Be(new AutoFilterColorFilter(AutoFilterColorFilterKind.CellFillColor, color));
        result.CriteriaText.Should().Be("Apple");
    }

    [Fact]
    public void BuildResult_DistinguishesNoFillColorFilterFromNoColorSelection()
    {
        var result = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.None,
            [new AutoFilterDialogItem("Apple", "Apple", true)],
            "",
            "",
            new AutoFilterColorFilter(AutoFilterColorFilterKind.NoFill, null));

        result.ColorFilter.Should().Be(new AutoFilterColorFilter(AutoFilterColorFilterKind.NoFill, null));
    }

    [Fact]
    public void CreateClearFilterResult_RequestsExplicitClearAction()
    {
        AutoFilterDialog.CreateClearFilterResult()
            .Should()
            .Be(new AutoFilterDialogResult(
                AutoFilterSortDirection.None,
                [],
                "",
                "",
                null,
                AutoFilterDialogAction.ClearFilter));
    }

    [Fact]
    public void GetCriteriaSuggestions_ReturnsFilterFamilyCriteriaFromMenuPlan()
    {
        var menuPlan = new AutoFilterMenuPlan(
            "Fruit",
            AutoFilterMenuFilterKind.Text,
            [
                new AutoFilterMenuEntry("Sort A to Z", AutoFilterMenuEntryKind.SortAscending),
                new AutoFilterMenuEntry("Text Filters", AutoFilterMenuEntryKind.FilterFamily, ["contains:", "blank"]),
                new AutoFilterMenuEntry(new AutoFilterChecklistItem("Apple", "Apple"))
            ]);

        AutoFilterDialog.GetCriteriaSuggestions(menuPlan)
            .Should()
            .Equal("contains:", "blank");
    }

    [Theory]
    [InlineData(AutoFilterMenuFilterKind.Text, "Contains", "contains:Blue")]
    [InlineData(AutoFilterMenuFilterKind.Number, "Greater Than", ">42")]
    [InlineData(AutoFilterMenuFilterKind.Date, "After", "date>2026-05-21")]
    public void BuildCriteriaText_UsesTypedOperatorTemplates(
        AutoFilterMenuFilterKind filterKind,
        string optionLabel,
        string expected)
    {
        var option = AutoFilterDialog.GetCriteriaOptions(filterKind)
            .Single(item => item.Label == optionLabel);

        var value = filterKind switch
        {
            AutoFilterMenuFilterKind.Text => "Blue",
            AutoFilterMenuFilterKind.Number => "42",
            _ => "2026-05-21"
        };

        AutoFilterDialog.BuildCriteriaText(option, value).Should().Be(expected);
    }

    [Fact]
    public void BuildBetweenCriteriaText_UsesSeparateMinimumAndMaximumValues()
    {
        var option = AutoFilterDialog.GetCriteriaOptions(AutoFilterMenuFilterKind.Number)
            .Single(item => item.Label == "Between");

        AutoFilterDialog.BuildBetweenCriteriaText(option, " 10 ", "20")
            .Should()
            .Be("between:10:20");
    }

    [Theory]
    [InlineData("Top 10", "top:5")]
    [InlineData("Bottom 10 Percent", "bottompercent:25")]
    public void BuildTopBottomCriteriaText_UsesExcelCountControl(string optionLabel, string expected)
    {
        var option = AutoFilterDialog.GetCriteriaOptions(AutoFilterMenuFilterKind.Number)
            .Single(item => item.Label == optionLabel);

        AutoFilterDialog.BuildTopBottomCriteriaText(option, expected.Split(':')[1])
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("Today", "date=2026-05-22")]
    [InlineData("Yesterday", "date=2026-05-21")]
    [InlineData("Tomorrow", "date=2026-05-23")]
    [InlineData("This Week", "datebetween:2026-05-17:2026-05-23")]
    [InlineData("Last Week", "datebetween:2026-05-10:2026-05-16")]
    [InlineData("Next Week", "datebetween:2026-05-24:2026-05-30")]
    [InlineData("This Month", "datebetween:2026-05-01:2026-05-31")]
    [InlineData("Last Month", "datebetween:2026-04-01:2026-04-30")]
    [InlineData("Next Month", "datebetween:2026-06-01:2026-06-30")]
    [InlineData("This Year", "datebetween:2026-01-01:2026-12-31")]
    [InlineData("Last Year", "datebetween:2025-01-01:2025-12-31")]
    [InlineData("Next Year", "datebetween:2027-01-01:2027-12-31")]
    public void BuildDatePresetCriteriaText_UsesExcelDateFilterPresets(string preset, string expected)
    {
        AutoFilterDialog.BuildDatePresetCriteriaText(preset, new DateTime(2026, 5, 22))
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData(AutoFilterMenuFilterKind.Text, "Blanks", "blank")]
    [InlineData(AutoFilterMenuFilterKind.Number, "Above Average", "above average")]
    [InlineData(AutoFilterMenuFilterKind.Date, "Between", "datebetween:")]
    public void BuildCriteriaText_AllowsValueOptionalTypedCriteria(
        AutoFilterMenuFilterKind filterKind,
        string optionLabel,
        string expected)
    {
        var option = AutoFilterDialog.GetCriteriaOptions(filterKind)
            .Single(item => item.Label == optionLabel);

        AutoFilterDialog.BuildCriteriaText(option, string.Empty).Should().Be(expected);
    }

    [Fact]
    public void DialogSearch_NarrowsChecklistWithoutDroppingHiddenSelections()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("_searchBox.TextChanged");
        source.Should().Contain("FilterItems(_allItems, _searchBox.Text)");
        source.Should().Contain("GetSortDirection()");
        source.Should().Contain("_allItems");
        source.Should().Contain("_addCurrentSelectionToFilterBox.IsChecked == true");
        source.Should().Contain("GetResultItemsForSearchMode");
    }

    [Fact]
    public void DialogControls_ExposeExcelStyleKeyboardAccessKeys()
    {
        var source = ReadAutoFilterDialogSources();

        foreach (var content in new[]
        {
            "_No sort",
            "Sort _A to Z",
            "Sort _Z to A",
            "_Clear Filter From",
            "_Text Filters",
            "_Number Filters",
            "_Date Filters",
            "_Select All",
            "_Clear All",
            "_Add current selection to filter",
            "_OK",
            "_Cancel"
        })
            source.Should().Contain($"Content = \"{content}\"");

        source.Should().Contain("Content = \"_Criteria text\"");
        source.Should().Contain("Content = \"_Search\"");
    }

    [Fact]
    public void DialogControls_FilterValueChecklistExposesAutomationName()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("AutomationProperties.SetName(_checklistBox, \"Filter values\");");
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesFirstSortCommand()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_sortAscending.Focus();");
        source.Should().Contain("Keyboard.Focus(_sortAscending);");
    }

    [Fact]
    public void DialogControls_ExposeFilterByColorPickerWhenMenuPlanSupportsIt()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("_filterByColorGroup");
        source.Should().Contain("Header = \"Filter by Color\"");
        source.Should().Contain("PopulateColorChoices");
        source.Should().Contain("Cell Color");
        source.Should().Contain("Font Color");
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
        source.Should().Contain("Result = BuildResult(");
        source.Should().Contain("colorFilter,");
        source.Should().Contain("DialogResult = true;");
        source.Should().NotContain("button.Click += (_, _) => _selectedColorFilter = colorFilter;");
    }

    [Fact]
    public void DialogControls_ChecklistSupportsKeyboardToggleAndBoundaryNavigation()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("private readonly ListBox _checklistBox = new();");
        source.Should().Contain("AutomationProperties.SetName(_checklistBox, \"Filter values\");");
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
    public void DataFilterCommands_RouteColorFiltersAndCompositeCriteriaToRealCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));

        source.Should().Contain("result.Action == AutoFilterDialogAction.ClearFilter");
        source.Should().Contain("\"Clear Filter\"");
        source.Should().Contain("result.ColorFilter is { } colorFilter");
        source.Should().Contain("new CellFillColorFilterCommand");
        source.Should().Contain("new CellNoFillColorFilterCommand");
        source.Should().Contain("new CellFontColorFilterCommand");
        source.Should().Contain("FilterPromptPlanner.TryPlan");
        source.Should().Contain("promptPlan.CreateCommand");
    }

    [Fact]
    public void DataFilterCommands_ReapplyUsesRememberedFilterCommandWithoutOpeningDialog()
    {
        var dataSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));
        var homeEditingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeEditing.cs"));

        dataSource.Should().Contain("private GridRange? _lastAutoFilterRange;");
        dataSource.Should().Contain("private Func<GridRange, IWorkbookCommand>? _lastAutoFilterCommandFactory;");
        dataSource.Should().Contain("private bool TryExecuteRememberedAutoFilterCommand(");
        dataSource.Should().Contain("_lastAutoFilterCommandFactory = createCommand;");
        dataSource.Should().Contain("private void ReapplyAutoFilter()");
        dataSource.Should().Contain("TryExecuteRepeatableCurrentRangeCommand(");
        dataSource.Should().Contain("_lastAutoFilterCommandFactory");
        dataSource.Should().Contain("private void ClearRememberedAutoFilterCommand()");
        homeEditingSource.Should().Contain("private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => ReapplyAutoFilter();");
        homeEditingSource.Should().NotContain("private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);");
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
        source.Should().Contain("\"This Week\"");
        source.Should().Contain("\"Last Week\"");
        source.Should().Contain("\"Next Week\"");
        source.Should().Contain("\"This Year\"");
        source.Should().Contain("\"Last Year\"");
        source.Should().Contain("\"Next Year\"");
        source.Should().Contain("Date _preset");
        source.Should().Contain("_criteriaConnectorBox");
        source.Should().Contain("_criteriaOperatorBox2");
        source.Should().Contain("_criteriaValueBox2");
        source.Should().Contain("_customFilterGroup");
        source.Should().Contain("Header = \"Custom filter\"");
        source.Should().Contain("IsReadOnly = true");
        source.Should().Contain("_customFilterGroup.Visibility = Visibility.Visible");
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
    public void CriteriaPartial_DelegatesPureCriteriaBehaviorToPlanner()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFilterDialog.Criteria.cs"));

        source.Should().Contain("AutoFilterDialogCriteriaPlanner.BuildResult");
        source.Should().Contain("AutoFilterDialogCriteriaPlanner.BuildCriteriaText");
        source.Should().Contain("AutoFilterDialogCriteriaPlanner.BuildCompositeCriteriaText");
    }

    [Fact]
    public void DialogControls_BetweenAndTopBottomCriteriaLabelsTargetInputs()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("new Label { Content = \"_Minimum:\", Target = _betweenMinBox");
        source.Should().Contain("new Label { Content = \"And _maximum:\", Target = _betweenMaxBox");
        source.Should().Contain("new Label { Content = \"_Show:\", Target = _topBottomCountBox");
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
        source.Should().Contain("FirstOrDefault(button => button.Visibility == Visibility.Visible)");
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
        source.Should().Contain("ShowInvalidCriteriaWarning(\"Enter a filter value.\", _criteriaValueBox);");
        source.Should().Contain("ShowInvalidCriteriaWarning(\"Enter the first value for the between filter.\", _betweenMinBox);");
        source.Should().Contain("ShowInvalidCriteriaWarning(\"Enter the second value for the between filter.\", _betweenMaxBox);");
        source.Should().Contain("ShowInvalidCriteriaWarning(\"Enter a valid top or bottom count.\", _topBottomCountBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title);");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void DialogLayout_UsesSeparatorsBetweenExcelFilterMenuSections()
    {
        var source = ReadAutoFilterDialogSources();

        source.Should().Contain("AddFilterMenuSeparator(stack)");
        source.Should().Contain("new Separator");
    }

    [Theory]
    [InlineData("And", ">10", "<20", "and:>10|<20")]
    [InlineData("Or", "begins:Red", "ends:Apple", "or:begins:Red|ends:Apple")]
    public void BuildCompositeCriteriaText_ComposesExcelCustomFilterRows(
        string connector,
        string firstCriteria,
        string secondCriteria,
        string expected)
    {
        AutoFilterDialog.BuildCompositeCriteriaText(firstCriteria, connector, secondCriteria)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void TypedCriteriaResult_DrivesFilterConditionCommandRowVisibility()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(10));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var option = AutoFilterDialog.GetCriteriaOptions(AutoFilterMenuFilterKind.Number)
            .Single(item => item.Label == "Greater Than");
        var result = AutoFilterDialog.BuildResult(
            AutoFilterSortDirection.None,
            [
                new AutoFilterDialogItem("5", "5", true),
                new AutoFilterDialogItem("10", "10", true)
            ],
            "",
            AutoFilterDialog.BuildCriteriaText(option, "7"));

        FilterInputParser.TryParseCriterion(result.CriteriaText, out var criterion, out var error)
            .Should()
            .BeTrue(error);
        new FilterConditionCommand(sheet.Id, range, 1, criterion!).Apply(new SimpleCtx(workbook))
            .Success
            .Should()
            .BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2);
        sheet.FilterHiddenRows.Should().NotContain(3);
    }

    [Fact]
    public void CriteriaPlanner_ChecklistHotPathsAvoidLinqMaterialization()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFilterDialogCriteriaPlanner.cs"));
        var checklistBlock = source[
            source.IndexOf("public static IReadOnlyList<AutoFilterDialogItem> FilterItems", StringComparison.Ordinal)..
            source.IndexOf("public static string GetFilterFamilyHeader", StringComparison.Ordinal)];
        var resultBlock = source[
            source.IndexOf("public static AutoFilterDialogResult BuildResult", StringComparison.Ordinal)..
            source.IndexOf("public static AutoFilterDialogResult CreateClearFilterResult", StringComparison.Ordinal)];
        var suggestionsBlock = source[
            source.IndexOf("public static IReadOnlyList<string> GetCriteriaSuggestions", StringComparison.Ordinal)..
            source.IndexOf("public static IReadOnlyList<AutoFilterCriteriaOption> GetCriteriaOptions", StringComparison.Ordinal)];

        checklistBlock.Should().Contain("foreach (var item in items)");
        checklistBlock.Should().NotContain(".Where(");
        checklistBlock.Should().NotContain(".Select(");
        checklistBlock.Should().NotContain(".ToList(");
        checklistBlock.Should().NotContain(".ToHashSet(");
        resultBlock.Should().Contain("foreach (var item in resultItems)");
        resultBlock.Should().NotContain(".Where(");
        resultBlock.Should().NotContain(".Select(");
        resultBlock.Should().NotContain(".ToList(");
        suggestionsBlock.Should().Contain("foreach (var entry in menuPlan.Entries)");
        suggestionsBlock.Should().NotContain(".FirstOrDefault(");
        suggestionsBlock.Should().NotContain(".Where(");
        suggestionsBlock.Should().NotContain(".ToList(");
    }

    private static string ReadAutoFilterDialogSources()
    {
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFilterDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFilterDialog.Controls.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFilterDialog.Criteria.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFilterDialogCriteriaPlanner.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFilterDialog.State.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFilterDialogModel.cs")));
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
