using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class IconPickerDialogPolicySourceGuardTests
{
    [Fact]
    public void WpfIconPickerDelegatesPortableWorkflowAndKeepsRasterizationLocal()
    {
        var source = ReadHostSource("IconPickerDialog.cs");

        source.Should().Contain("new IconPickerDialogSession(");
        source.Should().Contain("IconPickerCatalog.LoadFromBaseDirectory(");
        source.Should().Contain("_session.ApplyFilter(");
        source.Should().Contain("_session.Select(");
        source.Should().Contain("_session.PlanAccept(");
        source.Should().Contain("SharpVectors.Converters.FileSvgReader");
        source.Should().Contain("SvgRasterizerHelper.RasterizeToInlineImage(");
        source.Should().NotContain("ContentIconCatalog");
        source.Should().NotContain("Directory.Enumerate");
        source.Should().NotContain("TitleCase(");
        source.Should().NotContain("IconPickerDialogPlanner.Filter(");
    }

    [Fact]
    public void WpfOnlyCatalogImplementationHasBeenRetired()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        File.Exists(Path.Combine(root, "freew", "FreeW.App.Host", "ContentIconCatalog.cs"))
            .Should().BeFalse();
    }

    private static string ReadHostSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", fileName));
    }
}
