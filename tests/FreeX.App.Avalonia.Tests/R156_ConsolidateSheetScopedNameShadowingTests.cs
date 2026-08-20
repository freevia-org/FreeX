using System.Reflection;
using System.Threading;

using Avalonia.Headless;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for freex-defined-name-collisions F1: the Consolidate dialog (Avalonia shell)
/// must resolve a typed reference the same way the Name Box does -- a name scoped to the active
/// sheet shadows a same-named workbook-global name (Workbook.TryGetNamedRange(name, contextSheetId,
/// ...) precedence, per Excel's own scope rule). Before the fix, TryParseConsolidateReference called
/// the 4-argument WorkbookReferenceNavigator.TryParseReferenceRange overload (resolveScopedName:
/// null), which only ever consulted the workbook-global NamedRanges dictionary and silently ignored
/// any sheet-scoped override.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R156_ConsolidateSheetScopedNameShadowingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task TryParseConsolidateReference_SheetScopedNameShadowsSameNamedWorkbookGlobalName()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet1 = window.Session.Workbook.GetSheetAt(0);
                var sheet2 = window.Session.Workbook.AddSheet("Sheet2");

                // Workbook-global 'Data' = Sheet1!A1:A5.
                var globalRange = new GridRange(
                    new CellAddress(sheet1.Id, 1, 1),
                    new CellAddress(sheet1.Id, 5, 1));
                window.Session.Workbook.DefineNamedRange("Data", globalRange);

                // Sheet2-scoped 'Data' = Sheet2!B1:B10, which must shadow the global name while
                // the active sheet is Sheet2.
                var scopedRange = new GridRange(
                    new CellAddress(sheet2.Id, 1, 2),
                    new CellAddress(sheet2.Id, 10, 2));
                window.Session.Workbook.DefineNamedRange("Data", scopedRange, metadata: null, sheet2.Id);

                window.Session.SelectSheet(sheet2.Id);

                var args = new object?[] { "Data", null };
                var parsed = (bool)InvokePrivate(window, "TryParseConsolidateReference", args)!;
                var resolved = (GridRange)args[1]!;

                parsed.Should().BeTrue();
                resolved.Should().Be(
                    scopedRange,
                    "the sheet-scoped 'Data' on the active sheet (Sheet2) must shadow the same-named workbook-global 'Data', matching the Name Box's resolution and Excel's own scope precedence");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                if (window.IsVisible)
                    window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sibling no-regression case: with no sheet-scoped override present, the plain workbook-global
    /// name must still resolve exactly as before -- the fix must not break the common, unshadowed
    /// path.
    /// </summary>
    [Fact]
    public async Task TryParseConsolidateReference_NoScopedOverride_StillResolvesTheWorkbookGlobalName()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet1 = window.Session.Workbook.GetSheetAt(0);
                var sheet2 = window.Session.Workbook.AddSheet("Sheet2");

                var globalRange = new GridRange(
                    new CellAddress(sheet1.Id, 1, 1),
                    new CellAddress(sheet1.Id, 5, 1));
                window.Session.Workbook.DefineNamedRange("Data", globalRange);

                window.Session.SelectSheet(sheet2.Id);

                var args = new object?[] { "Data", null };
                var parsed = (bool)InvokePrivate(window, "TryParseConsolidateReference", args)!;
                var resolved = (GridRange)args[1]!;

                parsed.Should().BeTrue();
                resolved.Should().Be(globalRange, "with no sheet-scoped override, the workbook-global name must resolve unchanged");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                if (window.IsVisible)
                    window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static object? InvokePrivate(MainWindow owner, string methodName, object?[] args)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing production method {methodName}.");
        return method.Invoke(owner, args);
    }
}
