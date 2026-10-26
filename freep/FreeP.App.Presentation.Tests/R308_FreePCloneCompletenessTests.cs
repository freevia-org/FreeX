using FluentAssertions;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r308: completes the hand-written-copy sweep with FreeP, so the class r307 surveyed is closed
/// across all three apps rather than in the one app that happened to have the largest instance.
/// </summary>
public sealed class R308_FreePCloneCompletenessTests
{
    public static TheoryData<Type> ClonableTypes() => new()
    {
        typeof(FieldRun),
        typeof(AnimationScaleBehavior),
    };

    [Theory]
    [MemberData(nameof(ClonableTypes))]
    public void CloneCarriesEveryScalarMember(Type type) =>
        CloneCompletenessAssertions.AssertCloneCarriesEveryScalar(type);

    [Fact]
    public void EveryNamedTypeStillExposesAParameterlessClone()
    {
        Type[] guarded = [typeof(FieldRun), typeof(AnimationScaleBehavior)];
        foreach (var type in guarded)
        {
            type.GetMethod("Clone", System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance, Type.EmptyTypes)
                .Should().NotBeNull($"{type.Name} is guarded here through its parameterless Clone()");
        }
    }
}
