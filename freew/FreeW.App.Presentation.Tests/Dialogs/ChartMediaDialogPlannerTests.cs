using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class ChartMediaDialogPlannerTests
{
    [Fact]
    public void InsertChartChromeResolvesThroughSharedResources()
    {
        var text = InsertChartDialogPlanner.ResolveText(key => $"localized:{key}");
        var surface = InsertChartDialogPlanner.BuildSurface(key => $"localized:{key}");
        var actions = InsertChartDialogPlanner.BuildActionButtons(key => $"localized:{key}");

        text.Title.Should().Be("localized:InsertChart_Dialog_Title");
        text.CategoryColumnHeader.Should().Be("localized:InsertChart_Category_Header");
        text.AddRowLabel.Should().Be("localized:InsertChart_AddRow_Label");
        surface.Title.Should().Be(text.Title);
        surface.Field(InsertChartDialogField.Data).Label.Should().Be("localized:InsertChart_Data_Label");
        actions.Select(action => action.Label).Should().Equal("localized:Common_Ok", "localized:Common_Cancel");

        ChartTitleDialogPlanner.BuildSurface(key => $"localized:{key}").Title
            .Should().Be("localized:ChartTitle_Dialog_Title");
        ChartAxisTitlesDialogPlanner.BuildSurface(key => $"localized:{key}")
            .Field(ChartAxisTitlesDialogField.Value).Label
            .Should().Be("localized:ChartAxisTitles_Value_Label");
        ChartSizeDialogPlanner.BuildSurface(key => $"localized:{key}")
            .Field(ChartSizeDialogField.Width).Label
            .Should().Be("localized:ChartSize_Width_Label");
    }

    [Fact]
    public void TitleAndAxisPoliciesTrimAndClearWhitespace()
    {
        ChartTitleDialogPlanner.BuildResult("  Sales  ")
            .Should().Be(new ChartTitleDialogResult(true, "Sales"));
        ChartTitleDialogPlanner.NormalizeTitle(" ").Should().BeNull();

        ChartAxisTitlesDialogPlanner.BuildResult(" Categories ", " ")
            .Should().Be(new ChartAxisTitlesDialogResult("Categories", null));
    }

    [Fact]
    public void InsertChartBuildsDefaultStateAndModelFromRows()
    {
        var state = InsertChartDialogPlanner.BuildInitialState(null, CultureInfo.InvariantCulture);

        state.Kind.Should().Be(ChartKind.Column);
        state.Title.Should().Be(InsertChartDialogPlanner.DefaultTitle);
        state.SeriesNames.Should().Equal(InsertChartDialogPlanner.DefaultSeriesName);
        state.Rows.Select(row => row.Category).Should().Equal("Q1", "Q2", "Q3", "Q4");

        InsertChartDialogPlanner.TryBuildResult(
                ChartKind.Line,
                "  Revenue  ",
                ["Sales"],
                [
                    new InsertChartDialogRow(" Jan ", ["1.5"]),
                    new InsertChartDialogRow("Feb", ["bad"]),
                ],
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Kind.Should().Be(ChartKind.Line);
        result.Title.Should().Be("Revenue");
        result.Categories.Should().Equal("Jan", "Feb");
        result.Series.Should().ContainSingle();
        result.Series[0].Values.Should().Equal(1.5, 0.0);
    }

    [Fact]
    public void InsertChartActionPlan_matches_WPF_authority()
    {
        InsertChartDialogPlanner.ActionButtons
            .Select(button => (button.Label, button.IsDefault, button.IsCancel))
            .Should().Equal(
                ("OK", true, false),
                ("Cancel", false, true));
    }

    [Fact]
    public void InsertChartRejectsRowsWithoutCategoryOrValues()
    {
        InsertChartDialogPlanner.TryBuildResult(
                ChartKind.Column,
                null,
                ["Sales"],
                [new InsertChartDialogRow(" ", [""])],
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeFalse();

        result.Should().BeNull();
        error.Should().Be(InsertChartDialogPlanner.EmptyRowsValidationMessage);
    }

    [Fact]
    public void InsertChartInitialStatePreservesTheSelectedChartData()
    {
        var seed = Chart.Create(
            ChartKind.Bar,
            ["North", "South"],
            [12.5, 8.25],
            "Revenue",
            "Regional revenue");

        var state = InsertChartDialogPlanner.BuildInitialState(seed, CultureInfo.InvariantCulture);

        state.Kind.Should().Be(ChartKind.Bar);
        state.Title.Should().Be("Regional revenue");
        state.SeriesNames.Should().Equal("Revenue");
        state.Rows.Select(row => row.Category).Should().Equal("North", "South");
        state.Rows.Select(row => row.SeriesValues.Single()).Should().Equal("12.5", "8.25");
    }

    [Fact]
    public void SmartArtPlannerPreservesWpfSeedFlatteningAndValidation()
    {
        var seed = new SmartArt { Kind = SmartArtKind.Hierarchy };
        seed.Nodes.Add(new SmartArtNode("Root", [new SmartArtNode("Child")]));

        var initialState = SmartArtDialogPlanner.BuildInitialState(seed);
        initialState.Kind.Should().Be(SmartArtKind.Hierarchy);
        initialState.NodeTexts.Should().Equal("Root", "Child");

        SmartArtDialogPlanner.TryBuildResult(
                SmartArtKind.Process,
                [" First ", "", "Second"],
                out var result,
                out var error)
            .Should().BeTrue();
        error.Should().BeNull();
        result!.Nodes.Select(node => node.Text).Should().Equal("First", "Second");

        SmartArtDialogPlanner.TryBuildResult(
                SmartArtKind.List,
                [" ", ""],
                out result,
                out error)
            .Should().BeFalse();
        result.Should().BeNull();
        error.Should().Be(SmartArtDialogPlanner.EmptyNodesValidationMessage);
    }

    [Fact]
    public void SmartArtPlannerOwnsTheSharedNodeTextLabel()
    {
        SmartArtDialogPlanner.NodeTextLabel.Should()
            .Be("Diagram text (one item per node - use Add/Remove to manage):");
    }

    [Fact]
    public void SmartArtPlannerOwnsTheWpfAuthorityGeometry()
    {
        SmartArtDialogPlanner.VisualMetrics.Should().Be(new SmartArtDialogVisualMetrics(
            DialogWidth: 440,
            MinimumDialogHeight: 360,
            OuterMargin: 14,
            LabelBottomMargin: 4,
            LayoutControlBottomMargin: 10,
            NodeListHeight: 130,
            NodeListBottomMargin: 6,
            EditorBottomMargin: 6,
            InlineActionSpacing: 6,
            InlineActionBottomMargin: 10,
            FooterTopMargin: 4,
            FooterButtonWidth: 72,
            InlineButtonHorizontalPadding: 8,
            ButtonVerticalPadding: 3));
    }

    [Fact]
    public void IconPickerPlannerFiltersByCategoryAndSearch()
    {
        var entries = new[]
        {
            new IconPickerEntry("Arrow Right", "Arrows", "arrow right arrows", "arrows/arrow-right.svg"),
            new IconPickerEntry("Laptop", "Technology", "laptop technology", "technology/laptop.svg"),
        };

        IconPickerDialogPlanner.Filter(entries, IconPickerDialogPlanner.AllCategoriesLabel, "right")
            .Select(entry => entry.Name).Should().Equal("Arrow Right");
        IconPickerDialogPlanner.Filter(entries, "Technology", null)
            .Select(entry => entry.Name).Should().Equal("Laptop");
        IconPickerDialogPlanner.Select(entries[0])
            .Should().Be(new IconPickerSelection("Arrow Right", "Arrows", "arrows/arrow-right.svg"));
    }
}
