using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class OleActivationServiceTests
{
    [Fact]
    public void TryActivate_EmptyPayload_ReturnsFalse()
    {
        OleActivationService.TryActivate(new OleObjectInfo()).Should().BeFalse();
    }

    [Theory]
    [InlineData(".XLSX", "xlsx")]
    [InlineData("docx", "docx")]
    [InlineData("../../payload", "bin")]
    [InlineData("", "bin")]
    public void ResolveExtension_NormalizesEmbeddedExtension(string extension, string expected)
    {
        OleActivationService.ResolveExtension(new OleObjectInfo
        {
            EmbeddedExtension = extension,
        }).Should().Be(expected);
    }

    [Fact]
    public void ResolveExtension_UsesContentTypeWhenExtensionIsUnknown()
    {
        OleActivationService.ResolveExtension(new OleObjectInfo
        {
            EmbeddedExtension = "bin",
            EmbeddedContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        }).Should().Be("xlsx");
    }
}
