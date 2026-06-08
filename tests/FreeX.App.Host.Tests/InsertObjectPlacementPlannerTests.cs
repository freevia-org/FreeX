using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class InsertObjectPlacementPlannerTests
{
    [Fact]
    public void CreateInsertPictureCommand_UsesDecodedNaturalImageSize()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Picture sizing");
            var sheet = workbook.AddSheet("Sheet1");
            var anchor = new CellAddress(sheet.Id, 3, 2);
            var imageBytes = ImageTestData.CreatePngBytes(pixelWidth: 41, pixelHeight: 29);
            var command = InsertObjectPlacementPlanner.CreateInsertPictureCommand(
                sheet.Id,
                anchor,
                imageBytes,
                "image/png");

            command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

            var picture = sheet.Pictures.Should().ContainSingle().Subject;
            picture.Anchor.Should().Be(anchor);
            picture.Width.Should().BeApproximately(41, 0.01);
            picture.Height.Should().BeApproximately(29, 0.01);
        });
    }

    [Fact]
    public void CreateInsertPictureCommand_FallsBackToDefaultSizeForInvalidImageBytes()
    {
        var workbook = new Workbook("Picture fallback sizing");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 3, 2);
        var command = InsertObjectPlacementPlanner.CreateInsertPictureCommand(
            sheet.Id,
            anchor,
            [1, 2, 3, 4],
            "image/png");

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Width.Should().Be(InsertObjectPlacementPlanner.DefaultPictureWidth);
        picture.Height.Should().Be(InsertObjectPlacementPlanner.DefaultPictureHeight);
    }
}
