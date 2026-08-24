using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class WaveAutoFilterColorGeometryMutationTests
{
    [Fact]
    public void GeometryContractIgnoresClickOffsetTokensInSubsequentFunctions()
    {
        var probe = ReadProbe();
        const string subsequentFunction = "probe_autofilter_mixed_type_persistence_physical() {";
        probe.Should().Contain(subsequentFunction, "the boundary mutation must precede a subsequent probe function");

        var mutatedProbe = probe.Replace(
            subsequentFunction,
            "probe_color_geometry_boundary_decoy() {\n" +
            "    local subsequent_click_x_offset=999\n" +
            "}\n\n" +
            subsequentFunction,
            StringComparison.Ordinal);
        Action validate = () => WaveAutoFilterColorGeometryAssertions.AssertBoundGeometry(mutatedProbe);

        validate.Should().NotThrow(
            "click-offset-like tokens in a separately named subsequent function are outside the color geometry contract");
    }

    [Fact]
    public void GeometryContractRejectsExtraClickOffsetAssignmentInsideColorFunction()
    {
        var probe = ReadProbe();
        const string assignment =
            "local sample_x_offset=84 sample_y_offset=216 click_x_offset=110 click_y_offset=220";
        probe.Should().Contain(assignment, "the boundary mutation must target the live color function");

        var mutatedProbe = probe.Replace(
            assignment,
            assignment + "\n    extra_click_x_offset=999",
            StringComparison.Ordinal);
        Action validate = () => WaveAutoFilterColorGeometryAssertions.AssertBoundGeometry(mutatedProbe);

        validate.Should().Throw<Exception>(
            "an extra click-x assignment inside the color function must remain invalid");
    }

    [Theory]
    [InlineData(
        "click_x_offset=110 click_y_offset=220",
        "click_x_offset=111 click_y_offset=220")]
    [InlineData("click_x_offset=190", "click_x_offset=191")]
    [InlineData(
        "click_autofilter_control \"$click_x_offset\" \"$click_y_offset\"",
        "click_autofilter_control \"$click_x_offset\" 221")]
    [InlineData(
        "local click_x=$((a1_x + click_x_offset)) click_y=$((a1_y + click_y_offset)) before_pixel=\"\" pixel=\"\"",
        "local click_x=110 click_y=220 before_pixel=\"\" pixel=\"\"")]
    public void GeometryContractRejectsClickTargetMutations(string original, string mutation)
    {
        var probe = ReadProbe();
        probe.Should().Contain(original, "the mutation must target the live geometry contract");

        var mutatedProbe = probe.Replace(original, mutation, StringComparison.Ordinal);
        Action validate = () => WaveAutoFilterColorGeometryAssertions.AssertBoundGeometry(mutatedProbe);

        validate.Should().Throw<Exception>("a click target mutation must not leave the source guard green");
    }

    private static string ReadProbe() =>
        TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
}
