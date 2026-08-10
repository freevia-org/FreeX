using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round-114 finding R114-app-name-manager-workbook-sentinel-3-2
/// (src/FreeX.App.Host/NamedRangeDialog.xaml.cs:317): the Name Manager identified the
/// workbook-global scope by comparing the Scope label string to the literal "Workbook"
/// (<c>ResolveScopeSheetId</c>) rather than by any dedicated identity. Nothing in
/// <see cref="Workbook.ValidateSheetNameStructure"/> reserves "Workbook" as a sheet name, so a
/// worksheet can legally be named exactly "Workbook" -- and a name actually scoped to that sheet
/// then carries the display label "Workbook" too, indistinguishable from the global sentinel.
/// <c>GetScopeOptions()</c>'s <c>Distinct()</c> also collapsed the sheet's own scope entry into the
/// sentinel, so the Scope combo could never even present that sheet's scope separately.
///
/// The fix threads the real scope identity (a nullable <see cref="SheetId"/>) end to end instead of
/// re-deriving it from the display label: <see cref="DefinedNameRow.Scope"/> tracks each
/// row's actual scope, <see cref="DefinedNameScopeOption"/> gives the Scope combo two distinct entries that
/// may share a label but not an identity, and <c>ResolveScopeSheetId(string)</c> was deleted outright
/// -- every caller that needs a scope identity now already has the real one in hand.
/// </summary>
public sealed class R114_NameManagerWorkbookSentinelTests
{
    // ── Delete (the most severe consequence: silently hitting an unrelated GLOBAL name) ─────────

    [Fact]
    public void DeleteButton_OnSheetLiterallyNamedWorkbook_RemovesTheScopedEntryNotAnUnrelatedGlobalName()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var sheet1 = workbook.AddSheet("Sheet1");
            var workbookSheet = workbook.AddSheet("Workbook");

            // A pre-existing GLOBAL name with the same text as the one we're about to delete --
            // proves Delete doesn't silently clobber it when the scoped entry is targeted instead.
            var globalRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
            workbook.DefineNamedRange("Rate", globalRange);

