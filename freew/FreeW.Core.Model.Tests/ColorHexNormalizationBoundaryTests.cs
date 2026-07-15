using System.IO;

namespace FreeW.Core.Model.Tests;

public sealed class ColorHexNormalizationBoundaryTests
{
    [Fact]
    public void ModelColorHexPoliciesStayLocalToThemeAndAccessibilityContracts()
    {
        var theme = ReadSource("freew", "FreeW.Core.Model", "DocumentTheme.cs");
        theme.Should().Contain("model-facing theme palette uses \"#RRGGBB\"");
        theme.Should().Contain("theme1.xml uses");
        theme.Should().NotContain("using Free.Shared.Drawing;");
        theme.Should().NotContain("using Free.Shared.Theme;");
        theme.Should().NotContain("DrawingMlRgbColor.TryParseHexRgb(");
        theme.Should().NotContain("ThemeColor.FromHex(");

        var accessibility = ReadSource("freew", "FreeW.Core.Model", "AccessibilityChecker.cs");
        accessibility.Should().Contain("WCAG helper intentionally");
        accessibility.Should().Contain("malformed values fall back to black");
        accessibility.Should().NotContain("using Free.Shared.Drawing;");
        accessibility.Should().NotContain("using Free.Shared.Theme;");
        accessibility.Should().NotContain("DrawingMlRgbColor.TryParseHexRgb(");
        accessibility.Should().NotContain("ThemeColor.FromHex(");
    }

    private static string ReadSource(params string[] relativePath)
    {
        var path = relativePath.Aggregate(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), Path.Combine);
        return File.ReadAllText(path);
    }

}
