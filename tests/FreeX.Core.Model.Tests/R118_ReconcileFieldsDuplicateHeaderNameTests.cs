using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R118-commands-pivot-duplicate-header: <see cref="PivotCacheFieldFactory.ReconcileFields"/> used to
/// build its existing-field lookup as <c>new Dictionary&lt;string, PivotCacheFieldModel&gt;</c> populated
/// via <c>TryAdd</c>, which collapses two cache fields sharing the same <see
/// cref="PivotCacheFieldModel.Name"/> (a source range with two identically-titled header columns -- e.g.
/// two columns both literally "Category" -- which nothing in <see cref="AddPivotTableCommand"/> prevents,
/// unlike real Excel, which auto-suffixes the second occurrence) down to a single dictionary entry: the
/// FIRST same-named field. On a later refresh, the SECOND live "Category" header would then be
/// (wrongly) reconciled against the FIRST field's record instead of its own, via <see
/// cref="PivotCacheFieldFactory.MergeFromSourceData"/> -- polluting the second column's reconciled
/// SharedItems with the first column's distinct values, while silently discarding the second field's own
/// original record.
///
/// Driven through the real product entry points: <see cref="AddPivotTableCommand.Apply"/> builds the
/// initial (duplicate-named) cache, then <see cref="RefreshPivotTableCommand.Apply"/> (F5 / "Refresh
/// PivotTable", which always calls <see cref="PivotTableRefreshService.Refresh"/> with
/// <c>rescanCacheSharedItems: true</c>, routing through <see cref="PivotCacheFieldFactory.ReconcileFields"/>)
/// is the entry point that reproduces the reconciliation bug after each duplicate-named column grows a
/// genuinely new, column-specific distinct value.
/// </summary>
public sealed class R118_ReconcileFieldsDuplicateHeaderNameTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot, PivotCacheModel Cache) BuildDuplicateHeaderPivot()
    {
        var workbook = new Workbook("R118DuplicateHeaderReconcile");
        var sheet = workbook.AddSheet("Data");

        // Two columns both literally titled "Category" -- AddPivotTableCommand builds one cache field
        // per source column directly from header text with no uniqueness check, so this is entirely
        // reachable through the real UI ("Insert PivotTable" over a range with two same-titled headers).
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("X"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("P"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("Y"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));

        var ctx = new TestCommandContext(workbook);
        var addPivot = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "C3"),
            Range(sheet, "E3", "F6"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [2]);
        addPivot.Apply(ctx).Success.Should().BeTrue();

        var cache = workbook.PivotCaches.Should().ContainSingle().Subject;
        cache.Fields.Should().HaveCount(3);
        cache.Fields.Select(f => f.Name).Should().Equal("Category", "Category", "Amount");
        // Sanity: at creation, each duplicate-named field already correctly reflects its OWN column
        // (creation goes straight through BuildFromSourceData per column, not ReconcileFields).
        cache.Fields[0].SharedItems.Should().Equal("X", "Y");
        cache.Fields[1].SharedItems.Should().Equal("P", "Q");

        var pivot = sheet.PivotTables.Single(p => p.Name == "PivotTable1");
        return (workbook, sheet, pivot, cache);
    }

    /// <summary>
    /// THE anchor test: after each duplicate-named "Category" column grows its OWN new distinct value
    /// (column A: X,Y -&gt; X,Z,Y ; column B: P,Q -&gt; M,P,Q), a refresh must reconcile each field
    /// against its OWN column, not smear one column's values onto the other's field.
    /// </summary>
    [Fact]
    public void Refresh_DuplicateNamedColumnsBothGrowOwnNewValue_ReconcilesEachFieldAgainstItsOwnColumn()
    {
        var (workbook, sheet, pivot, cache) = BuildDuplicateHeaderPivot();

        // Mutate existing data rows in place (no range/header change) so this stays an ordinary
        // refresh: column A's row2 gains a new distinct value "Z", column B's row2 gains a new distinct
        // value "M" -- each is unique to its own column.
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("Z"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("M"));

        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        refresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        cache.Fields.Should().HaveCount(3);
        cache.Fields.Select(f => f.Name).Should().Equal("Category", "Category", "Amount");

        var firstCategory = cache.Fields[0];
        var secondCategory = cache.Fields[1];

        // Bug (before fix): the dictionary-based lookup collapsed both "Category" fields to the FIRST
        // one, so the SECOND live "Category" header (column B) got merged against the FIRST field's
        // record (X, Y) instead of its own (P, Q) -- polluting the reconciled field at position 1 with
        // column A's values ("X"/"Y"/"Z") it must never contain, while the genuinely-new "M" from
        // column B would still show up (from a stray live-column-1 scan), making the corruption easy to
        // miss if only checking for the presence of "M".
        firstCategory.SharedItems.Should().Equal(["X", "Y", "Z"],
            "the FIRST Category column's own field must reflect only ITS OWN column's values");
        secondCategory.SharedItems.Should().Equal(["P", "Q", "M"],
            "the SECOND Category column's own field must reflect only ITS OWN column's values, not the first column's");

        secondCategory.SharedItems.Should().NotContain(["X", "Y", "Z"],
            "the second duplicate-named field must never absorb the first duplicate-named field's distinct values");
        firstCategory.SharedItems.Should().NotContain(["P", "Q", "M"],
            "the first duplicate-named field must never absorb the second duplicate-named field's distinct values");
    }

    /// <summary>
    /// No-regression sibling: two UNIQUELY-named columns (the ordinary, overwhelmingly common case)
    /// must keep reconciling exactly as before -- each field picks up only its own column's genuinely
    /// new distinct value, with no cross-contamination between fields. This must pass both before and
    /// after the r118 fix (it is the case the pre-fix dictionary-based lookup always got right, and the
    /// positional-first rewrite must not regress it).
    /// </summary>
    [Fact]
    public void Refresh_UniquelyNamedColumnsEachGrowOwnNewValue_NoRegression()
    {
        var workbook = new Workbook("R118UniqueHeaderReconcileNoRegression");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));

        var ctx = new TestCommandContext(workbook);
        var addPivot = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "C3"),
            Range(sheet, "E3", "F6"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [2]);
        addPivot.Apply(ctx).Success.Should().BeTrue();

        var cache = workbook.PivotCaches.Should().ContainSingle().Subject;

        // Each column grows its own new distinct value.
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q3"));

        var pivot = sheet.PivotTables.Single(p => p.Name == "PivotTable1");
        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        refresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        cache.Fields.Single(f => f.Name == "Region").SharedItems.Should().Equal("East", "West", "North");
        cache.Fields.Single(f => f.Name == "Quarter").SharedItems.Should().Equal("Q1", "Q2", "Q3");
    }
}
