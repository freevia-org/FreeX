using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed class FormControlRenderPlannerTests
{
    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheet = SheetId.New();
        return new GridRange(
            new CellAddress(sheet, startRow, startCol),
            new CellAddress(sheet, endRow, endCol));
    }

    [Fact]
    public void TryCreateAnchorRange_ConvertsOneBasedGridRangeToZeroBasedDrawingAnchor()
    {
        var control = new FormControlModel { Anchor = Range(3, 2, 5, 4) };

        var created = FormControlRenderPlanner.TryCreateAnchorRange(control, out var anchor);

        created.Should().BeTrue();
        anchor!.From.Column.Should().Be(1);
        anchor.From.Row.Should().Be(2);
        anchor.To.Column.Should().Be(3);
        anchor.To.Row.Should().Be(4);
        anchor.From.ColumnOffsetEmu.Should().Be(0);
        anchor.From.RowOffsetEmu.Should().Be(0);
    }

    [Fact]
    public void TryCreateAnchorRange_ReturnsFalseWhenAnchorMissing()
    {
        var control = new FormControlModel { Anchor = null };

        FormControlRenderPlanner.TryCreateAnchorRange(control, out var anchor)
            .Should().BeFalse();
        anchor.Should().BeNull();
    }

    [Theory]
    [InlineData(FormControlKind.CheckBox, true)]
    [InlineData(FormControlKind.OptionButton, true)]
    [InlineData(FormControlKind.Spinner, true)]
    [InlineData(FormControlKind.ScrollBar, true)]
    [InlineData(FormControlKind.Label, true)]
    [InlineData(FormControlKind.GroupBox, true)]
    [InlineData(FormControlKind.Button, false)]
    [InlineData(FormControlKind.DropDown, false)]
    [InlineData(FormControlKind.ListBox, false)]
    [InlineData(FormControlKind.Unknown, false)]
    public void IsRenderable_MatchesImplementedControlKinds(FormControlKind kind, bool expected)
    {
        FormControlRenderPlanner.IsRenderable(kind).Should().Be(expected);
    }

    [Fact]
    public void GetCaption_PrefersExplicitNameOverFallback()
    {
        var control = new FormControlModel { Kind = FormControlKind.CheckBox, Name = "Include weekends" };

        FormControlRenderPlanner.GetCaption(control).Should().Be("Include weekends");
    }

    [Fact]
    public void GetCaption_FallsBackToKindWhenNameMissing()
    {
        var control = new FormControlModel { Kind = FormControlKind.OptionButton, Name = null };

        FormControlRenderPlanner.GetCaption(control).Should().Be("Option Button");
    }

    [Fact]
    public void GetCaption_TrimsWhitespace()
    {
        var control = new FormControlModel { Kind = FormControlKind.CheckBox, Name = "  Tax  " };

        FormControlRenderPlanner.GetCaption(control).Should().Be("Tax");
    }
}
