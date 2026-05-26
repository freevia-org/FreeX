using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class NamedRangeDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesAccessKeyedFieldsAndCommands()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "NamedRangeDialog.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "NamedRangeDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "GridViewColumn")
            .Select(element => element.Attribute("Header")?.Value)
            .Should()
            .ContainInOrder(["Name", "Value", "Refers To", "Scope", "Comment"]);
    }

    [Fact]
    public void Dialog_ProvidesFilterAndRefersToRangePickerAffordance()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "NamedRangeDialog.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "NamedRangeDialog.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "NamedRangeDialog.xaml"));
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
        source.Should().Contain("MessageBoxButton.YesNo");
        source.Should().Contain("Delete the name");
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
    public void NameDefinitionDialogInvalidInputs_StayOpenAndFocusInvalidField()
    {
        var source = ReadNamedRangeDialogSource();

        source.Should().Contain("Func<string, bool>? isValidRange");
        source.Should().Contain("isValidRange: rangeText => NamedRangeInputParser.TryParseRange(_workbook, rangeText, out _)");
        source.Should().Contain("FocusNameInput();");
        source.Should().Contain("FocusRefersToInput();");
        source.Should().Contain("private void FocusNameInput()");
        source.Should().Contain("private void FocusRefersToInput()");
        source.Should().Contain("_refersToBox.Focus();");
        source.Should().Contain("_refersToBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_refersToBox);");
    }

    [Fact]
    public void NameManagerDialogOpenedFromKeyboard_FocusesNamesListOrNewButton()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "NamedRangeDialog.xaml"));
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

        source.Should().Contain("MessageBox.Show(this, \"Select a named range to edit.\"");
        source.Should().Contain("MessageBox.Show(this, \"Please enter a name.\"");
        source.Should().Contain("MessageBox.Show(this,");
        source.Should().Contain("MessageBox.Show(this, outcome.ErrorMessage ?? \"Could not define named range.\"");
        source.Should().Contain("MessageBox.Show(this, \"Select a named range to delete.\"");
        source.Should().Contain("MessageBox.Show(this, outcome.ErrorMessage ?? \"Could not delete.\"");
    }

    [Fact]
    public void Planner_FiltersWorkbookAndWorksheetScopedNames()
    {
        var workbookName = new NamedRangeViewModel("Sales", "Sheet1!A1:A2", "Sheet1!A1:A2", "Workbook", "");
        var sheetName = new NamedRangeViewModel("Local", "Sheet2!B1:B2", "Sheet2!B1:B2", "Sheet2", "");

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
                var picker = GetPrivateField<Button>(dialog, "_rangePickerButton");
                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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

    private static T GetControl<T>(NamedRangeDialog dialog, string name)
        where T : class
    {
        var field = typeof(NamedRangeDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static string ReadNamedRangeDialogSource() =>
        string.Join(Environment.NewLine, new[]
        {
            "NamedRangeDialog.xaml.cs",
            "NameDefinitionDialog.cs",
            "NamedRangeDialogPlanner.cs"
        }.Select(file => File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", file))));

    private static T GetPrivateField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }

    private static void InvokePrivate(NamedRangeDialog dialog, string methodName)
    {
        var method = typeof(NamedRangeDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
    }

    private static ICommandBus CreateCommandBus(Workbook workbook) =>
        new CommandBus(_ => new TestCommandContext(workbook));

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException("Sheet not found.");
    }
}
