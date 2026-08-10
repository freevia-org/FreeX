using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SubtotalDialogOwnershipTests
{
    [Fact]
    public void WpfDialog_UsesSharedPlannerWithoutUnusedRendererParserFacade()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var facadePath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "SubtotalDialogInputParser.cs");
        var source = DialogSourceTestSupport.ReadHostSources("SubtotalDialog.cs");

        File.Exists(facadePath).Should().BeFalse("subtotal parsing and result planning are portable presentation policy");
        source.Should().Contain("SharedSubtotalDialogPlanner.TryCreateResult(");
        source.Should().Contain("SharedSubtotalDialogPlanner.CreateRemoveAllResult()");
        source.Should().NotContain("SubtotalDialogInputParser.TryParse(");
    }
}
