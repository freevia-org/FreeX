using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class ShareWorkbookPlannerTests
{
    [Fact]
    public void CreatePlan_UsesCurrentFilePath_WhenWorkbookIsSaved()
    {
        var plan = ShareWorkbookPlanner.CreatePlan(@"C:\work\Budget.xlsx");

        plan.Kind.Should().Be(ShareWorkbookPlanKind.ShareExistingFile);
        plan.Path.Should().Be(@"C:\work\Budget.xlsx");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePlan_RequiresSaveAs_WhenWorkbookHasNoFilePath(string? currentFilePath)
    {
        var plan = ShareWorkbookPlanner.CreatePlan(currentFilePath);

        plan.Kind.Should().Be(ShareWorkbookPlanKind.SaveAsBeforeShare);
        plan.Path.Should().BeNull();
    }
}
