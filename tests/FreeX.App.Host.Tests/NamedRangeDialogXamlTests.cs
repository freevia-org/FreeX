using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
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
        source.Should().Contain("if (NamesList.SelectedItem is not NamedRangeViewModel)");
        source.Should().Contain("EditButton_Click(sender, e);");
        source.Should().Contain("e.Handled = true;");
        source.IndexOf("if (NamesList.SelectedItem is not NamedRangeViewModel)", StringComparison.Ordinal)
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

        var filterItems = document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FilterBox")
            .Descendants(presentation + "ComboBoxItem")
            .Select(element => element.Attribute("Content")?.Value)
            .ToArray();

        filterItems.Should().ContainInOrder(
            "All names",
            "Names scoped to workbook",
            "Names scoped to worksheet",
            "Names with errors",
            "Names without errors");
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
        source.Should().Contain("EditButton.IsEnabled = hasSelection");
        source.Should().Contain("DeleteButton.IsEnabled = hasSelection");
        source.Should().Contain("DialogMessageHelper.AskYesNo(this,");
        source.Should().Contain("UiText.Format(\"NamedRange_DeleteConfirmation\", vm.Name)");
        source.Should().Contain("RefersToPickerButton_Click");
        source.Should().Contain("RefersToBox.SelectAll()");
        source.Should().NotContain("IsEnabled = false");
        source.Should().Contain("GetScopeOptions");
        source.Should().Contain("NamedRangeMetadata");
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
        source.Should().Contain("isValidRange: rangeText => NamedRangeInputParser.TryParseRange(_workbook, rangeText, out _)");
        source.Should().Contain("validateName: _workbook.ValidateNamedRangeName");
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
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, UiText.Get(\"NamedRange_NameRequiredMessage\")");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get(\"NamedRange_DefineFailedMessage\")");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, UiText.Get(\"NamedRange_SelectDeleteMessage\")");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get(\"NamedRange_DeleteFailedMessage\")");
    }

    [Fact]
    public void NameManager_UsesSharedPresentationPlannerAndParser()
    {
        var hostSource = ReadNamedRangeDialogSource();
        var plannerSource = DialogSourceTestSupport.ReadPresentationSources("NamedRanges", "NamedRangeDialogPlanner.cs");
        var parserSource = DialogSourceTestSupport.ReadPresentationSources("NamedRanges", "NamedRangeInputParser.cs");

        hostSource.Should().Contain("using FreeX.App.Presentation.NamedRanges;");
        hostSource.Should().Contain("NamedRangeDialogPlanner.FilterItems(_items, selected)");
        hostSource.Should().Contain("NamedRangeInputParser.TryParseRange(_workbook, rangeText, out _)");
        plannerSource.Should().Contain("public static class NamedRangeDialogPlanner");
        parserSource.Should().Contain("public static class NamedRangeInputParser");
    }

    [Fact]
    public void Planner_FiltersWorkbookAndWorksheetScopedNames()
    {
        var workbookName = new NamedRangeViewModel("Sales", "Sheet1!A1:A2", "Sheet1!A1:A2", "Workbook", "");
        var sheetName = new NamedRangeViewModel(
            "Local", "Sheet2!B1:B2", "Sheet2!B1:B2", "Sheet2", "", scopeSheetId: new SheetId(Guid.NewGuid()));

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
        var validName = new NamedRangeViewModel("Sales", "Sheet1!A1:A2", "Sheet1!A1:A2", "Workbook", "");
        var errorValueName = new NamedRangeViewModel("BadValue", "#REF!", "Sheet1!A1:A2", "Workbook", "");
        var errorRefersToName = new NamedRangeViewModel("BadRef", "Sheet1!A1:A2", "#NAME?", "Workbook", "");

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

    private static ICommandBus CreateCommandBus(Workbook workbook) =>
        new CommandBus(_ => new TestCommandContext(workbook));
}
