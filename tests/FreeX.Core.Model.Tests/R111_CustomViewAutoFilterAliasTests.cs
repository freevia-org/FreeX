using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R111-custom-view-autofilter-alias: SaveCustomViewCommand.Apply (via AugmentCapturedState) must
/// deep-clone Sheet.AutoFilter into the WorksheetCustomViewState it stores in workbook.CustomViews,
/// not alias the live, mutable WorksheetAutoFilterModel -- because WorksheetAutoFilterColumnSync
/// (the real entry point every ordinary filter command routes through, see FilterCommand.cs) mutates
/// sheet.AutoFilter.FilterColumns in place (RemoveAll/Add/Sort), and FilterColumns is a get-only
/// List&lt;T&gt; with no setter, so it is designed to be mutated that way. Aliasing means a later,
/// completely ordinary filter edit silently rewrites an already-saved Custom View's "frozen"
/// snapshot -- and the customView XML XlsxCustomViewMapper later serializes from it -- to reflect
/// whatever the live filter happens to be, instead of what it was at save time. Excel ground truth:
/// a saved Custom View's filter state is immutable once saved; later filter edits never retroactively
/// alter it, and Show View always reapplies exactly the criteria that existed at save time.
/// </summary>
public sealed class R111_CustomViewAutoFilterAliasTests
{
    private static (Workbook workbook, Sheet sheet, GridRange range) BuildFilterableSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Banana"));
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        return (workbook, sheet, range);
    }

    [Fact]
    public void R111_SaveCustomViewCommand_LaterOrdinaryFilterEditDoesNotMutateSavedViewSnapshot()
    {
        var (workbook, sheet, range) = BuildFilterableSheet();
        var ctx = new TestCommandContext(workbook);

        // Real product entry points: turn on AutoFilter for the range, then set a value-list
        // filter criterion via the same command every interactive filter-dropdown pick runs.
        new ToggleWorksheetAutoFilterCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();
        new FilterCommand(sheet.Id, range, 0, ["Apple"]).Apply(ctx).Success.Should().BeTrue();

        // Save a Custom View with "Include filter settings" (the default).
        new SaveCustomViewCommand("V1").Apply(ctx).Success.Should().BeTrue();

        var savedState = workbook.CustomViews.Should().ContainSingle().Subject.Sheets.Should().ContainSingle().Subject;
        savedState.AutoFilter.Should().NotBeNull();
        var savedColumns = savedState.AutoFilter!.FilterColumns;
        savedColumns.Should().ContainSingle(c => c.Values.SequenceEqual(new[] { "Apple" }));

        // An ordinary, unrelated filter edit through the real FilterCommand entry point -- this is
        // exactly what WorksheetAutoFilterColumnSync.Apply/Restore does for every interactive
        // filter-dropdown pick in the app.
        new FilterCommand(sheet.Id, range, 0, ["Banana"]).Apply(ctx).Success.Should().BeTrue();

        // The live sheet's own AutoFilter reflects the new pick...
        sheet.AutoFilter!.FilterColumns.Should().ContainSingle(c => c.Values.SequenceEqual(new[] { "Banana" }));

        // ...but the already-saved view V1 must still remember "Apple" -- Excel freezes a Custom
        // View's filter state at save time; it is never retroactively rewritten by later edits.
        savedColumns.Should().ContainSingle(c => c.Values.SequenceEqual(new[] { "Apple" }),
            "SaveCustomViewCommand must deep-clone Sheet.AutoFilter, not alias the live mutable object");
    }

    [Fact]
    public void R111_ApplyCustomViewCommand_ShowViewThenOrdinaryFilterEditDoesNotMutatePersistedView()
    {
        var (workbook, sheet, range) = BuildFilterableSheet();
        var ctx = new TestCommandContext(workbook);

        new ToggleWorksheetAutoFilterCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();
        new FilterCommand(sheet.Id, range, 0, ["Apple"]).Apply(ctx).Success.Should().BeTrue();
        new SaveCustomViewCommand("V1").Apply(ctx).Success.Should().BeTrue();

        // Change the live filter after saving, then "Show" the saved view (ApplyCustomViewCommand)
        // -- the real product entry point for View > Custom Views > V1 > Show.
        new FilterCommand(sheet.Id, range, 0, ["Banana"]).Apply(ctx).Success.Should().BeTrue();
        new ApplyCustomViewCommand("V1").Apply(ctx).Success.Should().BeTrue();

        sheet.AutoFilter!.FilterColumns.Should().ContainSingle(c => c.Values.SequenceEqual(new[] { "Apple" }));

        var persistedColumns = workbook.CustomViews.Single().Sheets.Single().AutoFilter!.FilterColumns;

        // A further ordinary filter edit on the now-restored live sheet must not reach back into
        // the workbook's permanently persisted view V1 -- ApplyExtendedState/CustomViewStatePlanner.
        // ApplyState must clone onto the sheet, not wire the sheet up to share the view's own object.
        new FilterCommand(sheet.Id, range, 0, ["Banana"]).Apply(ctx).Success.Should().BeTrue();

        persistedColumns.Should().ContainSingle(c => c.Values.SequenceEqual(new[] { "Apple" }),
            "Showing a Custom View must clone its AutoFilter onto the sheet, not alias the view's own persisted object");
    }

    [Fact]
    public void R111_NoRegression_ApplyCustomViewCommand_StillRestoresSavedAutoFilterCriteria()
    {
        // Sibling/no-regression: the core "Show View restores the saved filter" behavior (unrelated
        // to aliasing) must still work after cloning is introduced on both the capture and apply
        // sides.
        var (workbook, sheet, range) = BuildFilterableSheet();
        var ctx = new TestCommandContext(workbook);

        new ToggleWorksheetAutoFilterCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();
        new FilterCommand(sheet.Id, range, 0, ["Apple"]).Apply(ctx).Success.Should().BeTrue();
        new SaveCustomViewCommand("V1").Apply(ctx).Success.Should().BeTrue();

        new FilterCommand(sheet.Id, range, 0, ["Banana"]).Apply(ctx).Success.Should().BeTrue();
        sheet.AutoFilter!.FilterColumns.Should().ContainSingle(c => c.Values.SequenceEqual(new[] { "Banana" }));

        new ApplyCustomViewCommand("V1").Apply(ctx).Success.Should().BeTrue();

        sheet.AutoFilter!.FilterColumns.Should().ContainSingle(c => c.Values.SequenceEqual(new[] { "Apple" }));
    }
}
