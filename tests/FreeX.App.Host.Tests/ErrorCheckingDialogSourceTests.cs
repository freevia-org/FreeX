using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ErrorCheckingDialogSourceTests
{
    [Fact]
    public void DialogListAndHeaderUseIssueWordingForMixedAuditResults()
    {
        var source = ReadErrorCheckingDialogSource();

        source.Should().Contain("UiText.Format(ErrorCheckingDialogPlanner.IssueCountHeaderKey, _issues.Count)");
        source.Should().Contain("Header = UiText.Get(ErrorCheckingDialogPlanner.IssueColumnHeaderKey)");
        source.Should().NotContain("error(s) found.");
        source.Should().NotContain("Header = \"Error\"");
        UiText.Get("ErrorChecking_IssueColumnHeader").Should().Be("Issue");
    }

    [Fact]
    public void ErrorCheckingEmptyResultMessageUsesIssueWording()
    {
        var source = ReadMainWindowFormulaCommandsSource();

        source.Should().Contain("UiText.Get(\"MainWindowMessage_ErrorCheckingNoIssues\")");
        UiText.Get("MainWindowMessage_ErrorCheckingNoIssues").Should().Be("No issues found.");
        source.Should().NotContain("No errors found.");
    }

    [Fact]
    public void ErrorCheckingDialog_ExposesOptionsCallbackButton()
    {
        var dialogSource = ReadErrorCheckingDialogSource();
        var formulaSource = ReadMainWindowFormulaCommandsSource();
        var backstageSource = ReadMainWindowBackstageSource();

        dialogSource.Should().Contain("Action? openOptions");
        dialogSource.Should().Contain("Content = UiText.Get(ErrorCheckingDialogPlanner.OptionsButtonKey)");
        dialogSource.Should().Contain("_openOptions?.Invoke()");
        formulaSource.Should().Contain("ShowOptionsDialog(OptionsDialogInitialSection.FormulaErrorChecking)");
        backstageSource.Should().Contain("private void ShowOptionsDialog(OptionsDialogInitialSection initialSection = OptionsDialogInitialSection.General)");
        // Updated for the J26 fix: the dialog now also seeds from the live workbook's calculation
        // settings (CalculationOptionsDialogState.FromWorkbook) instead of only the persisted
        // app options, so File > Options > Formulas reflects the workbook's actual CalculationMode.
        backstageSource.Should().Contain("_workbook.DisabledFormulaErrorCodes,");
        backstageSource.Should().Contain("initialSection,");
        backstageSource.Should().Contain("CalculationOptionsDialogState.FromWorkbook(_workbook),");
        backstageSource.Should().Contain("private void ErrorCheckingOptionsBtn_Click(object sender, RoutedEventArgs e)");
    }

    [Fact]
    public void ErrorCheckingParityCapture_UsesTheSharedIssueFixture()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");
        var avaloniaSource = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs");
        var avaloniaDialogSource = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Avalonia", "MainWindow.ErrorChecking.cs");

        source.Should().Contain("ErrorCheckingDialogPlanner.CreateParityIssues(sheetId)");
        source.Should().Contain("targetSurfaceId, \"dialog.ErrorChecking\"");
        source.Should().Contain("private static IReadOnlyList<FormulaErrorIssue> CreateErrorCheckingIssues(SheetId sheetId) =>");
        source.Should().Contain("ErrorCheckingDialogPlanner.CreateParityIssues(sheetId);");
        avaloniaSource.Should().Contain("(\"dialog.ErrorChecking\", () => ShowErrorCheckingParityDialogAsync()),");
        avaloniaDialogSource.Should().Contain("ErrorCheckingDialogPlanner.CreateParityIssues(sheetId)");
        avaloniaDialogSource.Should().NotContain("CreateErrorCheckingIssues(sheetId)");
    }

    [Fact]
    public void ErrorCheckingOptionsCommand_FocusesFormulaErrorCheckingOptions()
    {
        // After the declarative-ribbon cutover the "Error Checking Options" command lives as a menu
        // item under the Formulas tab "Error Checking" dropdown (key tip "O"), routed through the
        // generated handler map to ErrorCheckingOptionsBtn_Click rather than a hand-authored Click=.
        var optionsSource = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        var errorCheckingItem = FindErrorCheckingOptionsMenuItem();
        errorCheckingItem.KeyTip.Should().Be("O");
        FreeXRibbonHandlerMap.Handlers.Should().ContainKey("Error Checking Options")
            .WhoseValue.Should().Be("ErrorCheckingOptionsBtn_Click");

        optionsSource.Should().Contain("public enum OptionsDialogInitialSection");
        optionsSource.Should().Contain("FormulaErrorChecking");
        optionsSource.Should().Contain("TabList.SelectedIndex = _initialSection == OptionsDialogInitialSection.FormulaErrorChecking ? 1 : 0;");
        optionsSource.Should().Contain("if (_errorRuleBoxes.Values.FirstOrDefault() is { } firstRule)");
        optionsSource.Should().Contain("Keyboard.Focus(firstRule)");
    }

    [Fact]
    public void ErrorCheckingDialog_ExposesKeyboardAccessKeysForCommandButtons()
    {
        var source = ReadErrorCheckingDialogSource();

        foreach (var content in new[]
        {
            "GoToButtonKey",
            "PreviousButtonKey",
            "NextButtonKey",
            "IgnoreErrorButtonKey",
            "TraceErrorButtonKey",
            "OptionsButtonKey",
            "CloseButtonKey"
        })
            source.Should().Contain($"Content = UiText.Get(ErrorCheckingDialogPlanner.{content})");

        source.Should().Contain("Content = UiText.Get(ErrorCheckingDialogPlanner.CloseButtonKey), Width = ErrorCheckingDialogPlanner.CloseButtonWidth, Height = ErrorCheckingDialogPlanner.ButtonHeight, Margin = new Thickness(4, 0, 0, 0), IsCancel = true");
        UiText.Get("ErrorChecking_CloseButton").Should().Be("_Close");
    }

    [Fact]
    public void ErrorCheckingDialogOpenedFromKeyboard_FocusesIssueList()
    {
        var source = ReadErrorCheckingDialogSource();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_listView.Focus();");
        source.Should().Contain("Keyboard.Focus(_listView);");
        source.Should().Contain("NavigateSelected();");
    }

    [Fact]
    public void ErrorCheckingDialog_DoubleClickNavigateHandlesMouseEvent()
    {
        var source = ReadErrorCheckingDialogSource();
        var doubleClick = source[
            source.IndexOf("private void ListView_MouseDoubleClick", StringComparison.Ordinal)..
            source.IndexOf("private void ListView_KeyDown", StringComparison.Ordinal)];

        doubleClick.Should().Contain("_listView.SelectedItem is not FormulaErrorIssue issue");
        doubleClick.Should().Contain("_navigateTo(issue.Address);");
        doubleClick.Should().Contain("e.Handled = true;");
        doubleClick.IndexOf("_navigateTo(issue.Address);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(doubleClick.IndexOf("e.Handled = true;", StringComparison.Ordinal));
    }

    [Fact]
    public void ErrorCheckingDialog_LabelsIssueListWithAccessKeyAndAutomationName()
    {
        var source = ReadErrorCheckingDialogSource();

        source.Should().Contain("new Label { Content = UiText.Get(ErrorCheckingDialogPlanner.IssuesLabelKey), Target = _listView");
        source.Should().Contain("AutomationProperties.SetAutomationId(_listView, ErrorCheckingDialogPlanner.IssuesAutomationId);");
        source.Should().Contain("AutomationProperties.SetName(_listView, UiText.Get(ErrorCheckingDialogPlanner.IssuesAutomationNameKey));");
        UiText.Get("ErrorChecking_IssuesLabel").Should().Be("_Issues:");
    }

    [Fact]
    public void ErrorCheckingDialog_UsesExcelLikeErrorHelpAndActionStructure()
    {
        var source = ReadErrorCheckingDialogSource();

        source.Should().Contain("UiText.Get(ErrorCheckingDialogPlanner.HelpGroupHeaderKey)");
        source.Should().Contain("Content = UiText.Get(ErrorCheckingDialogPlanner.HelpButtonKey)");
        source.Should().Contain("ShowSelectedIssueHelp");
        source.Should().Contain("Content = UiText.Get(ErrorCheckingDialogPlanner.ShowCalculationStepsButtonKey)");
        source.Should().Contain("Content = UiText.Get(ErrorCheckingDialogPlanner.IgnoreErrorButtonKey)");
        source.Should().Contain("Content = UiText.Get(ErrorCheckingDialogPlanner.EditInFormulaBarButtonKey)");
        source.Should().NotContain("SystemSounds.Asterisk.Play");
        UiText.Get("ErrorChecking_HelpGroupHeader").Should().Be("Error help");
    }

    [Fact]
    public void ErrorCheckingDialog_UpdatesCommandDisabledStatesForSelectionBoundaries()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var issues = new[]
            {
                CreateIssue(sheetId, row: 1),
                CreateIssue(sheetId, row: 2)
            };
            var dialog = new ErrorCheckingDialog(issues, _ => { }, _ => true, _ => { });
            dialog.Show();
            try
            {
                var buttons = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Where(button => button.Content is string)
                    .GroupBy(button => (string)button.Content)
                    .ToDictionary(group => group.Key, group => group.ToList());
                var list = WpfTestTree.FindVisualDescendants<ListView>(dialog).Single();

                buttons["_Previous"].Single().IsEnabled.Should().BeFalse();
                buttons["_Next"].Single().IsEnabled.Should().BeTrue();
                buttons["_Go To"].Single().IsEnabled.Should().BeTrue();
                buttons["_Ignore Error"].Should().AllSatisfy(button => button.IsEnabled.Should().BeTrue());
                buttons["Show _Calculation Steps"].Single().IsEnabled.Should().BeTrue();
                buttons["_Help on this error"].Single().IsEnabled.Should().BeTrue();

                list.SelectedIndex = 1;

                buttons["_Previous"].Single().IsEnabled.Should().BeTrue();
                buttons["_Next"].Single().IsEnabled.Should().BeFalse();

                list.SelectedIndex = -1;

                buttons["_Previous"].Single().IsEnabled.Should().BeFalse();
                buttons["_Next"].Single().IsEnabled.Should().BeFalse();
                buttons["_Go To"].Single().IsEnabled.Should().BeFalse();
                buttons["_Ignore Error"].Should().AllSatisfy(button => button.IsEnabled.Should().BeFalse());
                buttons["_Trace Error"].Single().IsEnabled.Should().BeFalse();
                buttons["Show _Calculation Steps"].Single().IsEnabled.Should().BeFalse();
                buttons["_Edit in Formula Bar"].Single().IsEnabled.Should().BeFalse();
                buttons["_Help on this error"].Single().IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ErrorCheckingDialog_ShowCalculationStepsTargetsFormulaIssuesOnly()
    {
        var dialogSource = ReadErrorCheckingDialogSource();
        var formulaSource = ReadMainWindowFormulaCommandsSource();

        dialogSource.Should().Contain("Action<FormulaErrorIssue>? showCalculationSteps = null");
        dialogSource.Should().Contain("_showCalculationSteps = showCalculationSteps ?? traceError;");
        dialogSource.Should().Contain("_showStepsButton.Click += (_, _) => ShowCalculationStepsSelected();");
        dialogSource.Should().Contain("private static bool HasCalculationSteps(FormulaErrorIssue issue) =>");
        dialogSource.Should().Contain("ErrorCheckingDialogPlanner.HasCalculationSteps(issue)");
        dialogSource.Should().Contain("ErrorCheckingDialogPlanner.CreateCommandState");
        dialogSource.Should().Contain("_showStepsButton.IsEnabled = state.CanShowCalculationSteps;");
        dialogSource.Should().Contain("private void ShowCalculationStepsSelected()");
        dialogSource.Should().Contain("if (_listView.SelectedItem is FormulaErrorIssue issue && HasCalculationSteps(issue))");
        dialogSource.Should().Contain("_showCalculationSteps(issue);");

        formulaSource.Should().Contain("showCalculationSteps: issue =>");
        formulaSource.Should().Contain("FormulaEvaluationSummaryService.GetSummary(_workbook, issue.Address)");
        formulaSource.Should().Contain("new EvaluateFormulaDialog(summary)");
        formulaSource.Should().Contain("evaluationDialog.ShowDialog();");
    }

    [Fact]
    public void ErrorCheckingDialog_DisablesCalculationStepsForFormulaStoredAsTextIssues()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var issue = new FormulaErrorIssue(
                sheetId,
                "Sheet1",
                new CellAddress(sheetId, 1, 1),
                "A1",
                FormulaAuditingService.FormulaStoredAsTextErrorCode,
                null,
                "Formula is stored as text.");
            var dialog = new ErrorCheckingDialog([issue], _ => { }, _ => true, _ => { });
            dialog.Show();
            try
            {
                var buttons = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Where(button => button.Content is string)
                    .GroupBy(button => (string)button.Content)
                    .ToDictionary(group => group.Key, group => group.ToList());

                buttons["Show _Calculation Steps"].Single().IsEnabled.Should().BeFalse();
                buttons["_Go To"].Single().IsEnabled.Should().BeTrue();
                buttons["_Ignore Error"].Should().AllSatisfy(button => button.IsEnabled.Should().BeTrue());
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static FormulaErrorIssue CreateIssue(SheetId sheetId, uint row) =>
        new(
            sheetId,
            "Sheet1",
            new CellAddress(sheetId, row, 1),
            $"A{row}",
            ErrorValue.Value.Code,
            "=A1",
            "Formula uses an incompatible value.");

    private static RibbonMenuItem FindErrorCheckingOptionsMenuItem()
    {
        var tab = FreeXRibbon.Build().FindTab("FormulasTab");
        tab.Should().NotBeNull("the declarative ribbon must expose the Formulas tab");

        var errorChecking = tab!.Groups
            .SelectMany(group => group.Controls)
            .OfType<RibbonDropdown>()
            .FirstOrDefault(control => string.Equals(control.CommandId.Value, "Error Checking", StringComparison.Ordinal));
        errorChecking.Should().NotBeNull("the Formulas tab must expose the Error Checking dropdown");

        var optionsItem = errorChecking!.Menu.Items
            .FirstOrDefault(item => string.Equals(item.CommandId?.Value, "Error Checking Options", StringComparison.Ordinal));
        optionsItem.Should().NotBeNull("the Error Checking dropdown must expose the Options command");
        return optionsItem!;
    }

    private static string ReadErrorCheckingDialogSource() =>
        DialogSourceTestSupport.ReadHostSources("ErrorCheckingDialog.cs");

    private static string ReadMainWindowFormulaCommandsSource() =>
        DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");

    private static string ReadMainWindowBackstageSource() =>
        DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
}
