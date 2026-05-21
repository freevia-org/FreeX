using FluentAssertions;
using Freexcel.App.Host;
using Xunit;

namespace Freexcel.App.Host.Tests;

public sealed class FormulaInlineEditorLayoutPlannerTests
{
    [Fact]
    public void Create_MasksTheWholeCellAndPlacesTextOverlayInsideEditorChrome()
    {
        var layout = FormulaInlineEditorLayoutPlanner.Create(
            cellLeft: 100,
            cellTop: 40,
            cellWidth: 64,
            cellHeight: 20);

        layout.EditorRect.Left.Should().Be(98);
        layout.EditorRect.Top.Should().Be(38);
        layout.EditorRect.Width.Should().Be(68);
        layout.EditorRect.Height.Should().Be(24);

        layout.TextOverlayRect.Left.Should().Be(101);
        layout.TextOverlayRect.Top.Should().Be(41);
        layout.TextOverlayRect.Right.Should().BeLessThan(layout.EditorRect.Right);
        layout.TextOverlayRect.Bottom.Should().BeLessThanOrEqualTo(layout.EditorRect.Bottom);
    }
}
