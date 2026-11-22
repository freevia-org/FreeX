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

    [Fact]
    public void DataValidationDialog_HiddenFormulaRowsKeepTheirWpfRowGap()
    {
        // WPF collapses only the label and box; each formula row's DockPanel (margin 0,0,0,8) stays
        // in the grid, so the gap above "Ignore blank" holds both hidden rows' 8px.
        var source = DataValidationDialogSource();

        source.Should().Contain("formula1Label.IsVisible = plan.ShowFormula1;");
        source.Should().Contain("formula1Box.IsVisible = plan.ShowFormula1;");
        source.Should().Contain("formula2Label.IsVisible = plan.ShowFormula2;");
        source.Should().Contain("formula2Box.IsVisible = plan.ShowFormula2;");
        source.Should().NotContain("formula1Field.IsVisible");
        source.Should().NotContain("formula2Field.IsVisible");
    }

    [Fact]
    public void WpfSolidHighlightRows_PaintSelectedItemInkWhite()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "DialogControlStyles.cs"));
        var start = source.IndexOf("internal static void ApplyWpfSolidHighlightRows", StringComparison.Ordinal);
        var end = source.IndexOf("private static readonly IBrush WpfHighlightFace", start, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        var body = source[start..end];

        body.Should().Contain("new Setter(TemplatedControl.ForegroundProperty, WpfHighlightInk)");
        body.Should().NotContain("SelectionForegroundBrush");
        source.Should().Contain("WpfHighlightInk = new ImmutableSolidColorBrush(Colors.White)");
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
