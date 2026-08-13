using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class DataValidationDialogSourceTests
{
    [Fact]
    public void DataValidationDialog_DelegatesRuleEditorPlanningToPresentation()
    {
        var source = DataValidationDialogSource();

        source.Should().Contain("DataValidationDialogPlanner.CreateDefaultRule(");
        source.Should().Contain("DataValidationDialogPlanner.DefaultOperatorForType(");
        source.Should().Contain("DataValidationDialogPlanner.CreateVisibilityPlan(");
        source.Should().Contain("DataValidationDialogPlanner.ValidateCriteria(");
        source.Should().Contain("DataValidationDialogPlanner.CreateRule(new DataValidationRuleEditorInput");
        source.Should().Contain("new DvMessageVisibility(");
        source.Should().Contain("DataValidationDialogPlanner.GetFormula1FieldDescriptor(plan.Formula1Label)");
    }

    [Fact]
    public void DataValidationDialog_DoesNotKeepInlineCriteriaValidation()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().NotContain("CreateDefaultDataValidationRule");
        source.Should().NotContain("GetDefaultDataValidationOperator");
        source.Should().NotContain("TryValidateDataValidationCriteria");
        source.Should().NotContain("HasDataValidationListSource");
        source.Should().NotContain("TryValidateIntegralDataValidationCriterion");
        source.Should().NotContain("TryValidateNumericDataValidationCriterion");
    }

    private static string DataValidationDialogSource()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var start = source.IndexOf(
            "private async Task<DataValidationDialogResult?> ShowDataValidationInputDialogAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private static StackPanel CreateDataValidationField",
            start,
            StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
