using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionGroupedOutlineSymbolsTests
{
    [Fact]
    public void SetShowOutlineSymbols_AppliesToGroupedSheetsAsOneUndoableOperation()
    {
        using var session = CreateGroupedSession(out var first, out var second);
        var selection = new GridRange(
            new CellAddress(first.Id, 3, 4),
            new CellAddress(first.Id, 6, 7));
        session.SelectRange(selection);

        session.SetShowOutlineSymbols(false).Success.Should().BeTrue();

        first.ShowOutlineSymbols.Should().BeFalse();
        second.ShowOutlineSymbols.Should().BeFalse();
        session.SelectedRange.Should().Be(selection);
        session.GetCurrentGroupedEditSheetIds().Should().Equal(first.Id, second.Id);

        session.UndoLastEdit().Success.Should().BeTrue();
        first.ShowOutlineSymbols.Should().BeNull();
        second.ShowOutlineSymbols.Should().BeNull();

        session.RedoLastEdit().Success.Should().BeTrue();
        first.ShowOutlineSymbols.Should().BeFalse();
        second.ShowOutlineSymbols.Should().BeFalse();
    }

    [Fact]
    public void ActiveSheetAlreadyAtTarget_DoesNotHideAGroupedSiblingDifference()
    {
        using var session = CreateGroupedSession(out var first, out var second);
        first.ShowOutlineSymbols = true;
        second.ShowOutlineSymbols = false;

        var result = session.SetShowOutlineSymbols(true);

        result.Success.Should().BeTrue();
        result.IsNoOp.Should().BeFalse();
        first.ShowOutlineSymbols.Should().BeTrue();
        second.ShowOutlineSymbols.Should().BeTrue();
    }

    [Fact]
    public void SetShowOutlineSymbols_IsNoOpOnlyWhenEveryGroupedSheetMatches()
    {
        using var session = CreateGroupedSession(out var first, out var second);

        var result = session.SetShowOutlineSymbols(true);

        result.Success.Should().BeTrue();
        result.IsNoOp.Should().BeTrue();
        first.ShowOutlineSymbols.Should().BeNull();
        second.ShowOutlineSymbols.Should().BeNull();
    }

    [Fact]
    public void UngroupedMutation_LeavesOtherSheetsUnchanged()
    {
        using var session = new WorkbookSessionFactory().CreateNew(30, 20);
        var first = session.ActiveSheet;
        var second = session.Workbook.AddSheet("Second");

        session.SetShowOutlineSymbols(false).Success.Should().BeTrue();

        first.ShowOutlineSymbols.Should().BeFalse();
        second.ShowOutlineSymbols.Should().BeNull();
    }

    [Fact]
    public void BothRenderersRouteOutlineSymbolShortcutThroughWorkbookSession()
    {
        var wpf = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));
        var avalonia = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Avalonia", "MainWindow.KeyboardParity.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("_session.SetShowOutlineSymbols(next)")
                .And.NotContain("new SetWorksheetOutlineSymbolsCommand(");
        }

        avalonia.Should().NotContain("_session.SelectRange(range)",
            "the shared session preserves the selection for metadata-only mutations");
    }

    private static WorkbookSession CreateGroupedSession(out Sheet first, out Sheet second)
    {
        var session = new WorkbookSessionFactory().CreateNew(30, 20);
        first = session.ActiveSheet;
        second = session.Workbook.AddSheet("Second");
        session.SelectSheet(first.Id);
        session.SelectAllVisibleSheets().Should().BeTrue();
        return session;
    }
}
