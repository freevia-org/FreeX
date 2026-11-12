using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless guards for the two WPF worksheet context-menu affordances the Avalonia shell mirrors:
/// the search row above the commands and the floating mini toolbar. The WPF host
/// (<c>WorksheetContextMenuRenderer.AddSearchBox</c>, <c>MainWindow.WorksheetContextMiniToolbar.cs</c>)
/// is the authority for the row order, sizes and control order asserted here.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaWorksheetContextMenuSearchAndMiniToolbarTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private static RibbonMenu SampleMenu() => new(new[]
    {
        new RibbonMenuItem("Cu_t", new RibbonCommandId("Cut")),
        new RibbonMenuItem("_Copy", new RibbonCommandId("Copy")),
        RibbonMenuItem.Separator(),
        new RibbonMenuItem(
            "_Clear",
            CommandId: null,
            Children: new[]
            {
                new RibbonMenuItem("Clear C_ontents", new RibbonCommandId("ClearContents")),
                new RibbonMenuItem("Clear _Hyperlinks", new RibbonCommandId("ClearHyperlinks")),
            }),
    });

    [Fact]
    public Task AddSearchBox_InsertsTaggedSearchRowAndSeparatorAboveTheCommands() => RunOnUiThread(() =>
    {
        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(SampleMenu(), _ => { });

        var searchBox = AvaloniaWorksheetContextMenuSearch.AddSearchBox(menu);

        var items = menu.Items.Cast<Control>().ToList();
        items.Should().HaveCount(6);
        var searchItem = items[0].Should().BeOfType<MenuItem>().Subject;
        AvaloniaWorksheetContextMenuSearch.IsSearchMenuItem(searchItem).Should().BeTrue();
        searchItem.StaysOpenOnClick.Should().BeTrue();
        searchItem.Focusable.Should().BeFalse();
        searchItem.Padding.Should().Be(AvaloniaWorksheetContextMenuSearch.SearchItemPadding);
        var header = searchItem.Header.Should().BeOfType<Grid>().Subject;
        header.Width.Should().Be(180);
        header.Children.Should().ContainSingle().Which.Should().BeSameAs(searchBox);
        items[1].Should().BeOfType<Separator>();
        AvaloniaWorksheetContextMenuSearch.IsSearchMenuItem(items[1]).Should().BeTrue();

        // The commands keep their order behind the two leading rows.
        ((MenuItem)items[2]).Header.Should().Be("Cu_t");
        ((MenuItem)items[3]).Header.Should().Be("_Copy");
        items[4].Should().BeOfType<Separator>();
        ((MenuItem)items[5]).Header.Should().Be("_Clear");

        AutomationProperties.GetName(searchBox).Should().NotBeNullOrWhiteSpace();
        searchBox.PlaceholderText.Should().Be(AutomationProperties.GetName(searchBox));
        AvaloniaWorksheetContextMenuSearch.FindSearchBox(menu).Should().BeSameAs(searchBox);
    });

    [Fact]
    public Task SearchText_FiltersCommandsByCaptionAndRestoresThemWhenCleared() => RunOnUiThread(() =>
    {
        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(SampleMenu(), _ => { });
        var searchBox = AvaloniaWorksheetContextMenuSearch.AddSearchBox(menu);
        var items = menu.Items.Cast<Control>().ToList();

        searchBox.Text = "cop";

        items[0].IsVisible.Should().BeTrue("the search row is never filtered");
        items[1].IsVisible.Should().BeTrue("the search separator is never filtered");
        items[2].IsVisible.Should().BeFalse();
        items[3].IsVisible.Should().BeTrue();
        items[3].Focusable.Should().BeTrue();
        items[2].Focusable.Should().BeFalse("a filtered-out row is not an arrow-key stop");
        items[4].IsVisible.Should().BeFalse("a separator needs a visible command on both sides");
        items[5].IsVisible.Should().BeFalse();

        searchBox.Text = string.Empty;

        items.Should().OnlyContain(item => item.IsVisible);
        items.Skip(2).OfType<MenuItem>().Should().OnlyContain(item => item.Focusable);
    });

    [Fact]
    public Task SearchText_KeepsAParentWhoseChildMatches() => RunOnUiThread(() =>
    {
        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(SampleMenu(), _ => { });
        AvaloniaWorksheetContextMenuSearch.AddSearchBox(menu);
        var items = menu.Items.Cast<Control>().ToList();

        // The access-key marker is not part of the caption that is searched.
        AvaloniaWorksheetContextMenuSearch.ApplySearchFilter(menu, "Hyperlinks");

        items[2].IsVisible.Should().BeFalse();
        items[3].IsVisible.Should().BeFalse();
        items[4].IsVisible.Should().BeFalse();
        items[5].IsVisible.Should().BeTrue();
    });

    [Fact]
    public Task WorksheetCellMenu_StartsWithTheSearchRowAndStillDispatchesCommands() => Session.Dispatch(() =>
    {
        var window = new MainWindow([]);
        try
        {
            var menu = BuildWorksheetCellMenu(window);
            var items = menu.Items.Cast<Control>().ToList();

            AvaloniaWorksheetContextMenuSearch.IsSearchMenuItem(items[0]).Should().BeTrue();
            items[0].Should().BeOfType<MenuItem>();
            items[1].Should().BeOfType<Separator>();
            // Cut is the first command of the shared worksheet plan.
            items[2].Should().BeOfType<MenuItem>()
                .Which.Tag.Should().Be(WorksheetContextMenuAction.Cut.ToString());
            // The search row takes no key letter.
            ((MenuItem)items[0]).InputGesture.Should().BeNull();

            var searchBox = AvaloniaWorksheetContextMenuSearch.FindSearchBox(menu);
            searchBox.Should().NotBeNull();
            searchBox!.Text = "zzzz-no-such-command";
            items.Skip(2).Should().OnlyContain(item => !item.IsVisible);
            searchBox.Text = null;
            items.Should().OnlyContain(item => item.IsVisible);
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public Task MiniToolbar_HasTheWpfControlsInOrderWithTheWpfSizes() => Session.Dispatch(() =>
    {
        var window = new MainWindow([]);
        try
        {
            var toolbar = window.CreateWorksheetContextMiniToolbar();

            AutomationProperties.GetAutomationId(toolbar).Should().Be("WorksheetContextMiniToolbar");
            AutomationProperties.GetName(toolbar).Should().NotBeNullOrWhiteSpace();
            toolbar.Padding.Should().Be(new global::Avalonia.Thickness(4));
            toolbar.CornerRadius.Should().Be(new global::Avalonia.CornerRadius(5));

            var controls = toolbar.Child.Should().BeOfType<StackPanel>().Subject.Children.ToList();
            controls.Should().HaveCount(6);

            var fontName = controls[0].Should().BeOfType<ComboBox>().Subject;
            fontName.IsEditable.Should().BeTrue();
            fontName.Width.Should().Be(76);
            fontName.Height.Should().Be(26);
            fontName.Text.Should().NotBeNullOrWhiteSpace();

            var fontSize = controls[1].Should().BeOfType<ComboBox>().Subject;
            fontSize.IsEditable.Should().BeTrue();
            fontSize.Width.Should().Be(42);
            fontSize.Height.Should().Be(26);
            fontSize.ItemsSource!.Cast<string>().Should().Equal("8", "9", "10", "11", "12", "14", "16", "18");

            foreach (var control in controls.Skip(2))
            {
                var button = control.Should().BeOfType<Button>().Subject;
                button.Width.Should().Be(27);
                button.Height.Should().Be(26);
                AutomationProperties.GetName(button).Should().NotBeNullOrWhiteSpace();
            }

            // Bold, italic and center run commands; the fill button opens the colour palette.
            ((Button)controls[2]).Flyout.Should().BeNull();
            ((Button)controls[5]).Flyout.Should().NotBeNull();
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public Task MiniToolbar_ControlsApplyTheRibbonCommandsToTheSelection() => Session.Dispatch(() =>
    {
        var window = new MainWindow([]);
        try
        {
            var sheet = window.Session.ActiveSheet;
            window.Session.SelectCell(new CellAddress(sheet.Id, 2, 2));
            var controls = ((StackPanel)window.CreateWorksheetContextMiniToolbar().Child!).Children.ToList();

            Click((Button)controls[2]);
            window.Session.IsSelectedRangeStartBold.Should().BeTrue();

            Click((Button)controls[3]);
            window.Session.IsSelectedRangeStartItalic.Should().BeTrue();

            Click((Button)controls[4]);
            window.Session.SelectedRangeStartHorizontalAlignment.Should().Be(CellHAlign.Center);

            ((ComboBox)controls[1]).SelectedItem = "14";
            window.Session.SelectedRangeStartStyle.FontSize.Should().Be(14);

            ((ComboBox)controls[0]).SelectedItem = "Arial";
            window.Session.SelectedRangeStartStyle.FontName.Should().Be("Arial");
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public void MiniToolbar_IsForThePlainCellMenuOnly()
    {
        var sheetId = SheetId.New();
        var cell = new CellAddress(sheetId, 3, 3);

        MainWindow.ShouldShowWorksheetContextMiniToolbar(new GridRange(cell, cell)).Should().BeTrue();
        MainWindow.ShouldShowWorksheetContextMiniToolbar(new GridRange(
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 2, CellAddress.MaxCol))).Should().BeFalse();
        MainWindow.ShouldShowWorksheetContextMiniToolbar(new GridRange(
            new CellAddress(sheetId, 1, 2),
            new CellAddress(sheetId, CellAddress.MaxRow, 2))).Should().BeFalse();
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static ContextMenu BuildWorksheetCellMenu(MainWindow window) =>
        (ContextMenu)(typeof(MainWindow).GetMethod(
            "BuildWorksheetCellContextMenu",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "BuildWorksheetCellContextMenu"))
            .Invoke(window, null)!;
}
