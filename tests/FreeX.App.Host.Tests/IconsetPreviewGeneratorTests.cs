using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class IconsetPreviewGeneratorTests
{
    [Fact]
    public void IconsetPreviewGenerator_DoesNotDependOnNetworkByDefault()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Generate-IconsetPreview.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("[switch]$ValidateExcelLinkAvailability");
        script.Should().Contain("if ($SkipExcelLinkValidation -or -not $ValidateExcelLinkAvailability)");
        script.Should().Contain("$request.Method = \"HEAD\"");
    }

    [Fact]
    public void IconsetPreviewGenerator_CanValidateGeneratedPreviewWithoutRewriting()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Generate-IconsetPreview.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("[switch]$Check");
        script.Should().Contain("Iconset preview is stale");
        script.Should().Contain("Run tools/Generate-IconsetPreview.ps1 and commit the updated file.");
        script.Should().Contain("Iconset preview is up to date");
        script.Should().Contain("[IO.File]::ReadAllText($outputFullPath)");
        script.Should().Contain("return");
    }
}
