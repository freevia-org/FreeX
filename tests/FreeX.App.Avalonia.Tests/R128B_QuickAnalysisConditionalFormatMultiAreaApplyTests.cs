using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R128B (ScopeAudit on the r128 avalonia-cf-multiarea-1 fix): the fix routed the five
/// MainWindow.ConditionalFormat.cs entry points through BuildMultiAreaConditionalFormatCommand /
/// ResolveConditionalFormatSelectionRanges, but missed a sixth site --
/// ShowQuickAnalysisConditionalFormatDialogAsync in MainWindow.QuickAnalysis.cs (reached from the
/// Quick Analysis flyout's Conditional Formatting tab), which still built its command with
/// <c>ConditionalFormatRuleBuilder.ToApplyCommand(_session.ActiveSheet.Id, built)</c> -- applying the
/// rule built in the dialog to only the single active area of a Ctrl+click multi-area selection,
/// exactly the defect the r128 fix closed everywhere else. The fix routes that site through the same
/// shared range resolver and conditional-format command planner. WPF's Quick Analysis
/// conditional-format action (MainWindow.QuickAnalysis.cs
/// -> ShowCfDialog -> the same ribbon ShowCfDialog handler in MainWindow.HomeFormatting.cs, which
/// already re-resolves GetCurrentSelectionRanges at apply time) was checked and is NOT affected --
/// this is an Avalonia-only remaining gap.
///
/// Note: each lambda below ends with <c>return true;</c> so it binds to
/// HeadlessUnitTestSession.Dispatch's <c>Func&lt;Task&lt;TResult&gt;&gt;</c> overload (matching the
/// established pattern in R127_MultiAreaMergeCellsTests.cs) rather than the no-return-value
/// <c>Action</c> overload -- a void async lambda passed to the <c>Action</c> overload runs as
/// async-void, and its exceptions (including FluentAssertions failures) never propagate back to the
/// awaited Task, so the test would silently "pass" no matter what the assertions say.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R128B_QuickAnalysisConditionalFormatMultiAreaApplyTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ShowQuickAnalysisConditionalFormatDialog_MultiAreaSelection_AppliesToEveryDisjointArea()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            var sheet = window.Session.Workbook.AddSheet("QaCfMultiArea");
            window.Session.SelectSheet(sheet.Id);

            // Two disjoint areas, mirroring a Ctrl+click multi-area selection: SelectedRange is the
            // active (last-clicked) area, SelectedRanges holds both -- exactly what real Ctrl+click
            // selection produces via WorkbookSession.SelectRanges. Both need numeric content so Quick
            // Analysis's non-empty-selection gate (QuickAnalysisShellRequestPlanner.Build) considers
            // the selection eligible and offers the format.databars item.
            var areaA = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
            var areaB = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 7, 7));
            SeedNumericContent(sheet, areaA);
            SeedNumericContent(sheet, areaB);
            window.Session.SelectRanges(areaB, [areaA, areaB]);

            // Drives the real production path: ApplyQuickAnalysisItemAsync ->
            // ShowQuickAnalysisConditionalFormatDialogAsync -> ShowConditionalFormatRuleEditorAsync ->
            // ResolveConditionalFormatSelectionRanges -> ConditionalFormatCommandPlanner.PlanApplyRule ->
            // RunConditionalFormatCommand.
            await window.ApplyQuickAnalysisConditionalFormatItemForTestAsync(
                "format.databars", ConditionalFormatPreset.DataBar);

            // Before the fix, only areaB (the active area) got a rule; areaA was silently left with
            // no conditional format at all -- the same defect the r128 fix closed for the ribbon/menu
            // entry points.
            sheet.ConditionalFormats.Should().HaveCount(2, "each disjoint area must get its own rule, mirroring the already-fixed ribbon entry points");
            sheet.ConditionalFormats.Should().Contain(cf => cf.AppliesTo.Equals(areaA) && cf.RuleType == CfRuleType.DataBar);
            sheet.ConditionalFormats.Should().Contain(cf => cf.AppliesTo.Equals(areaB) && cf.RuleType == CfRuleType.DataBar);
            // Each area's rule is independent (distinct ids), matching Excel creating separate rules.
            sheet.ConditionalFormats.Select(cf => cf.Id).Distinct().Should().HaveCount(2);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // No-regression sibling: a plain single active-range Quick Analysis dialog apply (no multi-area
    // selection involved) must keep applying exactly one rule over that one range, unaffected by
    // routing the command construction through the multi-area-aware plumbing.
    [Fact]
    public async Task ShowQuickAnalysisConditionalFormatDialog_SingleActiveRange_StillAppliesOnlyThatRange_NoRegression()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            var sheet = window.Session.Workbook.AddSheet("QaCfSingleRange");
            window.Session.SelectSheet(sheet.Id);

            var range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 4));
            SeedNumericContent(sheet, range);
            window.Session.SelectRange(range);

            await window.ApplyQuickAnalysisConditionalFormatItemForTestAsync(
                "format.databars", ConditionalFormatPreset.DataBar);

            sheet.ConditionalFormats.Should().ContainSingle();
            sheet.ConditionalFormats[0].AppliesTo.Should().Be(range);
            sheet.ConditionalFormats[0].RuleType.Should().Be(CfRuleType.DataBar);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    /// <summary>Fills every cell in <paramref name="range"/> with a distinct number so Quick Analysis's
    /// non-empty-selection gate treats the range as eligible for the format.databars item.</summary>
    private static void SeedNumericContent(Sheet sheet, GridRange range)
    {
        double value = 1;
        for (var row = range.Start.Row; row <= range.End.Row; row++)
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value++));
    }
}
