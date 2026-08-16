namespace FreeP.App.Compositor.Tests;

public sealed class PresentationOutputFileNameStemOwnershipTests
{
    [Theory]
    [InlineData("PresentationPrintOutputPackageExecutor.cs")]
    [InlineData("PresentationVideoFramePackageExecutor.cs")]
    [InlineData("PresentationImageExportExecutor.cs")]
    public void OutputExecutors_DelegateFilenameStemNormalizationToSharedIo(string fileName)
    {
        var source = TestWorkspaceFileLocator.ReadAllText(
            "freep",
            "FreeP.App.Presentation",
            fileName);

        source.Should().Contain("OutputFileNameStemPolicy.Normalize(");
        source.Should().NotContain("Path.GetFileNameWithoutExtension");
        source.Should().NotContain("Path.GetInvalidFileNameChars");
    }
}
