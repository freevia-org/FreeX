using FluentAssertions;

using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression tests for the r123 finding (src/FreeX.App.Avalonia/Dialogs/DefinedNamesShellGlue.cs):
/// the Define Name editor's Comment field works the same way for a named formula/constant as for a
/// named range, but <see cref="DefinedNamesShellGlue.BuildDefineFormulaCommand"/> built its
/// <see cref="DefineNamedFormulaCommand"/> from only <c>draft.Name</c>/the refers-to text/
/// <c>draft.Scope.Sheet</c> -- never <c>draft.Comment</c> -- so a comment entered for a named
/// formula/constant on the Avalonia shell was silently discarded exactly like on the WPF shell (see
/// the sibling R123_NamedFormulaCommentDialogTests / R123_NamedFormulaCommentMetadataTests).
/// <see cref="DefinedNamesShellGlue.BuildRows"/>'s formula projections are also covered here: even
/// once the comment is persisted, the Name Manager list must read it back, or it is stored correctly
/// but never visible again.
/// </summary>
public sealed class R123_DefinedNamesShellGlueFormulaCommentTests
{
    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    private static CommandOutcome Run(Workbook workbook, IWorkbookCommand command) =>
        command.Apply(new GlueTestCommandContext(workbook));

    [Fact]
    public void BuildDefineFormulaCommand_WorkbookGlobal_PersistsComment()
    {
        var (workbook, _) = CreateWorkbook();
        var draft = new DefinedNameDraft("TaxRate", DefinedNameScope.Workbook, "=0.21", "Standard VAT rate");

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDefineFormulaCommand(draft));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.NamedFormulas["TaxRate"].Should().Be("0.21");
        workbook.TryGetNamedRangeMetadata("TaxRate", out var metadata).Should().BeTrue(
            "the Define Name editor's Comment must be persisted for a named formula/constant just " +
            "like it already is for a named range");
        metadata.Comment.Should().Be("Standard VAT rate");
    }

    [Fact]
    public void BuildDefineFormulaCommand_SheetScoped_PersistsComment()
    {
        var (workbook, sheet) = CreateWorkbook();
        var scope = DefinedNameScope.ForSheet(sheet.Id, sheet.Name);
        var draft = new DefinedNameDraft("LocalRate", scope, "=0.08", "Local sales tax");

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDefineFormulaCommand(draft));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.ScopedNamedFormulas[("LocalRate", sheet.Id)].Should().Be("0.08");
        workbook.TryGetScopedNamedRangeMetadata("LocalRate", sheet.Id, out var metadata).Should().BeTrue();
        metadata.Comment.Should().Be("Local sales tax");
    }

    // ── The persisted comment must also come back through BuildRows (Name Manager list) ──

    [Fact]
    public void BuildRows_WorkbookGlobalNamedFormula_IncludesComment()
    {
        var (workbook, _) = CreateWorkbook();
        Run(workbook, DefinedNamesShellGlue.BuildDefineFormulaCommand(
            new DefinedNameDraft("TaxRate", DefinedNameScope.Workbook, "=0.21", "Standard VAT rate")));

        var rows = DefinedNamesShellGlue.BuildRows(workbook);

        rows.Should().ContainSingle(r => r.Name == "TaxRate").Subject.Comment.Should().Be("Standard VAT rate");
    }

    [Fact]
    public void BuildRows_SheetScopedNamedFormula_IncludesComment()
    {
        var (workbook, sheet) = CreateWorkbook();
        var scope = DefinedNameScope.ForSheet(sheet.Id, sheet.Name);
        Run(workbook, DefinedNamesShellGlue.BuildDefineFormulaCommand(
            new DefinedNameDraft("LocalRate", scope, "=0.08", "Local sales tax")));

        var rows = DefinedNamesShellGlue.BuildRows(workbook);

        rows.Should().ContainSingle(r => r.Name == "LocalRate").Subject.Comment.Should().Be("Local sales tax");
    }

    // ── Sibling no-regression: BuildDefineCommand (range branch) already carried the comment ──

    [Fact]
    public void BuildDefineCommand_StillPersistsComment_Unaffected()
    {
        var (workbook, sheet) = CreateWorkbook();
        var draft = new DefinedNameDraft("Sales", DefinedNameScope.Workbook, "=Sheet1!A1:A2", "range comment");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        Run(workbook, DefinedNamesShellGlue.BuildDefineCommand(draft, range)).Success.Should().BeTrue();

        workbook.NamedRangeMetadataByName["Sales"].Comment.Should().Be("range comment");
    }

    // ── Sibling no-regression: a blank comment must not create a spurious non-empty entry ──

    [Fact]
    public void BuildDefineFormulaCommand_BlankComment_StoresEmptyComment_NotNull()
    {
        var (workbook, _) = CreateWorkbook();
        var draft = new DefinedNameDraft("PlainRate", DefinedNameScope.Workbook, "=0.05");

        Run(workbook, DefinedNamesShellGlue.BuildDefineFormulaCommand(draft)).Success.Should().BeTrue();

        workbook.TryGetNamedRangeMetadata("PlainRate", out var metadata).Should().BeTrue();
        metadata.Comment.Should().Be("");
    }

    /// <summary>A minimal <see cref="ICommandContext"/> for running named-range/formula commands against a workbook.</summary>
    private sealed class GlueTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
