using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaClickedCellContextActionTests
{
    private static readonly HeadlessUnitTestSession Headless =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(false, "ResolveComment", true)]
    [InlineData(true, "UnresolveComment", false)]
    public Task ThreadResolution_ChangesOnlyTheClickedThreadAndPreservesSelection(
        bool initiallyResolved, string action, bool expectedResolved) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var active = Address(sheet, 1, 1);
        var clicked = Address(sheet, 2, 2);
        sheet.ThreadedComments[active] = new ThreadedComment("Active thread") { IsResolved = initiallyResolved };
        var thread = new ThreadedComment("Clicked thread")
        {
            IsResolved = initiallyResolved,
            Replies = [new CommentReply("Retained reply")]
        };
        sheet.ThreadedComments[clicked] = thread;
        var selection = SelectContextRange(window, active);

        await ExecuteContextAction(window, action, clicked);

        sheet.ThreadedComments[clicked].IsResolved.Should().Be(expectedResolved);
        sheet.ThreadedComments[clicked].Replies.Should().Equal(thread.Replies);
        sheet.ThreadedComments[active].IsResolved.Should().Be(initiallyResolved);
        AssertSelection(window, selection);
        window.Session.UndoLastEdit().Success.Should().BeTrue();
        sheet.ThreadedComments[clicked].IsResolved.Should().Be(initiallyResolved);
        window.Session.RedoLastEdit().Success.Should().BeTrue();
        sheet.ThreadedComments[clicked].IsResolved.Should().Be(expectedResolved);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ShowHideNote_TogglesOnlyTheClickedNoteAndPreservesSelection(bool initiallyShown) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var active = Address(sheet, 1, 1);
        var clicked = Address(sheet, 2, 2);
        sheet.Comments[active] = "Active note";
        sheet.Comments[clicked] = "Clicked note";
        sheet.ShownComments.Add(active);
        if (initiallyShown)
            sheet.ShownComments.Add(clicked);
        var selection = SelectContextRange(window, active);

        await ExecuteContextAction(window, "ShowHideNote", clicked);

        sheet.ShownComments.Contains(clicked).Should().Be(!initiallyShown);
        sheet.ShownComments.Should().Contain(active);
        AssertSelection(window, selection);
        window.Session.UndoLastEdit().Success.Should().BeTrue();
        sheet.ShownComments.Contains(clicked).Should().Be(initiallyShown);
        window.Session.RedoLastEdit().Success.Should().BeTrue();
        sheet.ShownComments.Contains(clicked).Should().Be(!initiallyShown);
    });

    [Theory]
    [InlineData("ResolveComment")]
    [InlineData("ShowHideNote")]
    public Task RepeatLast_AppliesClickedReviewActionToTheNewSelection(string action) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var originalActive = Address(sheet, 1, 1);
        var clicked = Address(sheet, 2, 2);
        var next = Address(sheet, 4, 4);
        foreach (var address in new[] { originalActive, clicked, next })
        {
            sheet.ThreadedComments[address] = new ThreadedComment("Thread");
            sheet.Comments[address] = "Note";
        }

        bool Changed(CellAddress address) => action == "ResolveComment"
            ? sheet.ThreadedComments[address].IsResolved
            : sheet.ShownComments.Contains(address);
        var selection = SelectContextRange(window, originalActive);
        await ExecuteContextAction(window, action, clicked);
        Changed(clicked).Should().BeTrue();
        Changed(originalActive).Should().BeFalse();
        Changed(next).Should().BeFalse();
        AssertSelection(window, selection);

        window.Session.SelectCell(next);
        window.SheetGridHostForTest.Focus();
        var repeat = new KeyEventArgs { Key = Key.F4, PhysicalKey = PhysicalKey.F4 };
        await window.RaiseKeyDownForTest(repeat);

        repeat.Handled.Should().BeTrue();
        Changed(next).Should().BeTrue("F4 must resolve the current selection instead of reusing the context-menu target");
        Changed(clicked).Should().BeTrue();
        Changed(originalActive).Should().BeFalse();
        window.Session.ActiveCell.Should().Be(next);
        window.Session.UndoLastEdit().Success.Should().BeTrue();
        Changed(next).Should().BeFalse();
        Changed(clicked).Should().BeTrue("undoing the repeated edit must keep the original clicked-cell edit");
        window.Session.RedoLastEdit().Success.Should().BeTrue();
        Changed(next).Should().BeTrue();
        Changed(clicked).Should().BeTrue();
        Changed(originalActive).Should().BeFalse();
    });

    [Theory]
    [InlineData("ResolveComment")]
    [InlineData("ShowHideNote")]
    public Task ClickedReviewAction_StillHonorsEditObjectsProtection(string action) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var clicked = Address(sheet, 2, 2);
        sheet.ThreadedComments[clicked] = new ThreadedComment("Protected thread");
        sheet.Comments[clicked] = "Protected note";
        var selection = SelectContextRange(window, Address(sheet, 1, 1));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Remove(SheetProtectionPermission.EditObjects);

        await ExecuteContextAction(window, action, clicked);

        sheet.ThreadedComments[clicked].IsResolved.Should().BeFalse();
        sheet.ShownComments.Should().NotContain(clicked);
        window.Session.CanUndo.Should().BeFalse();
        window.Session.IsDirty.Should().BeFalse();
        AssertSelection(window, selection);
    });

    [Fact]
    public Task OpenHyperlink_FollowsTheClickedCellsWorkbookDestination() => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var active = Address(sheet, 1, 1);
        var clicked = Address(sheet, 2, 2);
        // Both links remain inside this workbook; this test never launches an external application.
        sheet.Hyperlinks[active] = "E20";
        sheet.HyperlinkMetadata[active] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);
        sheet.Hyperlinks[clicked] = "F30";
        sheet.HyperlinkMetadata[clicked] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);
        SelectContextRange(window, active);

        await ExecuteContextAction(window, "OpenHyperlink", clicked);

        window.Session.ActiveCell.Should().Be(Address(sheet, 30, 6));
        window.Session.SelectedRange.Should().Be(new GridRange(Address(sheet, 30, 6), Address(sheet, 30, 6)));
        window.Session.CanUndo.Should().BeFalse();
        window.Session.IsDirty.Should().BeFalse();
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task PivotOptions_OpensForClickedPivotAndPreservesSelectionOnAcceptOrCancel(bool accept) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var first = AddPivot(sheet, "ActivePivot", 1, 1, showRowGrandTotals: true);
        var clicked = AddPivot(sheet, "ClickedPivot", 1, 4, showRowGrandTotals: false);
        var selection = SelectContextRange(window, Address(sheet, 2, 1));

        await ExecuteContextAction(window, "PivotTableOptions", Address(sheet, 2, 4));

        var dialog = window.OwnedWindows.Should().ContainSingle().Which;
        AutomationProperties.GetAutomationId(dialog).Should().Be("PivotTableOptionsDialog");
        var tabs = Find<TabControl>(dialog, "PivotTableOptionsTabs");
        tabs.SelectedIndex = 1;
        dialog.UpdateLayout();
        var rowGrandTotals = Find<CheckBox>(dialog, "PivotOptionsRowGrandTotalsBox");
        rowGrandTotals.IsChecked.Should().BeFalse("the clicked pivot has different options from the active pivot");
        rowGrandTotals.IsChecked = true;
        AssertSelection(window, selection);

        Find<Button>(dialog, accept ? "PivotTableOptionsOkButton" : "PivotTableOptionsCancelButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        window.OwnedWindows.Should().BeEmpty();
        first.ShowRowGrandTotals.Should().BeTrue();
        clicked.ShowRowGrandTotals.Should().Be(accept);
        AssertSelection(window, selection);
        window.Session.CanUndo.Should().Be(accept);
        window.Session.IsDirty.Should().Be(accept);
        if (accept)
        {
            window.Session.UndoLastEdit().Success.Should().BeTrue();
            clicked.ShowRowGrandTotals.Should().BeFalse();
        }
    });

    private static async Task ExecuteContextAction(MainWindow window, string action, CellAddress clicked)
    {
        WorkbookApplicationCommandRouter.TryRouteWorksheetContextMenu(action, out var route).Should().BeTrue();
        var bindings = (WorkbookApplicationCommandBindings)typeof(MainWindow)
            .GetProperty("WorkbookApplicationCommands", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var result = await bindings.TryExecuteAsync(route!, targetAddress: clicked);
        result.IsBound.Should().BeTrue();
        result.Handled.Should().BeTrue();
    }

    private static (GridRange Primary, GridRange[] Areas, CellAddress Active) SelectContextRange(MainWindow window, CellAddress active)
    {
        var sheet = window.Session.ActiveSheet;
        var primary = new GridRange(Address(sheet, 1, 1), Address(sheet, 8, 5));
        var extra = new GridRange(Address(sheet, 10, 7), Address(sheet, 11, 8));
        window.Session.SelectRanges(primary, [primary, extra], active);
        return (primary, window.Session.SelectedRanges.ToArray(), active);
    }

    private static void AssertSelection(MainWindow window, (GridRange Primary, GridRange[] Areas, CellAddress Active) expected)
    {
        window.Session.SelectedRange.Should().Be(expected.Primary);
        window.Session.SelectedRanges.Should().Equal(expected.Areas);
        window.Session.ActiveCell.Should().Be(expected.Active);
    }

    private static PivotTableModel AddPivot(Sheet sheet, string name, uint row, uint col, bool showRowGrandTotals)
    {
        var pivot = new PivotTableModel
        {
            Name = name,
            SourceRange = new GridRange(Address(sheet, 20, 1), Address(sheet, 22, 2)),
            TargetRange = new GridRange(Address(sheet, row, col), Address(sheet, row + 3, col + 1)),
            ShowRowGrandTotals = showRowGrandTotals
        };
        sheet.PivotTables.Add(pivot);
        return pivot;
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

    private static T Find<T>(Window dialog, string id) where T : Control => dialog.GetVisualDescendants()
        .OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == id);

    private static Task Run(Func<MainWindow, Task> test) => Headless.Dispatch(async () =>
    {
        var window = new MainWindow([], null!, new FreeXOptionsRuntimeSession(
            load: static () => new AppOptions(), save: static _ => true));
        try
        {
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            await test(window);
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray())
                dialog.Close();
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }

        return true;
    }, CancellationToken.None);
}