            var scopedRange = new GridRange(new CellAddress(workbookSheet.Id, 2, 2), new CellAddress(workbookSheet.Id, 2, 2));
            workbook.DefineNamedRange("Rate", scopedRange, NamedRangeMetadata.WorkbookScope, workbookSheet.Id);

            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            try
            {
                // Selected by its Refers To text (stable across both the pre- and post-fix view
                // model) rather than by ScopeSheetId, so this test actually RUNS (and fails) against
                // the pre-fix code instead of merely failing to compile.
                var namesList = DialogSourceTestSupport.GetPrivateField<ListView>(dialog, "NamesList");
                var scopedRow = namesList.Items
                    .Cast<DefinedNameRow>()
                    .Single(vm => vm.Name == "Rate" && vm.RefersTo == "Workbook!B2:B2");
                namesList.SelectedItem = scopedRow;

                HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Yes;
                DialogSourceTestSupport.InvokePrivateHandler(dialog, "DeleteButton_Click");

                workbook.ScopedNamedRanges.Should().NotContainKey(("Rate", workbookSheet.Id),
                    "deleting the row scoped to the sheet literally named 'Workbook' must remove THAT entry");
                workbook.NamedRanges.Should().ContainKey("Rate",
                    "the unrelated pre-existing GLOBAL name of the same text must be left completely untouched");
                workbook.NamedRanges["Rate"].Should().Be(globalRange);
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
                dialog.Close();
            }
        });
    }

    // Sibling no-regression: deleting the GLOBAL "Rate" row (not the scoped one) in the very same
    // ambiguous workbook must still remove only the global entry and leave the scoped one alone --
    // the fix must not have simply flipped which scope Delete always hits.
    [Fact]
    public void DeleteButton_OnGlobalNameCoexistingWithSheetLiterallyNamedWorkbook_RemovesOnlyTheGlobalEntry()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var sheet1 = workbook.AddSheet("Sheet1");
            var workbookSheet = workbook.AddSheet("Workbook");

            var globalRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
            workbook.DefineNamedRange("Rate", globalRange);

            var scopedRange = new GridRange(new CellAddress(workbookSheet.Id, 2, 2), new CellAddress(workbookSheet.Id, 2, 2));
            workbook.DefineNamedRange("Rate", scopedRange, NamedRangeMetadata.WorkbookScope, workbookSheet.Id);

            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            try
            {
                // Selected by its Refers To text (see the sibling test above) so this test also
                // compiles and runs against the pre-fix code.
                var namesList = DialogSourceTestSupport.GetPrivateField<ListView>(dialog, "NamesList");
                var globalRow = namesList.Items
                    .Cast<DefinedNameRow>()
                    .Single(vm => vm.Name == "Rate" && vm.RefersTo == "Sheet1!A1:A1");
                namesList.SelectedItem = globalRow;

                HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Yes;
                DialogSourceTestSupport.InvokePrivateHandler(dialog, "DeleteButton_Click");

                workbook.NamedRanges.Should().NotContainKey("Rate", "deleting the global row must remove the global entry");
                workbook.ScopedNamedRanges.Should().ContainKey(("Rate", workbookSheet.Id),
                    "the sheet-scoped entry must be left completely untouched");
                workbook.ScopedNamedRanges[("Rate", workbookSheet.Id)].Should().Be(scopedRange);
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
                dialog.Close();
            }
        });
    }

    // ── Scope combo reachability (the UI must be able to present the sheet's own scope) ─────────

    [Fact]
    public void GetScopeOptions_OffersDistinctEntryForSheetLiterallyNamedWorkbook()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            workbook.AddSheet("Sheet1");
            var workbookSheet = workbook.AddSheet("Workbook");

            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            try
            {
                var method = typeof(NamedRangeDialog).GetMethod("GetScopeOptions", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new MissingMethodException(nameof(NamedRangeDialog), "GetScopeOptions");
                var options = (IReadOnlyList<DefinedNameScopeOption>)method.Invoke(dialog, null)!;

                var workbookLabelled = options.Where(o => o.Label == "Workbook").ToList();
                workbookLabelled.Should().HaveCount(2,
                    "both the global sentinel and the sheet literally named 'Workbook' must be offered");
                workbookLabelled.Should().Contain(o => o.SheetId == null, "the workbook-global sentinel entry");
                workbookLabelled.Should().Contain(o => o.SheetId == workbookSheet.Id, "the sheet's own distinct entry");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    // Sibling no-regression: the ordinary (no collision) case still offers exactly one option per
    // sheet plus the sentinel -- the fix must not have introduced spurious duplicates generally.
    [Fact]
    public void GetScopeOptions_NoCollision_OffersOneEntryPerSheetPlusSentinel()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var sheet1 = workbook.AddSheet("Sheet1");
            var sheet2 = workbook.AddSheet("Sheet2");

            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            try
            {
                var method = typeof(NamedRangeDialog).GetMethod("GetScopeOptions", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new MissingMethodException(nameof(NamedRangeDialog), "GetScopeOptions");
                var options = (IReadOnlyList<DefinedNameScopeOption>)method.Invoke(dialog, null)!;

                options.Should().HaveCount(3);
                options.Should().ContainSingle(o => o.Label == "Workbook" && o.SheetId == null);
                options.Should().ContainSingle(o => o.Label == "Sheet1" && o.SheetId == sheet1.Id);
                options.Should().ContainSingle(o => o.Label == "Sheet2" && o.SheetId == sheet2.Id);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    // ── Define (Edit without touching Scope must keep routing to the original identity) ──────────

    [Fact]
    public void DefineOrUpdateName_EditingEntryScopedToSheetLiterallyNamedWorkbook_KeepsItScopedThere()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var sheet1 = workbook.AddSheet("Sheet1");
            var workbookSheet = workbook.AddSheet("Workbook");

            var globalRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
            workbook.DefineNamedRange("Rate", globalRange);

            var scopedRange = new GridRange(new CellAddress(workbookSheet.Id, 2, 2), new CellAddress(workbookSheet.Id, 2, 2));
            workbook.DefineNamedRange("Rate", scopedRange, NamedRangeMetadata.WorkbookScope, workbookSheet.Id);

            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Ok;
            try
            {
                // Simulates re-saving the sheet-scoped row's own Edit dialog (same name, same scope
                // identity, Refers To moved one cell over) -- the combo's Scope label round-trips as
                // "Workbook" either way, so only the threaded ScopeSheetId identity keeps this
                // targeting the sheet-scoped entry instead of the unrelated global one.
                var newScopedRange = new GridRange(new CellAddress(workbookSheet.Id, 3, 3), new CellAddress(workbookSheet.Id, 3, 3));
                var definition = new NameDefinitionDialogResult(
                    "Rate", "Workbook", "", FormatRange(newScopedRange, workbook), workbookSheet.Id);

                InvokeDefineOrUpdateName(dialog, definition, originalName: "Rate", originalScope: "Workbook", originalScopeSheetId: workbookSheet.Id);

                workbook.ScopedNamedRanges.Should().ContainKey(("Rate", workbookSheet.Id));
                workbook.ScopedNamedRanges[("Rate", workbookSheet.Id)].Should().Be(newScopedRange);
                workbook.NamedRanges.Should().ContainKey("Rate");
                workbook.NamedRanges["Rate"].Should().Be(globalRange,
                    "the unrelated pre-existing global name must be left untouched by editing the scoped one");
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
                dialog.Close();
            }
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string FormatRange(GridRange range, Workbook workbook)
    {
        var sheetName = workbook.GetSheet(range.Start.Sheet)?.Name ?? "Sheet1";
        return $"{sheetName}!{range.Start.ToA1()}";
    }

    private static void InvokeDefineOrUpdateName(
        NamedRangeDialog dialog,
        NameDefinitionDialogResult definition,
        string? originalName,
        string? originalScope,
        SheetId? originalScopeSheetId)
    {
        var method = typeof(NamedRangeDialog).GetMethod("DefineOrUpdateName", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(NamedRangeDialog), "DefineOrUpdateName");
        method.Invoke(dialog, [definition, originalName, originalScope, originalScopeSheetId]);
    }

    private static Func<IWorkbookCommand, CommandOutcome> CreateCommandBus(Workbook workbook)
    {
        var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
        return command => commandBus.Execute(workbook.Id, command);
    }
}
