using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ChartDialogTests
{
    [Fact]
    public void ChartTypePickerPlanner_ReturnsOnlyRenderableChartTypesWithFriendlyLabels()
    {
        var options = ChartTypePickerPlanner.GetSupportedOptions();

        options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Column,
            ChartType.StackedColumn,
            ChartType.PercentStackedColumn,
            ChartType.Line,
            ChartType.ThreeDLine,
            ChartType.Pie,
            ChartType.ThreeDPie,
            ChartType.Doughnut,
            ChartType.Bar,
            ChartType.StackedBar,
            ChartType.PercentStackedBar,
            ChartType.ThreeDBar,
            ChartType.Scatter,
            ChartType.Bubble,
            ChartType.Area,
            ChartType.ThreeDArea,
            ChartType.Radar,
            ChartType.Stock,
            ChartType.Surface,
            ChartType.ThreeDSurface);
        options.Select(option => option.Type).Should().Contain(
        [
            ChartType.Treemap,
            ChartType.Sunburst,
            ChartType.Histogram,
            ChartType.Pareto,
            ChartType.BoxAndWhisker,
            ChartType.Waterfall,
            ChartType.Funnel
        ]);
        options.Should().NotContain(option => option.Type == ChartType.Map);
        options.Should().NotContain(option => !ChartAuthoringPlanner.CanAuthor(option.Type));
        options.Single(option => option.Type == ChartType.PercentStackedColumn).DisplayName
            .Should()
            .Be("100% Stacked Column");
    }

    [Fact]
    public void ChartTypePickerPlanner_RecommendsDefaultChartTypes()
    {
        var recommendations = ChartTypePickerPlanner.GetRecommendedOptions();

        recommendations.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Column,
            ChartType.Line,
            ChartType.Bar,
            ChartType.Pie,
            ChartType.Scatter);
        recommendations.Should().OnlyContain(option => option.IsRecommended);
    }

    [Fact]
    public void ChartTypePickerPlanner_GroupsRenderableTypesIntoExcelCategories()
    {
        var categories = ChartTypePickerPlanner.GetCategories();

        categories.Select(category => category.Name).Should().ContainInOrder(
            "Column",
            "Line",
            "Pie",
            "Bar",
            "Area",
            "X Y (Scatter)",
            "Stock",
            "Radar",
            "Surface",
            "Treemap",
            "Sunburst",
            "Histogram",
            "Box and Whisker Chart",
            "Waterfall",
            "Funnel");
        categories.Should().OnlyContain(category => category.Options.All(option => ChartAuthoringPlanner.CanAuthor(option.Type)));
        categories.Single(category => category.Name == "Column").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Column,
            ChartType.StackedColumn,
            ChartType.PercentStackedColumn,
            ChartType.ThreeDColumn);
        categories.Single(category => category.Name == "Line").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Line,
            ChartType.ThreeDLine);
        categories.Single(category => category.Name == "Pie").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Pie,
            ChartType.ThreeDPie,
            ChartType.Doughnut);
        categories.Single(category => category.Name == "Bar").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Bar,
            ChartType.StackedBar,
            ChartType.PercentStackedBar,
            ChartType.ThreeDBar);
        categories.Single(category => category.Name == "Area").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Area,
            ChartType.ThreeDArea);
        categories.Single(category => category.Name == "Surface").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Surface,
            ChartType.ThreeDSurface);
        categories.Single(category => category.Name == "Treemap").Options.Should().ContainSingle(option => option.Type == ChartType.Treemap);
        categories.Single(category => category.Name == "Sunburst").Options.Should().ContainSingle(option => option.Type == ChartType.Sunburst);
        categories.Single(category => category.Name == "Histogram").Options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Histogram,
            ChartType.Pareto);
        categories.Single(category => category.Name == "Box and Whisker Chart").Options.Should().ContainSingle(option => option.Type == ChartType.BoxAndWhisker);
        categories.Single(category => category.Name == "Waterfall").Options.Should().ContainSingle(option => option.Type == ChartType.Waterfall);
        categories.Single(category => category.Name == "Funnel").Options.Should().ContainSingle(option => option.Type == ChartType.Funnel);
    }

    [Fact]
    public void ChartTypePickerPlanner_BuildsSubtypeGalleryChoicesWithPreviewText()
    {
        var choices = ChartTypePickerPlanner.GetGalleryChoices("Bar");

        choices.Select(choice => choice.SubtypeName).Should().ContainInOrder(
            "Clustered Bar",
            "Stacked Bar",
            "100% Stacked Bar");
        choices.Should().OnlyContain(choice => choice.CategoryName == "Bar");
        choices.Should().OnlyContain(choice => !string.IsNullOrWhiteSpace(choice.PreviewText));
    }

    [Fact]
    public void ChartTypePickerPlanner_BuildsAdvancedFamilyGalleryChoices()
    {
        var histogramChoices = ChartTypePickerPlanner.GetGalleryChoices("Histogram");
        var waterfallChoices = ChartTypePickerPlanner.GetGalleryChoices("Waterfall");
        var funnelChoices = ChartTypePickerPlanner.GetGalleryChoices("Funnel");

        histogramChoices.Select(choice => choice.Type).Should().ContainInOrder(
            ChartType.Histogram,
            ChartType.Pareto);
        waterfallChoices.Should().ContainSingle(choice => choice.Type == ChartType.Waterfall);
        funnelChoices.Should().ContainSingle(choice => choice.Type == ChartType.Funnel);
        histogramChoices.Concat(waterfallChoices).Concat(funnelChoices)
            .Should()
            .OnlyContain(choice => ChartAuthoringPlanner.CanAuthor(choice.Type));
    }

    [Fact]
    public void ChartTypePickerPlanner_DelegatesCatalogToPresentationAndKeepsHostLocalized()
    {
        var hostSource = DialogSourceTestSupport.ReadHostSourceFile("ChartTypeDialogs.Planner.cs");
        var sharedSource = DialogSourceTestSupport.ReadPresentationSources("Charts", "Editing", "ChartTypePickerPlanner.cs");

        hostSource.Should().Contain("PresentationChartTypePickerPlanner.GetSupportedOptions()");
        hostSource.Should().Contain("PresentationChartTypePickerPlanner.GetCategories()");
        hostSource.Should().Contain("PresentationChartTypePickerPlanner.GetRecommendedOptions()");
        hostSource.Should().Contain("PresentationChartTypePickerPlanner.GetRecommendedGalleryChoices()");
        hostSource.Should().Contain("PresentationChartTypePickerPlanner.GetGalleryChoices(category.NameKey)");
        hostSource.Should().Contain("UiText.Get(plan.DisplayNameKey)");
        hostSource.Should().Contain("UiText.Get(plan.CategoryNameKey)");
        hostSource.Should().Contain("UiText.Format(plan.PreviewTextFormatKey, subtypeName)");
        hostSource.Should().NotContain("ChartTypeChangePlanner.GetSupportedChoices()");
        hostSource.Should().NotContain("ChartTypeChangePlanner.GetCategories()");
        hostSource.Should().NotContain("ChartTypeChangePlanner.GetRecommendedTypes()");
        hostSource.Should().NotContain("new(ChartType.Column, UiText.Get(\"ChartType_ClusteredColumn\")");
        sharedSource.Should().Contain("public sealed record ChartTypePickerOptionPlan");
        sharedSource.Should().Contain("public static IReadOnlyList<ChartTypeGalleryChoicePlan> GetGalleryChoices");
        sharedSource.Should().Contain("PreviewTextFormatKey");
    }

    [Fact]
    public void ChartTypeDialogs_ExposeExcelInsertAndChangeSurfaces()
    {
        var source = ReadChartTypeDialogSource();

        source.Should().Contain("UiText.Get(\"InsertChart_RecommendedChartsTab\")");
        source.Should().Contain("UiText.Get(\"InsertChart_AllChartsTab\")");
        source.Should().Contain("PresentationChartTypePickerPlanner.GetRecommendedPanel()");
        source.Should().Contain("PresentationChartTypePickerPlanner.GetAllChartsPanel()");
        source.Should().Contain("UiText.Get(panel.CategoryListAutomationNameResourceKey!)");
        source.Should().Contain("UiText.Get(panel.SubtypeGalleryAutomationNameResourceKey)");
        source.Should().Contain("UiText.Get(panel.HeadingResourceKey)");
        source.Should().Contain("UiText.Get(panel.HelpResourceKey)");
        source.Should().Contain("CreatePreviewPanel(panel.Preview)");
        source.Should().Contain("UiText.Get(preview.TitleResourceKey)");
        source.Should().Contain("UiText.Get(preview.SampleLabelResourceKey)");
    }

    [Fact]
    public void InsertChartDialog_BuildsResultForSelectedChartType()
    {
        var result = InsertChartDialog.CreateResult(ChartType.Line);

        result.ChartType.Should().Be(ChartType.Line);
        result.UseRecommendedLayout.Should().BeFalse();
    }

    [Fact]
    public void InsertChartDialog_UsesFirstRecommendationForRecommendedResult()
    {
        var result = InsertChartDialog.CreateRecommendedResult();

        result.ChartType.Should().Be(ChartType.Column);
        result.UseRecommendedLayout.Should().BeTrue();
    }

    [Fact]
    public void InsertChartDialogOpenedFromKeyboard_FocusesRecommendedGallery()
    {
        var source = ReadChartTypeDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed partial class InsertChartDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record ChangeChartTypeDialogResult", StringComparison.Ordinal)];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_recommendedGallery.Focus();");
        dialogSource.Should().Contain("Keyboard.Focus(_recommendedGallery);");
    }

    [Fact]
    public void ChartTypeGalleries_DoubleClickAcceptsSelectedSubtype()
    {
        var source = ReadChartTypeDialogSource();

        source.Should().Contain("_recommendedGallery.MouseDoubleClick += Gallery_MouseDoubleClick;");
        source.Should().Contain("_subtypeGallery.MouseDoubleClick += Gallery_MouseDoubleClick;");
        source.Should().Contain("private void Accept()");
        source.Should().Contain("private void Gallery_MouseDoubleClick(object sender, MouseButtonEventArgs e)");
        source.Should().Contain("e.Handled = true;");
        source.Should().Contain("_subtypeGallery.MouseDoubleClick += SubtypeGallery_MouseDoubleClick;");
        source.Should().Contain("private void AcceptSelectedChartType()");
        source.Should().Contain("private void SubtypeGallery_MouseDoubleClick(object sender, MouseButtonEventArgs e)");
    }

    [Fact]
    public void InsertChartDialogGalleryDoubleClickAcceptsAndHandlesMouseEvent()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new InsertChartDialog();
            var recommendedGallery = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_recommendedGallery");
            var doubleClick = CreateMouseDoubleClickEvent();

            dialog.Dispatcher.BeginInvoke(() => recommendedGallery.RaiseEvent(doubleClick));
            var accepted = dialog.ShowDialog();

            accepted.Should().BeTrue();
            doubleClick.Handled.Should().BeTrue();
            dialog.Result.ChartType.Should().Be(ChartType.Column);
            dialog.Result.UseRecommendedLayout.Should().BeTrue();
        });
    }

    [Fact]
    public void ChangeChartTypeDialogSubtypeDoubleClickAcceptsAndHandlesMouseEvent()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ChangeChartTypeDialog(ChartType.Bar);
            var subtypeGallery = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_subtypeGallery");
            var selectedChoice = subtypeGallery.Items
                .OfType<ChartTypeGalleryChoice>()
                .First();
            subtypeGallery.SelectedItem = selectedChoice;
            var doubleClick = CreateMouseDoubleClickEvent();

            dialog.Dispatcher.BeginInvoke(() => subtypeGallery.RaiseEvent(doubleClick));
            var accepted = dialog.ShowDialog();

            accepted.Should().BeTrue();
            doubleClick.Handled.Should().BeTrue();
            dialog.Result.ChartType.Should().Be(selectedChoice.Type);
        });
    }

    [Fact]
    public void ChangeChartTypeDialog_PreselectsCurrentTypeAndBuildsResult()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ChangeChartTypeDialog(ChartType.Bar);

            dialog.SelectedChartType.Should().Be(ChartType.Bar);
        });
        ChangeChartTypeDialog.CreateResult(ChartType.Area).ChartType.Should().Be(ChartType.Area);
    }

    [Fact]
    public void ChangeChartTypeDialogOpenedFromKeyboard_FocusesSubtypeGallery()
    {
        var source = ReadChartTypeDialogSource();
        var dialogSource = source[source.IndexOf("public sealed class ChangeChartTypeDialog", StringComparison.Ordinal)..];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_subtypeGallery.Focus();");
        dialogSource.Should().Contain("Keyboard.Focus(_subtypeGallery);");
    }

    [Fact]
    public void ChangeChartTypeDialog_UsesSharedPickerGeometryContract()
    {
        var changeSource = ReadChartTypeDialogSource();
        var pickerSource = DialogSourceTestSupport.ReadHostSourceFile("ChartTypeDialogs.PickerUi.cs");

        changeSource.Should().Contain("ChartTypeChangePlanner.PickerPanelHeight");
        changeSource.Should().Contain("ChartTypeChangePlanner.PickerCategoryColumnWidth");
        changeSource.Should().Contain("ChartTypeChangePlanner.PickerSubtypeColumnWidth");
        changeSource.Should().Contain("ChartTypeChangePlanner.PickerSubtypeWidth");
        pickerSource.Should().Contain("ChartTypeChangePlanner.PickerCategoryWidth");
        pickerSource.Should().Contain("ChartTypeChangePlanner.PickerPreviewWidth");
        pickerSource.Should().Contain("ChartTypeChangePlanner.PickerColumnGap");
    }

}
