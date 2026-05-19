using System.IO;
using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class ShareWorkbookPlannerTests
{
    [Fact]
    public void CreatePlan_UsesCurrentFilePath_WhenWorkbookIsSaved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(path, "workbook");

        try
        {
            var plan = ShareWorkbookPlanner.CreatePlan(path);

            plan.Kind.Should().Be(ShareWorkbookPlanKind.ShareExistingFile);
            plan.Path.Should().Be(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreatePlan_RequiresSaveAs_WhenCurrentFilePathNoLongerExists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");

        var plan = ShareWorkbookPlanner.CreatePlan(path);

        plan.Kind.Should().Be(ShareWorkbookPlanKind.SaveAsBeforeShare);
        plan.Path.Should().BeNull();
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
