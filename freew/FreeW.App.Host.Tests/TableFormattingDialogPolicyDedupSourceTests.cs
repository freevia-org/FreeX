using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class TableFormattingDialogPolicyDedupSourceTests
{
    [Theory]
    [InlineData("TablePropertiesDialog.cs", "_session.PlanAcceptance(")]
    [InlineData("BordersAndShadingDialog.cs", "_session.PlanAcceptance(")]
    public void Dialogs_RouteResultConstructionThroughPresentationPolicy(string fileName, string policyCall)
    {
        var source = ReadHostSource(fileName);

        source.Should().Contain(policyCall);
        source.Should().Contain("FreeW.App.Presentation.Dialogs");
    }

    [Fact]
    public void TablePropertiesDialog_DoesNotOwnMeasurementParsingOrResultConstruction()
    {
        var source = ReadHostSource("TablePropertiesDialog.cs");

        source.Should().NotContain("new TablePropertiesValues(");
        source.Should().NotContain("TryReadOptional(");
        source.Should().NotContain("double.TryParse(");
    }

    [Fact]
    public void BordersAndShadingDialog_DoesNotOwnBorderSelectionPolicy()
    {
        var source = ReadHostSource("BordersAndShadingDialog.cs");

        source.Should().NotContain("new ParagraphBorder(");
        source.Should().NotContain("new PageBorder(");
        source.Should().NotContain("double.TryParse(");
        source.Should().NotContain("private static int SettingIndexFor");
        source.Should().NotContain("private ParagraphBorder?");
        source.Should().NotContain("private PageBorder?");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

}
