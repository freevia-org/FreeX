using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class TableFormattingDialogPolicyDedupSourceTests
{
    [Theory]
    [InlineData("TablePropertiesDialog.cs", "TablePropertiesDialogPlanner.TryBuildResult(")]
    [InlineData("BordersAndShadingDialog.cs", "BordersAndShadingDialogPlanner.TryBuildResult(")]
    public void Dialogs_RouteResultConstructionThroughPresentationPlanners(string fileName, string plannerCall)
    {
        var source = ReadHostSource(fileName);

        source.Should().Contain(plannerCall);
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
        var path = Path.Combine(FindRepositoryRoot(), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
