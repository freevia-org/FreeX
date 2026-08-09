using System.Reflection;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for the r123 finding (src/FreeX.App.Host/NamedRangeDialog.xaml.cs): the New/Edit
/// Name dialog is a single form (Name, Scope, Comment, Refers To) used for both range-backed and
/// formula/constant-backed defined names -- the Comment field is shown/editable regardless of kind --
/// but <see cref="NamedRangeDialog.DefineOrUpdateName"/>'s fallback to the formula branch
/// (<c>DefineOrUpdateNamedFormula</c>, reached whenever Refers To doesn't parse as a plain range) never
/// threaded <c>definition.Comment</c> into the <see cref="DefineNamedFormulaCommand"/> it built, so a
/// comment entered for a named formula/constant (e.g. Name=TaxRate, RefersTo="=0.21",
/// Comment="Standard VAT rate") was silently discarded. These tests drive the real entry point
/// (<see cref="NamedRangeDialog.DefineOrUpdateName"/> via reflection, exactly like the sibling
/// R50_NameManagerNamedFormulaCrudTests) rather than the command directly, to prove the comment
/// actually reaches the model when a user completes the dialog.
/// </summary>
public sealed class R123_NamedFormulaCommentDialogTests
{
    [Fact]
    public void DefineOrUpdateName_NewNamedFormula_PersistsComment()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            workbook.AddSheet("Sheet1");
            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Ok;
            try
            {
                InvokeDefineOrUpdateName(
                    dialog,
                    new NameDefinitionDialogResult("TaxRate", "Workbook", "Standard VAT rate", "0.21"),
                    originalName: null,
                    originalScope: null);

                workbook.NamedFormulas.Should().ContainKey("TaxRate");
                workbook.NamedFormulas["TaxRate"].Should().Be("0.21");
                workbook.TryGetNamedRangeMetadata("TaxRate", out var metadata).Should().BeTrue(
                    "the Comment entered in the New Name dialog for a named formula/constant must be " +
                    "persisted, not silently discarded");
                metadata.Comment.Should().Be("Standard VAT rate");
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DefineOrUpdateName_EditExistingNamedFormula_UpdatesComment()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            workbook.AddSheet("Sheet1");
            workbook.NamedFormulas["Rate"] = "0.05";
            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Ok;
            try
            {
                InvokeDefineOrUpdateName(
                    dialog,
                    new NameDefinitionDialogResult("Rate", "Workbook", "Updated via edit", "0.10"),
                    originalName: "Rate",
                    originalScope: "Workbook");

                workbook.NamedFormulas["Rate"].Should().Be("0.10");
                workbook.TryGetNamedRangeMetadata("Rate", out var metadata).Should().BeTrue();
                metadata.Comment.Should().Be("Updated via edit");
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
                dialog.Close();
            }
        });
    }

    // Sibling no-regression: the range branch's comment handling (already working before r123) must
    // keep working exactly as before.
    [Fact]
    public void DefineOrUpdateName_NewNamedRange_StillPersistsComment()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            workbook.AddSheet("Sheet1");
            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Ok;
            try
            {
                InvokeDefineOrUpdateName(
                    dialog,
                    new NameDefinitionDialogResult("Sales", "Workbook", "range comment", "Sheet1!A1:A2"),
                    originalName: null,
                    originalScope: null);

                workbook.TryGetNamedRangeMetadata("Sales", out var metadata).Should().BeTrue();
                metadata.Comment.Should().Be("range comment");
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
                dialog.Close();
            }
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static void InvokeDefineOrUpdateName(
        NamedRangeDialog dialog,
        NameDefinitionDialogResult definition,
        string? originalName,
        string? originalScope,
        SheetId? originalScopeSheetId = null)
    {
        var method = typeof(NamedRangeDialog).GetMethod("DefineOrUpdateName", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(NamedRangeDialog), "DefineOrUpdateName");
        method.Invoke(dialog, [definition, originalName, originalScope, originalScopeSheetId]);
    }

    private static ICommandBus CreateCommandBus(Workbook workbook) =>
        new CommandBus(_ => new TestCommandContext(workbook));
}
