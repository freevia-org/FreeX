using FluentAssertions;

using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression tests for review-5 group Q (defined-name-formulas):
///   K19 — Name Manager must list NamedFormulas/ScopedNamedFormulas (formula/constant-valued
///         defined names), not only range names, or they are invisible and unmanageable.
///   K20 — The Define Name flow must accept a formula/constant refers-to (e.g. "=1.05" or
///         "=SUM(Sheet1!A:A)") and persist it as a named formula, not reject it because it does
///         not resolve to a plain range.
/// </summary>
public sealed class QDefinedNameFormulasShellGlueTests
{
    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    private static CommandOutcome Run(Workbook workbook, IWorkbookCommand command) =>
        command.Apply(new GlueTestCommandContext(workbook));

    // ── K19: BuildRows must project NamedFormulas / ScopedNamedFormulas ───────

    [Fact]
    public void BuildRows_IncludesWorkbookGlobalNamedFormula()
    {
        var (workbook, _) = CreateWorkbook();
        workbook.NamedFormulas["TaxRate"] = "1.05";

        var rows = DefinedNamesShellGlue.BuildRows(workbook);

        var row = rows.Should().ContainSingle(r => r.Name == "TaxRate").Subject;
        row.RefersTo.Should().Be("=1.05");
        row.ScopeLabel.Should().Be(DefinedNameScope.WorkbookLabel);
        row.Kind.Should().Be(DefinedNameKind.Formula);
    }

    [Fact]
    public void BuildRows_IncludesNamedFormulaAlongsideNamedRanges()
    {
        var (workbook, sheet) = CreateWorkbook();
        workbook.DefineNamedRange("Sales", new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)));
        workbook.NamedFormulas["Total"] = "SUM(Sheet1!A:A)";

        var rows = DefinedNamesShellGlue.BuildRows(workbook);

        rows.Should().HaveCount(2);
        rows.Should().Contain(r => r.Name == "Sales" && r.Kind == DefinedNameKind.Range);
        rows.Should().Contain(r => r.Name == "Total" && r.Kind == DefinedNameKind.Formula);
    }

    [Fact]
    public void BuildRows_IncludesSheetScopedNamedFormula_WithSheetAsScopeLabel()
    {
        var (workbook, sheet) = CreateWorkbook();
        workbook.DefineNamedFormula("LocalConst", "42", sheet.Id);

        var rows = DefinedNamesShellGlue.BuildRows(workbook);

        var row = rows.Should().ContainSingle(r => r.Name == "LocalConst").Subject;
        row.RefersTo.Should().Be("=42");
        row.ScopeLabel.Should().Be("Sheet1");
        row.IsWorkbookScoped.Should().BeFalse();
    }

    [Fact]
    public void ProjectRows_ErrorsFilter_CatchesNamedFormulaWithRefError()
    {
        var (workbook, _) = CreateWorkbook();
        workbook.NamedFormulas["Broken"] = "#REF!+1";

        var rows = DefinedNamesShellGlue.ProjectRows(workbook, DefinedNameFilter.Errors);

        rows.Should().ContainSingle().Which.Name.Should().Be("Broken");
    }

    // ── K19: deleting a named formula through the Name Manager's delete command ──

    [Fact]
    public void BuildDeleteCommand_RemovesWorkbookGlobalNamedFormula()
    {
        var (workbook, _) = CreateWorkbook();
        workbook.NamedFormulas["TaxRate"] = "1.05";

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDeleteCommand("TaxRate"));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.NamedFormulas.Should().NotContainKey("TaxRate");
    }

    [Fact]
    public void BuildDeleteCommand_ThenUndo_RestoresNamedFormula()
    {
        var (workbook, _) = CreateWorkbook();
        workbook.NamedFormulas["TaxRate"] = "1.05";
        var ctx = new GlueTestCommandContext(workbook);
        var command = DefinedNamesShellGlue.BuildDeleteCommand("TaxRate");

        command.Apply(ctx).Success.Should().BeTrue();
        workbook.NamedFormulas.Should().NotContainKey("TaxRate");

        command.Revert(ctx);

        workbook.NamedFormulas.Should().ContainKey("TaxRate");
        workbook.NamedFormulas["TaxRate"].Should().Be("1.05");
    }

    // ── K20: defining a formula/constant name through DefineNamedFormulaCommand ──

    [Fact]
    public void BuildDefineFormulaCommand_DefinesConstantNamedFormula()
    {
        var (workbook, _) = CreateWorkbook();
        var draft = new DefinedNameDraft("TaxRate", DefinedNameScope.Workbook, "=1.05", "");

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDefineFormulaCommand(draft));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.NamedFormulas.Should().ContainKey("TaxRate");
        workbook.NamedFormulas["TaxRate"].Should().Be("1.05");
    }

    [Fact]
    public void BuildDefineFormulaCommand_DefinesFullColumnSumNamedFormula()
    {
        var (workbook, _) = CreateWorkbook();
        var draft = new DefinedNameDraft("Total", DefinedNameScope.Workbook, "=SUM(Sheet1!A:A)", "");

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDefineFormulaCommand(draft));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.NamedFormulas["Total"].Should().Be("SUM(Sheet1!A:A)");
    }

    [Fact]
    public void BuildDefineFormulaCommand_ThenUndo_RemovesNewlyDefinedFormula()
    {
        var (workbook, _) = CreateWorkbook();
        var draft = new DefinedNameDraft("TaxRate", DefinedNameScope.Workbook, "=1.05", "");
        var ctx = new GlueTestCommandContext(workbook);
        var command = DefinedNamesShellGlue.BuildDefineFormulaCommand(draft);

        command.Apply(ctx).Success.Should().BeTrue();
        command.Revert(ctx);

        workbook.NamedFormulas.Should().NotContainKey("TaxRate");
    }

    [Fact]
    public void BuildDefineFormulaCommand_ReplacingExisting_ThenUndo_RestoresOldFormula()
    {
        var (workbook, _) = CreateWorkbook();
        workbook.NamedFormulas["TaxRate"] = "1.0";
        var ctx = new GlueTestCommandContext(workbook);
        var draft = new DefinedNameDraft("TaxRate", DefinedNameScope.Workbook, "=1.05", "");
        var command = DefinedNamesShellGlue.BuildDefineFormulaCommand(draft);

        command.Apply(ctx).Success.Should().BeTrue();
        workbook.NamedFormulas["TaxRate"].Should().Be("1.05");

        command.Revert(ctx);

        workbook.NamedFormulas["TaxRate"].Should().Be("1.0");
    }

    /// <summary>A minimal <see cref="ICommandContext"/> for running named-range/formula commands against a workbook.</summary>
    private sealed class GlueTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
