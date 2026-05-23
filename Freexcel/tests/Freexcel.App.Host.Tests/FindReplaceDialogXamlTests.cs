using System.IO;
using System.Xml.Linq;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
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
            .Contain(["Find _All", "_Find Next", "_Replace", "_Replace All", "_Close"]);

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
            .Attribute("Header")?.Value.Should().Be("_Options >>");
        document.Descendants(presentation + "Expander")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "OptionsExpander")
            .Attribute("IsExpanded")?.Value.Should().Be("False");

        AssertComboBoxContainsExactly(document, presentation, xaml, "WithinCombo", ["Workbook", "Sheet"]);
        AssertComboBoxContainsExactly(document, presentation, xaml, "SearchCombo", ["By Rows", "By Columns"]);
        AssertComboBoxContainsExactly(document, presentation, xaml, "LookInCombo", ["Formulas", "Values", "Notes", "Comments"]);
        document.Descendants(presentation + "ComboBoxItem")
            .Select(element => element.Attribute("IsEnabled")?.Value)
            .Should()
            .NotContain("False");
        document.Descendants(presentation + "ComboBoxItem")
            .Select(element => element.Attribute("ToolTip")?.Value ?? string.Empty)
            .Should()
            .NotContain(value => value.Contains("not available yet", StringComparison.OrdinalIgnoreCase));

        AssertCheckBoxContent(document, presentation, xaml, "MatchCaseBox", "Match _case");
        AssertCheckBoxContent(document, presentation, xaml, "MatchEntireBox", "Match entire cell _contents");

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["For_mat...", "Find _All"]);

        AssertNamedButton(document, presentation, xaml, "FindFormatButton", "For_mat...", "FindFormatButton_Click");
        AssertNamedButton(document, presentation, xaml, "ReplaceFindFormatButton", "For_mat...", "FindFormatButton_Click");
        AssertNamedButton(document, presentation, xaml, "ReplaceWithFormatButton", "For_mat...", "ReplaceWithFormatButton_Click");

        AssertNamedElement(document, presentation, xaml, "DataGrid", "FindResultsGrid");
    }

    [Fact]
    public void Dialog_OrdersReplaceBetweenFindNextAndReplaceAll()
    {
        var document = LoadDialogXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttonContents = document.Descendants(presentation + "StackPanel")
            .Last()
            .Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .ToList();

        buttonContents.Should().ContainInOrder("Find _All", "_Find Next", "_Replace", "_Replace All", "_Close");
    }

    [Fact]
    public void ReplaceSingleMatch_ReplacesOnlyTheSelectedValueCell()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(_ => new SimpleCommandContext(workbook));
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new TextValue("foo one"));
        sheet.SetCell(a2, new TextValue("foo two"));

        var replaced = FindReplaceDialogPlanner.ReplaceSingleMatch(
            workbook,
            commandBus,
            new FindResult(a2, "foo two"),
            "foo",
            "bar",
            matchCase: false,
            matchEntireCell: false);

        replaced.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("foo one"));
        sheet.GetCell(a2)!.Value.Should().Be(new TextValue("bar two"));
    }

    [Fact]
    public void Dialog_SourcePreservesHandlersAndSelectsReplaceTabForReplaceMode()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FindReplaceDialog.xaml.cs"));

        source.Should().Contain("private void FindNext_Click");
        source.Should().Contain("private void ReplaceAll_Click");
        source.Should().Contain("private void FindAll_Click");
        source.Should().Contain("FindReplaceTabs.SelectedItem = ReplaceTab");
        source.Should().Contain("CreateFindOptions()");
        source.Should().Contain("RequiredFormat: _findFormatDiff");
        source.Should().Contain("new FormatCellsDialog(baseStyle, FormatCellsDialogTab.Font)");
        source.Should().Contain("FindFormatButton_Click");
        source.Should().Contain("ReplaceWithFormatButton_Click");
        source.Should().Contain("OptionsExpander_Expanded");
        source.Should().Contain("OptionsExpander.Header = \"_Options <<\"");
        source.Should().Contain("OptionsExpander_Collapsed");
        source.Should().Contain("OptionsExpander.Header = \"_Options >>\"");
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

    private static void AssertNamedButton(
        XDocument document,
        XNamespace presentation,
        XNamespace xaml,
        string controlName,
        string content,
        string clickHandler)
    {
        var button = document.Descendants(presentation + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value == controlName);

        button.Attribute("Content")?.Value.Should().Be(content);
        button.Attribute("Click")?.Value.Should().Be(clickHandler);
    }

    private static void AssertComboBoxContainsExactly(
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
            .Equal(values);
    }

}

file sealed class SimpleCommandContext : ICommandContext
{
    public Workbook Workbook { get; }

    public SimpleCommandContext(Workbook workbook) => Workbook = workbook;

    public Sheet GetSheet(SheetId sheetId) =>
        Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
}
