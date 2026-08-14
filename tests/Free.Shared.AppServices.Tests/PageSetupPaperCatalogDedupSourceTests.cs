using System.IO;
using System.Text.RegularExpressions;

using Free.Shared.PageSetup;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// Guards the single paper-size table. FreeX (inches, OOXML codes) and FreeW (points) both project
/// from <see cref="PaperSizeCatalog"/>; neither may re-declare its own dimensions or code map.
/// </summary>
public sealed class PageSetupPaperCatalogDedupSourceTests
{
    private static readonly string[] FreeXPageLayoutModel = ["src", "FreeX.Core.Model", "PageLayout.cs"];
    private static readonly string[] FreeWPageSetupPlanner =
        ["freew", "FreeW.App.Presentation", "Dialogs", "PageSetupDialogPlanner.cs"];

    [Fact]
    public void FreeXCoreModel_ProjectsFromTheSharedCatalogInsteadOfItsOwnInchTable()
    {
        var source = ReadWorkspaceSource(FreeXPageLayoutModel);

        source.Should().Contain("using Free.Shared.PageSetup;");
        source.Should().Contain("PaperSizeCatalog.GetSizeInches(");
        source.Should().Contain("PaperSizeCatalog.GetOoxmlCode(");
        source.Should().Contain("PaperSizeCatalog.TryGetSizeFromOoxmlCode(");

        // The retired literal inch table and the retired OOXML code dictionaries.
        source.Should().NotContain("(8.5,  11.0)");
        source.Should().NotContain("(11.69, 16.54)");
        source.Should().NotContain("(8.27, 11.69)");
        source.Should().NotContain("new Dictionary<int, WorksheetPaperSize>");
        source.Should().NotContain("new Dictionary<WorksheetPaperSize, int>");
    }

    [Fact]
    public void FreeWPageSetupPlanner_ProjectsFromTheSharedCatalogInsteadOfItsOwnPointTable()
    {
        var source = ReadWorkspaceSource(FreeWPageSetupPlanner);

        source.Should().Contain("using Free.Shared.PageSetup;");
        source.Should().Contain("PaperSizeCatalog.GetSizePoints(");
        source.Should().Contain("PageOrientationRules.");
        source.Should().Contain("PageMarginTextPolicy.");

        // The retired literal point table: every named row used to carry its dimensions inline.
        source.Should().NotContain("612, 792");
        source.Should().NotContain("595.3, 841.9");
        source.Should().NotContain("841.9, 1190");
        source.Should().NotContain("708.7, 1000");
        source.Should().NotContain("522, 756");
    }

    [Fact]
    public void NeitherAppDeclaresItsOwnPaperDimensionRows()
    {
        // FreeX's table was a switch arm yielding a literal inch tuple, e.g. "=> (8.5,  11.0)".
        var freeXDimensionArm = new Regex(@"=>\s*\(\s*\d+(\.\d+)?\s*,\s*\d+(\.\d+)?\s*\)", RegexOptions.CultureInvariant);
        freeXDimensionArm.IsMatch(ReadWorkspaceSource(FreeXPageLayoutModel)).Should().BeFalse(
            "FreeX.Core.Model/PageLayout.cs must project inches from PaperSizeCatalog, not list them");

        // FreeW's table was a PageSetupPaperOption row ending in two literal point dimensions, e.g.
        // new("Letter (8.5\" x 11\")", "Letter (8.5 x 11 in)", 612, 792). The only surviving literal
        // row is the "Custom" affordance, whose 0,0 sentinel is not a paper size.
        var freeWDimensionRow = new Regex(
            @"""\s*,\s*(?!0\s*,\s*0\s*\))\d+(\.\d+)?\s*,\s*\d+(\.\d+)?\s*\)",
            RegexOptions.CultureInvariant);
        freeWDimensionRow.IsMatch(ReadWorkspaceSource(FreeWPageSetupPlanner)).Should().BeFalse(
            "FreeW's page-setup planner must project points from PaperSizeCatalog, not list them");
    }

    private static string ReadWorkspaceSource(string[] relativeParts) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(relativeParts));
}
