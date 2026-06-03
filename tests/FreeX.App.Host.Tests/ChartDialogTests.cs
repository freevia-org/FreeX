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
            "Surface");
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
    public void ChartTypeDialogs_ExposeExcelInsertAndChangeSurfaces()
    {
        var source = ReadChartTypeDialogSource();

        source.Should().Contain("UiText.Get(\"InsertChart_RecommendedChartsTab\")");
        source.Should().Contain("UiText.Get(\"InsertChart_AllChartsTab\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_CategoriesAutomationName\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_SubtypeGalleryAutomationName\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_PreviewTitle\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_ChooseChartTypeHeading\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_RecommendedHelpText\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_PreviewSampleLabel\")");
        source.Should().Contain("UiText.Get(\"ChartTypePicker_AllChartsHelpText\")");
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
            var recommendedGallery = GetPrivateField<ListBox>(dialog, "_recommendedGallery");
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
            var subtypeGallery = GetPrivateField<ListBox>(dialog, "_subtypeGallery");
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

}
