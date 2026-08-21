using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class GroupedApplyStyleCommandTests
{
    [Fact]
    public void Apply_AppliesStyleToSameRangeOnGroupedSheetsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        var sourceRange = new GridRange(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 1, 2));

        var command = new GroupedApplyStyleCommand(
            [sheet1.Id, sheet2.Id],
            sourceRange,
            new StyleDiff(Bold: true));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Empty cells use the style-only path — no blank Cell is materialised
        wb.GetStyle(sheet1.GetStyleOnly(1, 1)!.Value).Bold.Should().BeTrue();
        wb.GetStyle(sheet1.GetStyleOnly(1, 2)!.Value).Bold.Should().BeTrue();
        wb.GetStyle(sheet2.GetStyleOnly(1, 1)!.Value).Bold.Should().BeTrue();
        wb.GetStyle(sheet2.GetStyleOnly(1, 2)!.Value).Bold.Should().BeTrue();
        sheet1.CellCount.Should().Be(0);
        sheet2.CellCount.Should().Be(0);

        command.Revert(ctx);

        sheet1.GetCell(new CellAddress(sheet1.Id, 1, 1)).Should().BeNull();
        sheet1.GetCell(new CellAddress(sheet1.Id, 1, 2)).Should().BeNull();
        sheet2.GetCell(new CellAddress(sheet2.Id, 1, 1)).Should().BeNull();
        sheet2.GetCell(new CellAddress(sheet2.Id, 1, 2)).Should().BeNull();
        sheet1.GetStyleOnly(1, 1).Should().BeNull();
        sheet1.GetStyleOnly(1, 2).Should().BeNull();
        sheet2.GetStyleOnly(1, 1).Should().BeNull();
        sheet2.GetStyleOnly(1, 2).Should().BeNull();
    }

    [Fact]
    public void EstimatedBytes_ScalesWithGroupedRangeSize()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var range = new GridRange(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 3, 4));
        var command = new GroupedApplyStyleCommand(
            [sheet1.Id, sheet2.Id],
            range,
            new StyleDiff(Bold: true));

        command.Should().BeAssignableTo<IEstimatesMemory>();
        ((IEstimatesMemory)command).EstimatedBytes.Should().Be(4_800);
    }

    [Fact]
    public void Apply_ReusesRegisteredStyleForRepeatedBaseStyle()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        var command = new GroupedApplyStyleCommand(
            [sheet1.Id, sheet2.Id],
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 10, 10)),
            new StyleDiff(Bold: true));

        command.Apply(ctx).Success.Should().BeTrue();

        wb.StyleCount.Should().Be(2);
    }

    [Fact]
    public void Apply_UsesStyleDiffRegistrationCache()
    {
        var source = ModelSourceTestSupport.ReadCommandsSource("GroupedApplyStyleCommand.cs");
        var apply = source[
            source.IndexOf("public CommandOutcome Apply", StringComparison.Ordinal)..
            source.IndexOf("public void Revert", StringComparison.Ordinal)];

        source.Should().Contain("IEstimatesMemory");
        apply.Should().Contain("new Dictionary<StyleId, StyleId>()");
        apply.Should().Contain("StyleDiffStyleCache.GetOrRegister");
    }

    [Fact]
    public void Apply_RejectsProtectedGroupedSheetBeforeChangingAnySheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        var address1 = new CellAddress(sheet1.Id, 1, 1);
        var address2 = new CellAddress(sheet2.Id, 1, 1);
        sheet1.SetCell(address1, Cell.FromValue(new TextValue("old1")));
        sheet2.SetCell(address2, Cell.FromValue(new TextValue("old2")));
        var oldStyle1 = sheet1.GetCell(address1)!.StyleId;
        var oldStyle2 = sheet2.GetCell(address2)!.StyleId;
        sheet2.IsProtected = true;

        var command = new GroupedApplyStyleCommand(
            [sheet1.Id, sheet2.Id],
            new GridRange(address1, address1),
            new StyleDiff(Italic: true));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet1.GetCell(address1)!.StyleId.Should().Be(oldStyle1);
        sheet2.GetCell(address2)!.StyleId.Should().Be(oldStyle2);
    }

    [Fact]
    public void Apply_RejectsInvalidStyleChoicesBeforeChangingAnyGroupedSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        var address1 = new CellAddress(sheet1.Id, 1, 1);
        var address2 = new CellAddress(sheet2.Id, 1, 1);
        sheet1.SetCell(address1, Cell.FromValue(new TextValue("old1")));
        sheet2.SetCell(address2, Cell.FromValue(new TextValue("old2")));
        var oldStyle1 = sheet1.GetCell(address1)!.StyleId;
        var oldStyle2 = sheet2.GetCell(address2)!.StyleId;

        var command = new GroupedApplyStyleCommand(
            [sheet1.Id, sheet2.Id],
            new GridRange(address1, address1),
            new StyleDiff(TextRotation: 91));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet1.GetCell(address1)!.StyleId.Should().Be(oldStyle1);
        sheet2.GetCell(address2)!.StyleId.Should().Be(oldStyle2);
    }

    // freex-cell-styles-gallery F2: GroupedApplyStyleCommand's Pass 1 must clear a stale per-run
    // Bold=false override on every grouped sheet when the applied StyleDiff sets whole-cell
    // Bold=true, exactly as ApplyStyleCommand.Apply's Pass 1 does for a single (ungrouped) sheet --
    // otherwise the toolbar/gallery "make this bold" action silently leaves part of the cell text
    // non-bold on every sheet but the first one touched.
    [Fact]
    public void Apply_ClearsStaleRunBoldOverrideOnEveryGroupedSheetWhenWholeCellBoldIsApplied()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        var addr1 = new CellAddress(sheet1.Id, 1, 1);
        var addr2 = new CellAddress(sheet2.Id, 1, 1);

        sheet1.SetCell(addr1, Cell.FromValue(new TextValue("Hello")));
        sheet2.SetCell(addr2, Cell.FromValue(new TextValue("Hello")));
        var staleRuns = new List<CellTextRun>
        {
            new("He", Bold: false, Italic: null, Underline: null, Strikethrough: null,
                FontName: null, FontSize: null, FontColor: null),
            new("llo", Bold: null, Italic: null, Underline: null, Strikethrough: null,
                FontName: null, FontSize: null, FontColor: null),
        };
        sheet1.RichTextRuns[addr1] = staleRuns;
        sheet2.RichTextRuns[addr2] = staleRuns;

        var command = new GroupedApplyStyleCommand(
            [sheet1.Id, sheet2.Id],
            new GridRange(addr1, addr1),
            new StyleDiff(Bold: true));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet1.RichTextRuns[addr1][0].Bold.Should().BeNull(
            "the whole-cell Bold=true just applied must win over the stale per-run Bold=false override");
        sheet2.RichTextRuns[addr2][0].Bold.Should().BeNull(
            "the fix must apply to every grouped sheet, not just the first");
        wb.GetStyle(sheet1.GetCell(addr1)!.StyleId).Bold.Should().BeTrue();
        wb.GetStyle(sheet2.GetCell(addr2)!.StyleId).Bold.Should().BeTrue();

        command.Revert(ctx);

        sheet1.RichTextRuns[addr1][0].Bold.Should().BeFalse("undo must restore the original per-run override");
        sheet2.RichTextRuns[addr2][0].Bold.Should().BeFalse("undo must restore the original per-run override");
    }

    // Sibling no-regression: a StyleDiff that does NOT touch any rich-run font property (e.g. a
    // fill color change) must leave existing per-run overrides untouched on grouped sheets, just as
    // ApplyStyleCommand leaves them untouched on a single sheet.
    [Fact]
    public void Apply_LeavesRunOverridesUntouchedWhenDiffDoesNotAffectRichRunFontProperties()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        var addr1 = new CellAddress(sheet1.Id, 1, 1);
        var addr2 = new CellAddress(sheet2.Id, 1, 1);

        sheet1.SetCell(addr1, Cell.FromValue(new TextValue("Hello")));
        sheet2.SetCell(addr2, Cell.FromValue(new TextValue("Hello")));
        var runs = new List<CellTextRun>
        {
            new("He", Bold: false, Italic: null, Underline: null, Strikethrough: null,
                FontName: null, FontSize: null, FontColor: null),
        };
        sheet1.RichTextRuns[addr1] = runs;
        sheet2.RichTextRuns[addr2] = runs;

        var command = new GroupedApplyStyleCommand(
            [sheet1.Id, sheet2.Id],
            new GridRange(addr1, addr1),
            new StyleDiff(FillColor: new CellColor(0, 255, 0)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet1.RichTextRuns[addr1][0].Bold.Should().BeFalse("a fill-color-only change must not touch run font overrides");
        sheet2.RichTextRuns[addr2][0].Bold.Should().BeFalse("a fill-color-only change must not touch run font overrides");
    }
}
