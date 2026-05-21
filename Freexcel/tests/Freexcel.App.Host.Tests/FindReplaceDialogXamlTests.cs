using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class FindReplaceDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesAccessKeyedFieldsOptionsAndButtons()
    {
        var document = LoadDialogXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertLabelTargets(document, presentation, "_Find what:", "FindBox");
        AssertLabelTargets(document, presentation, "_Replace with:", "ReplaceBox");
        AssertLabelTargets(document, presentation, "_Within:", "WithinCombo");
        AssertLabelTargets(document, presentation, "_Search:", "SearchCombo");
        AssertLabelTargets(document, presentation, "_Look in:", "LookInCombo");

        document.Descendants(presentation + "CheckBox")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["Match _case", "Match entire cell _contents"]);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["Find All", "_Find Next", "_Replace All", "_Close"]);

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element =>
                    element.Attribute("Content")?.Value == content &&
                    element.Attribute("Target")?.Value == $"{{Binding ElementName={target}}}");

            label.Should().NotBeNull();
        }
    }

    [Fact]
    public void Dialog_ExposesExcelLikeFindReplaceTabs()
    {
        var document = LoadDialogXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var tabControl = document.Descendants(presentation + "TabControl")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "FindReplaceTabs");

        tabControl.Descendants(presentation + "TabItem")
            .Select(element => element.Attribute("Header")?.Value)
            .Should()
            .Contain(["_Find", "_Replace"]);

        AssertNamedElement(document, presentation, xaml, "TextBox", "FindBox");
        AssertNamedElement(document, presentation, xaml, "TextBox", "ReplaceBox");
    }

    [Fact]
    public void Dialog_ExposesExcelLikeOptionsAndFindAllSurface()
    {
        var document = LoadDialogXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        document.Descendants(presentation + "Expander")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "OptionsExpander")
            .Attribute("Header")?.Value.Should().Be("Options >>");

        AssertComboBoxContains(document, presentation, xaml, "WithinCombo", ["Sheet", "Workbook"]);
        AssertComboBoxContains(document, presentation, xaml, "SearchCombo", ["By Rows", "By Columns"]);
        AssertComboBoxContains(document, presentation, xaml, "LookInCombo", ["Formulas", "Values", "Comments"]);

        AssertCheckBoxContent(document, presentation, xaml, "MatchCaseBox", "Match _case");
        AssertCheckBoxContent(document, presentation, xaml, "MatchEntireBox", "Match entire cell _contents");

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["Format...", "Find All"]);

        AssertNamedElement(document, presentation, xaml, "DataGrid", "FindResultsGrid");
    }

    [Fact]
    public void Dialog_SourcePreservesHandlersAndSelectsReplaceTabForReplaceMode()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FindReplaceDialog.xaml.cs"));

        source.Should().Contain("private void FindNext_Click");
        source.Should().Contain("private void ReplaceAll_Click");
        source.Should().Contain("private void FindAll_Click");
        source.Should().Contain("FindReplaceTabs.SelectedItem = ReplaceTab");
    }

    private static XDocument LoadDialogXaml() =>
        XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FindReplaceDialog.xaml"));

    private static void AssertNamedElement(
        XDocument document,
        XNamespace presentation,
        XNamespace xaml,
        string elementName,
        string controlName)
    {
        document.Descendants(presentation + elementName)
            .Single(element => element.Attribute(xaml + "Name")?.Value == controlName);
    }

    private static void AssertCheckBoxContent(
        XDocument document,
        XNamespace presentation,
        XNamespace xaml,
        string controlName,
        string content)
    {
        document.Descendants(presentation + "CheckBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == controlName)
            .Attribute("Content")?.Value.Should().Be(content);
    }

    private static void AssertComboBoxContains(
        XDocument document,
        XNamespace presentation,
        XNamespace xaml,
        string controlName,
        IReadOnlyCollection<string> values)
    {
        var combo = document.Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == controlName);

        combo.Descendants(presentation + "ComboBoxItem")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(values);
    }
}
