using System.Collections.Generic;
using System.Linq;

using FluentAssertions;

using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-134 regression tests for finding R134-app-name-manager-workbook-sentinel
/// (src/FreeX.App.Avalonia/Dialogs/DefinedNamesShellGlue.cs:188): the Avalonia Name Manager resolved a
/// defined name's SCOPE by comparing its display label to the literal "Workbook" sentinel
/// (<c>DefinedNamesShellGlue.ResolveScopeSheetId(workbook, row.ScopeLabel)</c>), so a workbook containing a
/// worksheet actually named "Workbook" made a name scoped to THAT sheet indistinguishable from the true
/// workbook-global scope -- Delete could then remove the wrong entry (or an unrelated global name of the
/// same text), and the Define Name editor's Scope combo would silently re-pick the global scope on Edit.
///
/// Nothing in <see cref="Workbook.ValidateSheetNameStructure"/> reserves "Workbook" as a sheet name, so this
/// collision is reachable with ordinary user data. Mirrors the WPF host's already-fixed
/// R114-app-name-manager-workbook-sentinel-3-2 (NamedRangeDialog.xaml.cs), whose fix threads the real scope
/// identity (<c>NamedRangeViewModel.ScopeSheetId</c>) end to end instead of re-deriving it from the display
/// label.
///
/// The Avalonia fix adds <see cref="DefinedNameRow.ScopeSheetId"/> -- the row's real scope identity,
/// captured directly from <see cref="Workbook.ScopedNamedRanges"/>/<see cref="Workbook.ScopedNamedFormulas"/>
/// keys when <see cref="DefinedNamesShellGlue.BuildRows"/> projects them -- and threads it through the
/// Name Manager's Delete handler, the Define Name editor's Scope-combo pre-selection
/// (<c>MainWindow.FindScopeIndex</c>), and the duplicate-name guard (<c>MainWindow.OriginalNameForDuplicateCheck</c>,
/// covered by the updated R88 tests) instead of re-parsing <see cref="DefinedNameRow.ScopeLabel"/>.
/// </summary>
public sealed class R134_NameManagerWorkbookScopeIdentityTests
{
    private static (Workbook Workbook, Sheet Sheet1, Sheet WorkbookSheet) CreateAmbiguousWorkbook()
    {
        var workbook = new Workbook("Book");
        var sheet1 = workbook.AddSheet("Sheet1");
        var workbookSheet = workbook.AddSheet("Workbook");
        return (workbook, sheet1, workbookSheet);
    }

    private static GridRange Cell(Sheet sheet, uint row, uint col) =>
        new(new CellAddress(sheet.Id, row, col), new CellAddress(sheet.Id, row, col));

    // ── BuildRows: the row must carry the REAL scope identity, not just the (possibly-colliding) label ──

    [Fact]
    public void BuildRows_ForNameScopedToSheetLiterallyNamedWorkbook_CarriesThatSheetsRealIdentity()
    {
        var (workbook, _, workbookSheet) = CreateAmbiguousWorkbook();
        workbook.DefineNamedRange("Rate", Cell(workbookSheet, 2, 2), new NamedRangeMetadata("Workbook", ""), workbookSheet.Id);

        var row = DefinedNamesShellGlue.BuildRows(workbook).Single(r => r.Name == "Rate");

        // The label still reads "Workbook" (that IS the sheet's real display name) -- the point of the
        // fix is that ScopeSheetId disambiguates it from the true workbook-global scope even though the
        // label alone cannot.
        row.ScopeLabel.Should().Be("Workbook");
        row.ScopeSheetId.Should().Be(workbookSheet.Id);
    }

    // Sibling no-regression: a genuinely workbook-global name must still carry a null identity.
    [Fact]
    public void BuildRows_ForWorkbookGlobalName_CarriesNullScopeSheetId()
    {
        var (workbook, sheet1, _) = CreateAmbiguousWorkbook();
        workbook.DefineNamedRange("Total", Cell(sheet1, 1, 1));

        var row = DefinedNamesShellGlue.BuildRows(workbook).Single(r => r.Name == "Total");

        row.ScopeSheetId.Should().BeNull();
    }

    // ── Delete: the handler must route on the row's real identity, not a re-resolved label ──────────

