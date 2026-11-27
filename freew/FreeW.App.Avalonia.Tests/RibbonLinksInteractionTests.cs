using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Options;
using Xunit.Abstractions;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Exercises actual shell controls rather than directly showing a flyout or calling a command.
/// Headless success establishes the framework route, not Cocoa popup visibility or native AX parity.
/// </summary>
public sealed class RibbonLinksInteractionTests(ITestOutputHelper output)
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData("invoke", false)]
    [InlineData("pointer", false)]
    [InlineData("enter", false)]
    [InlineData("invoke", true)]
    public Task ActualCollapsedLinksButtonOpensItsMenu(string activation, bool resizeRoundTrip) =>
        InShell(window =>
        {
            SelectInsertTab(window);
            var button = FindCollapsedLinksByResizing(window);
            if (resizeRoundTrip)
            {
                FindExpandedHyperlinkByResizing(window);
                button = FindCollapsedLinksByResizing(window);
            }
            TopLevel.GetTopLevel(button).Should().BeSameAs(window);
            button.IsEnabled.Should().BeTrue();
            var flyout = button.Flyout.Should().BeOfType<MenuFlyout>().Subject;
            flyout.IsOpen.Should().BeFalse();

            output.WriteLine($"Activating {activation}, width {window.Bounds.Width}, button {button.Bounds}.");
            switch (activation)
            {
                case "invoke":
                    new ButtonAutomationPeer(button).Invoke();
                    break;
                case "pointer":
                    Click(button);
                    break;
                case "enter":
                    button.Focus().Should().BeTrue();
                    Press(window, Key.Enter, PhysicalKey.Enter);
                    break;
            }
            Settle(window);

            flyout.IsOpen.Should().BeTrue("actual Button activation must open its assigned flyout");
            TopLevel.GetTopLevel(button).Should().BeSameAs(window, "layout must retain the live popup anchor");
            var hyperlink = flyout.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Tag, "freew.hyperlink"));
            hyperlink.IsEnabled.Should().BeTrue();
            window.OwnedWindows.Should().BeEmpty("opening the group is not invoking Hyperlink");
            window.Editor.Document.PlainText.Should().Be("a😀b");
            flyout.Hide();
        });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task KeyboardEmojiSelectionSurvivesRibbonFocusAndSeedsTheHyperlinkDialog(
        bool setFont24, bool collapsed) => InShell(window =>
    {
        var editor = window.Editor;
        editor.CreateAutomationPeerForTests();
        if (setFont24)
            SetFont24ThroughTheRibbon(window);
        editor.Focus().Should().BeTrue();
        Press(window, Key.Home, PhysicalKey.Home);
        Press(window, Key.Right, PhysicalKey.ArrowRight);
        Press(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        AssertEmojiSelection(editor, "after keyboard selection");
        Settle(window);
        AssertEmojiSelection(editor, "after dispatcher/render work");

        var documentChanges = 0;
        editor.DocumentChanged += () => documentChanges++;
        SelectInsertTab(window);
        AssertEmojiSelection(editor, "after selecting Insert");
        if (collapsed)
        {
            var button = FindCollapsedLinksByResizing(window,
                () => AssertEmojiSelection(editor, "after each collapsed adaptive resize"));
            AssertEmojiSelection(editor, "after adaptive resize");
            // Focus the real group before Invoke so this includes the editor's focus-loss path.
            button.Focus().Should().BeTrue();
            AssertEmojiSelection(editor, "after group focus");
            new ButtonAutomationPeer(button).Invoke();
            Settle(window);
            var flyout = button.Flyout.Should().BeOfType<MenuFlyout>().Subject;
            flyout.IsOpen.Should().BeTrue();
            AssertEmojiSelection(editor, "after group activation");
            var hyperlink = flyout.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Tag, "freew.hyperlink"));
            Click(hyperlink);
        }
        else
        {
            var hyperlink = FindExpandedHyperlinkByResizing(window,
                () => AssertEmojiSelection(editor, "after each expanded adaptive resize"));
            AssertEmojiSelection(editor, "after expanded adaptive resize");
            hyperlink.Focus().Should().BeTrue();
            AssertEmojiSelection(editor, "after Hyperlink focus");
            new ButtonAutomationPeer(hyperlink).Invoke();
        }
        Settle(window);

        var dialog = window.OwnedWindows.OfType<HyperlinkDialog>().Should().ContainSingle().Which;
        dialog.UpdateLayout();
        var display = dialog.GetVisualDescendants().OfType<TextBox>()
            .Single(box => box.Parent is Grid && Grid.GetRow(box) == 0 && Grid.GetColumn(box) == 1);
        output.WriteLine($"Dialog display={display.Text}; editor selection={editor.SelectedText}.");
        display.Text.Should().Be("😀", "the dialog must receive the selected text, not the full paragraph");
        AssertEmojiSelection(editor, "after dialog opens");
        documentChanges.Should().Be(0, "navigation, focus and dialog opening must not reapply formatting or edit text");
        editor.Document.PlainText.Should().Be("a😀b");
        dialog.Close();
    });

    private Task InShell(Action<MainWindow> action) => Session.Dispatch(() =>
    {
        var window = new MainWindow([], new FreeWOptions(), new InMemoryApplicationOptionsStore<FreeWOptions>(),
            promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.DontSave),
            suppressStartupRecoveryOffer: true) { Width = 1500, Height = 900 };
        try
        {
            window.Show();
            window.Editor.InsertText("a😀b");
            window.KeyTextInput(string.Empty); // settle initial adaptive layout before locating controls
            WriteLoadedAssembly(typeof(MainWindow).Assembly);
            WriteLoadedAssembly(AppDomain.CurrentDomain.GetAssemblies()
                .Single(assembly => assembly.GetName().Name == "Free.Shared.Ribbon.Avalonia"));
            action(window);
        }
        finally
        {
            foreach (var child in window.OwnedWindows.ToArray())
                child.Close();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }, CancellationToken.None);

    private void WriteLoadedAssembly(System.Reflection.Assembly assembly)
    {
        var version = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly.Location)));
        output.WriteLine($"Loaded {assembly.GetName().Name}: {assembly.Location}; informational={version}; SHA256={hash}.");
    }

    private static void SelectInsertTab(MainWindow window)
    {
        var tabs = window.RibbonControlForTest.Should().BeOfType<TabControl>().Subject;
        var insert = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Tag, "insert"));
        Click(insert);
        Settle(window);
        tabs.SelectedItem.Should().BeSameAs(insert);
    }

    private Button FindCollapsedLinksByResizing(MainWindow window, Action? afterResize = null)
    {
        // Real Cocoa observed this at 1040; use the actual adaptive state rather than assuming
        // headless font metrics produce an identical threshold or forcing a private layout flag.
        foreach (var width in new[] { 1040, 960, 880, 800, 720, 640 })
        {
            window.Width = width;
            Settle(window);
            afterResize?.Invoke();
            var button = window.GetVisualDescendants().OfType<Button>()
                .SingleOrDefault(control => Equals(control.Tag, "collapsed:links"));
            if (button is not null)
            {
                output.WriteLine($"Links collapsed at client width {window.ClientSize.Width}.");
                return button;
            }
        }
        throw new InvalidOperationException("The real Insert Links group never reached a collapsed state.");
    }

    private Button FindExpandedHyperlinkByResizing(MainWindow window, Action? afterResize = null)
    {
        // Assert the real adaptive state; the native expansion observed at 1920 is not
        // a universal threshold for different headless fonts or display scaling.
        foreach (var width in new[] { 1500, 1680, 1920, 2240, 2560 })
        {
            window.Width = width;
            Settle(window);
            afterResize?.Invoke();
            var button = window.GetVisualDescendants().OfType<Button>()
                .SingleOrDefault(control => Equals(control.Tag, "freew.hyperlink"));
            if (button is null)
                continue;
            TopLevel.GetTopLevel(button).Should().BeSameAs(window);
            window.GetVisualDescendants().OfType<Button>()
                .Should().NotContain(control => Equals(control.Tag, "collapsed:links"),
                    "the attached Hyperlink button must belong to the expanded Links group");
            output.WriteLine($"Links expanded at client width {window.ClientSize.Width}.");
            return button;
        }
        throw new InvalidOperationException("The real Insert Links group never reached an expanded state.");
    }

    private static void SetFont24ThroughTheRibbon(MainWindow window)
    {
        window.Editor.SelectAll();
        window.KeyTextInput(string.Empty);
        var combo = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(control => Equals(control.Tag, "freew.font-size"));
        var input = combo.GetVisualDescendants().OfType<TextBox>().Single();
        input.Focus().Should().BeTrue();
        input.SelectAll();
        window.KeyTextInput("2");
        input.IsFocused.Should().BeTrue();
        window.KeyTextInput("4");
        input.IsFocused.Should().BeTrue();
        Press(window, Key.Enter, PhysicalKey.Enter);
        window.Editor.IsFocused.Should().BeTrue();
        window.Editor.GetCaretFormatting().Run.FontSizePt.Should().Be(24);
        window.Editor.Document.PlainText.Should().Be("a😀b");
    }

    private void AssertEmojiSelection(DocumentView editor, string stage)
    {
        var snapshot = editor.AutomationSnapshot();
        output.WriteLine($"{stage}: actual={editor.SelectedText}; semantic caret={snapshot.CaretOffset}; range={snapshot.Selection}; cached help={AutomationProperties.GetHelpText(editor)}.");
        editor.SelectedText.Should().Be("😀", stage);
        editor.TryGetDisplaySelectionProjection(out _, out var plan).Should().BeTrue(stage);
        plan.DisplayStart.Should().Be(1, stage);
        plan.DisplayEnd.Should().Be(3, stage);
        plan.SelectedText.Should().Be("😀", stage);
        snapshot.CaretOffset.Should().Be(3, stage);
        snapshot.Selection.Should().NotBeNull(stage);
        snapshot.Selection!.Start.Should().Be(1, stage);
        snapshot.Selection.Length.Should().Be(2, stage);
        AutomationProperties.GetHelpText(editor).Should().Be(editor.AutomationSelectionStatus(), stage);
    }

    private static void Click(Control control)
    {
        var top = TopLevel.GetTopLevel(control) ?? throw new InvalidOperationException("Control has no live top level.");
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)
            ?? throw new InvalidOperationException("Control cannot be translated to its top level.");
        top.MouseDown(point, MouseButton.Left);
        top.MouseUp(point, MouseButton.Left);
    }

    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, key == Key.Enter ? "\r" : null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
