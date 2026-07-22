using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R64-cache-invalidation-sweep-1 (RowColumnShiftHelpers.Rules.cs): the four whole-row/column
/// shift helpers (ShiftRuleRowsUp/Down, ShiftRuleColumnsUp/Down) mutate CF rules' AppliesTo (and
/// AdditionalRanges) in place but must also call <see cref="ConditionalFormatCollection.NotifyRulesChanged"/>
/// afterward so <see cref="ConditionalFormatCollection.Version"/> bumps — otherwise any cache keyed
/// on that Version (e.g. the viewport CF-context cache) keeps serving the stale pre-shift geometry
/// after a whole-row/column insert/delete that moves no cell content.
/// </summary>
public sealed class R64_CfShiftVersionInvalidation_Tests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static GridRange Range(SheetId id, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(id, r1, c1), new CellAddress(id, r2, c2));

    private static ConditionalFormat MakeCf(GridRange appliesTo) => new()
    {
        AppliesTo = appliesTo,
        RuleType = CfRuleType.ColorScale,
        Priority = 1
    };

    [Fact]
    public void InsertRows_AboveCfRule_NoCellsMoved_StillBumpsConditionalFormatsVersion()
    {
        // Empty sheet: a whole-row insert above the rule's range shifts the rule's AppliesTo
        // down but moves no actual cell content, so ContentVersion alone would not signal that
        // the CF geometry changed. ConditionalFormats.Version must bump regardless.
        var (_, sheet, ctx) = Setup();
        var cf = MakeCf(Range(sheet.Id, 10, 1, 20, 1)); // A10:A20
        sheet.ConditionalFormats.Add(cf);
        var versionBefore = sheet.ConditionalFormats.Version;

        new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3).Apply(ctx).Success.Should().BeTrue();

        sheet.ConditionalFormats.Version.Should().BeGreaterThan(versionBefore,
            because: "the rule's AppliesTo shifted from A10:A20 to A13:A23, so any Version-keyed cache must be invalidated");
        cf.AppliesTo.Should().Be(Range(sheet.Id, 13, 1, 23, 1), because: "geometry still shifts correctly");
    }

    [Fact]
    public void DeleteRows_AboveCfRule_NoCellsMoved_StillBumpsConditionalFormatsVersion()
    {
        var (_, sheet, ctx) = Setup();
        var cf = MakeCf(Range(sheet.Id, 10, 1, 20, 1)); // A10:A20
        sheet.ConditionalFormats.Add(cf);
        var versionBefore = sheet.ConditionalFormats.Version;

        new DeleteRowsCommand(sheet.Id, startRow: 1, count: 3).Apply(ctx).Success.Should().BeTrue();

        sheet.ConditionalFormats.Version.Should().BeGreaterThan(versionBefore,
            because: "the rule's AppliesTo shifted up from A10:A20 to A7:A17");
        cf.AppliesTo.Should().Be(Range(sheet.Id, 7, 1, 17, 1));
    }

    [Fact]
    public void InsertColumns_LeftOfCfRule_NoCellsMoved_StillBumpsConditionalFormatsVersion()
    {
        var (_, sheet, ctx) = Setup();
        var cf = MakeCf(Range(sheet.Id, 1, 10, 1, 20)); // J1:T1
        sheet.ConditionalFormats.Add(cf);
        var versionBefore = sheet.ConditionalFormats.Version;

        new InsertColumnsCommand(sheet.Id, beforeCol: 5, count: 3).Apply(ctx).Success.Should().BeTrue();

        sheet.ConditionalFormats.Version.Should().BeGreaterThan(versionBefore,
            because: "the rule's AppliesTo shifted right from J1:T1 to M1:W1");
        cf.AppliesTo.Should().Be(Range(sheet.Id, 1, 13, 1, 23));
    }

    [Fact]
    public void DeleteColumns_LeftOfCfRule_NoCellsMoved_StillBumpsConditionalFormatsVersion()
    {
        var (_, sheet, ctx) = Setup();
        var cf = MakeCf(Range(sheet.Id, 1, 10, 1, 20)); // J1:T1
        sheet.ConditionalFormats.Add(cf);
        var versionBefore = sheet.ConditionalFormats.Version;

        new DeleteColumnsCommand(sheet.Id, startCol: 1, count: 3).Apply(ctx).Success.Should().BeTrue();

        sheet.ConditionalFormats.Version.Should().BeGreaterThan(versionBefore,
            because: "the rule's AppliesTo shifted left from J1:T1 to G1:Q1");
        cf.AppliesTo.Should().Be(Range(sheet.Id, 1, 7, 1, 17));
    }

    [Fact]
    public void InsertRows_ShiftedCfRule_UndoStillRestoresOriginalGeometry()
    {
        // Sibling no-regression: the Version-bump addition must not disturb the existing
        // capture/restore undo path for a rule whose AppliesTo actually shifts.
        var (_, sheet, ctx) = Setup();
        var cf = MakeCf(Range(sheet.Id, 10, 1, 20, 1)); // A10:A20
        sheet.ConditionalFormats.Add(cf);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3);
        cmd.Apply(ctx).Success.Should().BeTrue();
        cf.AppliesTo.Should().Be(Range(sheet.Id, 13, 1, 23, 1));

        cmd.Revert(ctx);

        sheet.ConditionalFormats.Should().ContainSingle();
        sheet.ConditionalFormats[0].AppliesTo.Should().Be(Range(sheet.Id, 10, 1, 20, 1),
            because: "undo restores the original pre-insert AppliesTo");
    }

    [Fact]
    public void DeleteColumns_ShiftedCfRule_UndoStillRestoresOriginalGeometry()
    {
        var (_, sheet, ctx) = Setup();
        var cf = MakeCf(Range(sheet.Id, 1, 10, 1, 20)); // J1:T1
        sheet.ConditionalFormats.Add(cf);

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 1, count: 3);
        cmd.Apply(ctx).Success.Should().BeTrue();
        cf.AppliesTo.Should().Be(Range(sheet.Id, 1, 7, 1, 17));

        cmd.Revert(ctx);

        sheet.ConditionalFormats.Should().ContainSingle();
        sheet.ConditionalFormats[0].AppliesTo.Should().Be(Range(sheet.Id, 1, 10, 1, 20),
            because: "undo restores the original pre-delete AppliesTo");
    }
}
