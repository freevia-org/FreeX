using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r199: Show Outline Symbols (Ctrl+8) was the one member of the View-tab display group with no
/// per-window override. Zoom, Freeze, Split, Gridlines, Headings, Rulers and Show Formulas each got
/// one (R83/R85/R86/R87/R89) and this one was passed over every time, so hiding the outline in one
/// View &gt; New Window sibling hid it in all of them.
/// <para>
/// Found by asking, of a group of settings copied together, which member lives somewhere the copy
/// does not reach -- the same question that found the FreeW print-preview balloons bug in r198.
/// </para>
/// </summary>
public sealed class R199_OutlineSymbolsSiblingViewIndependenceTests
{
    [Fact]
    public void SetShowOutlineSymbols_DoesNotLeakAcrossSiblingViews()
    {
        var session = CreateSession();
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);
        session.IsShowingOutlineSymbols.Should().BeTrue("null on the sheet means shown");
        sibling.IsShowingOutlineSymbols.Should().BeTrue();

        var result = session.SetShowOutlineSymbols(false);

        result.Success.Should().BeTrue();
        session.IsShowingOutlineSymbols.Should().BeFalse();
        sibling.IsShowingOutlineSymbols.Should().BeTrue(
            "the sibling window never touched Ctrl+8");
    }

    [Fact]
    public void ASiblingsOwnStateGovernsItsNoOpDecision()
    {
        // The sibling must not be misled by the now-shared-false field into treating "hide" as a
        // change it has already made -- the trap the Show Formulas sibling test pins for its toggle.
        var session = CreateSession();
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);
        session.SetShowOutlineSymbols(false).Success.Should().BeTrue();

        var siblingResult = sibling.SetShowOutlineSymbols(false);

        siblingResult.Success.Should().BeTrue();
        sibling.IsShowingOutlineSymbols.Should().BeFalse();
    }

    [Fact]
    public void ASingleSessionStillAppliesAndUndoes()
    {
        // The control: per-view independence must not cost the ordinary single-window behaviour.
        var session = CreateSession();

        var result = session.SetShowOutlineSymbols(false);

        result.Success.Should().BeTrue();
        session.IsShowingOutlineSymbols.Should().BeFalse();
        session.ActiveSheet.ShowOutlineSymbols.Should().BeFalse();
        session.CanUndo.Should().BeTrue();

        session.UndoLastEdit().Success.Should().BeTrue();
        session.IsShowingOutlineSymbols.Should().BeTrue();
    }

    [Fact]
    public void SettingTheValueItAlreadyHas_IsANoOp()
    {
        var session = CreateSession();

        var result = session.SetShowOutlineSymbols(true);

        result.Success.Should().BeTrue();
        result.IsNoOp.Should().BeTrue();
        session.CanUndo.Should().BeFalse();
    }

    private static WorkbookSession CreateSession()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
    }
}
