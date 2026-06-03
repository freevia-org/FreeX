using System.IO;
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

        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FunctionArgumentsDialog.cs"));
        source.Should().Contain("AddArgumentRow(body, _arguments[index], argumentLabels[index])");
        source.Should().Contain("Content = labelText");
        source.Should().Contain("Target = box");
    }

    [Fact]
    public void FunctionArgumentsDialogOpenedFromKeyboard_FocusesFirstArgumentBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FunctionArgumentsDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_argumentBoxes.FirstOrDefault()");
        source.Should().Contain("firstArgument.Focus();");
        source.Should().Contain("firstArgument.SelectAll();");
        source.Should().Contain("Keyboard.Focus(firstArgument);");
    }

    [Fact]
    public void FunctionArgumentsDialog_ExposesExcelLikeHelpAction()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FunctionArgumentsDialog.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FunctionArgumentsDialog.cs"));

        source.Should().Contain("Text = UiText.Get(\"FunctionArguments_FormulaResultLabel\")");
        source.Should().Contain("AutomationProperties.SetName(_formulaPreview, UiText.Get(\"FunctionArguments_FormulaResultAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(_formulaPreview");
        UiText.Get("FunctionArguments_FormulaResultLabel").Should().Be("Formula result =");
    }
}
