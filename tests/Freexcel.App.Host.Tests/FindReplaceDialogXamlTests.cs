using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
        AssertNamedButton(document, presentation, xaml, "FindChooseFormatFromCellButton", "Choose From _Cell...", "ChooseFindFormatFromCellButton_Click");
        AssertNamedButton(document, presentation, xaml, "ReplaceFindChooseFormatFromCellButton", "Choose From _Cell...", "ChooseFindFormatFromCellButton_Click");
        AssertNamedButton(document, presentation, xaml, "ReplaceWithChooseFormatFromCellButton", "Choose From _Cell...", "ChooseReplaceWithFormatFromCellButton_Click");
        AssertNamedButton(document, presentation, xaml, "FindClearFormatButton", "_Clear", "FindClearFormatButton_Click");
        AssertNamedButton(document, presentation, xaml, "ReplaceFindClearFormatButton", "_Clear", "FindClearFormatButton_Click");
        AssertNamedButton(document, presentation, xaml, "ReplaceWithClearFormatButton", "_Clear", "ReplaceWithClearFormatButton_Click");
        AssertNamedElementHasAttribute(document, presentation, xaml, "Button", "FindClearFormatButton", "Visibility", "Collapsed");
        AssertNamedElementHasAttribute(document, presentation, xaml, "Button", "ReplaceFindClearFormatButton", "Visibility", "Collapsed");
        AssertNamedElementHasAttribute(document, presentation, xaml, "Button", "ReplaceWithClearFormatButton", "Visibility", "Collapsed");

        AssertNamedElement(document, presentation, xaml, "DataGrid", "FindResultsGrid");
    }

    [Fact]
    public void Dialog_FindAllResultsUseExcelLikeResultColumns()
    {
        var document = LoadDialogXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var grid = document.Descendants(presentation + "DataGrid")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "FindResultsGrid");

        grid.Attribute("SelectionChanged")?.Value.Should().Be("FindResultsGrid_SelectionChanged");
        grid.Descendants(presentation + "DataGridTextColumn")
            .Select(element => element.Attribute("Header")?.Value)
            .Should()
            .Equal("Book", "Sheet", "Name", "Cell", "Value", "Formula");
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
    public void BuildFindResultRows_ProjectsWorkbookSheetNameCellValueAndFormula()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Budget");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, Cell.FromFormula("=SUM(B2:B3)"));
        sheet.SetCell(b2, new TextValue("Budget match"));
        workbook.DefineNamedRange("InputCell", new GridRange(b2, b2));

        var rows = FindReplaceDialogPlanner.BuildFindResultRows(
            workbook,
            [
                new FindResult(a1, "=SUM(B2:B3)"),
                new FindResult(b2, "Budget match")
            ]);

        rows.Should().Equal(
            new FindResultRow("Book1", "Budget", "", a1, "A1", "=SUM(B2:B3)", "=SUM(B2:B3)"),
            new FindResultRow("Book1", "Budget", "InputCell", b2, "B2", "Budget match", ""));
    }

    [Fact]
    public void CreateFormatDiffFromCell_CapturesSelectedCellStyle()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Budget");
        var address = new CellAddress(sheet.Id, 2, 2);
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(1, 2, 3),
            NumberFormat = "$#,##0.00"
        });
        sheet.SetCell(address, Cell.FromValue(new TextValue("Budget")));
        sheet.GetCell(address)!.StyleId = styleId;

        var diff = FindReplaceDialogPlanner.CreateFormatDiffFromCell(workbook, address);

        diff.Should().NotBeNull();
        diff!.Bold.Should().BeTrue();
        diff.FillColor.Should().Be(new CellColor(1, 2, 3));
        diff.NumberFormat.Should().Be("$#,##0.00");
    }

    [Fact]
    public void ChooseFormatFromCell_UsesActiveWorksheetSelectionWhenNoResultRowIsSelected()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Budget");
            var address = new CellAddress(sheet.Id, 2, 2);
            var styleId = workbook.RegisterStyle(new CellStyle
            {
                Bold = true,
                FillColor = new CellColor(10, 20, 30)
            });
            sheet.SetCell(address, Cell.FromValue(new TextValue("Budget")));
            sheet.GetCell(address)!.StyleId = styleId;
            var commandBus = new CommandBus(_ => new SimpleCommandContext(workbook));
            var dialog = new FindReplaceDialog(
                () => workbook,
                commandBus,
                _ => { },
                getCurrentSheetId: () => sheet.Id,
                getActiveSelectionCell: () => address);
            dialog.Show();
            try
            {
                InvokePrivate(dialog, "ChooseFindFormatFromCellButton_Click");

                GetPrivateField<StyleDiff?>(dialog, "_findFormatDiff").Should().NotBeNull();
                GetPrivateControl<TextBlock>(dialog, "StatusLabel").Text.Should().Be("Format chosen from active worksheet cell.");
            }
            finally
            {
                dialog.Close();
            }
        });
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
        source.Should().Contain("ChooseFindFormatFromCellButton_Click");
        source.Should().Contain("ChooseReplaceWithFormatFromCellButton_Click");
        source.Should().Contain("PickFormatFromCell");
        source.Should().Contain("CreateFormatDiffFromCell(_getWorkbook(), address.Value)");
        source.Should().Contain("_getActiveSelectionCell");
        source.Should().Contain("FindClearFormatButton_Click");
        source.Should().Contain("ReplaceWithClearFormatButton_Click");
        source.Should().Contain("UpdateFormatStateButtons");
        source.Should().Contain("Format Set...");
        source.Should().Contain("replacementFormat: _replaceFormatDiff");
        source.Should().Contain("FindResultsGrid_SelectionChanged");
        source.Should().Contain("_navigateTo(row.Address)");
        source.Should().Contain("BuildFindResultRows(_getWorkbook(), _results)");
        source.Should().Contain("OptionsExpander_Expanded");
        source.Should().Contain("OptionsExpander.Header = \"_Options <<\"");
        source.Should().Contain("OptionsExpander_Collapsed");
        source.Should().Contain("OptionsExpander.Header = \"_Options >>\"");
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesFindOrReplaceSearchBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FindReplaceDialog.xaml.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("var target = FindReplaceTabs.SelectedItem == ReplaceTab ? ReplaceFindBox : FindBox;");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    private static XDocument LoadDialogXaml() =>
        XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FindReplaceDialog.xaml"));

    private static void InvokePrivate(FindReplaceDialog dialog, string methodName)
    {
        var method = typeof(FindReplaceDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
    }

    private static T GetPrivateField<T>(FindReplaceDialog dialog, string fieldName)
    {
        var field = typeof(FindReplaceDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeAssignableTo<T>().Subject;
    }

    private static T GetPrivateControl<T>(FindReplaceDialog dialog, string fieldName)
        where T : class
    {
        var field = typeof(FindReplaceDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

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

    private static void AssertNamedElementHasAttribute(
        XDocument document,
        XNamespace presentation,
        XNamespace xaml,
        string elementName,
        string controlName,
        string attributeName,
        string value)
    {
        document.Descendants(presentation + elementName)
            .Single(element => element.Attribute(xaml + "Name")?.Value == controlName)
            .Attribute(attributeName)?.Value.Should().Be(value);
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
