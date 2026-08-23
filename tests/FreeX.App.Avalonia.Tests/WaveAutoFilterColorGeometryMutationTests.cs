using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class WaveAutoFilterColorGeometryMutationTests
{
    [Theory]
    [InlineData(
        "click_x_offset=110 click_y_offset=220",
        "click_x_offset=111 click_y_offset=220")]
    [InlineData("click_x_offset=190", "click_x_offset=191")]
    [InlineData(
        "click_autofilter_control \"$click_x_offset\" \"$click_y_offset\"",
        "click_autofilter_control \"$click_x_offset\" 221")]
    public void GeometryContractRejectsClickTargetMutations(string original, string mutation)
    {
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        probe.Should().Contain(original, "the mutation must target the live geometry contract");

        var mutatedProbe = probe.Replace(original, mutation, StringComparison.Ordinal);
        Action validate = () => WaveAutoFilterColorGeometryAssertions.AssertBoundGeometry(mutatedProbe);

        validate.Should().Throw<Exception>("a click target mutation must not leave the source guard green");
    }
}
