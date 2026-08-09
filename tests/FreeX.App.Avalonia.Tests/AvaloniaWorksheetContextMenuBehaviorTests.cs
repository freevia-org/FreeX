using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Free.Shared.Ribbon;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Behavioral guards for the Avalonia worksheet context menu. The tests build the production menu,
/// click its rendered leaves, and assert model changes or state-driven enablement. This keeps the
/// Avalonia route aligned with the WPF host's shared worksheet planner instead of only checking that
/// command ids are present.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaWorksheetContextMenuBehaviorTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task InsertRowAboveFromCellMenu_ShiftsExistingCellsDown() => Session.Dispatch(() =>
    {
        var window = new MainWindow([]);
        try
        {
            var sheet = window.Session.ActiveSheet;
            var address = new CellAddress(sheet.Id, 3, 2);
            sheet.SetCell(address, new TextValue("keep"));
            window.Session.SelectCell(address);

            ClickWorksheetAction(window, WorksheetContextMenuAction.InsertRowAbove);

            sheet.GetValue(new CellAddress(sheet.Id, 4, 2)).Should().Be(new TextValue("keep"));
            sheet.GetCell(address).Should().BeNull();
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public Task InsertColumnLeftFromCellMenu_ShiftsExistingCellsRight() => Session.Dispatch(() =>
    {
        var window = new MainWindow([]);
        try
        {
            var sheet = window.Session.ActiveSheet;
            var address = new CellAddress(sheet.Id, 2, 3);
            sheet.SetCell(address, new TextValue("keep"));
            window.Session.SelectCell(address);

            ClickWorksheetAction(window, WorksheetContextMenuAction.InsertColumnLeft);

            sheet.GetValue(new CellAddress(sheet.Id, 2, 4)).Should().Be(new TextValue("keep"));
            sheet.GetCell(address).Should().BeNull();
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public Task DeleteRowsAndColumnsFromCellMenu_UsesWholeSelectionCommands() => Session.Dispatch(() =>
    {
        var window = new MainWindow([]);
        try
        {
            var sheet = window.Session.ActiveSheet;
            var rowTwo = new CellAddress(sheet.Id, 2, 2);
            var rowThree = new CellAddress(sheet.Id, 3, 2);
            sheet.SetCell(rowTwo, new TextValue("delete"));
            sheet.SetCell(rowThree, new TextValue("survive"));
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 2, 1),
                new CellAddress(sheet.Id, 2, CellAddress.MaxCol)));

            ClickWorksheetAction(window, WorksheetContextMenuAction.DeleteRows);

            sheet.GetValue(rowTwo).Should().Be(new TextValue("survive"));

            var colTwo = new CellAddress(sheet.Id, 2, 2);
            var colThree = new CellAddress(sheet.Id, 2, 3);
            sheet.SetCell(colTwo, new TextValue("delete column"));
            sheet.SetCell(colThree, new TextValue("survive column"));
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 1, 2),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 2)));

            ClickWorksheetAction(window, WorksheetContextMenuAction.DeleteColumns);

            sheet.GetValue(colTwo).Should().Be(new TextValue("survive column"));
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public Task InsertBelowAndRightAtWorksheetBounds_DoNotInsertBeforeTheActiveCell() => Session.Dispatch(() =>
    {
        var window = new MainWindow([]);
        try
        {
            var sheet = window.Session.ActiveSheet;
            var lastRow = new CellAddress(sheet.Id, CellAddress.MaxRow, 2);
            sheet.SetCell(lastRow, new TextValue("last row"));
            window.Session.SelectCell(lastRow);

            ClickWorksheetAction(window, WorksheetContextMenuAction.InsertRowBelow);

            sheet.GetValue(lastRow).Should().Be(new TextValue("last row"));

            var lastColumn = new CellAddress(sheet.Id, 2, CellAddress.MaxCol);
            sheet.SetCell(lastColumn, new TextValue("last column"));
            window.Session.SelectCell(lastColumn);

            ClickWorksheetAction(window, WorksheetContextMenuAction.InsertColumnRight);

            sheet.GetValue(lastColumn).Should().Be(new TextValue("last column"));
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public Task CellMenu_UsesWpfStateForCommentsNotesAndValidationDropdown() => Session.Dispatch(() =>
    {
        var window = new MainWindow([]);
        try
        {
            var sheet = window.Session.ActiveSheet;
            var address = new CellAddress(sheet.Id, 2, 2);
            sheet.ThreadedComments[address] = new ThreadedComment("Review") { IsResolved = false };
            sheet.Comments[address] = "Note";
            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(address, address),
                Type = DvType.List,
                Formula1 = "One,Two",
                ShowDropdown = true,
            });
            window.Session.SelectCell(address);

            var menu = BuildWorksheetCellMenu(window);

            FindAction(menu, WorksheetContextMenuAction.EditComment).IsEnabled.Should().BeTrue();
            FindAction(menu, WorksheetContextMenuAction.ResolveComment).IsEnabled.Should().BeTrue();
            FindAction(menu, WorksheetContextMenuAction.DeleteComment).IsEnabled.Should().BeTrue();
            FindAction(menu, WorksheetContextMenuAction.EditNote).IsEnabled.Should().BeTrue();
            FindAction(menu, WorksheetContextMenuAction.DeleteNote).IsEnabled.Should().BeTrue();
            FindAction(menu, WorksheetContextMenuAction.ShowHideNote).IsEnabled.Should().BeTrue();
            FindAction(menu, WorksheetContextMenuAction.PickFromDropDown).IsEnabled.Should().BeTrue();
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }
    }, CancellationToken.None);

    private static ContextMenu BuildWorksheetCellMenu(MainWindow window) =>
        (ContextMenu)(typeof(MainWindow).GetMethod(
            "BuildWorksheetCellContextMenu",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "BuildWorksheetCellContextMenu"))
            .Invoke(window, null)!;

    private static void ClickWorksheetAction(MainWindow window, WorksheetContextMenuAction action)
    {
        var item = FindAction(BuildWorksheetCellMenu(window), action);
        item.IsEnabled.Should().BeTrue($"{action} should be enabled in the worksheet cell menu");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static MenuItem FindAction(ContextMenu menu, WorksheetContextMenuAction action) =>
        FindItems(menu.Items).Single(item =>
            string.Equals(item.Tag as string, action.ToString(), StringComparison.Ordinal));

    private static IEnumerable<MenuItem> FindItems(IEnumerable<object?> items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in FindItems(item.Items))
                yield return child;
        }
    }
}
