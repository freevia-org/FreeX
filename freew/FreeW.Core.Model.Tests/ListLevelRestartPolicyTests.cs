namespace FreeW.Core.Model.Tests;

public sealed class ListLevelRestartPolicyTests
{
    [Theory]
    [InlineData(1, 0, null, true)]
    [InlineData(1, 0, 0, false)]
    [InlineData(2, 0, 1, true)]
    [InlineData(2, 1, 1, false)]
    [InlineData(3, 1, 2, true)]
    [InlineData(3, 0, 2, true)]
    [InlineData(3, 2, 2, false)]
    [InlineData(2, 1, null, true)]
    [InlineData(2, 0, null, true)]
    [InlineData(2, 1, 99, true)]
    [InlineData(2, 1, -1, true)]
    [InlineData(0, 0, null, false)]
    [InlineData(2, 2, 1, false)]
    public void RestartFollowsTheSpecifiedParentAndDistinguishesZeroFromOmitted(
        int level, int advancedLevel, int? restartAfterLevel, bool expected)
    {
        ListLevelRestartPolicy.ShouldRestart(level, advancedLevel, restartAfterLevel).Should().Be(expected);
    }
}
