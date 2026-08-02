using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the r114 finding: Workbook.DefineNamedRange (and the sheet-scoped
/// DefineNamedRange/DefineNamedFormula overloads) never checked or removed a same-text entry
/// in the *other* kind's dictionary (NamedRanges vs NamedFormulas / ScopedNamedRanges vs
/// ScopedNamedFormulas). "Create Names from Selection" (CreateNamedRangesFromSelectionCommand)
/// calls Workbook.DefineNamedRange directly for every candidate label with no cross-kind
/// duplicate probe, so a selection label that happened to match an existing named FORMULA
/// silently left both dictionaries holding the same key. FormulaEvaluator.EvaluateNamedRange
/// always resolves a bare name via NamedRanges before falling back to NamedFormulas, so the
/// stale formula entry became permanently unreachable (and, in the Avalonia Name Manager list
/// builder at MainWindow.DefinedNames.cs:633, which concatenates
/// NamedRanges.Keys + NamedFormulas.Keys, the same name would appear twice).
///
/// Excel ground truth: defined names are unique per scope regardless of kind; defining a name
/// as a range must supersede any previous formula-kind definition of that same name/scope (and
/// vice versa) rather than let both registrations coexist.
/// </summary>
public sealed class R114_NamedRangeFormulaCollisionTests
{
    // ── Workbook-global: the exact defect scenario, reached through the real command ────

    [Fact]
    public void CreateNamesFromSelection_LabelCollidingWithExistingNamedFormula_RemovesStaleFormulaEntry()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Pre-existing workbook-global named FORMULA, exactly as XLSX load / Name Manager
        // "New Name -> Refers to: =10+20" would create.
        wb.NamedFormulas["Total"] = "10+20";

        // A selection whose top-row label is "Total" -- this is what CreateNamedRangesFromSelectionCommand
        // (the WPF host's "Create Names from Selection" entry point) builds from real cell content.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(99));

        var command = new CreateNamedRangesFromSelectionCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            UseTopRow: true,
            UseLeftColumn: false,
            UseBottomRow: false,
            UseRightColumn: false);

        command.Apply(ctx).Success.Should().BeTrue();

        // The new named RANGE was created...
        wb.NamedRanges.Should().ContainKey("Total");
        // ...and the stale named FORMULA must no longer be registered under the same key -- prior
        // to the fix this assertion failed because Workbook.DefineNamedRange never touched
        // NamedFormulas, leaving "Total" registered in BOTH dictionaries simultaneously (the
        // formula permanently unreachable, and duplicated in any UI that lists
        // NamedRanges.Keys.Concat(NamedFormulas.Keys), e.g. MainWindow.DefinedNames.cs:633).
        wb.NamedFormulas.Should().NotContainKey("Total");
    }

    [Fact]
    public void DefineNamedRange_WorkbookGlobal_CollidingWithExistingNamedFormula_RemovesFormulaEntry()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        wb.NamedFormulas["Rate"] = "0.05";
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        wb.DefineNamedRange("Rate", range);

        wb.NamedRanges.Should().ContainKey("Rate");
        wb.NamedRanges["Rate"].Should().Be(range);
        wb.NamedFormulas.Should().NotContainKey("Rate");
    }

    // ── Sheet-scoped mirror of the same defect class ────────────────────────────────────

    [Fact]
    public void DefineNamedRange_SheetScoped_CollidingWithExistingScopedNamedFormula_RemovesFormulaEntry()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        wb.DefineNamedFormula("LocalRate", "0.07", sheet.Id);
        var range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2));

        wb.DefineNamedRange("LocalRate", range, null, sheet.Id);

        wb.ScopedNamedRanges.Should().ContainKey(("LocalRate", sheet.Id));
        wb.ScopedNamedFormulas.Should().NotContainKey(("LocalRate", sheet.Id));
    }

    [Fact]
    public void DefineNamedFormula_SheetScoped_CollidingWithExistingScopedNamedRange_RemovesRangeEntry()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 3, 3), new CellAddress(sheet.Id, 3, 3));
        wb.DefineNamedRange("Local", range, new NamedRangeMetadata("Sheet1", "note"), sheet.Id);

        wb.DefineNamedFormula("Local", "1+1", sheet.Id);

        wb.ScopedNamedFormulas.Should().ContainKey(("Local", sheet.Id));
        wb.ScopedNamedRanges.Should().NotContainKey(("Local", sheet.Id));
        // Metadata for the superseded range entry must not linger either.
        wb.TryGetScopedNamedRangeMetadata("Local", sheet.Id, out _).Should().BeFalse();
    }

    // ── No-regression: ordinary same-kind redefinition must keep working ────────────────

    [Fact]
    public void DefineNamedRange_RedefiningExistingRange_StillUpdatesRangeAndMetadata_NoFormulaSideEffect()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var original = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var updated = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 6, 6));
        wb.DefineNamedRange("Sales", original, new NamedRangeMetadata("Sheet1", "v1"));

        wb.DefineNamedRange("Sales", updated, new NamedRangeMetadata("Sheet1", "v2"));

        wb.NamedRanges["Sales"].Should().Be(updated);
        wb.NamedRangeMetadataByName["Sales"].Should().Be(new NamedRangeMetadata("Sheet1", "v2"));
        wb.NamedFormulas.Should().NotContainKey("Sales");
    }

    [Fact]
    public void DefineNamedRange_CaseOnlyRename_StillUpdatesKeyCasing()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        wb.DefineNamedRange("revenue", range);

        wb.DefineNamedRange("Revenue", range);

        wb.NamedRanges.Keys.Should().ContainSingle().Which.Should().Be("Revenue");
    }

    [Fact]
    public void DefineNamedRange_NoCollision_DoesNotTouchUnrelatedNamedFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        wb.NamedFormulas["OtherFormula"] = "3*3";
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        wb.DefineNamedRange("BrandNew", range);

        wb.NamedRanges.Should().ContainKey("BrandNew");
        wb.NamedFormulas.Should().ContainKey("OtherFormula");
        wb.NamedFormulas["OtherFormula"].Should().Be("3*3");
    }

    // ── End-to-end: the model no longer double-lists a colliding name ───────────────────

    [Fact]
    public void AfterCollision_NameNoLongerAppearsInBothDictionaries_SoAnyMergedListingIsNotDuplicated()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        wb.NamedFormulas["Combined"] = "1+1";

        wb.DefineNamedRange("Combined", new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));

        // Mirrors MainWindow.DefinedNames.cs:633's `NamedRanges.Keys.Concat(NamedFormulas.Keys)`
        // Name Manager listing -- before the fix "Combined" would appear twice.
        var mergedNames = wb.NamedRanges.Keys.Concat(wb.NamedFormulas.Keys)
            .Where(n => string.Equals(n, "Combined", StringComparison.OrdinalIgnoreCase));
        mergedNames.Should().ContainSingle();
    }
}
