namespace FreeP.RenderCompare.Tests;

public sealed class RenderCompareExitCodesTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(0, 2, 1, 2)]
    [InlineData(1, 0, 0, 1)]
    public void CombinePreservesEveryLegFailure(int wpfExitCode, int avaloniaExitCode, int powerPointExitCode, int expected)
    {
        RenderCompareExitCodes.Combine(wpfExitCode, avaloniaExitCode, powerPointExitCode)
            .Should()
            .Be(expected);
    }
}
