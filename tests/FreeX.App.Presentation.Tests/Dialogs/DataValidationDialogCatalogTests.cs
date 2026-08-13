using FluentAssertions;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class DataValidationDialogCatalogTests
{
    [Fact]
    public void ChoiceCatalogs_PreserveExcelOrderAndResolveRendererText()
    {
        static string Resolve(string key) => $"localized:{key}";

        var types = DataValidationDialogPlanner.CreateTypeChoices(Resolve);
        var operators = DataValidationDialogPlanner.CreateOperatorChoices(Resolve);
        var alerts = DataValidationDialogPlanner.CreateAlertStyleChoices(Resolve);

        types.Select(choice => choice.Type).Should().Equal(
            DvType.Any,
            DvType.WholeNumber,
            DvType.Decimal,
            DvType.List,
            DvType.Date,
            DvType.Time,
            DvType.TextLength,
            DvType.Custom);
        types[0].Label.Should().Be("localized:DataValidation_AnyValue");
        operators.Select(choice => choice.Operator).Should().Equal(
            DvOperator.Between,
            DvOperator.NotBetween,
            DvOperator.Equal,
            DvOperator.NotEqual,
            DvOperator.GreaterThan,
            DvOperator.LessThan,
            DvOperator.GreaterThanOrEqual,
            DvOperator.LessThanOrEqual);
        alerts.Select(choice => choice.AlertStyle).Should().Equal(
            DvAlertStyle.Stop,
            DvAlertStyle.Warning,
            DvAlertStyle.Information);
    }

    [Theory]
    [InlineData(DvFormula1Label.Source, "DataValidation_Source", "List source")]
    [InlineData(DvFormula1Label.Formula, "DataValidation_Formula", "evaluate to TRUE")]
    [InlineData(DvFormula1Label.Value, "DataValidation_Value", "Value for")]
    [InlineData(DvFormula1Label.Minimum, "DataValidation_Minimum", "Minimum value")]
    public void FormulaDescriptors_OwnLabelsAndHelp(
        DvFormula1Label label,
        string expectedResourceKey,
        string expectedHelpFragment)
    {
        var descriptor = DataValidationDialogPlanner.GetFormula1FieldDescriptor(label);

        descriptor.LabelResourceKey.Should().Be(expectedResourceKey);
        descriptor.HelpText.Should().Contain(expectedHelpFragment);
    }

    [Fact]
    public void RendererDialogs_ConsumeSharedCatalogsAndFormulaDescriptors()
    {
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var wpf = ReadSource("src", "FreeX.App.Host", "DataValidationDialog.xaml.cs");
        var xaml = ReadSource("src", "FreeX.App.Host", "DataValidationDialog.xaml");

        foreach (var source in new[] { avalonia, wpf })
        {
            source.Should().Contain("DataValidationDialogPlanner.CreateTypeChoices(")
                .And.Contain("DataValidationDialogPlanner.CreateOperatorChoices(")
                .And.Contain("DataValidationDialogPlanner.CreateAlertStyleChoices(")
                .And.Contain("DataValidationDialogPlanner.GetFormula1FieldDescriptor(");
        }

        avalonia.Should().NotContain("new(DvOperator.Between, \"Between\")")
            .And.NotContain("DataValidationFormula1HelpText");
        wpf.Should().NotContain("Formula1LabelKey");
        xaml.Should().NotContain("Tag=\"Between\"")
            .And.NotContain("Tag=\"WholeNumber\"");
    }

    private static string ReadSource(params string[] path) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFileFromBaseDirectory(path));
}
