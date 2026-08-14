using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FindReplaceDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesAccessKeyedFieldsOptionsAndButtons()
    {
        var document = LoadDialogXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertLabelTargets(document, presentation, "FindWhatLabel", "FindBox");
        AssertLabelTargets(document, presentation, "ReplaceWithLabel", "ReplaceBox");
        AssertLabelTargets(document, presentation, "WithinLabel", "WithinCombo");
        AssertLabelTargets(document, presentation, "SearchLabel", "SearchCombo");
        AssertLabelTargets(document, presentation, "LookInLabel", "LookInCombo");

        WithDialog(dialog =>
        {
            GetPrivateControl<Label>(dialog, "FindWhatLabel").Content.Should().Be("_Find what:");
            GetPrivateControl<Label>(dialog, "ReplaceWithLabel").Content.Should().Be("_Replace with:");
            GetPrivateControl<CheckBox>(dialog, "MatchCaseBox").Content.Should().Be("Match _case");
            GetPrivateControl<CheckBox>(dialog, "MatchEntireBox").Content.Should().Be("Match entire cell _contents");
            new[] { "FindAllBtn", "FindNextBtn", "ReplaceBtn", "ReplaceAllBtn", "CloseBtn" }
                .Select(name => GetPrivateControl<Button>(dialog, name).Content)
                .Should()
                .Equal("Find _All", "_Find Next", "_Replace", "_Replace All", "_Close");
        });

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string name, string target)
        {
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var label = document
                .Descendants(presentation + "Label")
                .Single(element =>
                    element.Attribute(xaml + "Name")?.Value == name &&
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

        WithDialog(dialog =>
        {
            GetPrivateControl<TabItem>(dialog, "FindTab").Header.Should().Be("_Find");
            GetPrivateControl<TabItem>(dialog, "ReplaceTab").Header.Should().Be("_Replace");
        });

        AssertNamedElement(document, presentation, xaml, "TextBox", "FindBox");
        AssertNamedElement(document, presentation, xaml, "TextBox", "ReplaceBox");
    }

    [Fact]
    public void Dialog_UsesSharedFindReplaceLayoutMetrics()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FindReplaceDialog.xaml");

        xaml.Should().Contain("FindReplaceDialog.FindReplaceFieldLabelColumnWidth");
        xaml.Should().NotContain("FindReplaceDialogPlanner.OptionsHeaderMinimumWidth");
        xaml.Should().Contain("FindReplaceDialogPlanner.FieldMinWidth");
        xaml.Should().Contain("FindReplaceDialogPlanner.FormatButtonWidth");
        xaml.Should().Contain("FindReplaceDialogPlanner.ClearFormatButtonWidth");
        xaml.Should().Contain("FindReplaceDialogPlanner.ChooseFormatButtonWidth");
        xaml.Should().Contain("FindReplaceDialogPlanner.ResultsMinimumHeight");
        xaml.Should().Contain("FindReplaceDialog.FindReplaceResultBookColumnWidth");
        xaml.Should().Contain("FindReplaceDialog.FindReplaceResultSheetColumnWidth");
        xaml.Should().Contain("FindReplaceDialog.FindReplaceResultNameColumnWidth");
        xaml.Should().Contain("FindReplaceDialog.FindReplaceResultCellColumnWidth");
    }

    [Fact]
    public void Dialog_SharesFindWhatTextAcrossFindAndReplaceTabs()
    {
        var document = LoadDialogXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        AssertNamedElementHasAttribute(document, presentation, xaml, "TextBox", "FindBox", "TextChanged", "FindBox_TextChanged");
        AssertNamedElementHasAttribute(document, presentation, xaml, "TextBox", "ReplaceFindBox", "TextChanged", "FindBox_TextChanged");

        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                _ => { });
            dialog.Show();
            try
            {
                var findBox = GetPrivateControl<TextBox>(dialog, "FindBox");
                var replaceFindBox = GetPrivateControl<TextBox>(dialog, "ReplaceFindBox");

                findBox.Text = "budget";
                replaceFindBox.Text.Should().Be("budget");

                replaceFindBox.Text = "forecast";
                findBox.Text.Should().Be("forecast");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Dialog_ExposesExcelLikeOptionsAndFindAllSurface()
    {
        var document = LoadDialogXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        document.Descendants(presentation + "Expander")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "OptionsExpander")
            .Attribute("IsExpanded")?.Value.Should().Be("False");

        AssertNamedElementHasAttribute(document, presentation, xaml, "ComboBox", "LookInCombo", "SelectedIndex", "0");

        AssertNamedButton(document, presentation, xaml, "FindFormatButton", "FindFormatButton_Click");
        AssertNamedButton(document, presentation, xaml, "ReplaceFindFormatButton", "FindFormatButton_Click");
        AssertNamedButton(document, presentation, xaml, "ReplaceWithFormatButton", "ReplaceWithFormatButton_Click");
        AssertNamedButton(document, presentation, xaml, "FindChooseFormatFromCellButton", "ChooseFindFormatFromCellButton_Click");
        AssertNamedButton(document, presentation, xaml, "ReplaceFindChooseFormatFromCellButton", "ChooseFindFormatFromCellButton_Click");
        AssertNamedButton(document, presentation, xaml, "ReplaceWithChooseFormatFromCellButton", "ChooseReplaceWithFormatFromCellButton_Click");
        AssertNamedButton(document, presentation, xaml, "FindClearFormatButton", "FindClearFormatButton_Click");
        AssertNamedButton(document, presentation, xaml, "ReplaceFindClearFormatButton", "FindClearFormatButton_Click");
        AssertNamedButton(document, presentation, xaml, "ReplaceWithClearFormatButton", "ReplaceWithClearFormatButton_Click");
        AssertNamedElementHasAttribute(document, presentation, xaml, "Button", "FindClearFormatButton", "Visibility", "Collapsed");
        AssertNamedElementHasAttribute(document, presentation, xaml, "Button", "ReplaceFindClearFormatButton", "Visibility", "Collapsed");
        AssertNamedElementHasAttribute(document, presentation, xaml, "Button", "ReplaceWithClearFormatButton", "Visibility", "Collapsed");

        AssertNamedElement(document, presentation, xaml, "DataGrid", "FindResultsGrid");

        WithDialog(dialog =>
        {
            GetPrivateControl<Expander>(dialog, "OptionsExpander").Header.Should().Be("_Options >>");
            GetPrivateControl<ComboBox>(dialog, "WithinCombo").Items.Cast<string>()
                .Should().Equal("Sheet", "Workbook");
            GetPrivateControl<ComboBox>(dialog, "SearchCombo").Items.Cast<string>()
                .Should().Equal("By Rows", "By Columns");
            GetPrivateControl<ComboBox>(dialog, "LookInCombo").Items.Cast<string>()
                .Should().Equal("Formulas", "Values", "Notes", "Comments");
            GetPrivateControl<CheckBox>(dialog, "MatchCaseBox").Content.Should().Be("Match _case");
            GetPrivateControl<CheckBox>(dialog, "MatchEntireBox").Content.Should().Be("Match entire cell _contents");
            GetPrivateControl<Button>(dialog, "FindFormatButton").Content.Should().Be("For_mat...");
            GetPrivateControl<Button>(dialog, "FindChooseFormatFromCellButton").Content.Should().Be("Choose From _Cell...");
            GetPrivateControl<Button>(dialog, "FindClearFormatButton").Content.Should().Be("_Clear");
        });
    }

    [Fact]
    public void Dialog_DefaultsWithinScopeToSheet()
    {
        WithDialog(dialog => GetPrivateControl<ComboBox>(dialog, "WithinCombo").SelectedIndex.Should().Be(0));
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
        WithDialog(dialog => GetPrivateControl<DataGrid>(dialog, "FindResultsGrid").Columns
            .Select(column => column.Header)
            .Should()
            .Equal("Book", "Sheet", "Name", "Cell", "Value", "Formula"));
    }

    [Fact]
    public void Dialog_OrdersReplaceBetweenFindNextAndReplaceAll()
    {
        var document = LoadDialogXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var buttonNames = document.Descendants(presentation + "StackPanel")
            .Last()
            .Descendants(presentation + "Button")
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .ToList();

        buttonNames.Should().ContainInOrder("FindAllBtn", "FindNextBtn", "ReplaceBtn", "ReplaceAllBtn", "CloseBtn");
    }

    [Fact]
    public void FindNextButton_IsDefaultAction()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FindReplaceDialog.xaml");

        xaml.Should().Contain("<Button x:Name=\"FindNextBtn\"");
        xaml.Should().Contain("x:Name=\"FindNextBtn\" Width=\"80\" Margin=\"0,0,8,0\" IsDefault=\"True\"");
        WithDialog(dialog => GetPrivateControl<Button>(dialog, "FindNextBtn").Content.Should().Be("_Find Next"));
    }

    [Fact]
    public void Dialog_ShowsReplaceActionsOnlyOnReplaceTab()
    {
        var document = LoadDialogXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        document.Descendants(presentation + "TabControl")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "FindReplaceTabs")
            .Attribute("SelectionChanged")?.Value.Should().Be("FindReplaceTabs_SelectionChanged");

        AssertNamedElementHasAttribute(document, presentation, xaml, "Button", "ReplaceBtn", "Visibility", "Collapsed");
        AssertNamedElementHasAttribute(document, presentation, xaml, "Button", "ReplaceAllBtn", "Visibility", "Collapsed");

        var source = ReadFindReplaceDialogSource();
        source.Should().Contain("private void FindReplaceTabs_SelectionChanged");
        source.Should().Contain("UpdateReplaceButtonVisibility();");
        source.Should().Contain("var visibility = FindReplaceDialogPlanner.ShowsReplaceCommands(OpenMode)");
        source.Should().Contain("? Visibility.Visible");
        source.Should().Contain(": Visibility.Collapsed;");
    }

    [Fact]
    public void ReplaceSingleMatch_ReplacesOnlyTheSelectedValueCell()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
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
    public void ReplaceSingleMatch_CanReplaceFormulaTextWhenLookInFormulas()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "SUM(B1:B5)");

        var replaced = FindReplaceDialogPlanner.ReplaceSingleMatch(
            workbook,
            commandBus,
            new FindResult(a1, "SUM(B1:B5)"),
            "SUM",
            "MAX",
            matchCase: false,
            matchEntireCell: false,
            lookIn: FindLookIn.Formulas);

        replaced.Should().BeTrue();
        sheet.GetCell(a1)!.FormulaText.Should().Be("MAX(B1:B5)");
        commandBus.Undo(workbook.Id).Success.Should().BeTrue();
        sheet.GetCell(a1)!.FormulaText.Should().Be("SUM(B1:B5)");
    }

    [Fact]
    public void DialogReplaceAll_UpdatesWorkbookRefreshesResultsAndNotifiesHost()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            sheet.SetCell(a1, new TextValue("foo one"));
            sheet.SetCell(b1, new TextValue("foo two"));
            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var refreshCount = 0;
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                _ => { },
                replaceMode: true,
                getCurrentSheetId: () => sheet.Id,
                onWorkbookChanged: () => refreshCount++);
            dialog.Show();
            try
            {
                GetPrivateControl<TextBox>(dialog, "ReplaceFindBox").Text = "foo";
                GetPrivateControl<TextBox>(dialog, "ReplaceBox").Text = "bar";

                InvokePrivate(dialog, "FindAll_Click");
                GetPrivateControl<DataGrid>(dialog, "FindResultsGrid").Items.Count.Should().Be(2);

                InvokePrivate(dialog, "ReplaceAll_Click");

                refreshCount.Should().Be(1);
                sheet.GetCell(a1)!.Value.Should().Be(new TextValue("bar one"));
                sheet.GetCell(b1)!.Value.Should().Be(new TextValue("bar two"));
                GetPrivateControl<DataGrid>(dialog, "FindResultsGrid").Items.Count.Should().Be(0);
                GetPrivateControl<TextBlock>(dialog, "StatusLabel").Text.Should().Be("Replaced 2 cell(s).");
                commandBus.Undo(workbook.Id).Success.Should().BeTrue();
                sheet.GetCell(a1)!.Value.Should().Be(new TextValue("foo one"));
                sheet.GetCell(b1)!.Value.Should().Be(new TextValue("foo two"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogReplaceOne_UpdatesCurrentMatchRefreshesResultsAndAdvances()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            sheet.SetCell(a1, new TextValue("foo one"));
            sheet.SetCell(b1, new TextValue("foo two"));
            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var navigated = new List<CellAddress>();
            var refreshCount = 0;
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                navigated.Add,
                replaceMode: true,
                getCurrentSheetId: () => sheet.Id,
                onWorkbookChanged: () => refreshCount++);
            dialog.Show();
            try
            {
                GetPrivateControl<TextBox>(dialog, "ReplaceFindBox").Text = "foo";
                GetPrivateControl<TextBox>(dialog, "ReplaceBox").Text = "bar";

                InvokePrivate(dialog, "FindNext_Click");
                InvokePrivate(dialog, "Replace_Click");

                refreshCount.Should().Be(1);
                sheet.GetCell(a1)!.Value.Should().Be(new TextValue("bar one"));
                sheet.GetCell(b1)!.Value.Should().Be(new TextValue("foo two"));
                GetPrivateControl<DataGrid>(dialog, "FindResultsGrid").Items.Count.Should().Be(1);
                navigated.Should().ContainInOrder(a1, b1);
                GetPrivateControl<TextBlock>(dialog, "StatusLabel").Text.Should().Be("Match 1 of 1");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void TryReplaceSingleMatch_ReturnsCommandFailureInsteadOfReportingReplacement()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new RejectingCommandBus("The sheet is protected.");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("foo"));

        var result = FindReplaceDialogPlanner.TryReplaceSingleMatch(
            workbook,
            commandBus,
            new FindResult(a1, "foo"),
            "foo",
            "bar",
            matchCase: false,
            matchEntireCell: false);

        result.Replaced.Should().BeFalse();
        result.Failure.Should().Be(new CommandOutcome(false, "The sheet is protected."));
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("foo"));
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
            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                _ => { },
                getCurrentSheetId: () => sheet.Id,
                getActiveSelectionCell: () => address);
            dialog.Show();
            try
            {
                InvokePrivate(dialog, "ChooseFindFormatFromCellButton_Click");

                DialogSourceTestSupport.GetPrivateField<StyleDiff>(dialog, "_findFormatDiff").Should().NotBeNull();
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
        var source = ReadFindReplaceDialogSource();

        source.Should().Contain("private void FindNext_Click");
        source.Should().Contain("private void ReplaceAll_Click");
        source.Should().Contain("private void FindAll_Click");
        source.Should().Contain("FindReplaceTabs.SelectedItem = ReplaceTab");
        source.Should().Contain("CreateFindOptions()");
        source.Should().Contain("requiredFormat: _findFormatDiff");
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
        source.Should().Contain("DialogText(FindReplaceDialogText.FormatSetButton)");
        source.Should().Contain("replacementFormat: _replaceFormatDiff");
        source.Should().Contain("FindResultsGrid_SelectionChanged");
        source.Should().Contain("_navigateTo(row.Address)");
        source.Should().Contain("BuildFindResultRows(_getWorkbook(), _results)");
        source.Should().Contain("OptionsExpander_Expanded");
        source.Should().Contain("OptionsExpander.Header = DialogText(FindReplaceDialogText.OptionsExpanded)");
        source.Should().Contain("OptionsExpander_Collapsed");
        source.Should().Contain("OptionsExpander.Header = DialogText(FindReplaceDialogText.Options)");
        source.Should().Contain("ApplySharedDialogSchema();");
        source.Should().Contain("FindReplaceDialogSchema.WithinChoices");
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesFindOrReplaceSearchBox()
    {
        var source = ReadFindReplaceDialogSource();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FindReplaceDialogPlanner.ShowsReplaceCommands(OpenMode) ? ReplaceFindBox : FindBox;");
        source.Should().Contain("DialogFocus.FocusAndSelect(ResolveSearchBox());");
    }

    [Fact]
    public void DialogTabSwitch_FocusesSearchBoxForSelectedTab()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                _ => { });
            dialog.Show();
            try
            {
                var tabs = GetPrivateControl<TabControl>(dialog, "FindReplaceTabs");
                var findBox = GetPrivateControl<TextBox>(dialog, "FindBox");
                var replaceTab = GetPrivateControl<TabItem>(dialog, "ReplaceTab");
                var replaceFindBox = GetPrivateControl<TextBox>(dialog, "ReplaceFindBox");
                var findTab = GetPrivateControl<TabItem>(dialog, "FindTab");

                Keyboard.FocusedElement.Should().BeSameAs(findBox);

                tabs.SelectedItem = replaceTab;

                Keyboard.FocusedElement.Should().BeSameAs(replaceFindBox);

                tabs.SelectedItem = findTab;

                Keyboard.FocusedElement.Should().BeSameAs(findBox);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogBlankSearch_ShowsOwnedWarningAndFocusesFindWhatBox()
    {
        var source = ReadFindReplaceDialogSource();

        source.Should().Contain("ShowBlankSearchWarning()");
        source.Should().Contain("private bool ShowBlankSearchWarning()");
        source.Should().Contain("DialogText(FindReplaceDialogText.FindWhatRequired)");
        source.Should().Contain("FocusSearchBox();");
        source.Should().Contain("private void FocusSearchBox()");
        source.Should().Contain("FindReplaceDialogPlanner.ShowsReplaceCommands(OpenMode) ? ReplaceFindBox : FindBox;");
        source.Should().Contain("DialogFocus.FocusAndSelect(ResolveSearchBox());");
    }

    private static XDocument LoadDialogXaml() =>
        XamlLocalizationTestHelper.LoadLocalizedXaml("FindReplaceDialog.xaml");

    private static void InvokePrivate(FindReplaceDialog dialog, string methodName)
        => DialogSourceTestSupport.InvokePrivateHandler(dialog, methodName);

    private static string ReadFindReplaceDialogSource() =>
        DialogSourceTestSupport.ReadHostSources("FindReplaceDialog.xaml.cs");

    private static T GetPrivateControl<T>(FindReplaceDialog dialog, string fieldName)
        where T : class
        => DialogSourceTestSupport.GetPrivateField<T>(dialog, fieldName);

    private static void WithDialog(Action<FindReplaceDialog> assertion)
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                _ => { });
            try
            {
                assertion(dialog);
            }
            finally
            {
                dialog.Close();
            }
        });
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

    private static void AssertNamedButton(
        XDocument document,
        XNamespace presentation,
        XNamespace xaml,
        string controlName,
        string clickHandler)
    {
        var button = document.Descendants(presentation + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value == controlName);

        button.Attribute("Click")?.Value.Should().Be(clickHandler);
    }

}

file sealed class RejectingCommandBus(string message) : ICommandBus
{
    public CommandOutcome Execute(WorkbookId workbookId, IWorkbookCommand command) => new(false, message);
    public CommandOutcome ExecuteRepeatable(WorkbookId workbookId, Func<IWorkbookCommand> commandFactory) => new(false, message);
    public CommandOutcome Undo(WorkbookId workbookId) => new(false, message);
    public CommandOutcome Redo(WorkbookId workbookId) => new(false, message);
    public bool CanUndo(WorkbookId workbookId) => false;
    public bool CanRedo(WorkbookId workbookId) => false;
    public CommandOutcome RepeatLast(WorkbookId workbookId) => new(false, message);
    public bool CanRepeat(WorkbookId workbookId) => false;
    public int GetUndoStackDepth(WorkbookId workbookId) => 0;
}
