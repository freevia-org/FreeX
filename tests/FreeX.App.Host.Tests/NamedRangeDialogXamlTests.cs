using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class NamedRangeDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesAccessKeyedFieldsAndCommands()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("NamedRangeDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "GroupBox")
            .Single()
            .Attribute("Header")?.Value.Should().Be("_Defined Names");

        AssertLabelTargets(document, presentation, "_Refers to:", "RefersToBox");

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_New...", "_Edit...", "_Delete", "_Close"]);

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        document.Descendants(presentation + "Button")
            .Single(element => element.Attribute(x + "Name")?.Value == "EditButton")
            .Attribute("IsEnabled")?.Value.Should().Be("False");
        document.Descendants(presentation + "Button")
            .Single(element => element.Attribute(x + "Name")?.Value == "DeleteButton")
            .Attribute("IsEnabled")?.Value.Should().Be("False");

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element => element.Attribute("Content")?.Value == content);

            label.Attribute("Target")?.Value.Should().Be($"{{Binding ElementName={target}}}");
        }
    }

    [Fact]
    public void DefinedNamesList_UsesExcelLikeColumns()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("NamedRangeDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "GridViewColumn")
            .Select(element => element.Attribute("Header")?.Value)
            .Should()
            .ContainInOrder(["Name", "Value", "Refers To", "Scope", "Comment"]);
    }

    [Fact]
    public void DefinedNamesList_DoubleClickOpensEditNameDialog()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("NamedRangeDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = ReadNamedRangeDialogSource();

        document.Descendants(presentation + "ListView")
            .Single(element => element.Attribute(x + "Name")?.Value == "NamesList")
            .Attribute("MouseDoubleClick")?.Value.Should().Be("NamesList_MouseDoubleClick");
        source.Should().Contain("private void NamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)");
        source.Should().Contain("if (NamesList.SelectedItem is not DefinedNameRow)");
        source.Should().Contain("EditButton_Click(sender, e);");
        source.Should().Contain("e.Handled = true;");
        source.IndexOf("if (NamesList.SelectedItem is not DefinedNameRow)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(source.IndexOf("EditButton_Click(sender, e);", StringComparison.Ordinal));
        source.IndexOf("EditButton_Click(sender, e);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(source.IndexOf("e.Handled = true;", StringComparison.Ordinal));
    }

    [Fact]
    public void DefinedNamesList_DoubleClickWithoutSelectionDoesNotOpenEditNameDialog()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            var namesList = GetControl<ListView>(dialog, "NamesList");

            namesList.SelectedItem = null;
            var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();

            namesList.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeFalse();
            typeof(NamedRangeDialog)
                .GetField("_activeDefinitionDialog", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)
                .Should().BeNull();
        });
    }

    [Fact]
    public void Dialog_ProvidesFilterAndRefersToRangePickerAffordance()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("NamedRangeDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var name in new[] { "FilterBox", "RefersToPickerButton" })
        {
            document.Descendants()
                .Any(element => element.Attribute(x + "Name")?.Value == name)
                .Should().BeTrue($"{name} should exist for Excel-like name manager workflow");
        }

        document.Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FilterBox")
            .Attribute("SelectionChanged")?.Value.Should().Be("FilterBox_SelectionChanged");

        var picker = document.Descendants(presentation + "Button")
            .Single(element => element.Attribute(x + "Name")?.Value == "RefersToPickerButton");
        picker.Attribute("Click")?.Value.Should().Be("RefersToPickerButton_Click");
        picker.Attribute("IsEnabled").Should().BeNull("the picker state is managed from the selected name");
        picker.Attribute("ToolTip")?.Value.Should().Be("Collapse dialog and select the referenced range");
        picker.Attribute("AutomationProperties.Name")?.Value.Should().Be("Select referenced range");
    }

    [Fact]
    public void Dialog_FilterMenu_OffersExcelLikeErrorFilters()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("NamedRangeDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FilterBox")
            .Descendants(presentation + "ComboBoxItem")
            .Should()
            .BeEmpty("the shared descriptor catalog owns filter order and labels");

        DefinedNameUiPolicy.Filters
            .Select(descriptor => FreeX.App.Localization.Loc.Get(descriptor.LabelResourceKey))
            .Should()
            .ContainInOrder(
            "All names",
            "Names scoped to workbook",
            "Names scoped to worksheet",
            "Names with errors",
            "Names without errors");

        ReadNamedRangeDialogSource().Should().Contain("FilterBox.ItemsSource = DefinedNameUiPolicy.Filters");
    }

    [Fact]
    public void Dialog_UsesExcelLikeRefersToSummaryInsteadOfInlineNameEditing()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("NamedRangeDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        document.Descendants()
            .Any(element => element.Attribute(x + "Name")?.Value == "NameBox")
            .Should().BeFalse("New/Edit should happen in the dedicated Excel-like name dialog");

        document.Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "RefersToBox")
            .Attribute("IsReadOnly")?.Value.Should().Be("True");
    }

    [Fact]
    public void Source_ProvidesNewEditNameDialogWithExcelNameFields()
    {
        var source = ReadNamedRangeDialogSource();

        source.Should().Contain("NameDefinitionDialog");
        source.Should().Contain("NamedRangeSelectionRequest");
        source.Should().Contain("_scopeBox");
        source.Should().Contain("_commentBox");
        source.Should().Contain("_refersToBox");
        source.Should().Contain("_rangePickerButton");
        source.Should().Contain("_rangePickerButton.Click");
        source.Should().Contain("_requestRangeSelection?.Invoke");
        source.Should().Contain("_refersToBox.SelectAll");
        source.Should().Contain("UpdateSelectionCommands");
        source.Should().Contain("DefinedNameUiPolicy.PlanManagerSelection");
        source.Should().Contain("EditButton.IsEnabled = plan.CanEdit");
        source.Should().Contain("DeleteButton.IsEnabled = plan.CanDelete");
        source.Should().Contain("DialogMessageHelper.AskYesNo(this,");
        source.Should().Contain("UiText.Format(\"NamedRange_DeleteConfirmation\", vm.Name)");
        source.Should().Contain("RefersToPickerButton_Click");
        source.Should().Contain("RefersToBox.SelectAll()");
        source.Should().NotContain("IsEnabled = false");
        source.Should().Contain("GetScopeOptions");
        source.Should().Contain("_definedNames.ScopeChoices");
        source.Should().Contain("_definedNames.BuildDeleteCommand(vm)");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("_nameBox.Focus();");
        source.Should().Contain("_nameBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_nameBox);");
    }

    [Fact]
    public void NameDefinitionDialog_EditorsExposeAutomationNames()
    {
        var source = ReadNamedRangeDialogSource();

        source.Should().Contain("AutomationProperties.SetName(_nameBox, UiText.Get(\"NameDefinition_NameAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetName(_scopeBox, UiText.Get(\"NameDefinition_ScopeAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetName(_commentBox, UiText.Get(\"NameDefinition_CommentAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetName(_refersToBox, UiText.Get(\"NameDefinition_RefersToAutomationName\"));");
    }

    [Fact]
    public void NameDefinitionDialogInvalidInputs_StayOpenAndFocusInvalidField()
    {
        var source = ReadNamedRangeDialogSource();

        source.Should().Contain("Func<string, bool>? isValidRange");
        source.Should().Contain("Func<string, string?>? validateName");
        source.Should().Contain("isValidRange: rangeText => _definedNames.ValidateRefersTo(rangeText).IsValid");
        source.Should().Contain("validateName: ValidateNameForNativeDialog");
        source.Should().Contain("ValidateNameInput(name, _validateName)");
        source.Should().Contain("FocusNameInput();");
        source.Should().Contain("FocusRefersToInput();");
        source.Should().Contain("private void FocusNameInput()");
        source.Should().Contain("private void FocusRefersToInput()");
        source.Should().Contain("_refersToBox.Focus();");
        source.Should().Contain("_refersToBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_refersToBox);");
    }

    [Theory]
    [InlineData("Sales")]
    [InlineData("_2026_Sales")]
    [InlineData("Q1.Total")]
    public void NameDefinitionDialogValidateNameInput_AcceptsWorkbookValidNames(string name)
    {
        var workbook = new Workbook("Book");

        NameDefinitionDialog.ValidateNameInput(name, workbook.ValidateNamedRangeName)
            .Should()
            .BeNull();
    }

    [Theory]
    [InlineData("", "Please enter a name.")]
    [InlineData("Sales Total", "letters, numbers, underscores, and periods")]
    [InlineData("1Sales", "start with a letter or underscore")]
    [InlineData("A1", "cell reference")]
    [InlineData("R1C1", "cell reference")]
    public void NameDefinitionDialogValidateNameInput_RejectsWorkbookInvalidNames(
        string name,
        string expectedMessage)
    {
        var workbook = new Workbook("Book");

        var error = NameDefinitionDialog.ValidateNameInput(name, workbook.ValidateNamedRangeName);

        error.Should().Contain(expectedMessage);
    }

    [Fact]
    public void NameManagerDialogOpenedFromKeyboard_FocusesNamesListOrNewButton()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("NamedRangeDialog.xaml");
        var source = ReadNamedRangeDialogSource();

        xaml.Should().Contain("x:Name=\"NewButton\"");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("NamesList.Items.Count > 0");
        source.Should().Contain("NamesList.Focus();");
        source.Should().Contain("Keyboard.Focus(NamesList);");
        source.Should().Contain("NewButton.Focus();");
        source.Should().Contain("Keyboard.Focus(NewButton);");
    }

    [Fact]
    public void NameManagerWarnings_FocusRelevantNameManagerField()
    {
        var source = ReadNamedRangeDialogSource();

        source.Should().Contain("FocusNamesListOrNewButton();");
        source.Should().Contain("private void FocusNamesListOrNewButton()");
        source.Should().Contain("FocusRefersToSummary();");
        source.Should().Contain("private void FocusRefersToSummary()");
        source.Should().Contain("RefersToBox.Focus();");
        source.Should().Contain("RefersToBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(RefersToBox);");
    }

    [Fact]
    public void NameManagerWarnings_UseOwnedMessageBoxes()
    {
        var source = ReadNamedRangeDialogSource();

        source.Should().Contain("DialogMessageHelper.ShowWarning(this, UiText.Get(\"NamedRange_SelectEditMessage\")");
        source.Should().Contain("DescribeDraftNameError(plan.Validation.Name.Error, plan.Draft.Name)");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get(\"NamedRange_DefineFailedMessage\")");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, UiText.Get(\"NamedRange_SelectDeleteMessage\")");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get(\"NamedRange_DeleteFailedMessage\")");
    }

    // R127: the New/Edit Name dialog's own-scope duplicate-name guard for named FORMULAS
    // (DefineOrUpdateNamedFormula) used to show a raw hardcoded-English interpolated string instead
    // of routing through UiText like every other warning in this dialog. Drives the real entry point
    // (the private DefineOrUpdateName handler NewButton_Click ultimately calls) through the shared
    // HeadlessMessageBox test seam so the captured message text is exactly what a user would see.
    [Fact]
    public void R127_DuplicateNamedFormula_ShowsLocalizedDuplicateWarning()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            var defineOrUpdateName = SourceTextTestSupport.GetPrivateMethod(dialog, "DefineOrUpdateName");
            try
            {
                // Seed an existing named formula "Sales" in workbook scope (no sheet is added to this
                // workbook, so a formula RefersTo avoids NamedRangeInputParser's sheet-count-0 bail-out
                // that a range RefersTo like "A1:A2" would hit).
                defineOrUpdateName.Invoke(
                    dialog,
                    [new NameDefinitionDialogResult("Sales", "Workbook", "", "=100"), null, null, null]);
                workbook.NamedFormulas.Should().ContainKey("Sales");

                string? capturedMessage = null;
                HeadlessMessageBox.Handler = (message, buttons) =>
                {
                    capturedMessage = message;
                    return UserMessageResult.Ok;
                };

                // A brand-new *formula* name (RefersTo doesn't parse as a range) colliding with the
                // already-defined "Sales" range in the same (workbook) scope must be rejected.
                defineOrUpdateName.Invoke(
                    dialog,
                    [new NameDefinitionDialogResult("Sales", "Workbook", "", "=1+1"), null, null, null]);

                capturedMessage.Should().Be(UiText.Format("NamedRange_NameAlreadyExistsInScopeMessage", "Sales"));

                // Rejected before DefineNamedFormulaCommand ran, so the original definition survives
                // untouched (it is not silently overwritten by the colliding "=1+1" attempt).
                workbook.NamedFormulas["Sales"].Should().Be("100");
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
                dialog.Close();
            }
        });
    }

    // R127 sibling/no-regression: editing the *same* named-formula entry (name+scope unchanged)
    // must still bypass the duplicate-name guard entirely (isSameEntry), exactly as before the fix —
    // the guard's message source changed, not its own-entry bypass logic.
    [Fact]
    public void R127_EditingSameNamedFormulaEntry_SkipsDuplicateWarningAndUpdatesFormula()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            var defineOrUpdateName = SourceTextTestSupport.GetPrivateMethod(dialog, "DefineOrUpdateName");
            try
            {
                defineOrUpdateName.Invoke(
                    dialog,
                    [new NameDefinitionDialogResult("Rate", "Workbook", "", "=1.05"), null, null, null]);
                workbook.NamedFormulas.Should().ContainKey("Rate");
                workbook.NamedFormulas["Rate"].Should().Be("1.05");

                string? capturedMessage = null;
                HeadlessMessageBox.Handler = (message, buttons) =>
                {
                    capturedMessage = message;
                    return UserMessageResult.Ok;
                };

                defineOrUpdateName.Invoke(
                    dialog,
                    [new NameDefinitionDialogResult("Rate", "Workbook", "", "=1.10"), "Rate", "Workbook", null]);

                capturedMessage.Should().BeNull("re-saving the same (name, scope) entry must not trigger the duplicate guard");
                workbook.NamedFormulas["Rate"].Should().Be("1.10");
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
                dialog.Close();
            }
        });
    }

    // R127B (ScopeAudit follow-up): the r127 fix localized only DefineOrUpdateNamedFormula's own
    // duplicate-name guard; DefineOrUpdateName's *range* branch (the far more common path — a
    // plain "A1:B2" Refers To) had no pre-check at all and surfaced DefineNamedRangeCommand's raw
    // hardcoded-English ErrorMessage verbatim instead. Drives the real entry point with a
    // range-shaped RefersTo colliding with an existing range name in the same scope.
    [Fact]
    public void R127B_DuplicateNamedRange_ShowsLocalizedDuplicateWarning()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            workbook.AddSheet("Sheet1");
            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            var defineOrUpdateName = SourceTextTestSupport.GetPrivateMethod(dialog, "DefineOrUpdateName");
            try
            {
                defineOrUpdateName.Invoke(
                    dialog,
                    [new NameDefinitionDialogResult("Sales", "Workbook", "", "A1:A2"), null, null, null]);
                workbook.NamedRanges.Should().ContainKey("Sales");

                string? capturedMessage = null;
                HeadlessMessageBox.Handler = (message, buttons) =>
                {
                    capturedMessage = message;
                    return UserMessageResult.Ok;
                };

                // A brand-new *range* name colliding with the already-defined "Sales" range in the
                // same (workbook) scope must be rejected with the localized message, not the raw
                // English text DefineNamedRangeCommand's own outcome.ErrorMessage carries.
                defineOrUpdateName.Invoke(
                    dialog,
                    [new NameDefinitionDialogResult("Sales", "Workbook", "", "A1:B2"), null, null, null]);

                capturedMessage.Should().Be(UiText.Format("NamedRange_NameAlreadyExistsInScopeMessage", "Sales"));

                // Rejected before DefineNamedRangeCommand ran, so the original definition survives
                // untouched (it is not silently overwritten by the colliding "A1:B2" attempt) —
                // still the single-column "A1:A2" range, not the two-column "A1:B2" range.
                workbook.NamedRanges["Sales"].ColCount.Should().Be(1u);
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
                dialog.Close();
            }
        });
    }

    // R127B sibling/no-regression: editing the *same* named-range entry (name+scope unchanged)
    // must still bypass the duplicate-name guard entirely (isSameEntry), exactly like the
    // named-formula branch already does — the new range-branch guard must not regress the
    // existing edit-in-place flow.
    [Fact]
    public void R127B_EditingSameNamedRangeEntry_SkipsDuplicateWarningAndUpdatesRange()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            workbook.AddSheet("Sheet1");
            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            var defineOrUpdateName = SourceTextTestSupport.GetPrivateMethod(dialog, "DefineOrUpdateName");
            try
            {
                defineOrUpdateName.Invoke(
                    dialog,
                    [new NameDefinitionDialogResult("Region", "Workbook", "", "A1:A2"), null, null, null]);
                workbook.NamedRanges.Should().ContainKey("Region");

                string? capturedMessage = null;
                HeadlessMessageBox.Handler = (message, buttons) =>
                {
                    capturedMessage = message;
                    return UserMessageResult.Ok;
                };

                defineOrUpdateName.Invoke(
                    dialog,
                    [new NameDefinitionDialogResult("Region", "Workbook", "", "A1:C3"), "Region", "Workbook", null]);

                capturedMessage.Should().BeNull("re-saving the same (name, scope) entry must not trigger the duplicate guard");
                workbook.NamedRanges["Region"].ColCount.Should().Be(3u);
                workbook.NamedRanges["Region"].RowCount.Should().Be(3u);
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
                dialog.Close();
            }
        });
    }

    // R127B source-contract companion: duplicate validation and command construction now live in
    // DefinedNamesSession. The renderer must inspect the typed, localized validation result before
    // executing the shared command plan.
    [Fact]
    public void R127B_DuplicateGuard_RoutesThroughSharedValidationBeforeCommandExecution()
    {
        var source = ReadNamedRangeDialogSource();

        var methodStart = source.IndexOf("private void DefineOrUpdateName(", StringComparison.Ordinal);
        var planSave = source.IndexOf("var plan = _definedNames.PlanSave(draft, original);", StringComparison.Ordinal);
        var validation = source.IndexOf("if (!plan.Validation.Name.IsValid)", StringComparison.Ordinal);
        var localizedMessage = source.IndexOf("DescribeDraftNameError(plan.Validation.Name.Error, plan.Draft.Name)", StringComparison.Ordinal);
        var commandExecution = source.IndexOf("_commandBus.Execute(_workbook.Id, plan.Command!)", StringComparison.Ordinal);

        methodStart.Should().BeGreaterThan(-1);
        planSave.Should().BeGreaterThan(methodStart);
        validation.Should().BeGreaterThan(planSave);
        localizedMessage.Should().BeGreaterThan(validation);
        commandExecution.Should().BeGreaterThan(localizedMessage);

    }

    [Fact]
    public void NameManager_UsesSharedDefinedNamesSession()
    {
        var hostSource = ReadNamedRangeDialogSource();
        var sessionSource = DialogSourceTestSupport.ReadPresentationSources("DefinedNames", "DefinedNamesSession.cs");

        hostSource.Should().Contain("private readonly DefinedNamesSession _definedNames;");
        hostSource.Should().Contain("_definedNames.ProjectRows(_items, selected)");
        hostSource.Should().Contain("_definedNames.PlanSave(draft, original)");
        hostSource.Should().Contain("DefinedNameValidationMessages.Describe(error).Resolve(UiText.Get)");
        hostSource.Should().Contain("RefersToValidationMessages.Describe(error).Resolve(UiText.Get)");
        hostSource.Should().NotContain("DefinedNameError.Blank =>");
        hostSource.Should().NotContain("RefersToError.Blank =>");
        hostSource.Should().NotContain("NamedRange_InvalidRangeFormatMessage");
        sessionSource.Should().Contain("public sealed class DefinedNamesSession");
    }

    [Theory]
    [InlineData(DefinedNameError.Blank)]
    [InlineData(DefinedNameError.InvalidFirstCharacter)]
    [InlineData(DefinedNameError.Duplicate)]
    public void NameManager_ResolvesSharedNameValidationMessage(DefinedNameError error)
    {
        var method = typeof(NamedRangeDialog).GetMethod(
            "DescribeNameError",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, [error]).Should().Be(
            DefinedNameValidationMessages.Describe(error).Resolve(FreeX.App.Localization.Loc.Get));
    }

    [Theory]
    [InlineData(RefersToError.Blank)]
    [InlineData(RefersToError.NotAFormula)]
    [InlineData(RefersToError.None)]
    public void NameManager_ResolvesSharedRefersToValidationMessage(RefersToError error)
    {
        var method = typeof(NamedRangeDialog).GetMethod(
            "DescribeRefersToError",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, [error]).Should().Be(
            RefersToValidationMessages.Describe(error).Resolve(FreeX.App.Localization.Loc.Get));
    }

    [Fact]
    public void Planner_FiltersWorkbookAndWorksheetScopedNames()
    {
        var workbookName = DefinedNameListProjector.CreateRow(
            "Sales", DefinedNameScope.Workbook, "Sheet1!A1:A2", "Sheet1!A1:A2");
        var sheetName = DefinedNameListProjector.CreateRow(
            "Local",
            DefinedNameScope.ForSheet(new SheetId(Guid.NewGuid()), "Sheet2"),
            "Sheet2!B1:B2",
            "Sheet2!B1:B2");

        NamedRangeDialogPlanner.FilterItems([workbookName, sheetName], NamedRangeFilterOption.All)
            .Should().Equal(workbookName, sheetName);
        NamedRangeDialogPlanner.FilterItems([workbookName, sheetName], NamedRangeFilterOption.Workbook)
            .Should().Equal(workbookName);
        NamedRangeDialogPlanner.FilterItems([workbookName, sheetName], NamedRangeFilterOption.Worksheet)
            .Should().Equal(sheetName);
    }

    [Fact]
    public void Planner_FiltersNamesWithAndWithoutFormulaErrors()
    {
        var validName = DefinedNameListProjector.CreateRow(
            "Sales", DefinedNameScope.Workbook, "Sheet1!A1:A2", "Sheet1!A1:A2");
        var errorValueName = DefinedNameListProjector.CreateRow(
            "BadValue", DefinedNameScope.Workbook, "Sheet1!A1:A2", "#REF!");
        var errorRefersToName = DefinedNameListProjector.CreateRow(
            "BadRef", DefinedNameScope.Workbook, "#NAME?", "Sheet1!A1:A2");

        NamedRangeDialogPlanner.FilterItems(
                [validName, errorValueName, errorRefersToName],
                NamedRangeFilterOption.Errors)
            .Should()
            .Equal(errorValueName, errorRefersToName);

        NamedRangeDialogPlanner.FilterItems(
                [validName, errorValueName, errorRefersToName],
                NamedRangeFilterOption.NoErrors)
            .Should()
            .Equal(validName);
    }

    [Fact]
    public void CreateRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        NamedRangeDialog.CreateRangeSelectionRequest(
                NamedRangeSelectionTarget.DefinitionRefersTo,
                " Sheet1!$A$1:$C$5 ")
            .Should()
            .Be(new NamedRangeSelectionRequest(
                NamedRangeSelectionTarget.DefinitionRefersTo,
                "Sheet1!$A$1:$C$5",
                CollapseDialog: true));
    }

    [Fact]
    public void NameManagerRefersToPicker_RaisesRangeSelectionRequest()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var requests = new List<NamedRangeSelectionRequest>();
            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook), requestRangeSelection: requests.Add);
            dialog.Show();
            try
            {
                GetControl<TextBox>(dialog, "RefersToBox").Text = " Sheet1!A1:C3 ";

                InvokePrivate(dialog, "RefersToPickerButton_Click");

                requests.Should().Equal(new NamedRangeSelectionRequest(
                    NamedRangeSelectionTarget.SelectedNameRefersTo,
                    "Sheet1!A1:C3",
                    CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void NameManagerRefersToPicker_RefocusesSummaryWithKeyboardFocus()
    {
        var source = ReadNamedRangeDialogSource();
        var handlerSource = source[
            source.IndexOf("private void RefersToPickerButton_Click", StringComparison.Ordinal)..
            source.IndexOf("private void NewButton_Click", StringComparison.Ordinal)];

        handlerSource.Should().Contain("RefersToBox.Focus();");
        handlerSource.Should().Contain("RefersToBox.SelectAll();");
        handlerSource.Should().Contain("Keyboard.Focus(RefersToBox);");
    }

    [Fact]
    public void NameDefinitionRefersToPicker_RaisesRangeSelectionRequest()
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<NamedRangeSelectionRequest>();
            var dialog = new NameDefinitionDialog(
                new NameDefinitionDialogResult("Sales", "Workbook", "", " Sheet1!$A$1:$C$5 "),
                ["Workbook"],
                requests.Add);
            dialog.Show();
            try
            {
                var picker = DialogSourceTestSupport.GetPrivateField<Button>(dialog, "_rangePickerButton");
                DialogSourceTestSupport.ClickButton(picker);

                requests.Should().Equal(new NamedRangeSelectionRequest(
                    NamedRangeSelectionTarget.DefinitionRefersTo,
                    "Sheet1!$A$1:$C$5",
                    CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void NameDefinitionRefersToPicker_ExposesAccessibleCollapseAffordance()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new NameDefinitionDialog(
                new NameDefinitionDialogResult("Sales", "Workbook", "", "Sheet1!$A$1:$C$5"),
                ["Workbook"]);
            try
            {
                var picker = DialogSourceTestSupport.GetPrivateField<Button>(dialog, "_rangePickerButton");

                System.Windows.Automation.AutomationProperties.GetName(picker)
                    .Should()
                    .Be("Select referenced range");
                System.Windows.Automation.AutomationProperties.GetHelpText(picker)
                    .Should()
                    .Be("Collapse dialog and select the referenced range from the worksheet.");
                picker.ToolTip.Should().Be("Collapse dialog and select the referenced range from the worksheet");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void NameDefinitionRefersToPicker_RefocusesInputWithKeyboardFocus()
    {
        var source = ReadNamedRangeDialogSource();
        var handlerSource = source[
            source.IndexOf("_rangePickerButton.Click += (_, _) =>", StringComparison.Ordinal)..
            source.IndexOf("Content = CreateContent();", StringComparison.Ordinal)];

        handlerSource.Should().Contain("_refersToBox.Focus();");
        handlerSource.Should().Contain("_refersToBox.SelectAll();");
        handlerSource.Should().Contain("Keyboard.Focus(_refersToBox);");
    }

    [Fact]
    public void NamedRangeDialogsApplyRangeSelection_UpdateRequestedRefersToField()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var manager = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            var definition = new NameDefinitionDialog(
                new NameDefinitionDialogResult("Sales", "Workbook", "", "Sheet1!A1:C3"),
                ["Workbook"]);
            try
            {
                manager.ApplyRangeSelection(NamedRangeSelectionTarget.SelectedNameRefersTo, "Sheet2!B2:D8");
                definition.ApplyRangeSelection("Sheet3!C4:E9");

                GetControl<TextBox>(manager, "RefersToBox").Text.Should().Be("Sheet2!B2:D8");
                GetControl<TextBox>(manager, "RefersToBox").SelectionLength.Should().Be("Sheet2!B2:D8".Length);
                DialogSourceTestSupport.GetPrivateField<TextBox>(definition, "_refersToBox").Text.Should().Be("Sheet3!C4:E9");
                DialogSourceTestSupport.GetPrivateField<TextBox>(definition, "_refersToBox").SelectionLength.Should().Be("Sheet3!C4:E9".Length);
            }
            finally
            {
                manager.Close();
                definition.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_WiresNamedRangePickersToCurrentSelection()
    {
        var formulaSource = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");
        var dataSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");
        var source = formulaSource + Environment.NewLine + dataSource;

        formulaSource.Should().Contain("request => ApplyNameDefinitionSelection(dialog, request)");
        dataSource.Should().Contain("request => ApplyNamedRangeSelection(dlg, request)");
        source.Should().Contain("private void ApplyNamedRangeSelection(");
        source.Should().Contain("private void ApplyNameDefinitionSelection(");
        source.Should().Contain("NamedRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("selectedRange => dialog.ApplyRangeSelection(request.Target, FormatWorkbookRange(selectedRange))");
        source.Should().Contain("selectedRange => dialog.ApplyRangeSelection(FormatWorkbookRange(selectedRange))");
    }

    private static T GetControl<T>(NamedRangeDialog dialog, string name)
        where T : class
        => DialogSourceTestSupport.GetPrivateField<T>(dialog, name);

    private static string ReadNamedRangeDialogSource() =>
        DialogSourceTestSupport.ReadHostSources(
            "NamedRangeDialog.xaml.cs",
            "NameDefinitionDialog.cs");

    private static void InvokePrivate(NamedRangeDialog dialog, string methodName)
        => DialogSourceTestSupport.InvokePrivateHandler(dialog, methodName);

    private static Func<IWorkbookCommand, CommandOutcome> CreateCommandBus(Workbook workbook)
    {
        var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
        return command => commandBus.Execute(workbook.Id, command);
    }
}
