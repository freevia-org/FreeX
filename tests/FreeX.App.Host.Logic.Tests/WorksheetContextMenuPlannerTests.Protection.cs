using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class WorksheetContextMenuPlannerTests
{
    private static readonly SheetProtectionPermission[] DefaultProtection =
        [SheetProtectionPermission.SelectLockedCells, SheetProtectionPermission.SelectUnlockedCells];

    private static IReadOnlyList<WorksheetContextMenuCommand> FlattenAllCommands(IEnumerable<WorksheetContextMenuCommand> commands) =>
        commands.SelectMany(command => new[] { command }.Concat(FlattenAllCommands(command.Children))).ToArray();

    private static WorksheetContextMenuCommand Find(IEnumerable<WorksheetContextMenuCommand> commands, WorksheetContextMenuAction action) =>
        FlattenAllCommands(commands).First(command => command.Action == action);

    private static IReadOnlyList<WorksheetContextMenuCommand> ProtectedMenu(params SheetProtectionPermission[] allowed) =>
        WorksheetContextMenuPlanner.BuildCommands(
            WorksheetContextMenuTargetKind.Worksheet,
            WorksheetContextMenuState.Default with { Protection = new HashSet<SheetProtectionPermission>(allowed) });

    [Fact]
    public void UnprotectedSheet_KeepsEveryEditingCommandEnabled()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.Worksheet, WorksheetContextMenuState.Default with
        {
            Clipboard = WorksheetContextMenuClipboardState.Copied,
        });

        Find(commands, WorksheetContextMenuAction.Cut).IsEnabled.Should().BeTrue();
        Find(commands, WorksheetContextMenuAction.InsertRowAbove).IsEnabled.Should().BeTrue();
        Find(commands, WorksheetContextMenuAction.FormatCells).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ProtectedSheet_GreysOutTheCommandsThatWouldEditIt_ButNotCopy()
    {
        var commands = ProtectedMenu(DefaultProtection);

        foreach (var action in new[]
                 {
                     WorksheetContextMenuAction.Cut, WorksheetContextMenuAction.Paste, WorksheetContextMenuAction.PasteSpecial,
                     WorksheetContextMenuAction.InsertRowAbove, WorksheetContextMenuAction.InsertColumnLeft,
                     WorksheetContextMenuAction.DeleteRows, WorksheetContextMenuAction.DeleteColumns,
                     WorksheetContextMenuAction.SortAscending, WorksheetContextMenuAction.Filter,
                     WorksheetContextMenuAction.FormatCells, WorksheetContextMenuAction.ClearContents,
                     WorksheetContextMenuAction.RowHeight, WorksheetContextMenuAction.ColumnWidth,
                     WorksheetContextMenuAction.NewComment, WorksheetContextMenuAction.DataValidation,
                 })
        {
            Find(commands, action).IsEnabled.Should().BeFalse($"{action} edits the sheet");
        }

        Find(commands, WorksheetContextMenuAction.Copy).IsEnabled.Should().BeTrue("copying does not change a protected sheet");
        Find(commands, WorksheetContextMenuAction.QuickAnalysis).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ProtectedSheet_GreysOutASubmenuWhoseEntriesAreAllGreyed()
    {
        var commands = ProtectedMenu(DefaultProtection);

        commands.Single(command => command.Header == "Sort and Filter").IsEnabled.Should().BeFalse();
        commands.Single(command => command.Header == "Data Tools").IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ProtectedSheet_ReEnablesWhatItsProtectionOptionsAllow()
    {
        var commands = ProtectedMenu(
            SheetProtectionPermission.SelectLockedCells,
            SheetProtectionPermission.InsertRows,
            SheetProtectionPermission.Sort,
            SheetProtectionPermission.FormatCells,
            SheetProtectionPermission.FormatRows);

        Find(commands, WorksheetContextMenuAction.InsertRowAbove).IsEnabled.Should().BeTrue();
        Find(commands, WorksheetContextMenuAction.SortAscending).IsEnabled.Should().BeTrue();
        Find(commands, WorksheetContextMenuAction.FormatCells).IsEnabled.Should().BeTrue();
        Find(commands, WorksheetContextMenuAction.RowHeight).IsEnabled.Should().BeTrue();
        Find(commands, WorksheetContextMenuAction.InsertColumnLeft).IsEnabled.Should().BeFalse("inserting columns is not allowed");
        Find(commands, WorksheetContextMenuAction.DeleteRows).IsEnabled.Should().BeFalse("deleting rows is not allowed");
        commands.Single(command => command.Header == "Sort and Filter").IsEnabled.Should().BeTrue("one of its entries is allowed");
    }

    [Fact]
    public void ResolveWorksheetState_ReadsTheSheetsProtection()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        var sheet = workbook.Sheets[0];
        var address = new CellAddress(sheet.Id, 1, 1);
        WorksheetContextMenuPlanner.ResolveWorksheetState(sheet, address).Protection.Should().BeNull();

        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.Sort);

        WorksheetContextMenuPlanner.ResolveWorksheetState(sheet, address).Protection.Should().Contain(SheetProtectionPermission.Sort);
    }
}
