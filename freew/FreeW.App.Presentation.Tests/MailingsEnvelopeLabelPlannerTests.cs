using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class MailingsEnvelopeLabelPlannerTests
{
    [Fact]
    public void EnvelopeCatalog_ExposesWordStylePresetsInDisplayOrder()
    {
        var sizes = MailingsEnvelopeLabelPlanner.GetEnvelopeSizes();

        sizes.Select(size => size.Name)
            .Should()
            .Equal(
                "DL  (110 \u00d7 220 mm)",
                "C5  (162 \u00d7 229 mm)",
                "C6  (114 \u00d7 162 mm)",
                "Comm-10 (4.125 \u00d7 9.5 in)",
                "Monarch (3.875 \u00d7 7.5 in)");
    }

    [Fact]
    public void PlanEnvelope_UsesPortraitDimensionsWithLandscapeOutput()
    {
        var dl = MailingsEnvelopeLabelPlanner.PlanEnvelope(MailingsEnvelopeLabelPlanner.DefaultEnvelopeIndex);
        var comm10 = MailingsEnvelopeLabelPlanner.PlanEnvelope(3);

        dl.WidthPt.Should().BeApproximately(311.811, 0.01);
        dl.HeightPt.Should().BeApproximately(623.622, 0.01);
        dl.MarginPt.Should().Be(18);
        dl.Landscape.Should().BeTrue();

        comm10.WidthPt.Should().BeApproximately(297, 0.01);
        comm10.HeightPt.Should().BeApproximately(684, 0.01);
        comm10.Landscape.Should().BeTrue();
    }

    [Fact]
    public void LabelCatalog_ExposesPresetAndCustomRows()
    {
        var presets = MailingsEnvelopeLabelPlanner.GetLabelPresets();

        presets.Should().HaveCount(5);
        presets[0].Name.Should().Be("Avery 5160 \u2014 3 \u00d7 10 (Letter)");
        presets[0].Rows.Should().Be(10);
        presets[0].Columns.Should().Be(3);
        presets[3].Name.Should().Be("Avery L7160 \u2014 3 \u00d7 7 (A4)");
        presets[3].Rows.Should().Be(7);
        presets[3].Columns.Should().Be(3);
        presets[MailingsEnvelopeLabelPlanner.CustomLabelPresetIndex].IsCustom.Should().BeTrue();
    }

    [Fact]
    public void PlanLabel_UsesPresetGeometry()
    {
        var plan = MailingsEnvelopeLabelPlanner.PlanLabel(0, customRowsText: null, customColumnsText: null);

        plan.Success.Should().BeTrue();
        plan.Result.Should().NotBeNull();
        plan.Result!.Value.Rows.Should().Be(10);
        plan.Result.Value.Columns.Should().Be(3);
        plan.Result.Value.PageWidthPt.Should().Be(612);
        plan.Result.Value.PageHeightPt.Should().Be(792);
        plan.Result.Value.MarginPt.Should().Be(18);
        plan.Result.Value.Landscape.Should().BeFalse();
    }

    [Fact]
    public void PlanLabel_UsesCustomGridOnLetter()
    {
        var plan = MailingsEnvelopeLabelPlanner.PlanLabel(
            MailingsEnvelopeLabelPlanner.CustomLabelPresetIndex,
            "4",
            "2");

        plan.Success.Should().BeTrue();
        plan.Result.Should().NotBeNull();
        plan.Result!.Value.Rows.Should().Be(4);
        plan.Result.Value.Columns.Should().Be(2);
        plan.Result.Value.PageWidthPt.Should().Be(612);
        plan.Result.Value.PageHeightPt.Should().Be(792);
    }

    [Theory]
    [InlineData("", "2")]
    [InlineData("0", "2")]
    [InlineData("4", "0")]
    [InlineData("x", "2")]
    [InlineData("4", "x")]
    public void PlanLabel_RejectsInvalidCustomGrid(string rows, string columns)
    {
        var plan = MailingsEnvelopeLabelPlanner.PlanLabel(
            MailingsEnvelopeLabelPlanner.CustomLabelPresetIndex,
            rows,
            columns);

        plan.Success.Should().BeFalse();
        plan.Issue.Should().Be(LabelSetupIssue.InvalidCustomGrid);
        plan.Result.Should().BeNull();
    }
}
