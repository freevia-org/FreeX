using FluentAssertions;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r308: the same hand-written-copy guard applied to FreeW's model types.
///
/// <para>r307's survey listed nineteen field-by-field copies across the three apps. Guarding them
/// one per round would have taken nineteen rounds and produced nineteen near-identical files; the
/// property is identical, so the machinery lives in <c>CloneCompletenessAssertions</c> and a type is
/// covered by being named here.</para>
///
/// <para>These four are FreeW's copies with a parameterless <c>Clone()</c>: a shape's effect list, a
/// section's page settings, a WordArt definition and a chart. Each is copied when its owner is
/// duplicated, and a member missing from the copy is one the duplicate silently lacks.</para>
/// </summary>
public sealed class R308_FreeWCloneCompletenessTests
{
    public static TheoryData<Type> ClonableTypes() => new()
    {
        typeof(ShapeEffectLst),
        typeof(PageSettings),
        typeof(WordArt),
        typeof(Chart),
    };

    [Theory]
    [MemberData(nameof(ClonableTypes))]
    public void CloneCarriesEveryScalarMember(Type type) =>
        CloneCompletenessAssertions.AssertCloneCarriesEveryScalar(type);

    [Fact]
    public void EveryNamedTypeStillExposesAParameterlessClone()
    {
        Type[] guarded = [typeof(ShapeEffectLst), typeof(PageSettings), typeof(WordArt), typeof(Chart)];
        foreach (var type in guarded)
        {
            type.GetMethod("Clone", System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance, Type.EmptyTypes)
                .Should().NotBeNull($"{type.Name} is guarded here through its parameterless Clone()");
        }
    }
}
