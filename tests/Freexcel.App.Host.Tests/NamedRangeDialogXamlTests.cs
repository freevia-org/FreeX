using System.IO;
using System.Xml.Linq;
using FluentAssertions;

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "NamedRangeDialog.xaml.cs"));

        source.Should().Contain("NameDefinitionDialog");
        source.Should().Contain("_scopeBox");
        source.Should().Contain("_commentBox");
        source.Should().Contain("_refersToBox");
        source.Should().Contain("_rangePickerButton");
        source.Should().Contain("_rangePickerButton.Click");
        source.Should().Contain("_refersToBox.SelectAll");
        source.Should().Contain("RefersToPickerButton.IsEnabled = NamesList.SelectedItem is NamedRangeViewModel");
        source.Should().Contain("RefersToPickerButton_Click");
        source.Should().Contain("RefersToBox.SelectAll()");
        source.Should().NotContain("IsEnabled = false");
        source.Should().Contain("GetScopeOptions");
        source.Should().Contain("NamedRangeMetadata");
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
}
