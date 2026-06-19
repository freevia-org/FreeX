using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotNamePlannerTests
{
    private static PivotTableModel Pivot(string name) => new() { Name = name };

    [Fact]
    public void Capture_ReturnsTheCurrentName()
    {
        PivotNamePlanner.Capture(Pivot("Sales")).Should().Be("Sales");
    }

    [Fact]
    public void Normalize_TrimsAndCollapsesNull()
    {
        PivotNamePlanner.Normalize("  Report  ").Should().Be("Report");
        PivotNamePlanner.Normalize(null).Should().BeEmpty();
    }

    [Fact]
    public void TryCreateResult_RejectsEmptyName()
    {
        var ok = PivotNamePlanner.TryCreateResult(Pivot("Sales"), "   ", _ => false, out var result, out var error);
        ok.Should().BeFalse();
        result.Should().BeNull();
        error.Should().Be(PivotNamePlanner.EmptyNameMessage);
    }

    [Fact]
    public void TryCreateResult_RejectsDuplicateName()
    {
        var ok = PivotNamePlanner.TryCreateResult(Pivot("Sales"), "Other", _ => true, out var result, out var error);
        ok.Should().BeFalse();
        result.Should().BeNull();
        error.Should().Be(PivotNamePlanner.DuplicateNameMessage);
    }

    [Fact]
    public void TryCreateResult_AllowsUnchangedNameWithoutCollisionCheck()
    {
        // Renaming to the same name must succeed even when the collision check would say "in use".
        var ok = PivotNamePlanner.TryCreateResult(Pivot("Sales"), "Sales", _ => true, out var result, out var error);
        ok.Should().BeTrue();
        error.Should().BeNull();
        result!.Name.Should().Be("Sales");
    }

    [Fact]
    public void TryCreateResult_TrimsAcceptedName()
    {
        var ok = PivotNamePlanner.TryCreateResult(Pivot("Sales"), "  Summary  ", _ => false, out var result, out var error);
        ok.Should().BeTrue();
        error.Should().BeNull();
        result!.Name.Should().Be("Summary");
    }
}
