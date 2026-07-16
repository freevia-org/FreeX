using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class CellSurfaceGridlinePlannerTests
{
    [Fact]
    public void HasVisibleFill_IsFalseForNullOrUnfilledStyle()
    {
        CellSurfaceGridlinePlanner.HasVisibleFill(null, WorkbookTheme.Office).Should().BeFalse();
        CellSurfaceGridlinePlanner.HasVisibleFill(new CellStyle(), WorkbookTheme.Office).Should().BeFalse();
    }

    [Fact]
    public void HasVisibleFill_RecognizesSolidThemeGradientAndPatternFills()
    {
        CellSurfaceGridlinePlanner.HasVisibleFill(
            new CellStyle { FillColor = new CellColor(1, 2, 3) }, WorkbookTheme.Office).Should().BeTrue();
        CellSurfaceGridlinePlanner.HasVisibleFill(
            new CellStyle { FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1) }, WorkbookTheme.Office).Should().BeTrue();
        CellSurfaceGridlinePlanner.HasVisibleFill(
            new CellStyle { GradientFill = new CellGradientFill() }, WorkbookTheme.Office).Should().BeTrue();
        CellSurfaceGridlinePlanner.HasVisibleFill(
            new CellStyle { FillPatternStyle = CellFillPatternStyle.Gray125 }, WorkbookTheme.Office).Should().BeTrue();
    }
}
