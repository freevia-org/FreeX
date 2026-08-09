using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FreeX.App.Presentation.Import;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R88-io-text-import-wizard-5-1: Data ▸ Refresh All re-imported at the CURRENT selection instead of the
/// original import anchor. <c>TryImportFromText</c> unconditionally computed
/// <c>destination = _session.SelectedRange.Start</c> even when called from
/// <c>RefreshImportedData</c>, so moving the selection after the first import and then refreshing wrote
/// the refreshed data wherever the cursor happened to be -- clobbering whatever the user had since typed
/// there -- while the original block was left stale. Fixed by remembering the exact anchor
/// (<see cref="ImportDataSource.Anchor"/>) the first import wrote to and re-targeting it on refresh.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R88_GetDataRefreshAnchorTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task RefreshImportedData_AfterSelectionMoves_ReimportsAtOriginalAnchorNotCurrentSelection() =>
        Session.Dispatch(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"freex-r88-getdata-refresh-anchor-{Guid.NewGuid():N}.csv");
            File.WriteAllText(path, "11,22\r\n33,44\r\n");
            try
            {
                var window = new MainWindow([]);
                var sheet = window.Session.Workbook.AddSheet("ImportSheet");
                window.Session.SelectSheet(sheet.Id);

                // Import lands at B2 (the active cell at import time) because Destination is
                // CurrentSheet.
                window.Session.SelectCell(new CellAddress(sheet.Id, 2, 2));
                var options = new ImportDataOptions
                {
                    Delimiter = ImportDelimiterKind.Comma,
                    Destination = ImportDestinationKind.CurrentSheet,
                };

                InvokeTryImportFromText(window, path, "11,22\r\n33,44\r\n", options).Should().BeTrue();
                sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(11));
                sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new NumberValue(22));

                // The user now clicks A20 and types an unrelated formula -- this must survive Refresh.
                window.Session.SelectCell(new CellAddress(sheet.Id, 20, 1));
                window.Session.CommitCellText("=1+1").Success.Should().BeTrue();
                sheet.GetValue(new CellAddress(sheet.Id, 20, 1)).Should().Be(new NumberValue(2));

                // The source file changes (as a real refreshed data source would) before Refresh All.
                File.WriteAllText(path, "99,88\r\n77,66\r\n");

                InvokePrivateVoid(window, "RefreshImportedData");

                // Refreshed data must land back at the original anchor (B2:C3), updated in place...
                sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(99),
                    "Refresh All must re-target the original import anchor, not the current selection");
                sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new NumberValue(88));
                sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new NumberValue(77));
                sheet.GetValue(new CellAddress(sheet.Id, 3, 3)).Should().Be(new NumberValue(66));

                // ...and A20's unrelated formula must be untouched, not clobbered by the refresh.
                sheet.GetValue(new CellAddress(sheet.Id, 20, 1)).Should().Be(new NumberValue(2),
                    "Refresh All must not overwrite whatever the user has since typed at the current selection");

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            finally
            {
                File.Delete(path);
            }
        }, CancellationToken.None);

    // No-regression sibling: a FIRST import (no prior remembered source) must still land at the current
    // selection exactly as before -- the anchor-override path only applies to a Refresh, never to the
    // initial Load.
    [Fact]
    public Task TryImportFromText_FirstImport_StillLandsAtCurrentSelection_NoRegression() =>
        Session.Dispatch(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"freex-r88-getdata-first-import-{Guid.NewGuid():N}.csv");
            File.WriteAllText(path, "5,6\r\n");
            try
            {
                var window = new MainWindow([]);
                var sheet = window.Session.Workbook.AddSheet("FirstImportSheet");
                window.Session.SelectSheet(sheet.Id);
                window.Session.SelectCell(new CellAddress(sheet.Id, 4, 3));

                var options = new ImportDataOptions
                {
                    Delimiter = ImportDelimiterKind.Comma,
                    Destination = ImportDestinationKind.CurrentSheet,
                };

                InvokeTryImportFromText(window, path, "5,6\r\n", options).Should().BeTrue();

                sheet.GetValue(new CellAddress(sheet.Id, 4, 3)).Should().Be(new NumberValue(5));
                sheet.GetValue(new CellAddress(sheet.Id, 4, 4)).Should().Be(new NumberValue(6));

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            finally
            {
                File.Delete(path);
            }
        }, CancellationToken.None);

    private static bool InvokeTryImportFromText(
        MainWindow window,
        string filePath,
        string decodedText,
        ImportDataOptions options)
    {
        var method = typeof(MainWindow).GetMethod(
            "TryImportFromText", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(MainWindow), "TryImportFromText");
        var args = new object?[] { filePath, decodedText, options, null, null };
        var result = (bool)method.Invoke(window, args)!;
        return result;
    }

    private static void InvokePrivateVoid(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, null);
    }
}
