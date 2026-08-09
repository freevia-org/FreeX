using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression tests for R74-commands-name-manager-4-2 (src/FreeX.App.Avalonia/MainWindow.cs,
/// TryDefineNameFromCellAddressBox): typing an EXISTING named-formula/constant's name into the Name
/// Box and pressing Enter silently redefined it as a workbook-scoped named RANGE over the current
/// selection -- <c>TryParseCellAddressBoxReferenceRange</c> only ever recognizes
/// NamedRanges/ScopedNamedRanges as navigable, so an existing formula-name (which has no GridRange
/// to navigate to) fell through to the same "define a brand-new name" path a truly-new name would
/// take, silently gaining a colliding NamedRanges entry that wins over the stale NamedFormulas one
/// at evaluation time. Mirrors the WPF host's equivalent fix in MainWindow.Editing.cs.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R74_NameBoxFormulaCollisionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Enter_WithExistingNamedFormulaName_DoesNotClobberItWithARange()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.Workbook.NamedFormulas["TaxRate"] = "0.08";

            window.CellAddressBoxTextForTest = "TaxRate";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.Workbook.NamedFormulas.Should().ContainKey("TaxRate");
            window.Session.Workbook.NamedFormulas["TaxRate"].Should().Be("0.08",
                "typing an existing named formula/constant's name in the Name Box must never silently redefine it");
            window.Session.Workbook.NamedRanges.Should().NotContainKey("TaxRate",
                "the Name Box must not add a colliding NamedRanges entry for a name that already exists as a NamedFormula");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Enter_WithExistingSheetScopedNamedFormulaName_DoesNotClobberItWithARange()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.Workbook.DefineNamedFormula("LocalRate", "0.05", sheet.Id);

            window.CellAddressBoxTextForTest = "LocalRate";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.Workbook.ScopedNamedFormulas.Should().ContainKey(("LocalRate", sheet.Id));
            window.Session.Workbook.ScopedNamedFormulas[("LocalRate", sheet.Id)].Should().Be("0.05");
            window.Session.Workbook.NamedRanges.Should().NotContainKey("LocalRate");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // Sibling no-regression: the ordinary "type a brand new name to define it" workflow must still work.
    [Fact]
    public async Task Enter_WithBrandNewUniqueName_StillDefinesNamedRange()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var selection = new GridRange(new CellAddress(sheet.Id, 3, 3), new CellAddress(sheet.Id, 4, 4));
            window.Session.SelectRange(selection);

            window.CellAddressBoxTextForTest = "BrandNewFormulaFreeName";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.Workbook.TryGetNamedRange("BrandNewFormulaFreeName", out var defined).Should().BeTrue();
            defined.Should().Be(selection);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // Sibling no-regression: typing an existing named RANGE's name must still navigate to it.
    [Fact]
    public async Task Enter_WithExistingNamedRangeName_StillNavigates()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var namedRange = new GridRange(new CellAddress(sheet.Id, 7, 2), new CellAddress(sheet.Id, 7, 2));
            window.Session.Workbook.DefineNamedRange("Total", namedRange);

            window.CellAddressBoxTextForTest = "Total";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.ActiveCell.Should().Be(namedRange.Start);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }
}
