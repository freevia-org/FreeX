using System.Threading;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

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
    }, CancellationToken.None);
}
