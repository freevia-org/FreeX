using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r308: closes the rest of FreeX's hand-written-copy class that r307 surveyed.
///
/// <para>r307 found nineteen field-by-field copies across the three apps and guarded the largest
/// unguarded one. Four already had guards -- `Sheet.Clone`, `CellStateSnapshot`, the picture clone,
/// the slicer copy-state -- and `Sheet.Clone`'s own summary lists the fields it once lost, so the
/// class is not hypothetical here.</para>
///
/// <para>These are the remaining FreeX types with a parameterless <c>Clone()</c>. Naming a type is
/// all it takes, which is the point: the alternative was one round and one near-identical file per
/// type.</para>
/// </summary>
public sealed class R308_FreeXCloneCompletenessTests
{
    public static TheoryData<Type> ClonableTypes() => new()
    {
        typeof(CellStyle),
        typeof(DataValidation),
        typeof(ChartDataTableModel),
    };

    [Theory]
    [MemberData(nameof(ClonableTypes))]
    public void CloneCarriesEveryScalarMember(Type type) =>
        CloneCompletenessAssertions.AssertCloneCarriesEveryScalar(type);

    /// <summary>
    /// Guards the list itself: a type that loses its parameterless Clone would silently drop out of
    /// the theory above rather than failing, so the count is pinned.
    /// </summary>
    [Fact]
    public void EveryNamedTypeStillExposesAParameterlessClone()
    {
        Type[] guarded = [typeof(CellStyle), typeof(DataValidation), typeof(ChartDataTableModel)];
        foreach (var type in guarded)
        {
            type.GetMethod("Clone", System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance, Type.EmptyTypes)
                .Should().NotBeNull($"{type.Name} is guarded here through its parameterless Clone()");
        }
    }
}
