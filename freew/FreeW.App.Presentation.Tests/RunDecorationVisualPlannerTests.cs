using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class RunDecorationVisualPlannerTests
{
    [Fact]
    public void Build_UsesCharacterShadingAheadOfHighlight()
    {
        var plan = RunDecorationVisualPlanner.Build(new RunFormatting
        {
            HighlightColorHex = "#FFFF00",
            CharacterShadingHex = "#92D050",
            CharacterShadingPattern = ShadingPattern.Pct25,
        });

        plan.BackgroundColorHex.Should().Be("#92D050");
        plan.BackgroundIsCharacterShading.Should().BeTrue();
        plan.CharacterShadingPattern.Should().Be(ShadingPattern.Pct25);
    }

    [Fact]
    public void Build_UsesHighlightWhenCharacterShadingIsMissing()
    {
        var plan = RunDecorationVisualPlanner.Build(new RunFormatting
        {
            HighlightColorHex = "#FFFF00",
        });

        plan.BackgroundColorHex.Should().Be("#FFFF00");
        plan.BackgroundIsCharacterShading.Should().BeFalse();
        plan.CharacterShadingPattern.Should().Be(ShadingPattern.Clear);
    }

    [Fact]
    public void Build_ExpandsBottomOnlyCharacterBorderToBottomEdge()
    {
        var border = new ParagraphBorder("#0070C0", 0.25, BottomOnly: true)
        {
            LineStyle = BorderLineStyle.Dashed,
        };

        var plan = RunDecorationVisualPlanner.Build(new RunFormatting
        {
            CharacterBorder = border,
        }, dipPerPoint: 4.0 / 3.0);

        plan.HasBorder.Should().BeTrue();
        plan.Border.Should().Be(border);
        plan.DrawTopBorder.Should().BeFalse();
        plan.DrawLeftBorder.Should().BeFalse();
        plan.DrawBottomBorder.Should().BeTrue();
        plan.DrawRightBorder.Should().BeFalse();
        plan.BorderWidthDip.Should().Be(RunDecorationVisualPlanner.MinimumBorderWidthDip);
    }

    [Fact]
    public void Build_PreservesFullCharacterBorderEdges()
    {
        var border = new ParagraphBorder("#C00000", 1.5)
        {
            LineStyle = BorderLineStyle.Double,
            Left = false,
        };

        var plan = RunDecorationVisualPlanner.Build(new RunFormatting
        {
            CharacterBorder = border,
        }, dipPerPoint: 2);

        plan.DrawTopBorder.Should().BeTrue();
        plan.DrawLeftBorder.Should().BeFalse();
        plan.DrawBottomBorder.Should().BeTrue();
        plan.DrawRightBorder.Should().BeTrue();
        plan.BorderWidthDip.Should().Be(3);
    }
}
