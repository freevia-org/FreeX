using FluentAssertions;

using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-21 regression test for finding R21-defined-name-management-2: the Avalonia Define Name
/// editor's glue ignored the user's chosen sheet scope and always defined a workbook-global name
/// (WPF's NamedRangeDialog correctly resolves and passes the scope-sheet id). A name defined with a
/// sheet scope must land in the workbook's sheet-scoped store (<see cref="Workbook.ScopedNamedRanges"/>
/// / <see cref="Workbook.ScopedNamedFormulas"/>), not the workbook-global one, and must not collide
/// with a workbook-global name of the same text — Excel allows both to coexist.
/// </summary>
public sealed class R21_DefinedNames_SheetScopeThreadedTests
{
    private static (Workbook Workbook, Sheet Sheet1, Sheet Sheet2) CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        return (workbook, sheet1, sheet2);
    }

    private static GridRange Range(Sheet sheet, uint row, uint col) =>
        new(new CellAddress(sheet.Id, row, col), new CellAddress(sheet.Id, row, col));

    private static CommandOutcome Run(Workbook workbook, IWorkbookCommand command) =>
        command.Apply(new GlueTestCommandContext(workbook));

    [Fact]
    public void BuildDefineCommand_WithSheetScope_DefinesSheetScopedRange_NotWorkbookGlobal()
    {
        var (workbook, sheet1, sheet2) = CreateWorkbook();

        // User picks "Sheet2" in the Define Name editor's Scope dropdown (BuildScopeChoices lists
        // it via DefinedNameScope.ForSheet), names it "Total", and refers-to B5.
        var scope = DefinedNameScope.ForSheet(sheet2.Id, "Sheet2");
        var draft = new DefinedNameDraft("Total", scope, "Sheet2!B5", "");
        var range = Range(sheet2, 5, 2);

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDefineCommand(draft, range));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // Must be sheet-scoped (Excel "localSheetId"), not workbook-global.
        workbook.NamedRanges.Should().NotContainKey("Total");
        workbook.ScopedNamedRanges.Should().ContainKey(("Total", sheet2.Id));
        workbook.ScopedNamedRanges[("Total", sheet2.Id)].Should().Be(range);
    }

    [Fact]
    public void BuildDefineCommand_WithSheetScope_CoexistsWithWorkbookGlobalNameOfSameText()
    {
        var (workbook, sheet1, sheet2) = CreateWorkbook();
        // A pre-existing workbook-global "Total" name.
        workbook.DefineNamedRange("Total", Range(sheet1, 1, 1));

        var scope = DefinedNameScope.ForSheet(sheet2.Id, "Sheet2");
        var draft = new DefinedNameDraft("Total", scope, "Sheet2!B5", "");
        var range = Range(sheet2, 5, 2);

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDefineCommand(draft, range));

        // Excel allows a workbook-global name and a sheet-scoped name with identical text to
        // coexist. Before the fix, this silently overwrote the pre-existing workbook-global entry
        // because both were funneled into the same global NamedRanges dictionary.
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.NamedRanges.Should().ContainKey("Total");
        workbook.NamedRanges["Total"].Should().Be(Range(sheet1, 1, 1));
        workbook.ScopedNamedRanges.Should().ContainKey(("Total", sheet2.Id));
        workbook.ScopedNamedRanges[("Total", sheet2.Id)].Should().Be(range);
    }

    [Fact]
    public void BuildDefineFormulaCommand_WithSheetScope_DefinesSheetScopedFormula_NotWorkbookGlobal()
    {
        var (workbook, _, sheet2) = CreateWorkbook();

        var scope = DefinedNameScope.ForSheet(sheet2.Id, "Sheet2");
        var draft = new DefinedNameDraft("LocalRate", scope, "=1.05", "");

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDefineFormulaCommand(draft));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.NamedFormulas.Should().NotContainKey("LocalRate");
        workbook.ScopedNamedFormulas.Should().ContainKey(("LocalRate", sheet2.Id));
        workbook.ScopedNamedFormulas[("LocalRate", sheet2.Id)].Should().Be("1.05");
    }

    [Fact]
    public void BuildDefineCommand_WithWorkbookScope_StillDefinesWorkbookGlobalName()
    {
        // Regression guard: the workbook scope choice (the default/first entry from
        // BuildScopeChoices) must still define a workbook-global name as before.
        var (workbook, sheet1, _) = CreateWorkbook();
        var draft = new DefinedNameDraft("Sales", DefinedNameScope.Workbook, "Sheet1!A1", "");
        var range = Range(sheet1, 1, 1);

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDefineCommand(draft, range));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.NamedRanges.Should().ContainKey("Sales");
        workbook.ScopedNamedRanges.Should().BeEmpty();
    }

    /// <summary>A minimal <see cref="ICommandContext"/> for running named-range commands against a workbook.</summary>
    private sealed class GlueTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
