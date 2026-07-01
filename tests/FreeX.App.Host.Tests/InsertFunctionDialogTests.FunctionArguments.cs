using System.Windows.Controls;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class InsertFunctionDialogTests
{
    [Fact]
    public void FunctionArgumentsDialog_ExposesExcelLikeArgumentMetadataForCommonFunctions()
    {
        FunctionArgumentsDialog.GetArgumentSpecs("IF")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Logical_test", "Value_if_true", "Value_if_false");

        FunctionArgumentsDialog.GetArgumentSpecs("XLOOKUP")
            .Select(argument => argument.Name)
            .Should()
            .StartWith(["Lookup_value", "Lookup_array", "Return_array"]);

        FunctionArgumentsDialog.GetArgumentSpecs("COUNTIF")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Range", "Criteria");

        FunctionArgumentsDialog.GetArgumentSpecs("INDEX")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Array", "Row_num", "Column_num");

        FunctionArgumentsDialog.GetArgumentSpecs("TEXT")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Value", "Format_text");

        FunctionArgumentsDialog.GetArgumentSpecs("FILTER")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Array", "Include", "If_empty");

        FunctionArgumentsDialog.GetArgumentSpecs("DSUM")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Database", "Field", "Criteria");

        FunctionArgumentsDialog.GetArgumentSpecs("CONVERT")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Number", "From_unit", "To_unit");

        FunctionArgumentsDialog.GetArgumentSpecs("MAP")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Array1", "Lambda", "Array2");

        FunctionArgumentsDialog.GetArgumentSpecs("GETPIVOTDATA")
            .Select(argument => argument.Name)
            .Should()
            .Equal("Data_field", "Pivot_table", "Field1", "Item1");
    }

    [Fact]
    public void FunctionArgumentsDialog_CreateFormula_UsesProvidedArgumentsAndTrimsTrailingBlanks()
    {
        FunctionArgumentsDialog.CreateFormula(" if ", ["A1>0", "\"Yes\"", ""])
            .Should()
            .Be("IF(A1>0, \"Yes\")");

        var source = ReadFunctionArgumentsDialogSource();
        source.Should().Contain("FunctionArgumentCatalog.GetArgumentSpecs(functionName)");
        source.Should().Contain("FunctionArgumentCatalog.BuildFormula(functionName, arguments)");
        source.Should().NotContain("KnownArguments");
    }

    [Fact]
    public void FunctionArgumentsDialog_ArgumentLabelsExposeAccessKeysAndTargets()
    {
        FunctionArgumentsDialog.CreateArgumentLabels(FunctionArgumentsDialog.GetArgumentSpecs("IF"))
            .Should()
            .Equal("_Logical__test:", "_Value__if__true:", "V_alue__if__false:");

        var xlookupLabels = FunctionArgumentsDialog.CreateArgumentLabels(FunctionArgumentsDialog.GetArgumentSpecs("XLOOKUP"));
        xlookupLabels.Should().AllSatisfy(label => label.Should().Contain("_"));
        xlookupLabels.Select(GetAccessKey).Should().OnlyHaveUniqueItems();

        var source = ReadFunctionArgumentsDialogSource();
        source.Should().Contain("AddArgumentRow(body, _arguments[index], argumentLabels[index], index)");
        source.Should().Contain("Content = labelText");
        source.Should().Contain("Target = box");
    }

    [Fact]
    public void FunctionArgumentsDialog_CreateRangeSelectionRequest_TrimsCurrentTextAndCollapses()
    {
        FunctionArgumentsDialog.CreateRangeSelectionRequest(1, " Sheet1!A1:B2 ")
            .Should()
            .Be(new FunctionArgumentRangeSelectionRequest(1, "Sheet1!A1:B2", CollapseDialog: true));
    }

    [Fact]
    public void FunctionArgumentsDialog_RangePickerRaisesRequestAndApplyUpdatesArgument()
    {
        StaTestRunner.Run(() =>
        {
            var function = InsertFunctionDialog.BuildCatalog().Single(entry => entry.Name == "SUM");
            var requests = new List<FunctionArgumentRangeSelectionRequest>();
            var dialog = new FunctionArgumentsDialog(function, requests.Add);
            var picker = WpfTestTree.FindLogicalDescendants<Button>(dialog)
                .First(button => string.Equals(button.Content?.ToString(), "...", StringComparison.Ordinal));

            DialogSourceTestSupport.ClickButton(picker);
            dialog.ApplyRangeSelection(0, "Sheet2!B2:D8");

            requests.Should().Equal(new FunctionArgumentRangeSelectionRequest(0, "", CollapseDialog: true));
            dialog.RangeSelectionRequest.Should().Be(requests[0]);
            WpfTestTree.FindLogicalDescendants<TextBox>(dialog)
                .First()
                .Text.Should().Be("Sheet2!B2:D8");
        });
    }

    [Fact]
    public void FunctionArgumentsDialogOpenedFromKeyboard_FocusesFirstArgumentBox()
    {
        var source = ReadFunctionArgumentsDialogSource();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("TextBox? firstArgument = null;");
        source.Should().Contain("foreach (var argumentBox in _argumentBoxes)");
        source.Should().Contain("firstArgument = argumentBox;");
        source.Should().Contain("firstArgument.Focus();");
        source.Should().Contain("firstArgument.SelectAll();");
        source.Should().Contain("Keyboard.Focus(firstArgument);");
    }

    [Fact]
    public void FunctionArgumentsDialog_ExposesExcelLikeHelpAction()
    {
        var source = ReadFunctionArgumentsDialogSource();

        source.Should().Contain("Content = UiText.Get(\"FunctionArguments_HelpButton\")");
        source.Should().Contain("ShowFunctionHelp");
        source.Should().Contain("btnRow.Children.Add(help)");
        source.Should().Contain("btnRow.Children.Add(ok)");
        source.Should().Contain("btnRow.Children.Add(cancel)");
        UiText.Get("FunctionArguments_HelpButton").Should().Be("_Help on this function");
    }

    [Fact]
    public void FunctionArgumentsDialog_LabelsFormulaResultPreviewForAccessibility()
    {
        var source = ReadFunctionArgumentsDialogSource();

        source.Should().Contain("Text = UiText.Get(\"FunctionArguments_FormulaResultLabel\")");
        source.Should().Contain("AutomationProperties.SetName(_formulaPreview, UiText.Get(\"FunctionArguments_FormulaResultAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(_formulaPreview");
        UiText.Get("FunctionArguments_FormulaResultLabel").Should().Be("Formula result =");
    }

    [Fact]
    public void FunctionArgumentsDialog_WiresArgumentReferencePickers()
    {
        var source = ReadFunctionArgumentsDialogSource();

        source.Should().Contain("DialogReferencePicker.CreateEditor(");
        source.Should().Contain("requestSelection: request => RequestRangeSelection(argumentIndex, request)");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest);");
        source.Should().Contain("public void ApplyRangeSelection(int argumentIndex, string rangeText)");
        source.Should().Contain("FocusRangeSelectionInput(textBox);");
    }
}
