using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartDataDialogSessionTests
{
    [Fact]
    public void SelectionTransitions_MoveAndRemoveActiveSeriesAndCategory()
    {
        var session = CreateSession(out _);

        session.SelectSeries(0);
        session.MoveActiveSeries(+1).Should().BeTrue();
        session.ActiveSeriesIndex.Should().Be(1);
        session.BuildCommitPlan().SeriesNames.Should().Equal("Budget", "Sales");

        session.RemoveActiveSeries().Should().BeTrue();
        session.ActiveSeriesIndex.Should().Be(0);
        session.BuildCommitPlan().SeriesNames.Should().Equal("Budget");

        session.SelectCategory(2);
        session.MoveActiveCategory(-1).Should().BeTrue();
        session.ActiveCategoryIndex.Should().Be(1);
        session.BuildCommitPlan().Categories.Should().Equal("Q1", "Q3", "Q2");

        session.RemoveActiveCategory().Should().BeTrue();
        session.ActiveCategoryIndex.Should().Be(1);
        session.BuildCommitPlan().Categories.Should().Equal("Q1", "Q2");
    }

    [Fact]
    public void MissingSelection_RemovesLastItemAndRejectsMove()
    {
        var session = CreateSession(out _);

        session.SelectSeries(-1);
        session.MoveActiveSeries(+1).Should().BeFalse();
        session.RemoveActiveSeries().Should().BeTrue();
        session.ActiveSeriesIndex.Should().Be(-1);
        session.BuildCommitPlan().SeriesNames.Should().Equal("Sales");

        session.SelectCategory(99);
        session.MoveActiveCategory(-1).Should().BeFalse();
        session.RemoveActiveCategory().Should().BeTrue();
        session.ActiveCategoryIndex.Should().Be(-1);
        session.BuildCommitPlan().Categories.Should().Equal("Q1", "Q2");
    }

    [Fact]
    public void StructuralTransitions_AddAndSwitchRowsAndColumns()
    {
        var session = CreateSession(out _);

        session.AddSeries();
        session.AddCategory();
        session.SeriesCount.Should().Be(3);
        session.CategoryCount.Should().Be(4);

        session.SwitchRowsAndColumns();

        session.SeriesCount.Should().Be(4);
        session.CategoryCount.Should().Be(3);
        session.BuildCommitPlan().Categories.Should().Equal("Sales", "Budget", "Series 3");
    }

    [Fact]
    public void TryApplyEdits_RejectsFirstInvalidValueWithoutPartialApplication()
    {
        var session = CreateSession(out _);
        var edits = new ChartDataDialogEdits(
            [new ChartDataDialogSeriesNameEdit(0, "Forecast")],
            [new ChartDataDialogCategoryEdit(0, "January")],
            [
                new ChartDataDialogValueEdit(0, 0, "12.5"),
                new ChartDataDialogValueEdit(1, 1, "not-a-number"),
            ]);

        session.TryApplyEdits(edits, CultureInfo.InvariantCulture, out var validation)
            .Should().BeFalse();

        validation.IsValid.Should().BeFalse();
        validation.InvalidValueEditIndex.Should().Be(1);
        validation.Message.Should().Be(ChartDataDialogPlanner.InvalidNumericValueMessage);
        var commit = session.BuildCommitPlan();
        commit.SeriesNames.Should().Equal("Sales", "Budget");
        commit.Categories.Should().Equal("Q1", "Q2", "Q3");
        commit.Values[0][0].Should().Be(1.0);
    }

    [Fact]
    public void TryApplyEdits_AppliesNamesCategoriesAndCultureAwareNullableValues()
    {
        var session = CreateSession(out _);
        var french = CultureInfo.GetCultureInfo("fr-FR");
        var edits = new ChartDataDialogEdits(
            [new ChartDataDialogSeriesNameEdit(1, "Actual")],
            [new ChartDataDialogCategoryEdit(1, "Second")],
            [
                new ChartDataDialogValueEdit(0, 1, "12,5"),
                new ChartDataDialogValueEdit(1, 0, "   "),
            ]);

        session.TryApplyEdits(edits, french, out var validation).Should().BeTrue();

        validation.Should().Be(ChartDataDialogValidationDecision.Valid);
        var commit = session.BuildCommitPlan();
        commit.SeriesNames.Should().Equal("Sales", "Actual");
        commit.Categories.Should().Equal("Q1", "Second", "Q3");
        commit.Values[0].Should().Equal(new double?[] { 1.0, 12.5, 3.0 });
        commit.Values[1].Should().Equal(new double?[] { null, null, 6.0 });
    }

    [Fact]
    public void TryCommit_AppliesOneEditorCommandOnlyAfterValidation()
    {
        var session = CreateSession(out var editor);
        session.SetChartType(ChartType.LineMarkers);
        var invalid = new ChartDataDialogEdits(
            [],
            [],
            [new ChartDataDialogValueEdit(0, 0, "invalid")]);

        session.TryCommit(invalid, CultureInfo.InvariantCulture, out _).Should().BeFalse();
        editor.SelectedChart!.ChartType.Should().Be(ChartType.ColumnClustered);
        editor.SelectedChart.Series[0].Values[0].Should().Be(1.0);

        var valid = new ChartDataDialogEdits(
            [new ChartDataDialogSeriesNameEdit(0, "Forecast")],
            [new ChartDataDialogCategoryEdit(0, "January")],
            [new ChartDataDialogValueEdit(0, 0, "42")]);
        session.TryCommit(valid, CultureInfo.InvariantCulture, out var validation)
            .Should().BeTrue();

        validation.Should().Be(ChartDataDialogValidationDecision.Valid);
        editor.SelectedChart.ChartType.Should().Be(ChartType.LineMarkers);
        editor.SelectedChart.Categories[0].Should().Be("January");
        editor.SelectedChart.Series[0].Name.Should().Be("Forecast");
        editor.SelectedChart.Series[0].Values[0].Should().Be(42.0);
    }

    private static ChartDataDialogSession CreateSession(out EditingSession editor)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["Q1", "Q2", "Q3"]);

        var sales = new ChartSeries { Name = "Sales" };
        sales.Values.AddRange([1.0, 2.0, 3.0]);
        chart.Series.Add(sales);

        var budget = new ChartSeries { Name = "Budget" };
        budget.Values.AddRange([4.0, null, 6.0]);
        chart.Series.Add(budget);

        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Chart",
            Kind = SlideShapeKind.Chart,
            Chart = chart,
        });
        presentation.Slides.Add(slide);
        editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(42);
        return new ChartDataDialogSession(editor);
    }
}
