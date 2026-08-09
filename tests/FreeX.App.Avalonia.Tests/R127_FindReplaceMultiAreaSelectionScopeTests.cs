using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R127-findreplace-selectionscope-multiarea-1
/// (src/FreeX.App.Avalonia/MainWindow.cs's CaptureFindReplaceSelectionScopeAtOpen).
///
/// Excel restricts Replace All / Find All to the pre-open selection whenever more than one cell was
/// selected before opening Find &amp; Replace -- INCLUDING a multi-area (Ctrl+click) selection, where the
/// scope is the UNION of every disjoint area, not just the last one clicked. Before this fix,
/// CaptureFindReplaceSelectionScopeAtOpen only ever read <c>_session.SelectedRange</c> (a single
/// GridRange), never <c>_session.SelectedRanges</c> (the app's actual multi-area representation, set via
/// <c>WorkbookSession.SelectRanges</c> for a Ctrl+click additional area). A user who selected B2:C4, then
/// Ctrl+clicked to also select E2:F4, had SelectedRange collapsed to just the newest area (E2:F4) while
/// SelectedRanges held the full union ([B2:C4, E2:F4]); Replace All silently dropped matches inside B2:C4
/// even though it stayed visibly selected.
///
/// The fix resolves the scope through SelectionStyleCommandPlanner.ResolveRanges (the same choke point
/// MainWindow.Outline.cs, MainWindow.MergePaste.cs, etc. already use for this exact
/// SelectedRange/SelectedRanges duality), which prefers _session.SelectedRanges when populated and falls
/// back to _session.SelectedRange otherwise.
///
/// These tests drive the REAL production entry point: <c>ShowFindDialogAsync</c> (the actual Ctrl+F
/// handler) via reflection, exactly as R119_FindReplaceStaleScopeTests does, then click the real
/// "Replace All" button in the dialog to exercise the full production path end to end.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R127_FindReplaceMultiAreaSelectionScopeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ReplaceAll_WithMultiAreaCtrlClickSelectionAtOpen_ReplacesInsideEveryArea()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet = window.Session.ActiveSheet;
                var sheetId = sheet.Id;
                var b3 = new CellAddress(sheetId, 3, 2); // inside B2:C4
                var e3 = new CellAddress(sheetId, 3, 5); // inside E2:F4
                var a100 = new CellAddress(sheetId, 100, 1); // outside both areas
                sheet.SetCell(b3, new TextValue("FY2024"));
                sheet.SetCell(e3, new TextValue("FY2024"));
                sheet.SetCell(a100, new TextValue("FY2024"));

                var areaOne = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 4, 3)); // B2:C4
                var areaTwo = new GridRange(new CellAddress(sheetId, 2, 5), new CellAddress(sheetId, 4, 6)); // E2:F4

                // Mirrors a Ctrl+click additional area: SelectRanges sets SelectedRange to the newest
                // (primary) area while SelectedRanges accumulates the full union.
                window.Session.SelectRanges(areaTwo, [areaOne, areaTwo]);

                await InvokePrivateTaskAsync(window, "ShowReplaceDialogAsync");
                var dialog = FindOwnedFindReplaceWindow(window);

                var replaceFindBox = dialog.GetVisualDescendants().OfType<TextBox>()
                    .Single(t => AutomationProperties.GetAutomationId(t) == "FindReplaceReplaceFindBox");
                replaceFindBox.Text = "2024";
                var replaceWithBox = dialog.GetVisualDescendants().OfType<TextBox>()
                    .Single(t => AutomationProperties.GetAutomationId(t) == "FindReplaceReplaceWithBox");
                replaceWithBox.Text = "2025";

                var replaceAllButton = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(b => AutomationProperties.GetAutomationId(b) == "FindReplaceReplaceAllButton");
                replaceAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, replaceAllButton));

                sheet.GetValue(b3.Row, b3.Col).Should().Be(
                    new TextValue("FY2025"), "B3 is inside the first Ctrl+click selected area");
                sheet.GetValue(e3.Row, e3.Col).Should().Be(
                    new TextValue("FY2025"),
                    "R127-findreplace-selectionscope-multiarea-1: E3 is inside the second (most-recently-clicked) " +
                    "area -- pre-fix, this was the ONLY area honored, so this assertion alone would already pass " +
                    "before the fix; B3 is the one that proves the regression");
                sheet.GetValue(a100.Row, a100.Col).Should().Be(
                    new TextValue("FY2024"),
                    "A100 is outside both selected areas and must be left untouched");

                dialog.Close();
            }
            finally
            {
                foreach (var owned in window.OwnedWindows.ToArray())
                {
                    if (owned.IsVisible)
                        owned.Close();
                }

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                if (window.IsVisible)
                    window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReplaceAll_WithSingleContiguousSelectionAtOpen_StillRestrictsToSelection()
    {
        // Sibling no-regression case: the ordinary single-area multi-cell scenario (SelectedRanges holds
        // just the one primary range) must keep working exactly as it did before this fix.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet = window.Session.ActiveSheet;
                var sheetId = sheet.Id;
                var b3 = new CellAddress(sheetId, 3, 2);
                var a100 = new CellAddress(sheetId, 100, 1);
                sheet.SetCell(b3, new TextValue("FY2024"));
                sheet.SetCell(a100, new TextValue("FY2024"));

                var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 5, 4)); // B2:D5
                window.Session.SelectRange(range);

                await InvokePrivateTaskAsync(window, "ShowReplaceDialogAsync");
                var dialog = FindOwnedFindReplaceWindow(window);

                var replaceFindBox = dialog.GetVisualDescendants().OfType<TextBox>()
                    .Single(t => AutomationProperties.GetAutomationId(t) == "FindReplaceReplaceFindBox");
                replaceFindBox.Text = "2024";
                var replaceWithBox = dialog.GetVisualDescendants().OfType<TextBox>()
                    .Single(t => AutomationProperties.GetAutomationId(t) == "FindReplaceReplaceWithBox");
                replaceWithBox.Text = "2025";

                var replaceAllButton = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(b => AutomationProperties.GetAutomationId(b) == "FindReplaceReplaceAllButton");
                replaceAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, replaceAllButton));

                sheet.GetValue(b3.Row, b3.Col).Should().Be(new TextValue("FY2025"), "B3 is inside the selection scope");
                sheet.GetValue(a100.Row, a100.Col).Should().Be(
                    new TextValue("FY2024"), "A100 is outside the selection scope and must be left untouched");

                dialog.Close();
            }
            finally
            {
                foreach (var owned in window.OwnedWindows.ToArray())
                {
                    if (owned.IsVisible)
                        owned.Close();
                }

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                if (window.IsVisible)
                    window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static Window FindOwnedFindReplaceWindow(MainWindow owner) =>
        owner.OwnedWindows.Single(window =>
            string.Equals(AutomationProperties.GetAutomationId(window), "FindReplaceDialog", StringComparison.Ordinal));

    private static Task InvokePrivateTaskAsync(MainWindow owner, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing production dialog opener {methodName}.");
        return method.Invoke(owner, null) as Task
            ?? throw new InvalidOperationException($"Production dialog opener {methodName} did not return Task.");
    }
}
