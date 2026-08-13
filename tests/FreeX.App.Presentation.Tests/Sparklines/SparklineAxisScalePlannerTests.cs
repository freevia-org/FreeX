using FluentAssertions;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Sparklines;

public sealed class SparklineAxisScalePlannerTests
{
    [Fact]
    public void Build_ResolvesFiniteGroupBoundsForEveryRenderer()
    {
        var first = GroupSparkline(7);
        var second = GroupSparkline(7);
        var values = new Dictionary<Guid, IReadOnlyList<double>>
        {
            [first.Id] = [-4, double.NaN, 2],
            [second.Id] = [double.PositiveInfinity, 9],
        };

        var plan = SparklineAxisScalePlanner.Build([first, second], values);

        plan.Resolve(first).Should().Be(new SparklineAxisScale(-4, 9, 9));
        plan.Resolve(second).Should().Be(new SparklineAxisScale(-4, 9, 9));
    }

    [Fact]
    public void Resolve_CombinesCustomAndGroupMaximumAbsoluteWithoutOverridingIndividualAxis()
    {
        var groupMember = GroupSparkline(3);
        var mixed = new SparklineModel
        {
            GroupId = 3,
            MinAxisType = SparklineAxisScaling.Custom,
            ManualMin = -12,
            MaxAxisType = SparklineAxisScaling.Group,
        };
        var values = new Dictionary<Guid, IReadOnlyList<double>>
        {
            [groupMember.Id] = [-2, 8],
            [mixed.Id] = [1],
        };

        var scale = SparklineAxisScalePlanner.Build([groupMember, mixed], values).Resolve(mixed);

        scale.Minimum.Should().Be(-12);
        scale.Maximum.Should().Be(8);
        scale.MaximumAbsolute.Should().Be(12);
    }

    [Fact]
    public void Resolve_LeavesIndividualBoundsUnset()
    {
        var sparkline = new SparklineModel();

        var scale = SparklineAxisScalePlanner.Build(
            [sparkline],
            new Dictionary<Guid, IReadOnlyList<double>> { [sparkline.Id] = [-2, 5] })
            .Resolve(sparkline);

        scale.Should().Be(default(SparklineAxisScale));
    }

    private static SparklineModel GroupSparkline(int groupId) => new()
    {
        GroupId = groupId,
        MinAxisType = SparklineAxisScaling.Group,
        MaxAxisType = SparklineAxisScaling.Group,
    };
}
