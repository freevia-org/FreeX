using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class InsertObjectPlannerTests
{
    [Theory]
    [InlineData("photo.png", "image/png")]
    [InlineData("scan.JPG", "image/jpeg")]
    [InlineData("anim.gif", "image/gif")]
    [InlineData("art.webp", "image/webp")]
    public void ImageContentTypeForPath_RecognizesImages(string path, string expected)
    {
        InsertObjectPlanner.ImageContentTypeForPath(path).Should().Be(expected);
        InsertObjectPlanner.IsEmbeddableImagePath(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("data.xlsx")]
    [InlineData("notes.txt")]
    [InlineData("noextension")]
    public void ImageContentTypeForPath_ReturnsNullForNonImages(string path)
    {
        InsertObjectPlanner.ImageContentTypeForPath(path).Should().BeNull();
        InsertObjectPlanner.IsEmbeddableImagePath(path).Should().BeFalse();
    }

    [Fact]
    public void TryPlan_RejectsMissingFilePath()
    {
        var ok = InsertObjectPlanner.TryPlan("  ", fileExists: true, linkToFile: false, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(InsertObjectValidationError.MissingFilePath);
    }

    [Fact]
    public void TryPlan_RejectsNonExistentFile()
    {
        var ok = InsertObjectPlanner.TryPlan("C:/tmp/x.pdf", fileExists: false, linkToFile: false, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(InsertObjectValidationError.FileNotFound);
    }

    [Fact]
    public void TryPlan_ImageFile_EmbedsAsPicture()
    {
        var ok = InsertObjectPlanner.TryPlan(
            "C:/pics/diagram.png", fileExists: true, linkToFile: false, out var plan, out var error);

        ok.Should().BeTrue();
        error.Should().Be(InsertObjectValidationError.None);
        plan.Rendering.Should().Be(InsertObjectRendering.EmbedImageAsPicture);
        plan.ImageContentType.Should().Be("image/png");
        plan.DisplayName.Should().Be("diagram.png");
        plan.LinkPath.Should().BeNull();
    }

    [Fact]
    public void TryPlan_NonImageFile_BecomesIconPlaceholder()
    {
        var ok = InsertObjectPlanner.TryPlan(
            "C:/docs/quarterly.pdf", fileExists: true, linkToFile: false, out var plan, out _);

        ok.Should().BeTrue();
        plan.Rendering.Should().Be(InsertObjectRendering.IconPlaceholder);
        plan.ImageContentType.Should().BeNull();
        plan.DisplayName.Should().Be("quarterly.pdf");
    }

    [Fact]
    public void TryPlan_LinkToFile_RecordsLinkPath()
    {
        var ok = InsertObjectPlanner.TryPlan(
            "C:/docs/quarterly.pdf", fileExists: true, linkToFile: true, out var plan, out _);

        ok.Should().BeTrue();
        plan.LinkToFile.Should().BeTrue();
        plan.LinkPath.Should().Be("C:/docs/quarterly.pdf");
    }

    [Fact]
    public void DisplayNameForPath_ReturnsFileNameOnly()
    {
        InsertObjectPlanner.DisplayNameForPath("C:/a/b/c.docx").Should().Be("c.docx");
    }
}
