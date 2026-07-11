using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R24-rich-text-inline-3: whole-cell font-formatting commands (Bold/Italic/Font Color/Font
/// Name/Size), applied via <see cref="ApplyStyleCommand"/>, only ever mutated <c>cell.StyleId</c>
/// and never touched <see cref="Sheet.RichTextRuns"/>. That let a per-run override (e.g. a run
/// carrying an explicit red <c>FontColor</c> from a paste/import) permanently mask a subsequent
/// whole-cell "Font Color: Blue" ribbon command for that run, even though the rest of the cell's
/// text correctly turned blue. Real Excel's whole-cell direct-formatting commands (no partial-text/
/// edit-mode selection) override the entire cell's rendered attribute uniformly, clearing
/// per-character overrides for that property.
/// </summary>
public sealed class R24_ApplyStyleRichTextOverrideTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx, CellAddress Address) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);

        var cell = Cell.FromValue(new TextValue("Hello World"));
        sheet.SetCell(addr, cell);

        // "Hello " has no per-run overrides (inherits the cell style); " World" carries an
        // explicit run-level Bold + red FontColor override, as would come from a paste/import.
        sheet.RichTextRuns[addr] = new List<CellTextRun>
        {
            new("Hello ", Bold: null, Italic: null, Underline: null, Strikethrough: null,
                FontName: null, FontSize: null, FontColor: null),
            new("World", Bold: true, Italic: null, Underline: null, Strikethrough: null,
                FontName: null, FontSize: null, FontColor: CellRunColor.FromRgb(new CellColor(255, 0, 0))),
        };

        return (wb, sheet, ctx, addr);
    }

    [Fact]
    public void ApplyingWholeCellFontColor_ClearsStaleRunColorOverride_SoTheNewColorRendersUniformly()
    {
        var (wb, sheet, ctx, addr) = Setup();
        var blue = new CellColor(0, 0, 255);

        var command = new ApplyStyleCommand(sheet.Id, new GridRange(addr, addr), new StyleDiff(FontColor: blue));
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();

        var newStyle = wb.GetStyle(sheet.GetCell(addr)!.StyleId);
        newStyle.FontColor.Should().Be(blue);

        var runs = sheet.RichTextRuns[addr];
        runs.Should().HaveCount(2);
        runs[0].FontColor.Should().BeNull();
        // Before the fix, the second run's stale red FontColor override survived here and kept
        // masking the new uniform Blue at render time (CellRichRunLayoutPlanner only falls back to
        // the cell style when the run's FontColor is null).
        runs[1].FontColor.Should().BeNull("the whole-cell Font Color command must clear the stale per-run red override");

        // Bold was not part of this diff, so the unrelated per-run Bold override on "World" must
        // be left untouched.
        runs[1].Bold.Should().BeTrue();
    }

    [Fact]
    public void ApplyingWholeCellBold_ClearsStaleRunBoldOverride()
    {
        var (wb, sheet, ctx, addr) = Setup();

        var command = new ApplyStyleCommand(sheet.Id, new GridRange(addr, addr), new StyleDiff(Bold: false));
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();

        var runs = sheet.RichTextRuns[addr];
        runs[1].Bold.Should().BeNull("the whole-cell Bold command must clear the stale per-run Bold override, not leave it stuck true");

        // The unrelated red FontColor override on "World" must survive since this diff never
        // touched font color.
        runs[1].FontColor.Should().Be(CellRunColor.FromRgb(new CellColor(255, 0, 0)));
    }

    [Fact]
    public void ApplyingUnrelatedStyleProperty_LeavesRichTextRunOverridesUntouched()
    {
        var (wb, sheet, ctx, addr) = Setup();

        // A fill-color change has no per-run analogue on CellTextRun; it must not disturb any
        // existing rich-text run overrides.
        var command = new ApplyStyleCommand(sheet.Id, new GridRange(addr, addr), new StyleDiff(FillColor: new CellColor(0, 255, 0)));
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();

        var runs = sheet.RichTextRuns[addr];
        runs[1].Bold.Should().BeTrue();
        runs[1].FontColor.Should().Be(CellRunColor.FromRgb(new CellColor(255, 0, 0)));
    }

    [Fact]
    public void RevertingWholeCellFontColor_RestoresOriginalRunOverrides()
    {
        var (wb, sheet, ctx, addr) = Setup();
        var originalRuns = sheet.RichTextRuns[addr];
        var blue = new CellColor(0, 0, 255);

        var command = new ApplyStyleCommand(sheet.Id, new GridRange(addr, addr), new StyleDiff(FontColor: blue));
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.RichTextRuns[addr][1].FontColor.Should().BeNull();

        command.Revert(ctx);

        var restoredRuns = sheet.RichTextRuns[addr];
        restoredRuns.Should().HaveCount(originalRuns.Count);
        restoredRuns[1].FontColor.Should().Be(CellRunColor.FromRgb(new CellColor(255, 0, 0)));
        restoredRuns[1].Bold.Should().BeTrue();
    }
}
