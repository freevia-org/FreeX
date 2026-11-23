using System.Threading;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class WorksheetAutomationIdentityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task RetainedCellPeer_UpdatesValueNameHelpAndRaisesPropertyNotifications() => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        var address = Address(sheet, 1, 1);
        sheet.SetCell(address, new NumberValue(7));
        sheet.Comments[address] = "Original note";
        Refresh(window);
        var grid = GridPeer(window);
        var cell = Cell(grid, "A1");
        var value = cell.GetProvider<IValueProvider>()!;
        cell.GetHelpText().Should().Be("Note: Original note");
        value.Value.Should().Be("7");
        var changes = new List<AutomationProperty>();
        cell.PropertyChanged += (_, args) => changes.Add(args.Property);

        sheet.SetCell(address, new NumberValue(9));
        sheet.Comments[address] = "Updated note";
        sheet.Hyperlinks[address] = "https://example.com/current";
        Refresh(window);

        Cell(grid, "A1").Should().BeSameAs(cell);
        cell.GetProvider<IValueProvider>().Should().BeSameAs(value);
        value.Value.Should().Be("9");
        cell.GetName().Should().StartWith("A1: 9").And.Contain("has note").And.Contain("has a hyperlink");
        cell.GetHelpText().Should().Be("Note: Updated note");
        changes.Should().Contain(AutomationElementIdentifiers.NameProperty)
            .And.Contain(ValuePatternIdentifiers.ValueProperty)
            .And.Contain(AutomationElementIdentifiers.HelpTextProperty);

        changes.Clear();
        Refresh(window);
        changes.Should().NotContain(AutomationElementIdentifiers.NameProperty)
            .And.NotContain(ValuePatternIdentifiers.ValueProperty)
            .And.NotContain(AutomationElementIdentifiers.HelpTextProperty);
    });

    [Fact]
    public Task RetainedSelectionPeers_FollowRealKeyboardFocusWithoutChangingIdentity() => Run(async window =>
    {
        var grid = GridPeer(window);
        var first = Cell(grid, "A1");
        var second = Cell(grid, "B1");
        var firstSelection = first.GetProvider<ISelectionItemProvider>()!;
        var secondSelection = second.GetProvider<ISelectionItemProvider>()!;
        var firstChanges = new List<AutomationProperty>();
        var secondChanges = new List<AutomationProperty>();
        var gridChanges = new List<AutomationProperty>();
        first.PropertyChanged += (_, args) => firstChanges.Add(args.Property);
        second.PropertyChanged += (_, args) => secondChanges.Add(args.Property);
        grid.PropertyChanged += (_, args) => gridChanges.Add(args.Property);
        window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
        first.HasKeyboardFocus().Should().BeTrue();
        firstSelection.IsSelected.Should().BeTrue();
        secondSelection.IsSelected.Should().BeFalse();

        await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Right });
        Drain(window);

        Cell(grid, "A1").Should().BeSameAs(first);
        Cell(grid, "B1").Should().BeSameAs(second);
        first.HasKeyboardFocus().Should().BeFalse();
        second.HasKeyboardFocus().Should().BeTrue();
        firstSelection.IsSelected.Should().BeFalse();
        secondSelection.IsSelected.Should().BeTrue();
        grid.GetProvider<ISelectionProvider>()!.GetSelection().Should().Equal(second);
        secondSelection.SelectionContainer.Should().BeSameAs(grid.GetProvider<ISelectionProvider>());
        second.GetParent().Should().BeSameAs(grid);
        FocusedPeer(window).Should().BeSameAs(second);
        firstChanges.Should().Contain(SelectionItemPatternIdentifiers.IsSelectedProperty);
        secondChanges.Should().Contain(SelectionItemPatternIdentifiers.IsSelectedProperty);
        gridChanges.Should().Contain(SelectionPatternIdentifiers.SelectionProperty);
    });

    [Fact]
    public Task RetainedCoordinatePeer_ReadsTheNewSheetSnapshotAfterSheetSwitch() => Run(window =>
    {
        var firstSheet = window.Session.ActiveSheet;
        var firstAddress = Address(firstSheet, 1, 1);
        firstSheet.SetCell(firstAddress, new NumberValue(7));
        firstSheet.Comments[firstAddress] = "First sheet";
        firstSheet.Hyperlinks[firstAddress] = "https://example.com/first";
        Refresh(window);
        var grid = GridPeer(window);
        var cell = Cell(grid, "A1");
        cell.GetName().Should().Contain("has a hyperlink");

        var nextSheet = window.Session.Workbook.AddSheet("Next accessibility sheet");
        var nextAddress = Address(nextSheet, 1, 1);
        nextSheet.SetCell(nextAddress, new NumberValue(19));
        nextSheet.Comments[nextAddress] = "Second sheet";
        window.Session.SelectSheet(nextSheet.Id);
        Refresh(window);

        GridPeer(window).Should().BeSameAs(grid);
        Cell(grid, "A1").Should().BeSameAs(cell);
        cell.GetAutomationId().Should().Be("Cell_A1");
        cell.GetProvider<IValueProvider>()!.Value.Should().Be("19");
        cell.GetName().Should().StartWith("A1: 19").And.NotContain("has a hyperlink");
        cell.GetHelpText().Should().Be("Note: Second sheet");
    });

    [Fact]
    public Task OverlappingScroll_RetainsCellPeerAndPublishesNewBounds() => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        sheet.SetCell(Address(sheet, 3, 3), new NumberValue(33));
        Refresh(window);
        var grid = GridPeer(window);
        var cell = Cell(grid, "C3");
        var before = cell.GetBoundingRectangle();
        before.Width.Should().BeGreaterThan(0);
        before.Height.Should().BeGreaterThan(0);
        var changes = new List<AutomationProperty>();
        cell.PropertyChanged += (_, args) => changes.Add(args.Property);

        window.Session.SetViewportOrigin(2, 2);
        Refresh(window);

        Cell(grid, "C3").Should().BeSameAs(cell);
        var after = cell.GetBoundingRectangle();
        after.X.Should().BeLessThan(before.X);
        after.Y.Should().BeLessThan(before.Y);
        after.Size.Should().Be(before.Size);
        cell.IsOffscreen().Should().BeFalse();
        cell.GetProvider<IValueProvider>()!.Value.Should().Be("33");
        changes.Should().Contain(AutomationElementIdentifiers.BoundingRectangleProperty);
    });

    [Fact]
    public Task RepeatedFarScroll_BoundsCacheAndClearsHeldOffViewportCellData() => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(22));
        Refresh(window);
        var grid = GridPeer(window);
        var held = Cell(grid, "B2");
        held.GetProvider<IValueProvider>()!.Value.Should().Be("22");
        foreach (var (row, column) in new (uint, uint)[]
        {
            (1000, 100), (2000, 200), (3000, 300), (1000, 100), (4000, 400), (2000, 200),
        })
        {
            window.Session.SetViewportOrigin(row, column);
            Refresh(window);
            var visible = Cells(grid).ToArray();
            visible.Should().NotBeEmpty();
            visible.Select(peer => peer.GetAutomationId()).Should().OnlyHaveUniqueItems();
            GridHost(window).CachedCellPeerCount.Should().BeLessThanOrEqualTo(visible.Length + 1,
                "only the current viewport and an offscreen active cell may remain cached");
            held.GetAutomationId().Should().Be("Cell_B2");
            held.GetName().Should().Be("B2");
            held.GetProvider<IValueProvider>()!.Value.Should().BeEmpty();
            held.GetProvider<ISelectionItemProvider>()!.IsSelected.Should().BeFalse();
            held.GetBoundingRectangle().Size.Should().Be(default(Size));
            held.IsOffscreen().Should().BeTrue();
            held.HasKeyboardFocus().Should().BeFalse();
        }
    });

    [Fact]
    public Task MergedCoordinates_StayDistinctAndSelectedWhileCornerFocusCycles() => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = Address(sheet, 1, 1);
        var merge = new GridRange(anchor, Address(sheet, 2, 2));
        sheet.SetCell(anchor, new NumberValue(42));
        sheet.Hyperlinks[anchor] = "https://example.com/anchor";
        sheet.AddMergedRegion(merge);
        window.Session.SelectRanges(merge, [merge], anchor);
        Refresh(window);
        var grid = GridPeer(window);
        var peers = new[] { "A1", "B1", "B2", "A2" }.ToDictionary(address => address, address => Cell(grid, address));
        window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
        foreach (var next in new[] { "B1", "B2", "A2", "A1" })
        {
            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.OemPeriod, KeyModifiers = KeyModifiers.Control });
            Drain(window);
            foreach (var (address, peer) in peers)
            {
                Cell(grid, address).Should().BeSameAs(peer);
                peer.GetAutomationId().Should().Be($"Cell_{address}");
                peer.GetName().Should().Contain("is merged");
                peer.GetProvider<ISelectionItemProvider>()!.IsSelected.Should().BeTrue();
            }
            grid.GetProvider<ISelectionProvider>()!.GetSelection().OrderBy(peer => peer.GetAutomationId())
                .Should().Equal(peers.Values.OrderBy(peer => peer.GetAutomationId()));
            peers[next].HasKeyboardFocus().Should().BeTrue();
            peers.Values.Count(peer => peer.HasKeyboardFocus()).Should().Be(1);
            FocusedPeer(window).Should().BeSameAs(peers[next]);
            peers["A1"].GetProvider<IValueProvider>()!.Value.Should().Be("42");
            peers["A1"].GetName().Should().Contain("has a hyperlink");
            foreach (var covered in new[] { "B1", "B2", "A2" })
            {
                peers[covered].GetProvider<IValueProvider>()!.Value.Should().BeEmpty();
                peers[covered].GetName().Should().NotContain("has a hyperlink");
            }
        }
    });

    [Fact]
    public Task TemporaryVisualReparent_PreservesLogicalCellsAndTheirCurrentData() => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        sheet.SetCell(Address(sheet, 1, 1), new NumberValue(19));
        Refresh(window);
        var grid = GridPeer(window);
        var held = Cell(grid, "A1");
        var count = grid.GetChildren().Count;
        var content = window.Content;
        window.Content = null;
        window.Content = content;
        Drain(window);

        grid.GetChildren().Should().HaveCount(count);
        Cell(grid, "A1").Should().BeSameAs(held);
        held.GetProvider<IValueProvider>()!.Value.Should().Be("19");
        held.GetParent().Should().BeSameAs(grid);
        held.IsEnabled().Should().BeTrue();
    });

    [Fact]
    public Task InlineEditorRebuild_KeepsCachedCellParentAndRealEditorFocus() => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        sheet.SetCell(Address(sheet, 1, 1), new NumberValue(7));
        Refresh(window);
        var grid = GridPeer(window);
        var cell = Cell(grid, "A1");
        var childrenChanges = 0;
        cell.ChildrenChanged += (_, _) => childrenChanges++;
        window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
        await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.F2 });
        Drain(window);
        var firstEditor = Editor(cell);
        firstEditor.GetParent().Should().BeSameAs(cell);
        firstEditor.HasKeyboardFocus().Should().BeTrue();
        firstEditor.GetProvider<IValueProvider>()!.IsReadOnly.Should().BeFalse();
        window.RaiseInlineCellEditorTextInputForTest("!");
        firstEditor.GetProvider<IValueProvider>()!.Value.Should().Be("7!");
        childrenChanges = 0;

        Refresh(window);

        Cell(grid, "A1").Should().BeSameAs(cell);
        var nextEditor = Editor(cell);
        nextEditor.Should().NotBeSameAs(firstEditor);
        nextEditor.GetParent().Should().BeSameAs(cell);
        nextEditor.HasKeyboardFocus().Should().BeTrue();
        FocusedPeer(window).Should().BeSameAs(nextEditor);
        nextEditor.GetProvider<IValueProvider>()!.IsReadOnly.Should().BeFalse();
        nextEditor.GetProvider<IValueProvider>()!.Value.Should().Be("7!");
        childrenChanges.Should().BeGreaterThan(0);
        window.RaiseInlineCellEditorKeyDownForTest(new KeyEventArgs { Key = Key.Escape });
        Drain(window);
        cell.GetChildren().Should().BeEmpty();
        cell.HasKeyboardFocus().Should().BeTrue();
    });

    [Fact]
    public Task ClosingWindow_ClearsCacheAndDisablesPreviouslyHeldPeers() => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        sheet.SetCell(Address(sheet, 1, 1), new NumberValue(7));
        Refresh(window);
        var grid = GridPeer(window);
        var held = Cell(grid, "A1");
        GridHost(window).CachedCellPeerCount.Should().BeGreaterThan(0);
        held.IsEnabled().Should().BeTrue();

        window.AllowCloseWithoutDirtyPromptForParityCapture();
        window.Close();
        Dispatcher.UIThread.RunJobs();

        GridHost(window).CachedCellPeerCount.Should().Be(0);
        held.IsEnabled().Should().BeFalse();
        held.HasKeyboardFocus().Should().BeFalse();
        held.IsOffscreen().Should().BeTrue();
        held.GetProvider<IValueProvider>()!.Value.Should().BeEmpty();
        held.GetChildren().Should().BeEmpty();
        grid.GetChildren().Should().BeEmpty();
        grid.GetProvider<ISelectionProvider>()!.GetSelection().Should().BeEmpty();
        GridHost(window).CachedCellPeerCount.Should().Be(0);
    });

    private static AutomationPeer FocusedPeer(MainWindow window)
    {
        var focused = window.FocusManager!.GetFocusedElement();
        focused.Should().BeAssignableTo<Control>();
        return ControlAutomationPeer.CreatePeerForElement((Control)focused!);
    }

    private static WorksheetGridHost GridHost(MainWindow window) => (WorksheetGridHost)window.SheetGridHostForTest;

    private static AutomationPeer GridPeer(MainWindow window) =>
        ControlAutomationPeer.CreatePeerForElement(window.SheetGridHostForTest);

    private static AutomationPeer Cell(AutomationPeer grid, string address) =>
        Cells(grid).Single(peer => peer.GetAutomationId() == $"Cell_{address}");

    private static IEnumerable<AutomationPeer> Cells(AutomationPeer grid) =>
        grid.GetChildren().Where(peer => peer.GetAutomationControlType() == AutomationControlType.DataItem);

    private static AutomationPeer Editor(AutomationPeer cell) =>
        cell.GetChildren().Single(peer => peer.GetAutomationControlType() == AutomationControlType.Edit);

    private static CellAddress Address(Sheet sheet, uint row, uint column) => new(sheet.Id, row, column);

    private static void Refresh(MainWindow window)
    {
        // Direct model mutations do not refresh WorkbookSession.Viewport. A repeated identical
        // size is a no-op, so force reflow before rebuilding the real window at the fixed size.
        window.Session.UpdateViewportSize(482, 882);
        window.Session.UpdateViewportSize(481, 881);
        window.RefreshShellForTest();
        Drain(window);
    }

    private static void Drain(MainWindow window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static Task Run(Action<MainWindow> test) => Run(window =>
    {
        test(window);
        return Task.CompletedTask;
    });

    private static Task Run(Func<MainWindow, Task> test) => Session.Dispatch(async () =>
    {
        var options = new AppOptions { ShowFormulaBar = true };
        var optionsSession = new FreeXOptionsRuntimeSession(
            initialOptions: options, load: () => options.Clone(), save: _ => true);
        var window = new MainWindow([], null!, optionsSession);
        try
        {
            var sheet = window.Session.Workbook.AddSheet("Accessibility identity");
            window.Session.SelectSheet(sheet.Id);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            Refresh(window);
            await test(window);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }
        return true;
    }, CancellationToken.None);
}
