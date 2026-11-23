using System.Threading;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class WorksheetAutomationPeerTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task WorksheetPeer_ExposesPopulatedAndBlankCellsAsAccessibleLeafItems() => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        sheet.SetCell(Address(sheet, 1, 1), new NumberValue(42));
        Refresh(window);

        var grid = GridPeer(window);
        grid.GetAutomationControlType().Should().Be(AutomationControlType.DataGrid);
        grid.IsControlElement().Should().BeTrue();
        grid.IsContentElement().Should().BeTrue();
        var selection = grid.GetProvider<ISelectionProvider>();
        selection.Should().NotBeNull();
        selection!.CanSelectMultiple.Should().BeTrue();
        selection.IsSelectionRequired.Should().BeFalse();

        var populated = Cell(grid, "A1");
        var blank = Cell(grid, "B1");
        foreach (var cell in new[] { populated, blank })
        {
            cell.GetAutomationControlType().Should().Be(AutomationControlType.DataItem);
            cell.IsControlElement().Should().BeTrue();
            cell.IsContentElement().Should().BeTrue();
            cell.GetChildren().Should().BeEmpty("cell text and decoration must not be announced twice");
        }

        populated.GetName().Should().Be("A1: 42");
        populated.GetProvider<IValueProvider>()!.Value.Should().Be("42");
        blank.GetName().Should().Be("B1");
        blank.GetProvider<IValueProvider>()!.Value.Should().BeEmpty();
    });

    [Fact]
    public Task CellProviders_KeepMutationOwnedByTheWorksheetEditor() => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        var address = Address(sheet, 1, 1);
        sheet.SetCell(address, new NumberValue(42));
        Refresh(window);
        var range = window.Session.SelectedRange;
        var grid = GridPeer(window);
        var cell = Cell(grid, "A1");
        var value = cell.GetProvider<IValueProvider>()!;
        var selection = cell.GetProvider<ISelectionItemProvider>()!;

        value.IsReadOnly.Should().BeTrue();
        selection.SelectionContainer.Should().BeSameAs(grid.GetProvider<ISelectionProvider>());
        Action setValue = () => value.SetValue("99");
        Action select = selection.Select;
        Action add = selection.AddToSelection;
        Action remove = selection.RemoveFromSelection;
        setValue.Should().Throw<InvalidOperationException>();
        select.Should().Throw<InvalidOperationException>();
        add.Should().Throw<InvalidOperationException>();
        remove.Should().Throw<InvalidOperationException>();
        sheet.GetValue(address).Should().Be(new NumberValue(42));
        window.Session.SelectedRange.Should().Be(range);
    });

    [Fact]
    public Task SelectionProvider_ReportsDisjointAreasAndTheTrueActiveAnchor() => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        var first = new GridRange(Address(sheet, 1, 1), Address(sheet, 1, 2));
        var activeArea = new GridRange(Address(sheet, 1, 3), Address(sheet, 3, 3));
        var anchor = Address(sheet, 3, 3);
        sheet.SetCell(anchor, new NumberValue(100));
        window.Session.SelectRanges(activeArea, [first, activeArea], anchor);
        Refresh(window);
        window.ActiveCellBorderForTest!.Focus().Should().BeTrue();

        var grid = GridPeer(window);
        grid.GetProvider<ISelectionProvider>()!.GetSelection().Select(peer => peer.GetAutomationId())
            .Should().BeEquivalentTo("Cell_A1", "Cell_B1", "Cell_C1", "Cell_C2", "Cell_C3");
        Cell(grid, "C3").GetProvider<ISelectionItemProvider>()!.IsSelected.Should().BeTrue();
        Cell(grid, "D4").GetProvider<ISelectionItemProvider>()!.IsSelected.Should().BeFalse();
        Cell(grid, "C3").HasKeyboardFocus().Should().BeTrue();
        Cell(grid, "C1").HasKeyboardFocus().Should().BeFalse(
            "the normalized top-left of the range is not the active editing anchor");
        Cell(grid, "C3").GetName().Should().Be("C3: 100");
    });

    [Fact]
    public Task CellPeer_AnnouncesFormulaMergeHyperlinkAndCommentMetadata() => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        var address = Address(sheet, 1, 1);
        sheet.SetFormula(address, "1+2");
        sheet.Comments[address] = "Check this figure.";
        sheet.Hyperlinks[address] = "https://example.com/report";
        sheet.AddMergedRegion(new GridRange(address, Address(sheet, 1, 2)));
        Refresh(window);

        var peer = Cell(GridPeer(window), "A1");
        peer.GetName().Should().StartWith("A1")
            .And.Contain("has note")
            .And.Contain("is a formula")
            .And.Contain("is merged")
            .And.Contain("has a hyperlink");
        peer.GetHelpText().Should().Be("Note: Check this figure.");
        peer.GetChildren().Should().BeEmpty();
    });

    [Fact]
    public Task InlineCellEditor_RemainsReachableThroughTheCellPeer() => Run(window =>
    {
        var address = window.Session.ActiveCell;
        window.BeginInlineCellEditForTest(address, "Pending text", 12);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var cell = Cell(GridPeer(window), "A1");
        var editor = cell.GetChildren().Single(peer =>
            peer.GetAutomationControlType() == AutomationControlType.Edit);
        editor.IsControlElement().Should().BeTrue();
        editor.GetProvider<IValueProvider>()!.Value.Should().Be("Pending text");
        editor.GetProvider<IValueProvider>()!.IsReadOnly.Should().BeFalse();
    });

    [Theory]
    [InlineData("normal")]
    [InlineData("frozen")]
    [InlineData("split")]
    public Task MergedCellCornerCycling_PreservesFocusAndTheTrueActiveAddress(string panes) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = Address(sheet, 2, 2);
        var merge = new GridRange(anchor, Address(sheet, 3, 3));
        sheet.SetCell(anchor, new NumberValue(42));
        sheet.Hyperlinks[anchor] = "https://example.com/merged";
        sheet.AddMergedRegion(merge);
        if (panes == "frozen")
        {
            sheet.FrozenRows = 1;
            sheet.FrozenCols = 1;
        }
        else if (panes == "split")
        {
            sheet.SplitRow = 2;
            sheet.SplitColumn = 2;
        }

        window.Session.SelectRanges(merge, [merge], anchor);
        Refresh(window);
        window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
        var grid = GridPeer(window);
        foreach (var corner in new[] { Address(sheet, 2, 3), Address(sheet, 3, 3), Address(sheet, 3, 2), anchor })
        {
            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.OemPeriod, KeyModifiers = KeyModifiers.Control });
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            window.Session.ActiveCell.Should().Be(corner);
            window.Session.SelectedRange.Should().Be(merge);
            window.Session.SelectedRanges.Should().Equal(merge);
            var focused = Cells(grid).Single(peer => peer.HasKeyboardFocus());
            var address = $"{CellAddress.NumberToColumnName(corner.Col)}{corner.Row}";
            focused.GetAutomationId().Should().Be($"Cell_{address}");
            if (corner == anchor)
                focused.GetName().Should().Be($"{address}: 42, is merged, has a hyperlink");
            else
                focused.GetName().Should().Be($"{address}, is merged",
                    "WPF exposes the literal covered coordinate, without copying anchor content or hyperlinks");
            focused.GetProvider<IValueProvider>()!.Value.Should().Be(corner == anchor ? "42" : "");
            focused.GetProvider<ISelectionItemProvider>()!.IsSelected.Should().BeTrue();
            Cells(grid).Select(peer => peer.GetAutomationId()).Should().OnlyHaveUniqueItems();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MergedCellWithHiddenOrScrolledAnchor_PreservesActiveIdentity(bool hideAnchor) => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = Address(sheet, 2, 2);
        var active = Address(sheet, 9, 4);
        var merge = new GridRange(anchor, Address(sheet, 10, 4));
        sheet.SetCell(anchor, new NumberValue(73));
        sheet.AddMergedRegion(merge);
        window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
        window.Session.SelectRanges(merge, [merge], active);
        if (hideAnchor)
        {
            sheet.HiddenRows.Add(anchor.Row);
            sheet.HiddenCols.Add(anchor.Col);
        }
        else
        {
            window.Session.SetViewportOrigin(7, 3);
        }
        Refresh(window);

        window.Session.ActiveCell.Should().Be(active);
        window.Session.SelectedRange.Should().Be(merge);
        var focused = Cells(GridPeer(window)).Single(peer => peer.HasKeyboardFocus());
        focused.GetAutomationId().Should().Be("Cell_D9");
        focused.GetName().Should().Be("D9, is merged");
        focused.GetProvider<IValueProvider>()!.Value.Should().BeEmpty();
        focused.GetProvider<ISelectionItemProvider>()!.IsSelected.Should().BeTrue();
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task MergedCoveredCellEditing_ExposesFocusedEditorAndPreservesLiteralCommitTarget(
        bool readyTyping, bool scrollPastAnchor) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = Address(sheet, 2, 2);
        var covered = Address(sheet, 2, 4);
        var merge = new GridRange(anchor, Address(sheet, 10, 4));
        sheet.SetCell(anchor, new NumberValue(73));
        sheet.AddMergedRegion(merge);
        var originalCoveredValue = sheet.GetValue(covered);
        window.Session.SelectRanges(merge, [merge], anchor);
        Refresh(window);
        window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
        await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.OemPeriod, KeyModifiers = KeyModifiers.Control });
        window.Session.ActiveCell.Should().Be(covered);
        if (scrollPastAnchor)
        {
            window.Session.SetViewportOrigin(7, 3);
            Refresh(window);
        }

        if (readyTyping)
        {
            var target = window.ActiveCellBorderForTest!;
            target.RaiseEvent(new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Source = target,
                Text = "pending",
            });
        }
        else
        {
            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.F2 });
        }
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        window.Session.FormulaEditAddress.Should().Be(covered);
        window.Session.SelectedRange.Should().Be(merge);
        var editor = window.ActiveCellBorderForTest!.GetVisualDescendants().OfType<TextBox>().Single();
        editor.IsFocused.Should().BeTrue();
        var editorPeer = Cell(GridPeer(window), "D2").GetChildren()
            .Single(peer => peer.GetAutomationControlType() == AutomationControlType.Edit);
        editorPeer.HasKeyboardFocus().Should().BeTrue();
        var value = editorPeer.GetProvider<IValueProvider>()!;
        value.IsReadOnly.Should().BeFalse();
        value.Value.Should().Be(readyTyping ? "pending" : "");
        if (!readyTyping)
            window.RaiseInlineCellEditorTextInputForTest("pending");
        window.RaiseInlineCellEditorTextInputForTest("!");
        value.Value.Should().Be("pending!");

        window.RaiseInlineCellEditorKeyDownForTest(new KeyEventArgs { Key = Key.Enter });
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        window.Session.FormulaEditAddress.Should().BeNull();
        window.SheetGridHostForTest.GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();
        // As on WPF, the literal covered address is passed to EditCellsCommand, which deliberately
        // skips covered-member writes rather than planting hidden content or changing the anchor.
        sheet.GetValue(anchor).Should().Be(new NumberValue(73));
        sheet.GetValue(covered).Should().Be(originalCoveredValue);
        sheet.MergedRegions.Should().Contain(merge);
        Cells(GridPeer(window)).Count(peer => peer.HasKeyboardFocus()).Should().Be(1);
    });

    [Fact]
    public Task ExistingGridPeer_ObservesChangedValuesAndKeyboardNavigationAfterRebuild() => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var address = Address(sheet, 1, 1);
        sheet.SetCell(address, new NumberValue(7));
        Refresh(window);
        var grid = GridPeer(window);
        Cell(grid, "A1").GetProvider<IValueProvider>()!.Value.Should().Be("7");

        sheet.SetCell(address, new NumberValue(9));
        Refresh(window);
        GridPeer(window).Should().BeSameAs(grid, "the worksheet accessibility container survives rebuilds");
        Cell(grid, "A1").GetName().Should().Be("A1: 9");
        Cell(grid, "A1").GetProvider<IValueProvider>()!.Value.Should().Be("9");
        window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
        await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Right });
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        window.Session.ActiveCell.Should().Be(Address(sheet, 1, 2));
        Cell(grid, "B1").HasKeyboardFocus().Should().BeTrue();
        Cell(grid, "A1").HasKeyboardFocus().Should().BeFalse();
        grid.GetProvider<ISelectionProvider>()!.GetSelection().Select(peer => peer.GetAutomationId())
            .Should().Equal("Cell_B1");
    });

    [Fact]
    public Task Scrolling_ExposesOnlyCurrentViewportCellsThroughThePersistentGridPeer() => Run(window =>
    {
        var grid = GridPeer(window);
        var before = Cells(grid).Select(peer => peer.GetAutomationId()).ToArray();
        before.Should().Contain("Cell_A1");
        before.Length.Should().BeInRange(1, 2000);

        window.Session.SetViewportOrigin(1000, 100);
        Refresh(window);

        var after = Cells(grid).Select(peer => peer.GetAutomationId()).ToArray();
        after.Should().Contain("Cell_CV1000");
        after.Should().NotContain("Cell_A1");
        after.Should().OnlyHaveUniqueItems();
        after.Length.Should().BeInRange(1, 2000,
            "accessibility enumeration must follow the visible viewport rather than every worksheet cell");
    });

    private static AutomationPeer GridPeer(MainWindow window) =>
        ControlAutomationPeer.CreatePeerForElement(window.SheetGridHostForTest);

    private static AutomationPeer Cell(AutomationPeer grid, string address) =>
        Cells(grid).Single(peer => peer.GetAutomationId() == $"Cell_{address}");

    private static IEnumerable<AutomationPeer> Cells(AutomationPeer parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child.GetAutomationControlType() == AutomationControlType.DataItem)
                yield return child;
            else
                foreach (var descendant in Cells(child))
                    yield return descendant;
        }
    }

    private static CellAddress Address(Sheet sheet, uint row, uint column) => new(sheet.Id, row, column);

    private static void Refresh(MainWindow window)
    {
        window.Session.UpdateViewportSize(482, 882);
        window.Session.UpdateViewportSize(481, 881);
        window.RefreshShellForTest();
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
            initialOptions: options,
            load: () => options.Clone(),
            save: _ => true);
        var window = new MainWindow([], null!, optionsSession);
        try
        {
            var sheet = window.Session.Workbook.AddSheet("Accessibility");
            window.Session.SelectSheet(sheet.Id);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            Refresh(window);
            await test(window);
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
        return true;
    }, CancellationToken.None);
}
