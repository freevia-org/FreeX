namespace FreeW.Core.Model.Tests;

public sealed class PictureContentControlModelTests
{
    [Fact]
    public void Factory_CreatesPictureControlOverExactImage()
    {
        var image = new InlineImage([1, 2, 3], 96, 48);

        var run = Run.PictureControl(image, tag: "HeroPicture", alias: "Hero picture");

        run.Text.Should().BeEmpty();
        run.Image.Should().BeSameAs(image);
        run.Control.Should().Be(new ContentControl(
            ContentControlKind.Picture,
            Tag: "HeroPicture",
            Alias: "Hero picture"));
    }
}
