using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for R74-commands-name-manager-4-1 (src/FreeX.App.Host/NameDefinitionDialog.cs
/// + src/FreeX.App.Host/MainWindow.FormulaCommands.cs): the ribbon Formulas ▸ Defined Names ▸
/// "Define Name" dialog had no duplicate-name check at all. Redefining a name that already exists
/// as a NamedFormula/constant (e.g. "Revenue" = 0.08) as a plain range via this dialog silently
/// added a NamedRanges entry alongside the stale NamedFormulas one -- and NamedRanges wins at
/// evaluation time, so every formula referencing the name silently changed value. Excel's own New
/// Name dialog rejects this outright with "A name conflicts with an existing name."
///
/// The fix adds <c>MainWindow.NameConflictsWithExistingDefinition(name, scope)</c>, checked in
/// <c>DefineNameBtn_Click</c> right after the dialog closes and before the
/// <see cref="FreeX.Core.Commands.DefineNamedRangeCommand"/> is constructed/executed. These tests
/// drive that helper directly via reflection (the ribbon dialog itself is a real modal WPF window
/// and cannot be scripted headlessly here), and a source-contract check confirms the click handler
/// actually wires the check in ahead of command execution.
/// </summary>
public sealed class R74_DefineNameDuplicateCheckTests
{
    [Fact]
    public void NameConflictsWithExistingDefinition_ExistingWorkbookNamedFormula_ReturnsTrue()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                workbook.NamedFormulas["Revenue"] = "0.08";

                var conflicts = (bool)R49MainWindowTestHarness.Invoke(
                    window, "NameConflictsWithExistingDefinition", "Revenue", "Workbook")!;

                conflicts.Should().BeTrue(
                    "redefining an existing NamedFormula/constant as a range must be rejected as a " +
                    "duplicate name, matching Excel's New Name dialog");
                workbook.NamedFormulas.Should().ContainKey("Revenue");
                workbook.NamedFormulas["Revenue"].Should().Be("0.08",
                    "the pre-existing named formula must be left completely untouched by the check itself");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void NameConflictsWithExistingDefinition_ExistingWorkbookNamedRange_ReturnsTrue()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.Sheets[0];
                workbook.DefineNamedRange(
                    "Sales", new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)));

                var conflicts = (bool)R49MainWindowTestHarness.Invoke(
                    window, "NameConflictsWithExistingDefinition", "Sales", "Workbook")!;

                conflicts.Should().BeTrue("a brand-new Define Name must not silently clobber an existing NamedRange either");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void NameConflictsWithExistingDefinition_ExistingSheetScopedFormula_ChecksOnlyThatSheetScope()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.Sheets[0];
                workbook.DefineNamedFormula("LocalRate", "0.05", sheet.Id);

                var conflictsInScope = (bool)R49MainWindowTestHarness.Invoke(
                    window, "NameConflictsWithExistingDefinition", "LocalRate", sheet.Name)!;
                var conflictsWorkbookScope = (bool)R49MainWindowTestHarness.Invoke(
                    window, "NameConflictsWithExistingDefinition", "LocalRate", "Workbook")!;

                conflictsInScope.Should().BeTrue("a sheet-scoped named formula must be treated as a duplicate within its own sheet scope");
                conflictsWorkbookScope.Should().BeFalse(
                    "a name scoped to one sheet must not block defining a same-text workbook-scoped name -- Excel allows these to coexist");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: a brand-new, non-colliding name must still be reported as definable.
    [Fact]
    public void NameConflictsWithExistingDefinition_BrandNewUniqueName_ReturnsFalse()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                workbook.NamedFormulas["Revenue"] = "0.08";

                var conflicts = (bool)R49MainWindowTestHarness.Invoke(
                    window, "NameConflictsWithExistingDefinition", "BrandNewName", "Workbook")!;

                conflicts.Should().BeFalse("a brand-new, non-colliding name must still be definable through the ribbon dialog");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Wiring guard: confirms DefineNameBtn_Click actually calls the duplicate check (and rejects
    // before constructing the command) rather than only having the helper exist unused.
    [Fact]
    public void DefineNameBtnClick_ChecksForConflictBeforeExecutingTheDefineCommand()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");

        var clickHandlerStart = source.IndexOf("private void DefineNameBtn_Click", StringComparison.Ordinal);
        var clickHandlerEnd = source.IndexOf("private void CreateNamesFromSelectionBtn_Click", StringComparison.Ordinal);
        clickHandlerStart.Should().BeGreaterThanOrEqualTo(0);
        clickHandlerEnd.Should().BeGreaterThan(clickHandlerStart);
        var handlerSource = source[clickHandlerStart..clickHandlerEnd];

        var conflictCheckIndex = handlerSource.IndexOf("NameConflictsWithExistingDefinition(", StringComparison.Ordinal);
        var executeCommandIndex = handlerSource.IndexOf("new DefineNamedRangeCommand(", StringComparison.Ordinal);

        conflictCheckIndex.Should().BeGreaterThanOrEqualTo(0, "DefineNameBtn_Click must call the duplicate-name conflict check");
        executeCommandIndex.Should().BeGreaterThan(conflictCheckIndex,
            "the conflict check must run and be able to return before the DefineNamedRangeCommand is constructed/executed");
        handlerSource.Should().Contain("UiText.Get(\"NameDefinition_NameConflictsMessage\")");
    }
}
