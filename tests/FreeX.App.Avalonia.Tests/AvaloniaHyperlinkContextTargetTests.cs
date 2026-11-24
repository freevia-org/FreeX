using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Ribbon;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaHyperlinkContextTargetTests
{
    private static readonly HeadlessUnitTestSession Headless =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    public static IEnumerable<object[]> LinkTypesAndAcceptance =>
        from type in Enum.GetValues<HyperlinkTargetKind>()
        from accept in new[] { false, true }
        select new object[] { type, accept };

    [Theory]
    [MemberData(nameof(LinkTypesAndAcceptance))]
    public Task EditMenu_PrefillsClickedLinkAndCommitsOnlyItsCapturedCell(HyperlinkTargetKind type, bool accept) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = At(sheet, 1, 1);
        var clicked = At(sheet, 2, 2);
        SeedLink(sheet, anchor, "Anchor", "https://example.test/anchor");
        var original = InitialTarget(sheet, type);
        SeedLink(sheet, clicked, "Clicked", original, type);
        var originalMetadata = sheet.HyperlinkMetadata[clicked];
        var selection = SelectAreas(window, anchor);
        await ClickMenu(window, "Hyperlink", clicked);
        var dialog = Dialog(window);
        Find<ListBox>(dialog, "HyperlinkLinkTypeBox").SelectedIndex.Should().Be(type switch
        {
            HyperlinkTargetKind.PlaceInThisDocument => 1,
            HyperlinkTargetKind.CreateNewDocument => 2,
            HyperlinkTargetKind.EmailAddress => 3,
            _ => 0
        });
        Find<TextBox>(dialog, "HyperlinkDisplayTextBox").Text.Should().Be("Clicked");
        await EditSubPrompt(dialog, "HyperlinkScreenTipButton", "Clicked tip", "Changed tip");
        if (type == HyperlinkTargetKind.ExistingFileOrWebPage)
            await EditSubPrompt(dialog, "HyperlinkBookmarkButton", "Clicked bookmark", "Changed bookmark");
        Find<TextBox>(dialog, "HyperlinkTargetTextBox").Text.Should().Be(type switch
        {
            HyperlinkTargetKind.PlaceInThisDocument => "B8",
            HyperlinkTargetKind.EmailAddress => "clicked@example.test",
            HyperlinkTargetKind.CreateNewDocument => "clicked-link.fxl",
            _ => original
        });
        if (type == HyperlinkTargetKind.EmailAddress)
            Find<TextBox>(dialog, "HyperlinkEmailSubjectTextBox").Text.Should().Be("Clicked subject");
        Find<TextBox>(dialog, "HyperlinkDisplayTextBox").Text = "Changed clicked";
        Find<TextBox>(dialog, "HyperlinkTargetTextBox").Text = type switch
        {
            HyperlinkTargetKind.PlaceInThisDocument => "C9",
            HyperlinkTargetKind.EmailAddress => "changed@example.test",
            HyperlinkTargetKind.CreateNewDocument => "changed-link.fxl",
            _ => "https://example.test/changed"
        };
        if (type == HyperlinkTargetKind.EmailAddress)
            Find<TextBox>(dialog, "HyperlinkEmailSubjectTextBox").Text = "Changed subject";
        // Selection can change programmatically while a native modal owner is disabled.
        window.Session.SelectCell(At(sheet, 3, 3));
        selection = Capture(window);
        await Finish(dialog, accept);

        sheet.Hyperlinks[clicked].Should().Be(accept ? ChangedTarget(sheet, type) : original);
        sheet.GetValue(clicked).Should().Be(new TextValue(accept ? "Changed clicked" : "Clicked"));
        sheet.HyperlinkMetadata[clicked].Should().Be(accept
            ? new HyperlinkMetadata(type, "Changed tip", type == HyperlinkTargetKind.ExistingFileOrWebPage ? "Changed bookmark" : "Clicked bookmark")
            : originalMetadata);
        sheet.Hyperlinks[anchor].Should().Be("https://example.test/anchor");
        sheet.GetValue(anchor).Should().Be(new TextValue("Anchor"));
        sheet.Hyperlinks.Should().NotContainKey(At(sheet, 3, 3));
        AssertSelection(window, selection);
        window.Session.CanUndo.Should().Be(accept);
        if (accept)
        {
            window.Session.UndoLastEdit().Success.Should().BeTrue();
            sheet.Hyperlinks[clicked].Should().Be(original);
            sheet.GetValue(clicked).Should().Be(new TextValue("Clicked"));
            sheet.HyperlinkMetadata[clicked].Should().Be(originalMetadata);
            window.Session.RedoLastEdit().Success.Should().BeTrue();
            sheet.Hyperlinks[clicked].Should().Be(ChangedTarget(sheet, type));
        }
    });

    [Fact]
    public Task InsertMenu_UsesClickedUnlinkedCellRatherThanLinkedSelectionAnchor() => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = At(sheet, 1, 1);
        var clicked = At(sheet, 2, 2);
        SeedLink(sheet, anchor, "Anchor", "https://example.test/anchor");
        sheet.SetCell(clicked, new TextValue("Unlinked clicked"));
        var selection = SelectAreas(window, anchor);
        await ClickMenu(window, "Hyperlink", clicked);
        var dialog = Dialog(window);
        Find<TextBox>(dialog, "HyperlinkDisplayTextBox").Text.Should().Be("Unlinked clicked");
        Find<TextBox>(dialog, "HyperlinkTargetTextBox").Text.Should().BeEmpty();
        Find<TextBox>(dialog, "HyperlinkTargetTextBox").Text = "https://example.test/new";
        await Finish(dialog, true);
        sheet.Hyperlinks[clicked].Should().Be("https://example.test/new");
        sheet.Hyperlinks[anchor].Should().Be("https://example.test/anchor");
        AssertSelection(window, selection);
    });

    [Fact]
    public Task GroupedEdit_UpdatesCapturedCoordinatesOnEveryGroupedSheetAsOneHistoryEntry() => Run(async window =>
    {
        var first = window.Session.ActiveSheet;
        var second = window.Session.Workbook.AddSheet("Grouped detail");
        window.Session.SelectAllVisibleSheets();
        window.Session.SelectSheet(first.Id);
        window.Session.SelectAllVisibleSheets();
        foreach (var sheet in new[] { first, second })
        {
            SeedLink(sheet, At(sheet, 1, 1), "Anchor", "https://example.test/anchor");
            SeedLink(sheet, At(sheet, 2, 2), "Clicked", "https://example.test/old");
        }
        var selection = SelectAreas(window, At(first, 1, 1));
        await ClickMenu(window, "Hyperlink", At(first, 2, 2));
        var dialog = Dialog(window);
        Find<TextBox>(dialog, "HyperlinkTargetTextBox").Text = "https://example.test/grouped";
        await Finish(dialog, true);
        foreach (var sheet in new[] { first, second })
        {
            sheet.Hyperlinks[At(sheet, 2, 2)].Should().Be("https://example.test/grouped");
            sheet.Hyperlinks[At(sheet, 1, 1)].Should().Be("https://example.test/anchor");
        }
        AssertSelection(window, selection);
        window.Session.GetUndoHistory(10).Should().ContainSingle();
        window.Session.UndoLastEdit().Success.Should().BeTrue();
        foreach (var sheet in new[] { first, second }) sheet.Hyperlinks[At(sheet, 2, 2)].Should().Be("https://example.test/old");
    });

    [Fact]
    public Task GroupedEdit_ProtectedSiblingRollsBackEveryTarget() => Run(async window =>
    {
        var first = window.Session.ActiveSheet;
        var second = window.Session.Workbook.AddSheet("Protected grouped detail");
        window.Session.SelectAllVisibleSheets();
        foreach (var sheet in new[] { first, second })
            SeedLink(sheet, At(sheet, 2, 2), "Clicked", "https://example.test/old");
        SelectAreas(window, At(first, 1, 1));
        await ClickMenu(window, "Hyperlink", At(first, 2, 2));
        var dialog = Dialog(window);
        Find<TextBox>(dialog, "HyperlinkTargetTextBox").Text = "https://example.test/blocked-group";
        second.IsProtected = true;
        second.ProtectionPermissions.Remove(SheetProtectionPermission.InsertHyperlinks);
        await Finish(dialog, true);
        foreach (var sheet in new[] { first, second }) sheet.Hyperlinks[At(sheet, 2, 2)].Should().Be("https://example.test/old");
        window.Session.CanUndo.Should().BeFalse();
    });

    [Theory]
    [InlineData("session")]
    [InlineData("sheet")]
    [InlineData("group")]
    [InlineData("target-sheet-removed")]
    [InlineData("_isOpening")]
    [InlineData("_isSaving")]
    public Task AcceptedDialog_RefusesAnInvalidatedTarget(string invalidation) => Run(async window =>
    {
        var session = window.Session;
        var original = session.ActiveSheet;
        var clicked = At(original, 2, 2);
        SeedLink(original, clicked, "Clicked", "https://example.test/old");
        var other = session.Workbook.AddSheet("Other target");
        SeedLink(other, At(other, 2, 2), "Other", "https://example.test/other");
        SelectAreas(window, At(original, 1, 1));
        await ClickMenu(window, "Hyperlink", clicked);
        var dialog = Dialog(window);
        Find<TextBox>(dialog, "HyperlinkTargetTextBox").Text = "https://example.test/invalidated";
        FieldInfo? busyField = null;
        switch (invalidation)
        {
            case "session": window.ReplaceSession(new WorkbookSessionFactory().CreateNew(600, 800, includeObjects: true)); break;
            case "sheet": session.SelectSheet(other.Id); break;
            case "group": session.SelectAllVisibleSheets(); break;
            case "target-sheet-removed":
                session.Workbook.RemoveSheet(original.Id);
                session.SelectSheet(other.Id);
                break;
            default:
                busyField = typeof(MainWindow).GetField(invalidation, BindingFlags.NonPublic | BindingFlags.Instance)!;
                busyField.SetValue(window, true);
                break;
        }
        try { await Finish(dialog, true); }
        finally { busyField?.SetValue(window, false); }
        original.Hyperlinks[clicked].Should().Be("https://example.test/old");
        other.Hyperlinks[At(other, 2, 2)].Should().Be("https://example.test/other");
        window.Session.CanUndo.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Acceptance_RechecksInsertHyperlinksProtectionAfterOpening(bool allowed) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var clicked = At(sheet, 2, 2);
        SeedLink(sheet, clicked, "Clicked", "https://example.test/old");
        SelectAreas(window, At(sheet, 1, 1));
        await ClickMenu(window, "Hyperlink", clicked);
        var dialog = Dialog(window);
        Find<TextBox>(dialog, "HyperlinkTargetTextBox").Text = "https://example.test/protected";
        sheet.IsProtected = true;
        if (allowed) sheet.ProtectionPermissions.Add(SheetProtectionPermission.InsertHyperlinks);
        else sheet.ProtectionPermissions.Remove(SheetProtectionPermission.InsertHyperlinks);
        await Finish(dialog, true);
        sheet.Hyperlinks[clicked].Should().Be(allowed ? "https://example.test/protected" : "https://example.test/old");
        window.Session.CanUndo.Should().Be(allowed);
    });

    [Fact]
    public Task EditHistory_RestoresOriginalRichTextPhoneticGuideAndMetadata() => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var clicked = At(sheet, 2, 2);
        SeedLink(sheet, clicked, "Clicked", "https://example.test/old");
        var runs = new[] { new CellTextRun("Clicked", true, null, null, null, null, null, null) };
        var guide = new CellPhoneticGuide(["<rPh sb='0' eb='7'><t>guide</t></rPh>"], null);
        sheet.RichTextRuns[clicked] = runs;
        sheet.CellPhoneticGuides[clicked] = guide;
        var metadata = sheet.HyperlinkMetadata[clicked];
        SelectAreas(window, At(sheet, 1, 1));
        await ClickMenu(window, "Hyperlink", clicked);
        var dialog = Dialog(window);
        Find<TextBox>(dialog, "HyperlinkDisplayTextBox").Text = "Replacement";
        await Finish(dialog, true);
        sheet.RichTextRuns.Should().NotContainKey(clicked);
        sheet.CellPhoneticGuides.Should().NotContainKey(clicked);
        window.Session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetValue(clicked).Should().Be(new TextValue("Clicked"));
        sheet.HyperlinkMetadata[clicked].Should().Be(metadata);
        sheet.RichTextRuns[clicked].Should().Equal(runs);
        sheet.CellPhoneticGuides[clicked].Should().Be(guide);
        window.Session.RedoLastEdit().Success.Should().BeTrue();
        sheet.GetValue(clicked).Should().Be(new TextValue("Replacement"));
        sheet.RichTextRuns.Should().NotContainKey(clicked);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task RibbonAndCommandK_KeepPrimaryAnchorAndWpfSelectionAftermath(bool keyboard, bool moveSelection) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = At(sheet, 1, 1);
        var active = At(sheet, 2, 2);
        SeedLink(sheet, anchor, "Anchor", "https://example.test/anchor");
        SeedLink(sheet, active, "Active", "https://example.test/active");
        var selection = SelectAreas(window, active);
        Task? keyTask = null;
        if (keyboard)
            keyTask = window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.K, PhysicalKey = PhysicalKey.K, KeyModifiers = KeyModifiers.Meta });
        else
        {
            window.RibbonCommandRegistryForTest!.TryGet(new RibbonCommandId("Insert Link"), out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
        }
        await Drain();
        var dialog = Dialog(window);
        Find<TextBox>(dialog, "HyperlinkDisplayTextBox").Text.Should().Be("Anchor");
        Find<TextBox>(dialog, "HyperlinkTargetTextBox").Text = "https://example.test/anchor-edited";
        if (moveSelection)
        {
            window.Session.SelectCell(At(sheet, 3, 3));
            selection = Capture(window);
        }
        await Finish(dialog, true);
        if (keyTask is not null) await keyTask;
        sheet.Hyperlinks[anchor].Should().Be("https://example.test/anchor-edited");
        sheet.Hyperlinks[active].Should().Be("https://example.test/active");
        sheet.Hyperlinks.Should().NotContainKey(At(sheet, 3, 3));
        // WPF's InsertLink -> TryExecuteRepeatableCommand synchronizes all selected areas and
        // preserves its selection anchor through the shared selection-preserving session pipeline.
        AssertSelection(window, selection);
        window.Session.CanRepeatLastAction.Should().BeTrue("WPF Insert Link registers the accepted link as Repeat Last");
    });

    [Fact]
    public Task PhysicalF4_RebuildsCurrentAnchorAndGroupedTargetsWithAcceptedLinkAndUndoRedo() => Run(async window =>
    {
        var first = window.Session.ActiveSheet;
        var second = window.Session.Workbook.AddSheet("Repeat grouped detail");
        var clicked = At(first, 2, 2);
        SeedLink(first, At(first, 1, 1), "Anchor", "https://example.test/anchor");
        SeedLink(first, clicked, "Clicked", "https://example.test/old");
        foreach (var sheet in new[] { first, second })
            SeedLink(sheet, At(sheet, 3, 3), "Next", "https://example.test/next");
        SelectAreas(window, At(first, 1, 1));
        await ClickMenu(window, "Hyperlink", clicked);
        var dialog = Dialog(window);
        Find<TextBox>(dialog, "HyperlinkTargetTextBox").Text = "https://example.test/accepted";
        Find<TextBox>(dialog, "HyperlinkDisplayTextBox").Text = "Accepted text";
        await Finish(dialog, true);
        window.Session.CanRepeatLastAction.Should().BeTrue();
        second.Hyperlinks.Should().NotContainKey(At(second, 2, 2), "the sibling was not grouped for the initial command");

        window.Session.SelectAllVisibleSheets();
        var range = new GridRange(At(first, 3, 3), At(first, 4, 4));
        var extra = new GridRange(At(first, 10, 7), At(first, 11, 8));
        window.Session.SelectRanges(range, [range, extra], At(first, 4, 4));
        var selection = Capture(window);
        await PressF4(window);

        foreach (var sheet in new[] { first, second })
        {
            var next = At(sheet, 3, 3);
            sheet.Hyperlinks[next].Should().Be("https://example.test/accepted");
            sheet.GetValue(next).Should().Be(new TextValue("Accepted text"));
            sheet.HyperlinkMetadata[next].Should().Be(new HyperlinkMetadata(
                HyperlinkTargetKind.ExistingFileOrWebPage, "Clicked tip", "Clicked bookmark"));
            sheet.Hyperlinks.Should().NotContainKey(At(sheet, 4, 4), "repeat uses the primary anchor, not the active cell");
        }
        first.Hyperlinks[clicked].Should().Be("https://example.test/accepted");
        second.Hyperlinks.Should().NotContainKey(At(second, 2, 2), "repeat must not reuse the original clicked coordinate");
        window.OwnedWindows.Should().BeEmpty("repeat uses the accepted plan without reopening its dialog");
        window.Session.GetUndoHistory(10).Should().HaveCount(2);
        AssertSelection(window, selection);
        window.Session.UndoLastEdit().Success.Should().BeTrue();
        foreach (var sheet in new[] { first, second }) sheet.Hyperlinks[At(sheet, 3, 3)].Should().Be("https://example.test/next");
        first.Hyperlinks[clicked].Should().Be("https://example.test/accepted");
        window.Session.RedoLastEdit().Success.Should().BeTrue();
        foreach (var sheet in new[] { first, second }) sheet.Hyperlinks[At(sheet, 3, 3)].Should().Be("https://example.test/accepted");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PhysicalF4_RechecksInsertHyperlinksProtectionOnItsNewTarget(bool allowed) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var clicked = At(sheet, 2, 2);
        var next = At(sheet, 3, 3);
        SeedLink(sheet, clicked, "Clicked", "https://example.test/old");
        SeedLink(sheet, next, "Next", "https://example.test/next");
        SelectAreas(window, At(sheet, 1, 1));
        await ClickMenu(window, "Hyperlink", clicked);
        var dialog = Dialog(window);
        Find<TextBox>(dialog, "HyperlinkTargetTextBox").Text = "https://example.test/accepted";
        await Finish(dialog, true);
        window.Session.SelectCell(next);
        sheet.IsProtected = true;
        if (allowed) sheet.ProtectionPermissions.Add(SheetProtectionPermission.InsertHyperlinks);
        else sheet.ProtectionPermissions.Remove(SheetProtectionPermission.InsertHyperlinks);
        await PressF4(window);
        sheet.Hyperlinks[clicked].Should().Be("https://example.test/accepted");
        sheet.Hyperlinks[next].Should().Be(allowed ? "https://example.test/accepted" : "https://example.test/next");
        window.Session.GetUndoHistory(10).Should().HaveCount(allowed ? 2 : 1);
        if (allowed)
        {
            window.Session.UndoLastEdit().Success.Should().BeTrue();
            sheet.Hyperlinks[next].Should().Be("https://example.test/next");
            window.Session.RedoLastEdit().Success.Should().BeTrue();
            sheet.Hyperlinks[next].Should().Be("https://example.test/accepted");
        }
    });

    [Theory]
    [InlineData("RemoveHyperlinks", false)]
    [InlineData("ClearHyperlinks", true)]
    public Task RemovalMenus_StillCoverAllSelectedAreasAndKeepTheirDistinctFormatting(string action, bool stripsFormatting) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var first = At(sheet, 1, 1);
        var clicked = At(sheet, 2, 2);
        var disjoint = At(sheet, 10, 7);
        var outside = At(sheet, 20, 10);
        var style = new CellStyle { Underline = true, FontColor = CellColor.FromArgb(0, 0, 255) };
        var styleId = window.Session.Workbook.RegisterStyle(style);
        foreach (var address in new[] { first, clicked, disjoint, outside })
        {
            SeedLink(sheet, address, "Link", "https://example.test/link");
            sheet.GetCell(address)!.StyleId = styleId;
        }
        var selection = SelectAreas(window, first);
        await ClickMenu(window, action, clicked);
        foreach (var address in new[] { first, clicked, disjoint })
        {
            sheet.Hyperlinks.Should().NotContainKey(address);
            var resultStyle = window.Session.Workbook.GetStyle(sheet.GetCell(address)!.StyleId);
            resultStyle.Underline.Should().Be(!stripsFormatting);
            sheet.GetValue(address).Should().Be(new TextValue("Link"));
        }
        sheet.Hyperlinks.Should().ContainKey(outside);
        AssertSelection(window, selection);
        window.Session.UndoLastEdit().Success.Should().BeTrue();
        foreach (var address in new[] { first, clicked, disjoint }) sheet.Hyperlinks.Should().ContainKey(address);
    });

    private static string InitialTarget(Sheet sheet, HyperlinkTargetKind type) => type switch
    {
        HyperlinkTargetKind.PlaceInThisDocument => HyperlinkDialogPanePlanner.ComposeDocumentLocation(new(sheet.Name, false), "B8"),
        HyperlinkTargetKind.EmailAddress => "mailto:clicked@example.test?subject=Clicked%20subject",
        HyperlinkTargetKind.CreateNewDocument => Path.Combine(Path.GetTempPath(), "clicked-link.fxl"),
        _ => "https://example.test/clicked"
    };
    private static string ChangedTarget(Sheet sheet, HyperlinkTargetKind type) => type switch
    {
        HyperlinkTargetKind.PlaceInThisDocument => HyperlinkDialogPanePlanner.ComposeDocumentLocation(new(sheet.Name, false), "C9"),
        HyperlinkTargetKind.EmailAddress => "mailto:changed@example.test?subject=Changed%20subject",
        HyperlinkTargetKind.CreateNewDocument => Path.Combine(Path.GetTempPath(), "changed-link.fxl"),
        _ => "https://example.test/changed"
    };
    private static void SeedLink(Sheet sheet, CellAddress address, string text, string target, HyperlinkTargetKind type = HyperlinkTargetKind.ExistingFileOrWebPage)
    {
        sheet.SetCell(address, new TextValue(text));
        sheet.Hyperlinks[address] = target;
        sheet.HyperlinkMetadata[address] = new(type, "Clicked tip", "Clicked bookmark");
    }
    private static CellAddress At(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);
    private static Window Dialog(MainWindow window) => window.OwnedWindows.OfType<Window>()
        .Single(dialog => AutomationProperties.GetAutomationId(dialog) == "HyperlinkCompactDialog");
    private static T Find<T>(Control parent, string id) where T : Control => parent.GetVisualDescendants().OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static async Task Drain() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    private static async Task PressF4(MainWindow window)
    {
        window.SheetGridHostForTest.Focus();
        var key = new KeyEventArgs { Key = Key.F4, PhysicalKey = PhysicalKey.F4 };
        await window.RaiseKeyDownForTest(key);
        key.Handled.Should().BeTrue();
        await Drain();
    }

    private static async Task EditSubPrompt(Window owner, string buttonId, string expected, string replacement)
    {
        Find<Button>(owner, buttonId).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Drain();
        var prompt = owner.OwnedWindows.OfType<Window>().Single();
        var box = prompt.GetVisualDescendants().OfType<TextBox>().Single();
        box.Text.Should().Be(expected);
        box.Text = replacement;
        prompt.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, UiText.Get("Common_Ok")))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Drain();
    }

    private static async Task Finish(Window dialog, bool accept)
    {
        Find<Button>(dialog, accept ? "HyperlinkOkButton" : "HyperlinkCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Drain();
        await Drain();
    }
    private static async Task ClickMenu(MainWindow window, string action, CellAddress clicked)
    {
        var menu = (ContextMenu)typeof(MainWindow).GetMethod("BuildWorksheetCellContextMenuFor", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [clicked])!;
        try
        {
            menu.Open(window.SheetGridHostForTest);
            var item = Items(menu.Items).Single(item => item.Tag as string == action);
            item.IsEnabled.Should().BeTrue();
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        finally { menu.Close(); }
        await Drain();
    }
    private static IEnumerable<MenuItem> Items(IEnumerable<object?> items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in Items(item.Items)) yield return child;
        }
    }
    private static (GridRange Range, GridRange[] Areas, CellAddress Active) SelectAreas(MainWindow window, CellAddress active)
    {
        var sheet = window.Session.ActiveSheet;
        var primary = new GridRange(At(sheet, 1, 1), At(sheet, 8, 5));
        var extra = new GridRange(At(sheet, 10, 7), At(sheet, 11, 8));
        window.Session.SelectRanges(primary, [primary, extra], active);
        return Capture(window);
    }
    private static (GridRange Range, GridRange[] Areas, CellAddress Active) Capture(MainWindow window) =>
        (window.Session.SelectedRange, window.Session.SelectedRanges.ToArray(), window.Session.ActiveCell);
    private static void AssertSelection(MainWindow window, (GridRange Range, GridRange[] Areas, CellAddress Active) expected)
    {
        window.Session.SelectedRange.Should().Be(expected.Range);
        window.Session.SelectedRanges.Should().Equal(expected.Areas);
        window.Session.ActiveCell.Should().Be(expected.Active);
    }
    private static Task Run(Func<MainWindow, Task> test) => Headless.Dispatch(async () =>
    {
        var window = new MainWindow([], null!, new FreeXOptionsRuntimeSession(load: static () => new AppOptions(), save: static _ => true));
        try
        {
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            await test(window);
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
        return true;
    }, CancellationToken.None);
}
