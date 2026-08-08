using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R128-avalonia-cf-multiarea-1: the Avalonia shell's conditional-formatting entry points
/// (ApplyConditionalFormatPreset, ApplyConditionalFormatIconSet, ApplyHighlightGreaterThanPresetAsync,
/// ClearConditionalFormatsFromSelection, ShowConditionalFormatNewRuleDialogAsync --
/// MainWindow.ConditionalFormat.cs) used to read only <c>_session.SelectedRange</c> (the single
/// active area) and build/execute exactly one command against it, ignoring
/// <c>_session.SelectedRanges</c> entirely. A Ctrl+click multi-area selection therefore only got a
/// conditional format applied to (or cleared from) its active/last-clicked area, silently leaving
/// every other disjoint area untouched -- unlike Excel, and unlike the WPF host's
/// ApplyConditionalFormatPreset(ConditionalFormat rule) (MainWindow.HomeFormatting.cs), which
/// already routes through GetCurrentSelectionRanges + SelectionStyleCommandPlanner.CreateRangeCommand.
/// The fix adds the same choke point to the Avalonia shell
/// (BuildMultiAreaConditionalFormatCommand / ResolveConditionalFormatSelectionRanges) and routes
/// every one of those entry points through it.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R128_ConditionalFormatMultiAreaApplyTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task ApplyConditionalFormatPreset_MultiAreaSelection_AppliesToEveryDisjointArea() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CfMultiAreaPreset");
            window.Session.SelectSheet(sheet.Id);

            // Two disjoint areas, mirroring a Ctrl+click multi-area selection: SelectedRange is the
            // active (last-clicked) area, SelectedRanges holds both (exactly what real Ctrl+click
            // selection produces via WorkbookSession.SelectRanges).
            var areaA = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
            var areaB = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 7, 7));
            window.Session.SelectRanges(areaB, [areaA, areaB]);

            InvokePrivate(window, "ApplyConditionalFormatPreset", [typeof(ConditionalFormatPreset)], [ConditionalFormatPreset.DataBar]);

            // Before the fix, only areaB (the active area) got a rule; areaA was silently left with
            // no conditional format at all.
            sheet.ConditionalFormats.Should().HaveCount(2, "each disjoint area must get its own rule, mirroring the WPF host");
            sheet.ConditionalFormats.Should().Contain(cf => cf.AppliesTo.Equals(areaA) && cf.RuleType == CfRuleType.DataBar);
            sheet.ConditionalFormats.Should().Contain(cf => cf.AppliesTo.Equals(areaB) && cf.RuleType == CfRuleType.DataBar);
            // Each area's rule is independent (distinct ids), matching Excel creating separate rules.
            sheet.ConditionalFormats.Select(cf => cf.Id).Distinct().Should().HaveCount(2);

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task ClearConditionalFormatsFromSelection_MultiAreaSelection_ClearsEveryDisjointArea() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CfMultiAreaClear");
            window.Session.SelectSheet(sheet.Id);

            var areaA = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
            var areaB = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 7, 7));

            var ruleA = ConditionalFormatPresetFactory.BuildRule(ConditionalFormatPreset.DataBar, areaA);
            var ruleB = ConditionalFormatPresetFactory.BuildRule(ConditionalFormatPreset.DataBar, areaB);
            window.Session.ExecuteReviewCommand(new ApplyConditionalFormatCommand(sheet.Id, ruleA)).Success.Should().BeTrue();
            window.Session.ExecuteReviewCommand(new ApplyConditionalFormatCommand(sheet.Id, ruleB)).Success.Should().BeTrue();
            sheet.ConditionalFormats.Should().HaveCount(2);

            window.Session.SelectRanges(areaB, [areaA, areaB]);

            InvokePrivate(window, "ClearConditionalFormatsFromSelection", [], []);

            // Before the fix, only areaB's rule was cleared; areaA's rule silently survived.
            sheet.ConditionalFormats.Should().BeEmpty("clearing a multi-area selection must clear every disjoint area's rules");

            window.Close();
        }, CancellationToken.None);

    // No-regression sibling: a plain single active-range preset apply (no multi-area selection
    // involved) must keep applying exactly one rule over that one range, unaffected by routing the
    // command construction through the multi-area-aware plumbing.
    [Fact]
    public Task ApplyConditionalFormatPreset_SingleActiveRange_StillAppliesOnlyThatRange_NoRegression() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CfSingleRangePreset");
            window.Session.SelectSheet(sheet.Id);

            var range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 4));
            window.Session.SelectRange(range);

            InvokePrivate(window, "ApplyConditionalFormatPreset", [typeof(ConditionalFormatPreset)], [ConditionalFormatPreset.DataBar]);

            sheet.ConditionalFormats.Should().ContainSingle();
            sheet.ConditionalFormats[0].AppliesTo.Should().Be(range);
            sheet.ConditionalFormats[0].RuleType.Should().Be(CfRuleType.DataBar);

            window.Close();
        }, CancellationToken.None);

    private static void InvokePrivate(MainWindow window, string methodName, System.Type[] paramTypes, object?[] args)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance, null, paramTypes, null)
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, args);
    }
}
