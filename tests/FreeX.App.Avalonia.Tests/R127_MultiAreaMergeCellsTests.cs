using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R127-avalonia-mergepaste-multiarea-1: MainWindow.MergePaste.cs's MergeSelectedRangeAsync
/// ("Merge Cells") and MergeAcrossSelectedRangeAsync ("Merge Across") used to build their command
/// against only the single active <c>_session.SelectedRange</c>, silently ignoring every other
/// disjoint area of a Ctrl+click multi-area selection (<c>_session.SelectedRanges</c>) -- unlike
/// Excel, and unlike the WPF host's fix for the identical defect
/// (R127-homeformatting-multiarea-merge-1, MainWindow.HomeFormatting.cs).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R127_MultiAreaMergeCellsTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task MergeSelectedRangeAsync_MultiAreaSelection_MergesEveryDisjointArea()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MergeCellsMultiAreaFixture");
            window.Session.SelectSheet(sheet.Id);

            var areaB = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 3)); // B1:C1
            var areaE = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 6)); // E1:F1 -- active
            window.Session.SelectRanges(areaE, [areaB, areaE]);

            await window.MergeSelectedRangeForTestAsync();

            // Before the fix, only E1:F1 (the active area) was merged; B1:C1 was silently left
            // untouched.
            sheet.MergedRegions.Should().Contain(areaB, "B1:C1's disjoint area must also be merged");
            sheet.MergedRegions.Should().Contain(areaE, "E1:F1 (the active area) must be merged");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MergeAcrossSelectedRangeAsync_MultiAreaSelection_MergesEveryDisjointAreaPerRow()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MergeAcrossMultiAreaFixture");
            window.Session.SelectSheet(sheet.Id);

            var areaB = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 2, 3)); // B1:C2
            var areaE = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 2, 6)); // E1:F2 -- active
            window.Session.SelectRanges(areaE, [areaB, areaE]);

            await window.MergeAcrossSelectedRangeForTestAsync();

            // Merge Across merges each ROW of each area independently: B1:C1, B2:C2, E1:F1, E2:F2.
            sheet.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 3)), "row 1 of the disjoint B area must be merged");
            sheet.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 3)), "row 2 of the disjoint B area must be merged");
            sheet.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 6)), "row 1 of the active E area must be merged");
            sheet.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 2, 6)), "row 2 of the active E area must be merged");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // No-regression sibling: a plain single active-range Merge Cells (no Ctrl+click multi-area
    // selection) must keep merging exactly that one range.
    [Fact]
    public async Task MergeSelectedRangeAsync_SingleActiveRange_StillMergesOnlyThatRange()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MergeCellsSingleRangeFixture");
            window.Session.SelectSheet(sheet.Id);

            var range = new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 3, 3)); // B3:C3
            window.Session.SelectRange(range);

            await window.MergeSelectedRangeForTestAsync();

            sheet.MergedRegions.Should().ContainSingle();
            sheet.MergedRegions.Should().Contain(range);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

}
