using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class CaptionHeaderFooterDialogPlannerTests
{
    [Fact]
    public void Caption_plan_exposes_catalog_and_selects_default_label()
    {
        var plan = CaptionDialogPlanner.Build(CaptionLabel.Table);

        plan.Title.Should().Be("Insert Caption");
        plan.LabelPrompt.Should().Be("Label:");
        plan.CaptionPrompt.Should().Be("Caption:");
        plan.Choices.Select(choice => choice.Value).Should().Equal(
            CaptionLabel.Figure,
            CaptionLabel.Table,
            CaptionLabel.Equation);
        plan.Choices.Select(choice => choice.Label).Should().Equal("Figure", "Table", "Equation");
        plan.SelectedIndex.Should().Be(1);
    }

    [Fact]
    public void Caption_result_clamps_selection_and_normalizes_text()
    {
        CaptionDialogPlanner.BuildResult(99, "  Energy  ")
            .Should().Be(new CaptionDialogResult(CaptionLabel.Equation, "Energy"));
        CaptionDialogPlanner.BuildResult(-1, null)
            .Should().Be(new CaptionDialogResult(CaptionLabel.Figure, string.Empty));
    }

    [Theory]
    [InlineData(false, "Header", "Edit Header", "Header text:")]
    [InlineData(true, "Footer", "Edit Footer", "Footer text:")]
    public void Header_footer_text_plan_projects_surface_copy(
        bool footer,
        string initial,
        string expectedTitle,
        string expectedPrompt)
    {
        var plan = HeaderFooterTextDialogPlanner.Build(footer, initial);

        plan.Title.Should().Be(expectedTitle);
        plan.PromptLabel.Should().Be(expectedPrompt);
        plan.InitialText.Should().Be(initial);
    }

    [Fact]
    public void Header_footer_text_result_normalizes_null_to_empty()
    {
        HeaderFooterTextDialogPlanner.BuildResult(null).Should().BeEmpty();
        HeaderFooterTextDialogPlanner.BuildResult(" text ").Should().Be(" text ");
    }
}
