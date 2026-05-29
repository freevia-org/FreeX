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
}
