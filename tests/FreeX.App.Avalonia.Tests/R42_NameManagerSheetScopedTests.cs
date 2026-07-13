using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-42 regression tests for two Avalonia Name Manager bugs:
///
/// R42-commands-name-manager-3-1 (<see cref="DefinedNamesShellGlue.BuildDeleteCommand"/>): Delete/Rename could
/// never target a sheet-scoped defined name because the glue always built a <see
/// cref="RemoveNamedRangeCommand"/> with no scope-sheet id, so the command only probed the workbook-global
/// dictionaries and either reported "does not exist" or — worse — deleted an unrelated same-text
/// workbook-global name instead. Fixed by threading the resolved scope-sheet id (via the new <see
/// cref="DefinedNamesShellGlue.ResolveScopeSheetId"/> helper, mirroring the WPF host's
/// NamedRangeDialog.ResolveScopeSheetId) through to the command.
///
/// R42-commands-name-manager-3-2 (MainWindow.DefinedNames.cs's <c>ExistingDefinedNames</c>): the Define Name
/// duplicate check only ever looked at workbook-global names, so a new sheet-scoped name could silently
/// overwrite an existing same-scope sheet-scoped name instead of being rejected. Fixed by making the duplicate
/// check scope-aware: it now looks at <see cref="Workbook.ScopedNamedRanges"/>/<see
/// cref="Workbook.ScopedNamedFormulas"/> filtered to the target sheet when the target scope is a worksheet,
/// and at the workbook-global dictionaries only when the target scope is the workbook — matching Excel's rule
/// that same-text names in different scopes may coexist, but not within the same scope.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R42_NameManagerSheetScopedTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static (Workbook Workbook, Sheet Sheet1, Sheet Sheet2) CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        return (workbook, sheet1, sheet2);
    }

    private static GridRange Cell(Sheet sheet, uint row, uint col) =>
        new(new CellAddress(sheet.Id, row, col), new CellAddress(sheet.Id, row, col));

    private static CommandOutcome Run(Workbook workbook, IWorkbookCommand command) =>
        command.Apply(new GlueTestCommandContext(workbook));

    // ── R42-commands-name-manager-3-1: BuildDeleteCommand / ResolveScopeSheetId ───────────────────

    [Fact]
    public void ResolveScopeSheetId_ForSheetLabel_ResolvesToThatSheetsId()
    {
        var (workbook, sheet1, _) = CreateWorkbook();

        var resolved = DefinedNamesShellGlue.ResolveScopeSheetId(workbook, "Sheet1");

        resolved.Should().Be(sheet1.Id);
    }

    [Fact]
    public void ResolveScopeSheetId_ForWorkbookLabel_ResolvesToNull()
    {
        var (workbook, _, _) = CreateWorkbook();

        DefinedNamesShellGlue.ResolveScopeSheetId(workbook, DefinedNameScope.WorkbookLabel).Should().BeNull();
        DefinedNamesShellGlue.ResolveScopeSheetId(workbook, null).Should().BeNull();
    }

    [Fact]
    public void BuildDeleteCommand_WithSheetScope_DeletesTheSheetScopedName()
    {
        var (workbook, sheet1, _) = CreateWorkbook();
        workbook.DefineNamedRange("Rate", Cell(sheet1, 1, 1), new NamedRangeMetadata("Sheet1", ""), sheet1.Id);

        // Before the fix, BuildDeleteCommand(name) had no scope parameter at all, so it always built
        // new RemoveNamedRangeCommand(name) with scopeSheetId defaulting to null, which only probes the
        // workbook-global dictionaries and never finds a sheet-scoped entry.
        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDeleteCommand("Rate", sheet1.Id));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.ScopedNamedRanges.Should().NotContainKey(("Rate", sheet1.Id));
    }

    [Fact]
    public void BuildDeleteCommand_WithSheetScope_DoesNotTouchUnrelatedWorkbookGlobalNameOfSameText()
    {
        var (workbook, sheet1, _) = CreateWorkbook();
        // A workbook-global "Rate" and a distinct sheet-scoped "Rate" coexist (Excel allows this).
        workbook.DefineNamedRange("Rate", Cell(sheet1, 5, 5));
        workbook.DefineNamedRange("Rate", Cell(sheet1, 1, 1), new NamedRangeMetadata("Sheet1", ""), sheet1.Id);

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDeleteCommand("Rate", sheet1.Id));

        // Only the sheet-scoped entry must be removed; the workbook-global "Rate" must be untouched.
        // Before the fix, the missing scope argument meant the delete either failed outright ("does not
        // exist") or — worse — deleted this unrelated workbook-global entry while leaving the intended
        // sheet-scoped one in place.
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.ScopedNamedRanges.Should().NotContainKey(("Rate", sheet1.Id));
        workbook.NamedRanges.Should().ContainKey("Rate");
        workbook.NamedRanges["Rate"].Should().Be(Cell(sheet1, 5, 5));
    }

    [Fact]
    public void BuildDeleteCommand_WithWorkbookScope_StillDeletesWorkbookGlobalName()
    {
        // Regression guard: the no-scope (workbook) delete path used elsewhere must keep working.
        var (workbook, sheet1, _) = CreateWorkbook();
        workbook.DefineNamedRange("Temp", Cell(sheet1, 1, 1));

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDeleteCommand("Temp"));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.NamedRanges.Should().NotContainKey("Temp");
    }

    // ── R42-commands-name-manager-3-2: scope-aware duplicate check ────────────────────────────────

    [Fact]
    public async Task ExistingDefinedNames_ForSheetScope_IncludesSheetScopedNamesOnThatSheet()
    {
        await Session.Dispatch(() =>
        {
            var (workbook, sheet1, _) = CreateWorkbook();
            workbook.DefineNamedRange("Rate", Cell(sheet1, 1, 1), new NamedRangeMetadata("Sheet1", ""), sheet1.Id);

            // Before the fix, ExistingDefinedNames(workbook) ignored ScopedNamedRanges entirely, so a
            // sheet-scoped "Rate" was invisible to the duplicate check and a second sheet-scoped "Rate"
            // definition on the same sheet would silently overwrite it instead of being rejected.
            var existing = InvokeExistingDefinedNames(workbook, DefinedNameScope.ForSheet(sheet1.Id, "Sheet1"));

            existing.Should().Contain("Rate");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExistingDefinedNames_ForWorkbookScope_DoesNotFalselyFlagASheetScopedNameOfSameText()
    {
        await Session.Dispatch(() =>
        {
            var (workbook, sheet1, _) = CreateWorkbook();
            workbook.DefineNamedRange("Rate", Cell(sheet1, 1, 1), new NamedRangeMetadata("Sheet1", ""), sheet1.Id);

            // Excel allows a workbook-scoped name to coexist with a sheet-scoped name of the same text, so
            // defining a *workbook*-scoped "Rate" must not be blocked by the sheet-scoped "Rate" that
            // already exists on Sheet1 — the duplicate check must be scoped to the target scope only.
            var existing = InvokeExistingDefinedNames(workbook, DefinedNameScope.Workbook);

            existing.Should().NotContain("Rate");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExistingDefinedNames_ForSheetScope_DoesNotLeakNamesScopedToADifferentSheet()
    {
        await Session.Dispatch(() =>
        {
            var (workbook, sheet1, sheet2) = CreateWorkbook();
            workbook.DefineNamedRange("Rate", Cell(sheet1, 1, 1), new NamedRangeMetadata("Sheet1", ""), sheet1.Id);

            // A "Rate" scoped to Sheet1 must not collide with a new "Rate" being defined scoped to Sheet2 —
            // each sheet scope is independent.
            var existing = InvokeExistingDefinedNames(workbook, DefinedNameScope.ForSheet(sheet2.Id, "Sheet2"));

            existing.Should().NotContain("Rate");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExistingDefinedNames_ForSheetScope_DrivesValidatorToRejectSameScopeDuplicate()
    {
        await Session.Dispatch(() =>
        {
            var (workbook, sheet1, _) = CreateWorkbook();
            workbook.DefineNamedRange("Rate", Cell(sheet1, 1, 1), new NamedRangeMetadata("Sheet1", ""), sheet1.Id);

            var existing = InvokeExistingDefinedNames(workbook, DefinedNameScope.ForSheet(sheet1.Id, "Sheet1"));
            var result = DefinedNameValidator.Validate("Rate", existing, originalName: null);

            // Matches Excel's New Name dialog: defining a second "Rate" in the exact same (Sheet1) scope
            // must be rejected as a duplicate, not silently allowed to overwrite the first one.
            result.IsValid.Should().BeFalse();
            result.Error.Should().Be(DefinedNameError.Duplicate);
        }, CancellationToken.None);
    }

    private static IEnumerable<string> InvokeExistingDefinedNames(Workbook workbook, DefinedNameScope scope)
    {
        var method = typeof(MainWindow).GetMethod(
            "ExistingDefinedNames", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(MainWindow), "ExistingDefinedNames");
        return (IEnumerable<string>)method.Invoke(null, [workbook, scope])!;
    }

    /// <summary>A minimal <see cref="ICommandContext"/> for running named-range commands against a workbook.</summary>
    private sealed class GlueTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
