using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the r123 finding: a Comment entered for a named FORMULA/constant (e.g.
/// Name=TaxRate, RefersTo=0.21, Comment="Standard VAT rate") was silently discarded, never
/// persisted anywhere in the model. FreeX's New/Edit Name dialog is a single form (Name, Scope,
/// Comment, Refers To) used for both range-backed and formula/constant-backed defined names -- the
/// Comment field is always shown/editable regardless of kind -- but <see cref="DefineNamedFormulaCommand"/>
/// (the formula counterpart of <see cref="DefineNamedRangeCommand"/>, reached whenever Refers To
/// doesn't parse as a plain range) had no metadata parameter at all and its Apply() only ever wrote
/// Workbook.NamedFormulas/ScopedNamedFormulas, never Workbook.NamedRangeMetadataByName or the
/// scoped metadata dictionary -- the only places Hidden/Comment are stored. Excel ground truth:
/// Name Manager's comment field works identically for a named formula/constant as for a named
/// range -- the comment is stored on the &lt;definedName comment="..."&gt; element and survives
/// save/reopen and re-editing in Name Manager either way.
///
/// These tests drive the real entry point (<see cref="DefineNamedFormulaCommand"/>.Apply via
/// <see cref="TestCommandContext"/>) rather than poking the model dictionaries directly, matching
/// what NamedRangeDialog.xaml.cs (WPF) and DefinedNamesShellGlue.BuildDefineFormulaCommand
/// (Avalonia) both actually construct.
/// </summary>
public sealed class R123_NamedFormulaCommentMetadataTests
{
    // ── The exact defect scenario: workbook-global named formula/constant with a Comment ──

    [Fact]
    public void DefineNamedFormulaCommand_WorkbookGlobal_NewName_PersistsComment()
    {
        var wb = new Workbook("Test");
        wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var outcome = new DefineNamedFormulaCommand(
            "TaxRate", "0.21", scopeSheetId: null,
            metadata: new NamedRangeMetadata("Workbook", "Standard VAT rate")).Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.NamedFormulas["TaxRate"].Should().Be("0.21");
        wb.TryGetNamedRangeMetadata("TaxRate", out var metadata).Should().BeTrue(
            "the Comment entered in the New/Edit Name dialog for a named formula/constant must be " +
            "persisted, exactly like it already is for a named range");
        metadata.Comment.Should().Be("Standard VAT rate");
    }

    [Fact]
    public void DefineNamedFormulaCommand_SheetScoped_NewName_PersistsComment()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var outcome = new DefineNamedFormulaCommand(
            "LocalRate", "0.08", sheet.Id,
            metadata: new NamedRangeMetadata("Sheet1", "Local sales tax")).Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.ScopedNamedFormulas[("LocalRate", sheet.Id)].Should().Be("0.08");
        wb.TryGetScopedNamedRangeMetadata("LocalRate", sheet.Id, out var metadata).Should().BeTrue();
        metadata.Comment.Should().Be("Local sales tax");
    }

    // ── Redefine (Edit Name) must update the comment, and Undo must restore the old one ──

    [Fact]
    public void DefineNamedFormulaCommand_Redefine_UpdatesComment_AndUndoRestoresPrevious()
    {
        var wb = new Workbook("Test");
        wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        new DefineNamedFormulaCommand("Rate", "0.05", metadata: new NamedRangeMetadata("Workbook", "v1")).Apply(ctx);

        var redefine = new DefineNamedFormulaCommand("Rate", "0.10", metadata: new NamedRangeMetadata("Workbook", "v2"));
        redefine.Apply(ctx).Success.Should().BeTrue();
        wb.NamedRangeMetadataByName["Rate"].Comment.Should().Be("v2");

        redefine.Revert(ctx);
        wb.NamedFormulas["Rate"].Should().Be("0.05", "Revert must restore the previous formula text");
        wb.NamedRangeMetadataByName["Rate"].Comment.Should().Be(
            "v1", "Revert must also restore the previous Comment, not leave the redefined one behind");
    }

    // ── Deleting a commented named formula must not leave the comment orphaned ──

    [Fact]
    public void RemoveNamedRangeCommand_DeletingCommentedNamedFormula_ClearsMetadata_AndUndoRestoresIt()
    {
        var wb = new Workbook("Test");
        wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new DefineNamedFormulaCommand("Rate", "0.05", metadata: new NamedRangeMetadata("Workbook", "keep me")).Apply(ctx);

        var remove = new RemoveNamedRangeCommand("Rate");
        remove.Apply(ctx).Success.Should().BeTrue();
        wb.NamedFormulas.Should().NotContainKey("Rate");
        wb.NamedRangeMetadataByName.Should().NotContainKey(
            "Rate", "deleting a named formula must drop its Comment too, or a later unrelated name " +
                    "reusing the same text would spuriously inherit the old comment");

        remove.Revert(ctx);
        wb.NamedFormulas["Rate"].Should().Be("0.05");
        wb.NamedRangeMetadataByName["Rate"].Comment.Should().Be("keep me", "Undo of the delete must restore the comment too");
    }

    // ── No-regression: callers that never pass metadata (file-load, structural rewrites, sheet
    // ── copy) must not have pre-existing metadata wiped out by an unrelated redefine ──

    [Fact]
    public void DefineNamedFormulaCommand_WithoutMetadata_LeavesExistingMetadataUntouched()
    {
        var wb = new Workbook("Test");
        wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new DefineNamedFormulaCommand("Rate", "0.05", metadata: new NamedRangeMetadata("Workbook", "do not lose me")).Apply(ctx);

        // Simulates a caller with no metadata to contribute (e.g. RowColumnShiftHelpers rewriting
        // the formula text after an insert/delete) -- must not reset the comment to empty.
        var outcome = new DefineNamedFormulaCommand("Rate", "0.06").Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.NamedFormulas["Rate"].Should().Be("0.06");
        wb.NamedRangeMetadataByName["Rate"].Comment.Should().Be(
            "do not lose me", "a Define call that has no metadata to contribute must leave the " +
                              "existing comment alone, not silently clear it");
    }

    [Fact]
    public void Workbook_DefineNamedFormula_ScopedOverload_WithoutMetadata_LeavesExistingMetadataUntouched()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        wb.DefineNamedFormula("LocalRate", "0.05", sheet.Id, new NamedRangeMetadata("Sheet1", "keep"));

        // Mirrors XlsxNamedRangeMapper's load path / RowColumnShiftHelpers' rewrite path, which
        // call the 3-arg overload with no metadata.
        wb.DefineNamedFormula("LocalRate", "0.07", sheet.Id);

        wb.ScopedNamedFormulas[("LocalRate", sheet.Id)].Should().Be("0.07");
        wb.TryGetScopedNamedRangeMetadata("LocalRate", sheet.Id, out var metadata).Should().BeTrue();
        metadata.Comment.Should().Be("keep");
    }

    // ── Sibling no-regression: the range branch (DefineNamedRangeCommand) already worked and
    // ── must keep working exactly as before ──

    [Fact]
    public void DefineNamedRangeCommand_StillPersistsComment_Unaffected()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        new DefineNamedRangeCommand("Sales", range, new NamedRangeMetadata("Workbook", "range comment")).Apply(ctx)
            .Success.Should().BeTrue();

        wb.NamedRangeMetadataByName["Sales"].Comment.Should().Be("range comment");
    }
}
