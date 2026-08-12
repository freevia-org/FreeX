using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class EmbeddedObjectVisualPlannerTests
{
    [Fact]
    public void Build_uses_one_icon_label_size_and_colour_contract()
    {
        var icon = new InlineImage([1, 2, 3], 72, 48) { AltText = "Quarterly workbook" };
        var embedded = EmbeddedObject.Create(
            [4, 5, 6],
            "Excel.Sheet.12",
            icon,
            widthPt: 144,
            heightPt: 90);

        var plan = EmbeddedObjectVisualPlanner.Build(embedded);

        plan.WidthPt.Should().Be(144);
        plan.HeightPt.Should().Be(90);
        plan.Label.Should().Be("Excel.Sheet.12");
        plan.AccessibleName.Should().Be("Quarterly workbook");
        plan.HelpText.Should().Be("Embedded Excel.Sheet.12 object");
        plan.Icon.Should().BeSameAs(icon);
        plan.BackgroundColorHex.Should().Be("#F3F6FB");
        plan.BorderColorHex.Should().Be("#C0C8D8");
        plan.ForegroundColorHex.Should().Be("#404040");
    }

    [Fact]
    public void Build_normalizes_invalid_sizes_and_describes_linked_fallback()
    {
        var linked = EmbeddedObject.CreateLinked("book.xlsx", " ");
        linked.WidthPt = double.NaN;
        linked.HeightPt = 0;

        var plan = EmbeddedObjectVisualPlanner.Build(linked);

        plan.WidthPt.Should().Be(EmbeddedObjectVisualPlanner.DefaultSizePt);
        plan.HeightPt.Should().Be(EmbeddedObjectVisualPlanner.DefaultSizePt);
        plan.Label.Should().Be("Embedded object");
        plan.AccessibleName.Should().Be("Embedded object");
        plan.HelpText.Should().Be("Linked embedded object");
        plan.Icon.Should().BeNull();
    }
}
