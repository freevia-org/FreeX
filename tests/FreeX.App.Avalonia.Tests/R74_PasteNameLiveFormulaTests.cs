using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;

using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression tests for R74-commands-name-manager-4-3 (src/FreeX.App.Avalonia/MainWindow.PasteNames.cs,
/// ApplyPasteNameReference): Formulas ▸ Use in Formula ▸ Paste Names wrote the selected defined
/// name's current RefersTo address into the target cell as static TEXT
/// (<c>Cell.FromValue(new TextValue(item.RefersTo))</c>) rather than a live "=Name" formula, so the
/// pasted cell never re-evaluated when the name's target was later redefined -- Excel's Paste Name
/// inserts a live reference. The fix writes <c>Cell.FromFormula(item.Name)</c> instead.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R74_PasteNameLiveFormulaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ApplyPasteNameReference_WritesALiveNameFormula_NotStaticText()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var namedRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));
            window.Session.Workbook.DefineNamedRange("Revenue", namedRange);

            var targetAddress = new CellAddress(sheet.Id, 20, 5);
            window.Session.SelectCell(targetAddress);

            var applied = (bool)InvokeApplyPasteNameReference(window, new PasteNamesItem("Revenue", "Sheet1!A1:A10"))!;

            applied.Should().BeTrue();
            var cell = sheet.GetCell(targetAddress);
            cell.Should().NotBeNull();
            cell!.FormulaText.Should().Be("Revenue",
                "Paste Names must insert a live '=Name' formula so the cell re-evaluates as the name changes, not the static RefersTo text");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // Sibling no-regression: the pasted formula must actually evaluate to the named range's live
    // value (not merely exist as inert formula text).
    [Fact]
    public async Task ApplyPasteNameReference_PastedFormulaEvaluatesTheNamedRangesCurrentValue()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var sourceCell = new CellAddress(sheet.Id, 1, 1);
            window.Session.Workbook.DefineNamedRange("Revenue", new GridRange(sourceCell, sourceCell));
            sheet.SetCell(sourceCell, Cell.FromValue(new NumberValue(42)));

            var targetAddress = new CellAddress(sheet.Id, 20, 5);
            window.Session.SelectCell(targetAddress);

            InvokeApplyPasteNameReference(window, new PasteNamesItem("Revenue", "Sheet1!A1"));
            window.Session.RecalculateWorkbook();

            sheet.GetCell(targetAddress)!.Value.Should().Be(new NumberValue(42),
                "the pasted '=Name' formula must evaluate live off the name's current target, exactly like any other formula reference to it");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    private static object? InvokeApplyPasteNameReference(MainWindow window, PasteNamesItem item) =>
        typeof(MainWindow)
            .GetMethod("ApplyPasteNameReference", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [item]);
}
