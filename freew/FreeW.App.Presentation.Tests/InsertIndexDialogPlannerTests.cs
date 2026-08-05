using FluentAssertions;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class InsertIndexDialogPlannerTests
{
    [Fact]
    public void BuildInitialState_NormalizesSeed()
    {
        InsertIndexDialogPlanner.BuildInitialState(" People ")
            .Should().Be(new InsertIndexDialogState("People"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildResult_UsesDefaultIndexForBlankIdentifier(string? identifier)
    {
        InsertIndexDialogPlanner.BuildResult(new InsertIndexDialogState(identifier ?? string.Empty))
            .Should().Be(new InsertIndexDialogResult(null));
    }

    [Fact]
    public void BuildResult_TrimsAlternateIdentifier()
    {
        InsertIndexDialogPlanner.BuildResult(new InsertIndexDialogState(" People "))
            .Should().Be(new InsertIndexDialogResult("People"));
    }
}
