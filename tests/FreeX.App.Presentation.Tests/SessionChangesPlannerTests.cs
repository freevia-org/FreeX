using FluentAssertions;
using Free.Shared.Commands;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Tests;

public sealed class SessionChangesPlannerTests
{
    [Fact]
    public void Create_SeparatesTheCurrentUndoAndRedoLabels()
    {
        var plan = SessionChangesPlanner.Create(
            [new CommandHistoryEntry("Set A1"), new CommandHistoryEntry("Format B2")],
            [new CommandHistoryEntry("Insert row")]);

        plan.UndoEntries.Should().Equal("Set A1", "Format B2");
        plan.RedoEntries.Should().Equal("Insert row");
        plan.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Scope_IsExplicitlyLocalToTheOpenSession_NotRevisionHistoryOrCollaboration()
    {
        SessionChangesPlanner.Title.Should().Be("Changes in this session");
        SessionChangesPlanner.ScopeMessage.Should().Contain("while this workbook is open");
        SessionChangesPlanner.ScopeMessage.Should().Contain("not a saved revision history");
        SessionChangesPlanner.ScopeMessage.Should().Contain("does not include collaborators");

        var plan = SessionChangesPlanner.Create([], []);
        plan.IsEmpty.Should().BeTrue();
    }
}
