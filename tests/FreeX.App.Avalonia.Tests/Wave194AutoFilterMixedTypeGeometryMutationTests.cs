using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave194AutoFilterMixedTypeGeometryMutationTests
{
    [Fact]
    public void GeometryContractRejectsHardCodedActualClick()
    {
        AssertMutationRejected(
            "click_autofilter_control \"$mixed_type_target_click_x_offset\" \"$mixed_type_target_click_y_offset\"",
            "click_autofilter_control 74 362");
    }

    [Fact]
    public void GeometryContractRejectsDeadLiteralComment()
    {
        AssertMutationRejected(
            "click_autofilter_control \"$mixed_type_target_click_x_offset\" \"$mixed_type_target_click_y_offset\"",
            "click_autofilter_control 74 362\n" +
            "        # click_autofilter_control \"$mixed_type_target_click_x_offset\" \"$mixed_type_target_click_y_offset\"");
    }

    [Fact]
    public void GeometryContractRejectsDeadBranchCorrectClickFollowedByLiveWrongClick()
    {
        AssertMutationRejected(
            "click_autofilter_control \"$mixed_type_target_click_x_offset\" \"$mixed_type_target_click_y_offset\"",
            "if false; then\n" +
            "            click_autofilter_control \"$mixed_type_target_click_x_offset\" \"$mixed_type_target_click_y_offset\"\n" +
            "        fi\n" +
            "        click_autofilter_control \"$mixed_type_target_click_x_offset\" \"$((mixed_type_target_click_y_offset + 1))\"");
    }

    [Fact]
    public void GeometryContractRejectsWrongCropConsumerMaskedByDeadComment()
    {
        AssertMutationRejected(
            "convert \"$first\" -crop \"$mixed_type_target_geometry\" +repage \"$first_crop\"",
            "convert \"$first\" -crop \"260x18+$((a1_x + 68))+$((a1_y + 353))\" +repage \"$first_crop\"\n" +
            "        # convert \"$first\" -crop \"$mixed_type_target_geometry\" +repage \"$first_crop\"");
    }

    [Fact]
    public void GeometryContractRejectsHelperScopeSubstitution()
    {
        AssertMutationRejected(
            "local before=\"$1\" destination=\"$2\"\n" +
            "        local before_crop=",
            "local before=\"$1\" destination=\"$2\"\n" +
            "        local mixed_type_target_geometry=\"260x18+$((a1_x + 68))+$((a1_y + 353))\"\n" +
            "        local before_crop=");
    }

    private static void AssertMutationRejected(string original, string mutation)
    {
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        probe.Should().Contain(original, "the mutation must target the live mixed-type geometry contract");

        var mutatedProbe = probe.Replace(original, mutation, StringComparison.Ordinal);
        Action validate = () => Wave194AutoFilterMixedTypeGeometryAssertions.AssertBoundGeometry(mutatedProbe);

        validate.Should().Throw<Exception>("a geometry mutation must not leave the source guard green");
    }
}
