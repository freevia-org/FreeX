using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class InsertPictureCommandFactoryTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Theory]
    [InlineData("photo.png", "image/png")]
    [InlineData("PHOTO.PNG", "image/png")]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("a.bmp", "image/bmp")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.tiff", "image/tiff")]
    public void ContentTypeForPath_MapsKnownExtensions(string path, string expected) =>
        InsertPictureCommandFactory.ContentTypeForPath(path).Should().Be(expected);

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.xlsx")]
    [InlineData("noextension")]
    public void ContentTypeForPath_UnsupportedReturnsNull(string path) =>
        InsertPictureCommandFactory.ContentTypeForPath(path).Should().BeNull();

    [Fact]
    public void IsSupportedImagePath_TrueOnlyForImages()
    {
        InsertPictureCommandFactory.IsSupportedImagePath("a.png").Should().BeTrue();
        InsertPictureCommandFactory.IsSupportedImagePath("a.docx").Should().BeFalse();
    }

    [Fact]
    public void Build_NonPositiveSize_FallsBackToDefault_AndAddsPictureOnApply()
    {
        var workbook = new Workbook("Pics");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);

        var command = InsertPictureCommandFactory.Build(
            sheet.Id, anchor, [1, 2, 3, 4], "image/png", width: 0, height: -5);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Width.Should().Be(InsertPictureCommandFactory.DefaultWidth);
        picture.Height.Should().Be(InsertPictureCommandFactory.DefaultHeight);
        picture.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void Build_PositiveSize_IsPreserved()
    {
        var workbook = new Workbook("Pics");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 2, 3);

        var command = InsertPictureCommandFactory.Build(
            sheet.Id, anchor, [9], "image/jpeg", width: 320, height: 200);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.Pictures[0].Width.Should().Be(320);
        sheet.Pictures[0].Height.Should().Be(200);
    }
}
