using FreeX.App.Services;
using FreeX.Core.Commands;

namespace FreeX.App.Avalonia.Tests;

public sealed class WorkbookSessionCommandBoundaryRegressionTests
{
    [Fact]
    public void SharedReviewCommandBoundary_DoesNotDirtyAvaloniaSessionForNoOp()
    {
        using var session = new WorkbookSessionFactory().CreateNew(240, 320);

        var result = session.ExecuteReviewCommand(
            new SetFreezePanesCommand(session.ActiveSheet.Id, frozenRows: 0, frozenCols: 0));

        result.Success.Should().BeTrue();
        result.IsNoOp.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }
}
