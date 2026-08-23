namespace FreeP.App.Compositor.Tests;

public sealed class OoxmlOnOffLexicalAdoptionTests
{
    [Fact]
    public void FreePReadersDelegateOnOffTokenPolicyToSharedOpc()
    {
        var omml = Read("freep", "FreeP.App.Presentation", "Math", "OmmlParser.cs");
        var packageReader = Read("freep", "FreeP.Core.IO", "PptxPackageReader.cs");

        omml.Should().Contain("OoxmlOnOffLexical.Parse(");
        omml.Should().NotContain("\"0\" or \"false\" or \"off\" => false");
        packageReader.Should().Contain("OoxmlOnOffLexical.Parse(");
        packageReader.Should().NotContain("\"0\" or \"false\" or \"off\" => false");
    }

    private static string Read(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}
