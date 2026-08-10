using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FormulaCommandSourceTests
{
    // After the declarative-ribbon cutover the Function Library / Formula Auditing commands no longer
    // live as hand-authored XAML buttons. Their title + key tip now come from the single-source
    // declarative model (FreeXRibbon.Build()) and their Click handler from the generated
    // FreeXRibbonHandlerMap. The InlineData below is (declarative command id, label, key tip, handler).
    [Theory]
    [InlineData("AutoSum#FormulasAutoSumPickerBtn_Click", "AutoSum", "U", "FormulasAutoSumPickerBtn_Click")]
    [InlineData("Recently Used", "Recently Used", "RU", "FormulaRecentlyUsedBtn_Click")]
    [InlineData("Financial", "Financial", "Y", "FormulaFinancialBtn_Click")]
    [InlineData("Logical Functions", "Logical Functions", "L", "FormulaLogicalBtn_Click")]
    [InlineData("Text Functions", "Text Functions", "TF", "FormulaTextBtn_Click")]
    [InlineData("Date & Time", "Date & Time", "DT", "FormulaDateBtn_Click")]
    [InlineData("Lookup & Reference", "Lookup & Reference", "K", "FormulaLookupBtn_Click")]
    [InlineData("Math & Trig", "Math & Trig", "MT", "FormulaMathBtn_Click")]
    [InlineData("More Functions#FormulaMoreBtn_Click", "More Functions", "MF", "FormulaMoreBtn_Click")]
    public void FunctionLibraryCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string commandId,
        string label,
        string keyTip,
        string handler)
    {
        var control = FindFormulasTabControl(commandId);

        control.Label.Should().Be(label);
        control.KeyTip.Should().Be(keyTip);
        FreeXRibbonHandlerMap.Handlers.Should().ContainKey(commandId)
            .WhoseValue.Should().Be(handler);
    }

    [Fact]
    public void InsertFunctionCommand_RemainsWiredToTheFormulaBarFxButton()
    {
        // "Insert Function" is the formula-bar fx button (key tip "FX"), not a Function Library ribbon
        // button; it survived the ribbon cutover in the MainWindow chrome and still opens the dialog.
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();

        xaml.Should().Contain("x:Name=\"FormulaBarFxButton\"");
        xaml.Should().Contain("Click=\"InsertFunctionBtn_Click\"");
        xaml.Should().Contain("ribbonWpf:RibbonTooltip.KeyTip=\"FX\"");
        xaml.Should().Contain("ribbonWpf:RibbonMetadata.CommandName=\"Insert Function\"");
    }

    [Theory]
    [InlineData("Trace Precedents", "Trace Precedents", "TP", "TracePrecedentsBtn_Click")]
    [InlineData("Trace Dependents", "Trace Dependents", "TD", "TraceDependentsBtn_Click")]
    [InlineData("Remove Arrows#RemoveArrowsBtn_Click", "Remove Arrows", "RA", "RemoveArrowsBtn_Click")]
    [InlineData("Show Formulas", "Show Formulas", "SF", "ShowFormulasBtn_Click")]
    [InlineData("Error Checking", "Error Checking", "EC", "ErrorCheckBtn_Click")]
    [InlineData("Evaluate Formula", "Evaluate Formula", "V", "EvaluateFormulaBtn_Click")]
    [InlineData("Watch Window", "Watch Window", "W", "WatchWindowBtn_Click")]
    public void FormulaAuditingCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string commandId,
        string label,
        string keyTip,
        string handler)
    {
        var control = FindFormulasTabControl(commandId);

        control.Label.Should().Be(label);
        control.KeyTip.Should().Be(keyTip);
        FreeXRibbonHandlerMap.Handlers.Should().ContainKey(commandId)
            .WhoseValue.Should().Be(handler);
    }

    private static RibbonControl FindFormulasTabControl(string commandId)
    {
        var tab = FreeXRibbon.Build().FindTab("FormulasTab");
        tab.Should().NotBeNull("the declarative ribbon must expose the Formulas tab");

        var control = tab!.Groups
            .SelectMany(group => group.Controls)
            .FirstOrDefault(c => string.Equals(c.CommandId.Value, commandId, StringComparison.Ordinal));
        control.Should().NotBeNull($"the Formulas tab must expose the '{commandId}' command");
        return control!;
    }

    [Fact]
    public void FormulaCommandHandlers_RouteThroughExpectedDialogsMenusAndServices()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");
        var nameManagerSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

        source.Should().Contain("var dlg = new InsertFunctionDialog();");
        source.Should().Contain("dlg.SelectedFunction is not { } function");
        source.Should().Contain("InsertFormulaFunction(function);");
        nameManagerSource.Should().Contain("new NamedRangeDialog(");
        source.Should().Contain("new NameDefinitionDialog(");
        source.Should().Contain("request => ApplyNameDefinitionSelection(dialog, request)");
        source.Should().Contain("var plan = definedNames.PlanSave(draft);");
        source.Should().Contain("DefinedNameUiPolicy.BuildScopeOptions(definedNames.ScopeChoices)");
        source.Should().NotContain("new NamedRangeDialog(",
            "Define Name should open a creation flow instead of duplicating Name Manager");
        source.Should().Contain("new CreateNamesFromSelectionDialog { Owner = this }");
        source.Should().Contain("new CreateNamedRangesFromSelectionCommand(");
        source.Should().Contain("PasteNamesPlanner.BuildItems(_workbook, FormatWorkbookRange)");
        source.Should().Contain("new PasteNamesDialog(items)");
        source.Should().Contain("PasteNamesPlanner.TryBuildPasteListEdits(range.Start, items, out var edits, out var error)");
        source.Should().Contain("DescribePasteNamesListError(error)");
        source.Should().Contain("DefinedNameUiPolicy.GetPasteNamesListErrorResourceKey(error, DefinedNameUiProfile.Wpf)");
        source.Should().Contain("DefinedNameUiPolicy.PlanUseInFormula(");
        source.Should().Contain("FormulaInsertionService.InsertDefinedName(");
        source.Should().Contain("BeginFormulaBarFormulaEdit(result.Text, result.CaretIndex);");
        source.Should().Contain("MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>())");
        source.Should().Contain("OpenRibbonContextMenu(btn, cm);");
        source.Should().Contain("FormulaTraceArrowPlanner.GetNextPrecedentTraceArrows");
        source.Should().Contain("FormulaTraceArrowPlanner.GetNextDependentTraceArrows");
        source.Should().Contain("_formulaTraceArrows.AddRange(arrows);");
        source.Should().Contain("RemoveTraceArrows(FormulaTraceArrowKind.Precedent, \"Remove Precedent Arrows\");");
        source.Should().Contain("RemoveTraceArrows(FormulaTraceArrowKind.Dependent, \"Remove Dependent Arrows\");");
        source.Should().Contain("_formulaTraceArrows.RemoveAll(arrow => arrow.Kind == kind.Value)");
        source.Should().Contain("FormulaFinancialBtn_Click(object sender, RoutedEventArgs e) => OpenFormulaFunctionMenu(sender, [\"PMT\", \"NPV\", \"IRR\", \"RATE\", \"PV\", \"FV\"]);");
        source.Should().Contain("FormulaMoreBtn_Click(object sender, RoutedEventArgs e)    => InsertFunctionBtn_Click(sender, e);");
        source.Should().Contain("InsertFunctionCatalogPlanner.BuildCatalog()");
        source.Should().Contain("new FunctionArgumentsDialog(");
        source.Should().Contain("request => ApplyFunctionArgumentRangeSelection(argumentsDialog, request)");
        source.Should().Contain("private void ApplyFunctionArgumentRangeSelection(");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("ShowOwnedDialog(argumentsDialog)");
        source.Should().Contain("BeginFormulaBarFormulaEdit(\"=\" + argumentsDialog.ResultFormula);");
        source.Should().Contain("InsertRawFormulaFunction(normalizedName);");
        source.Should().Contain("BeginFormulaBarFormulaEdit($\"={funcName}(\");");
        source.Should().Contain("private void BeginFormulaBarFormulaEdit(string text, int? caretIndex = null)");
        source.Should().Contain("ShowOptionsDialog(OptionsDialogInitialSection.FormulaErrorChecking)");
        backstageSource.Should().Contain("private void ErrorCheckingOptionsBtn_Click(object sender, RoutedEventArgs e)");
        backstageSource.Should().Contain("ShowOptionsDialog(OptionsDialogInitialSection.FormulaErrorChecking)");
    }
}
