using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class PictureInsertionPlacementPlannerTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void CreateInsertPictureCommand_UsesDecodedNativeImageSize()
    {
        var workbook = new Workbook("Picture sizing");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 3, 2);
        var command = PictureInsertionPlacementPlanner.CreateInsertPictureCommand(
            sheet.Id,
            anchor,
            [1, 2, 3, 4],
            "image/png",
            new PictureInsertionSize(41, 29));

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Anchor.Should().Be(anchor);
        picture.Width.Should().BeApproximately(41, 0.01);
        picture.Height.Should().BeApproximately(29, 0.01);
    }

    [Theory]
    [InlineData(double.NaN, 29)]
    [InlineData(41, double.PositiveInfinity)]
    [InlineData(0, 29)]
    [InlineData(41, -1)]
    public void NormalizeSize_RejectsInvalidDimensions(double width, double height)
    {
        PictureInsertionPlacementPlanner.NormalizeSize(width, height).Should().BeNull();
    }

    [Fact]
    public void CreateInsertPictureCommand_FallsBackToDefaultSizeWhenDecodeFails()
    {
        var workbook = new Workbook("Picture fallback sizing");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 3, 2);
        var command = PictureInsertionPlacementPlanner.CreateInsertPictureCommand(
            sheet.Id,
            anchor,
            [1, 2, 3, 4],
            "image/png");

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Width.Should().Be(PictureInsertionPlacementPlanner.DefaultPictureWidth);
        picture.Height.Should().Be(PictureInsertionPlacementPlanner.DefaultPictureHeight);
    }
}
