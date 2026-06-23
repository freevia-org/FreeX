using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class CreateTableDialogPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void TryParse_ParsesRangeHeaderFlagAndTrimmedStyle()
    {
        CreateTableDialogPlanner.TryParse(
                SheetId,
                " A1:C12 ",
                firstRowHasHeaders: false,
                tableStyleName: " TableStyleMedium2 ",
                out var plan,
                out var errorKey)
            .Should().BeTrue(errorKey);

        plan.Range.Should().Be(new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 12, 3)));
        plan.FirstRowHasHeaders.Should().BeFalse();
        plan.TableStyleName.Should().Be("TableStyleMedium2");
        errorKey.Should().BeNull();
    }

    [Theory]
    [InlineData("", CreateTableDialogPlanner.MissingRangeMessageKey)]
    [InlineData("A1", CreateTableDialogPlanner.MinimumRowsMessageKey)]
    [InlineData("A1:C1", CreateTableDialogPlanner.MinimumRowsMessageKey)]
    [InlineData("bad", CreateTableDialogPlanner.InvalidRangeMessageKey)]
    public void TryParse_RejectsInvalidTableRange(string rangeText, string expectedErrorKey)
    {
        CreateTableDialogPlanner.TryParse(
                SheetId,
                rangeText,
                firstRowHasHeaders: true,
                tableStyleName: "TableStyleMedium2",
                out _,
                out var errorKey)
            .Should().BeFalse();

        errorKey.Should().Be(expectedErrorKey);
    }

    [Fact]
    public void DialogContract_UsesStableWindowsSizedSurfaceAndAutomationIds()
    {
        CreateTableDialogPlanner.Width.Should().Be(360);
        CreateTableDialogPlanner.Height.Should().Be(190);
        CreateTableDialogPlanner.ButtonWidth.Should().Be(76);
        CreateTableDialogPlanner.DialogAutomationId.Should().Be("CreateTableDialog");
        CreateTableDialogPlanner.RangeBoxAutomationId.Should().Be("CreateTableRangeBox");
        CreateTableDialogPlanner.HeadersBoxAutomationId.Should().Be("CreateTableHeadersBox");
    }
}
