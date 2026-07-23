using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class CellShadingDialogPlannerTests
{
    [Fact]
    public void SelectPaletteColor_returns_the_shared_Wpf_palette_hex()
    {
        var result = CellShadingDialogPlanner.SelectPaletteColor(2);

        result.Accepted.Should().BeTrue();
        result.Hex.Should().Be("#00B0F0");
    }

    [Fact]
    public void SelectNoColor_is_an_accepted_clear_result()
    {
        var result = CellShadingDialogPlanner.SelectNoColor();

        result.Accepted.Should().BeTrue();
        result.Hex.Should().BeNull();
    }

    [Fact]
    public void Cancel_is_distinct_from_an_accepted_clear_result()
    {
        var result = CellShadingDialogPlanner.Cancel();

        result.Accepted.Should().BeFalse();
        result.Hex.Should().BeNull();
    }
}
