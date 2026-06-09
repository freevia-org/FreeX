using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FormulaCommandSourceTests
{
    [Theory]
    [InlineData("Insert Function", "F", "InsertFunctionBtn_Click")]
    [InlineData("AutoSum", "U", "FormulasAutoSumPickerBtn_Click")]
    [InlineData("Recently Used", "RU", "FormulaRecentlyUsedBtn_Click")]
    [InlineData("Financial", "Y", "FormulaFinancialBtn_Click")]
    [InlineData("Logical Functions", "L", "FormulaLogicalBtn_Click")]
    [InlineData("Text Functions", "TF", "FormulaTextBtn_Click")]
    [InlineData("Date &amp; Time", "DT", "FormulaDateBtn_Click")]
    [InlineData("Lookup &amp; Reference", "K", "FormulaLookupBtn_Click")]
    [InlineData("Math &amp; Trig", "MT", "FormulaMathBtn_Click")]
    [InlineData("More Functions", "MF", "FormulaMoreBtn_Click")]
    public void FunctionLibraryCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var elementName = title == "Insert Function" ? "local:AutomationInvokeButton" : "Button";
        var button = ReadFormulasTabXaml()
            .ExtractElementByInvariantCommandName(elementName, title, $"Click=\"{handler}\"");

        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Name Manager", "N", "NamedRangesButton_Click")]
    [InlineData("Define Name", "DN", "DefineNameBtn_Click")]
    [InlineData("Use in Formula", "I", "UseInFormulaBtn_Click")]
    [InlineData("Create from Selection", "CS", "CreateNamesFromSelectionBtn_Click")]
    public void DefinedNameCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var button = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractButtonElementByInvariantCommandName(title);

        button.ShouldContainLocalizedAttribute("Content", title);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Sum", "S", "AutoSumSumMenuItem_Click")]
    [InlineData("Average", "A", "AutoSumAvgMenuItem_Click")]
    [InlineData("Count Numbers", "C", "AutoSumCountMenuItem_Click")]
    [InlineData("Count All", "T", "AutoSumCountAllMenuItem_Click")]
    [InlineData("Max", "X", "AutoSumMaxMenuItem_Click")]
    [InlineData("Min", "M", "AutoSumMinMenuItem_Click")]
    [InlineData("More Functions...", "F", "AutoSumMoreMenuItem_Click")]
    public void FormulaAutoSumMenuItems_ExposeExpectedKeyTipsAndHandlers(
        string header,
        string keyTip,
        string handler)
    {
        var item = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractElementByLocalizedAttributeValue("MenuItem", "Header", header);

        item.ShouldContainLocalizedAttribute("Header", header);
        item.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        item.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Trace Precedents", "TP", "TracePrecedentsBtn_Click")]
    [InlineData("Trace Dependents", "TD", "TraceDependentsBtn_Click")]
    [InlineData("Remove Arrows", "RA", "RemoveArrowsBtn_Click")]
    [InlineData("Show Formulas", "SF", "ShowFormulasBtn_Click")]
    [InlineData("Error Checking", "EC", "ErrorCheckBtn_Click")]
    [InlineData("Evaluate Formula", "V", "EvaluateFormulaBtn_Click")]
    [InlineData("Watch Window", "W", "WatchWindowBtn_Click")]
    public void FormulaAuditingCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var elementName = title == "Show Formulas" ? "ToggleButton" : "Button";
        var button = ReadFormulasTabXaml()
            .ExtractElementByInvariantCommandName(elementName, title, $"Click=\"{handler}\"");

        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Calculate Now", "CN", "CalcNowBtn_Click")]
    [InlineData("Calculate Sheet", "SC", "CalcSheetBtn_Click")]
    [InlineData("Calculation Options", "O", "CalcOptionsBtn_Click")]
    public void FormulaCalculationCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var button = ReadFormulasTabXaml()
            .ExtractButtonElementByInvariantCommandName(title, $"Click=\"{handler}\"");

        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void FormulaCommandHandlers_RouteThroughExpectedDialogsMenusAndServices()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");

        source.Should().Contain("var dlg = new InsertFunctionDialog();");
        source.Should().Contain("BeginFormulaBarFormulaEdit(\"=\" + dlg.SelectedFormula);");
        source.Should().Contain("new NamedRangeDialog(");
        source.Should().Contain("new CreateNamesFromSelectionDialog { Owner = this }");
        source.Should().Contain("new CreateNamedRangesFromSelectionCommand(");
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
        source.Should().Contain("new FunctionArgumentsDialog(function) { Owner = this }");
        source.Should().Contain("ShowOwnedDialog(argumentsDialog)");
        source.Should().Contain("BeginFormulaBarFormulaEdit(\"=\" + argumentsDialog.ResultFormula);");
        source.Should().Contain("InsertRawFormulaFunction(normalizedName);");
        source.Should().Contain("BeginFormulaBarFormulaEdit($\"={funcName}(\");");
        source.Should().Contain("private void BeginFormulaBarFormulaEdit(string text, int? caretIndex = null)");
    }

    private static string ReadFormulasTabXaml()
    {
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var start = xaml.IndexOf("<TabItem Header=\"{local:Loc Key=MainWindow_Header_Formulas}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the Formulas ribbon tab should be present");

        var end = xaml.IndexOf("<TabItem Header=\"{local:Loc Key=MainWindow_Header_Data}\"", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the Data ribbon tab should follow the Formulas ribbon tab");
        return xaml[start..end];
    }
}
