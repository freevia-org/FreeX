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

    [Fact]
    public void Layout_is_shared_by_both_palette_hosts()
    {
        var layout = CellShadingDialogPlanner.Layout;

        layout.PanelMargin.Should().Be(8);
        layout.PaletteWidth.Should().Be(156);
        layout.SwatchSize.Should().Be(22);
        layout.SwatchMargin.Should().Be(2);
        layout.ClearTopMargin.Should().Be(6);
        layout.ClearHorizontalMargin.Should().Be(2);
        layout.ClearHorizontalPadding.Should().Be(8);
        layout.SwatchBorderHex.Should().Be("#808080");
    }

    [Fact]
    public void Palette_has_twelve_choices_and_rejects_out_of_range_selection()
    {
        CellShadingDialogPlanner.Palette.Should().HaveCount(12);
        var act = () => CellShadingDialogPlanner.SelectPaletteColor(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
