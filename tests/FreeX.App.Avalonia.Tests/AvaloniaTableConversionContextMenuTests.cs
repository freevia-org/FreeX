using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaTableConversionContextMenuTests
{
    private static readonly HeadlessUnitTestSession Headless =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task WorksheetMenuConversion_PreservesCellsAndRewritesReferencesWithUndoRedo() => Run(async window =>
    {
        var table = SeedTable(window);
        var sheet = window.Session.ActiveSheet;
        var valueCell = sheet.GetCell(Address(sheet, 2, 2))!;
        var style = valueCell.StyleId;
        var formula = sheet.GetCell(Address(sheet, 1, 4))!;

        WithConfirmation(window, "Yes", () => ClickConversion(window));

        AssertConverted(window);
        valueCell.Value.Should().Be(new NumberValue(12));
        valueCell.StyleId.Should().Be(style);
        window.Session.Workbook.GetStyle(style).FillColor.Should().Be(new CellColor(30, 80, 120));
        window.Session.IsDirty.Should().BeTrue();
        window.Session.CanUndo.Should().BeTrue();

        await PressHistoryKey(window, Key.Z);
        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
        formula.FormulaText.Should().Be("SUM(Sales[Amount])");
        sheet.FilterHiddenRows.Should().BeEquivalentTo(new uint[] { 2, 8 });
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo(new uint[] { 3, 8 });
        sheet.ActiveValueFilterColumns[2].Should().Equal("12");
        valueCell.StyleId.Should().Be(style);
        window.Session.CanUndo.Should().BeFalse();
        window.Session.CanRedo.Should().BeTrue();

        await PressHistoryKey(window, Key.Y);
        AssertConverted(window);
        valueCell.Value.Should().Be(new NumberValue(12));
        valueCell.StyleId.Should().Be(style);
    });

    [Theory]
    [InlineData("No")]
    [InlineData("Close")]
    [InlineData("Escape")]
    public Task WorksheetMenuConversion_CancellationLeavesWorkbookAndHistoryUntouched(string answer) => Run(window =>
    {
        var table = SeedTable(window);
        WithConfirmation(window, answer, () => ClickConversion(window));
        AssertUnchanged(window, table);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("Yes", true)]
    [InlineData("No", false)]
    public Task RibbonConversion_UsesTheSameOwnedConfirmation(string answer, bool accepted) => Run(window =>
    {
        var table = SeedTable(window);
        WithConfirmation(window, answer, () =>
        {
            window.RibbonCommandRegistryForTest!.TryGet(new RibbonCommandId("Convert to Range"), out var command)
                .Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
        });
        if (accepted)
            AssertConverted(window);
        else
            AssertUnchanged(window, table);
        return Task.CompletedTask;
    });

    [Fact]
    public Task WorksheetMenuConversion_RejectsSheetProtectedWhileConfirmationIsOpen() => Run(window =>
    {
        var table = SeedTable(window);
        WithConfirmation(window, "Yes", () => ClickConversion(window),
            beforeAnswer: () => window.Session.ActiveSheet.IsProtected = true);
        AssertUnchanged(window, table);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ProtectedSheet_MenuDisablesConversion() => Run(window =>
    {
        var table = SeedTable(window);
        window.Session.ActiveSheet.IsProtected = true;
        var menu = BuildCellMenu(window);
        MenuItems(menu.Items).Single(item =>
            item.Tag as string == nameof(WorksheetContextMenuAction.ConvertTableToRange))
            .IsEnabled.Should().BeFalse();
        window.OwnedWindows.Should().BeEmpty();
        AssertUnchanged(window, table);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ClickedTableMenu_PreservesExistingActiveTableTargeting() => Run(window =>
    {
        SeedTable(window);
        var sheet = window.Session.ActiveSheet;
        var clickedTable = new StructuredTableModel
        {
            Id = 2, Name = "Other", DisplayName = "Other",
            Range = new GridRange(Address(sheet, 5, 1), Address(sheet, 7, 2)),
        };
        sheet.StructuredTables.Add(clickedTable);
        var range = new GridRange(Address(sheet, 1, 1), Address(sheet, 7, 2));
        window.Session.SelectRanges(range, [range], Address(sheet, 2, 2));

        WithConfirmation(window, "Yes", () => ClickConversion(window, Address(sheet, 6, 2)));

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(clickedTable);
        return Task.CompletedTask;
    });

    private static StructuredTableModel SeedTable(MainWindow window)
    {
        var sheet = window.Session.ActiveSheet;
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Item"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Amount"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("First"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(12));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Second"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(30));
        sheet.GetCell(Address(sheet, 2, 2))!.StyleId = window.Session.Workbook.RegisterStyle(
            new CellStyle { FillColor = new CellColor(30, 80, 120), Bold = true });
        sheet.SetCell(Address(sheet, 1, 4), Cell.FromFormula("SUM(Sales[Amount])"));
        var table = new StructuredTableModel
        {
            Id = 1, Name = "Sales", DisplayName = "Sales", HasAutoFilter = true,
            Range = new GridRange(Address(sheet, 1, 1), Address(sheet, 3, 2)),
            Columns = { new StructuredTableColumnModel(1, "Item"), new StructuredTableColumnModel(2, "Amount") },
        };
        sheet.StructuredTables.Add(table);
        sheet.FilterHiddenRows.UnionWith([2, 8]);
        sheet.ValueFilterHiddenRows.UnionWith([3, 8]);
        sheet.ActiveValueFilterColumns[2] = new[] { "12" };
        window.Session.SelectCell(Address(sheet, 2, 2));
        window.Session.IsDirty.Should().BeFalse();
        window.Session.CanUndo.Should().BeFalse();
        return table;
    }

    private static void AssertConverted(MainWindow window)
    {
        var sheet = window.Session.ActiveSheet;
        sheet.StructuredTables.Should().BeEmpty();
        sheet.GetValue(Address(sheet, 1, 1)).Should().Be(new TextValue("Item"));
        sheet.GetValue(Address(sheet, 3, 2)).Should().Be(new NumberValue(30));
        sheet.GetCell(Address(sheet, 1, 4))!.FormulaText.Should().NotContain("Sales[")
            .And.Contain("$B$2:$B$3");
        sheet.FilterHiddenRows.Should().Equal(8u);
        sheet.ValueFilterHiddenRows.Should().Equal(8u);
        sheet.ActiveValueFilterColumns.Should().NotContainKey(2);
    }

    private static void AssertUnchanged(MainWindow window, StructuredTableModel table)
    {
        var sheet = window.Session.ActiveSheet;
        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
        sheet.GetCell(Address(sheet, 1, 4))!.FormulaText.Should().Be("SUM(Sales[Amount])");
        sheet.FilterHiddenRows.Should().BeEquivalentTo(new uint[] { 2, 8 });
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo(new uint[] { 3, 8 });
        sheet.ActiveValueFilterColumns[2].Should().Equal("12");
        window.Session.IsDirty.Should().BeFalse();
        window.Session.CanUndo.Should().BeFalse();
    }

    private static void ClickConversion(MainWindow window, CellAddress? clicked = null)
    {
        var menu = BuildCellMenu(window, clicked);
        try
        {
            menu.Open(window.SheetGridHostForTest);
            var item = MenuItems(menu.Items).Single(item =>
                item.Tag as string == nameof(WorksheetContextMenuAction.ConvertTableToRange));
            item.IsEnabled.Should().BeTrue();
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        finally { menu.Close(); }
    }

    private static ContextMenu BuildCellMenu(MainWindow window, CellAddress? clicked = null) =>
        (ContextMenu)typeof(MainWindow).GetMethod("BuildWorksheetCellContextMenuFor",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [clicked])!;

    private static IEnumerable<MenuItem> MenuItems(IEnumerable<object?> items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in MenuItems(item.Items))
                yield return child;
        }
    }

    private static void WithConfirmation(MainWindow owner, string answer, Action invoke, Action? beforeAnswer = null)
    {
        Exception? promptFailure = null;
        var answered = false;
        var returned = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (returned)
                return;
            AvaloniaSynchronousUserMessageDialog? dialog = null;
            try
            {
                dialog = owner.OwnedWindows.OfType<AvaloniaSynchronousUserMessageDialog>().Single();
                dialog.IsVisible.Should().BeTrue();
                owner.IsEnabled.Should().BeFalse();
                dialog.Title.Should().Be(UiText.Get("MainWindow_TooltipTitle_ConvertToRange"));
                dialog.GetVisualDescendants().OfType<TextBlock>().Should().Contain(text =>
                    text.Text == UiText.Get("TableDesign_ConvertToRangeConfirmation"));
                dialog.MessageButtons.Should().Be(UserMessageButtons.YesNo);
                dialog.MessageIcon.Should().Be(UserMessageIcon.Question);
                dialog.DismissedResult.Should().Be(UserMessageResult.No);
                var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
                buttons.Should().HaveCount(2);
                answered = true;
                beforeAnswer?.Invoke();
                switch (answer)
                {
                    case "Yes": buttons.Single(button => button.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
                    case "No": buttons.Single(button => button.IsCancel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
                    case "Escape": dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); break;
                    default: dialog.Close(); break;
                }
            }
            catch (Exception ex) { promptFailure = ex; }
            finally { dialog?.Close(); }
        }, DispatcherPriority.Background);
        try { invoke(); }
        finally { returned = true; }
        if (promptFailure is not null)
            ExceptionDispatchInfo.Capture(promptFailure).Throw();
        answered.Should().BeTrue("conversion must show the real owned confirmation before mutating the workbook");
        owner.OwnedWindows.Should().BeEmpty();
        owner.IsEnabled.Should().BeTrue();
    }

    private static async Task PressHistoryKey(MainWindow window, Key key)
    {
        window.SheetGridHostForTest.Focus();
        var args = new KeyEventArgs { Key = key, KeyModifiers = KeyModifiers.Control };
        await window.RaiseKeyDownForTest(args);
        args.Handled.Should().BeTrue();
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

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
