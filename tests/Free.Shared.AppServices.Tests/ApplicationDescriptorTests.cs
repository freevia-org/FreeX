namespace Free.Shared.AppServices.Tests;

public sealed class ApplicationFrameDescriptorTests
{
    [Fact]
    public void FrameDescriptor_CreatesCanonicalApplicationTitlePolicy()
    {
        var descriptor = ApplicationFrameDescriptor.Create("FreeP", "Untitled");

        descriptor.Title.Should().Be(new ApplicationWindowTitleSpec(
            ApplicationName: "FreeP",
            DefaultDocumentDisplayName: "Untitled",
            DirtyMarker: " *",
            Separator: " \u2014 ",
            ApplicationPlacement: WindowTitleApplicationPlacement.DocumentThenApplication));
    }

    [Fact]
    public void FrameDescriptor_PreservesProductSpecificTitleConventions()
    {
        var descriptor = ApplicationFrameDescriptor.Create(
            "FreeX",
            "Book1",
            dirtyMarker: "*",
            separator: " - ");

        ApplicationWindowTitlePolicy.Compose(descriptor.Title, "Budget", isDirty: true)
            .Should().Be("Budget* - FreeX");
    }

    [Fact]
    public void FrameDescriptor_PrefersOptionsStoreDirectoryForStatusLabels()
    {
        var descriptor = ApplicationFrameDescriptor.Create("FreeW", "Untitled");
        var optionsStorePath = Path.Combine("root", "FreeW", "options.json");

        descriptor.ResolveDataFolderLabel(optionsStorePath, new ThrowingPathProvider())
            .Should().Be(Path.GetDirectoryName(optionsStorePath));
    }

    [Theory]
    [InlineData("src", "FreeX.App.Presentation", "Shell", "WorkbookTitleFormatter.cs")]
    [InlineData("freew", "FreeW.App.Presentation", "Shell", "FreeWApplicationFrameDescriptor.cs")]
    [InlineData("freep", "FreeP.App.Presentation", "FreePApplicationFrameDescriptor.cs")]
    public void ProductFrameWrappers_DelegateCanonicalPolicy(params string[] pathParts)
    {
        var source = TestWorkspaceFileLocator.ReadAllText(pathParts);

        source.Should().Contain("ApplicationFrameDescriptor.Create(");
        source.Should().NotContain("new ApplicationWindowTitleSpec(");
    }

    private sealed class ThrowingPathProvider : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() =>
            throw new InvalidOperationException("The configured options path should win.");
    }
}