    [Fact]
    public void DeleteFlow_OnSheetLiterallyNamedWorkbook_RemovesTheScopedEntryNotAnUnrelatedGlobalName()
    {
        var (workbook, sheet1, workbookSheet) = CreateAmbiguousWorkbook();

        // A pre-existing GLOBAL name with the same text as the one we're about to delete -- proves
        // Delete doesn't silently clobber it when the scoped entry is the one actually targeted.
        var globalRange = Cell(sheet1, 1, 1);
        workbook.DefineNamedRange("Rate", globalRange);

        var scopedRange = Cell(workbookSheet, 2, 2);
        workbook.DefineNamedRange("Rate", scopedRange, new NamedRangeMetadata("Workbook", ""), workbookSheet.Id);

        // Selected by its Refers To text (stable regardless of the fix) rather than by ScopeSheetId, so
        // this test exercises the real BuildRows projection instead of hand-constructing the row.
        var row = DefinedNamesShellGlue.BuildRows(workbook)
            .Single(r => r.Name == "Rate" && r.RefersTo == "Workbook!B2");

        // This is exactly what MainWindow.DefinedNames.cs's Delete button handler now does: read the
        // row's own tracked scope identity directly, never re-resolve it from the display label.
        var command = DefinedNamesShellGlue.BuildDeleteCommand(row.Name, row.ScopeSheetId);
        var outcome = command.Apply(new GlueTestCommandContext(workbook));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.ScopedNamedRanges.Should().NotContainKey(("Rate", workbookSheet.Id),
            "deleting the row scoped to the sheet literally named 'Workbook' must remove THAT entry");
        workbook.NamedRanges.Should().ContainKey("Rate",
            "the unrelated pre-existing GLOBAL name of the same text must be left completely untouched");
        workbook.NamedRanges["Rate"].Should().Be(globalRange);
    }

    // Sibling no-regression: deleting the GLOBAL "Rate" row (not the scoped one) in the very same
    // ambiguous workbook must still remove only the global entry and leave the scoped one alone -- the
    // fix must not have simply flipped which scope Delete always hits.
    [Fact]
    public void DeleteFlow_OnGlobalNameCoexistingWithSheetLiterallyNamedWorkbook_RemovesOnlyTheGlobalEntry()
    {
        var (workbook, sheet1, workbookSheet) = CreateAmbiguousWorkbook();

        var globalRange = Cell(sheet1, 1, 1);
        workbook.DefineNamedRange("Rate", globalRange);

        var scopedRange = Cell(workbookSheet, 2, 2);
        workbook.DefineNamedRange("Rate", scopedRange, new NamedRangeMetadata("Workbook", ""), workbookSheet.Id);

        var row = DefinedNamesShellGlue.BuildRows(workbook)
            .Single(r => r.Name == "Rate" && r.RefersTo == "Sheet1!A1");

        var command = DefinedNamesShellGlue.BuildDeleteCommand(row.Name, row.ScopeSheetId);
        var outcome = command.Apply(new GlueTestCommandContext(workbook));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.NamedRanges.Should().NotContainKey("Rate", "deleting the global row must remove the global entry");
        workbook.ScopedNamedRanges.Should().ContainKey(("Rate", workbookSheet.Id),
            "the sheet-scoped entry must be left completely untouched");
        workbook.ScopedNamedRanges[("Rate", workbookSheet.Id)].Should().Be(scopedRange);
    }

    // ── Define Name editor's Scope combo pre-selection (Edit) must key off identity too ──────────────

    [Fact]
    public void FindScopeIndex_ForSeedScopedToSheetLiterallyNamedWorkbook_SelectsThatSheetsEntryNotTheGlobalSentinel()
    {
        var (workbook, _, workbookSheet) = CreateAmbiguousWorkbook();
        var choices = DefinedNamesShellGlue.BuildScopeChoices(workbook);

        var index = MainWindow.FindScopeIndexForTest(choices, workbookSheet.Id);

        choices[index].Scope.Sheet.Should().Be(workbookSheet.Id,
            "the Scope combo must pre-select the sheet literally named 'Workbook', not the index-0 global sentinel that shares its label");
        index.Should().NotBe(0);
    }

    // Sibling no-regression: the true workbook-global scope (no seed, or a workbook-scoped seed) must
    // still land on the global sentinel entry.
    [Fact]
    public void FindScopeIndex_ForWorkbookGlobalScope_SelectsTheGlobalSentinelEntry()
    {
        var (workbook, _, _) = CreateAmbiguousWorkbook();
        var choices = DefinedNamesShellGlue.BuildScopeChoices(workbook);

        var index = MainWindow.FindScopeIndexForTest(choices, null);

        index.Should().Be(0);
        choices[0].Scope.Sheet.Should().BeNull();
    }

    // Sibling no-regression: an ordinary (non-colliding) sheet scope must still resolve to its own entry.
    [Fact]
    public void FindScopeIndex_ForOrdinarySheetScope_SelectsThatSheetsEntry()
    {
        var (workbook, sheet1, _) = CreateAmbiguousWorkbook();
        var choices = DefinedNamesShellGlue.BuildScopeChoices(workbook);

        var index = MainWindow.FindScopeIndexForTest(choices, sheet1.Id);

        choices[index].Scope.Sheet.Should().Be(sheet1.Id);
    }

    /// <summary>A minimal <see cref="ICommandContext"/> for running named-range commands against a workbook.</summary>
    private sealed class GlueTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
