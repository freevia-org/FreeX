using Avalonia.Controls;

namespace FreeW.App.Avalonia.Tests;

public sealed class SwitchWindowsMenuParitySourceTests
{
    [Fact]
    public void Switch_windows_menu_marks_the_active_window_as_a_checkable_item()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("IsChecked = ReferenceEquals(target, this)");
        source.Should().Contain("ToggleType = MenuItemToggleType.CheckBox");
    }

    /// <summary>
    /// Round 166 sweep104 F1: the assertions above only pin strings inside
    /// <c>ShowDocumentWindowPicker</c>'s own body, which stays untouched -- and this test stays
    /// green -- even if the ribbon/menu dispatch line that reaches it is deleted or repointed to a
    /// no-op. Deleting the one-line wiring in <c>BuildRibbon()</c> disables View &gt; Switch Windows
    /// entirely on the Avalonia shell with no test noticing. Pin the call site itself by scoping the
    /// assertion to <c>BuildRibbon()</c>'s body via brace matching, mirroring the round-165
    /// TogglePageBreakPreview remediation.
    /// </summary>
    [Fact]
    public void BuildRibbon_wires_the_switch_windows_action_to_the_document_window_picker()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        var buildRibbonBody = MethodBody(source, "private Control BuildRibbon()");
        buildRibbonBody.Should().Contain(
            "SwitchWindows:   ShowDocumentWindowPicker,",
            "entering View > Switch Windows is what opens the picker; without this wiring line " +
            "ShowDocumentWindowPicker is unreachable dead code");

        // Sibling no-regression check: the adjacent window-management actions on the same ribbon
        // callbacks record (New Window / Arrange All) must still be wired the same way -- this fix
        // must not have disturbed them.
        buildRibbonBody.Should().Contain("NewWindow:       OpenNewWindow,");
        buildRibbonBody.Should().Contain("ArrangeAll:      ArrangeAllWindows,");
    }

    /// <summary>
    /// Returns the body of <paramref name="signature"/> by brace matching, so an assertion can be
    /// scoped to one method rather than to the whole file.
    /// </summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"{signature} must exist for this contract to mean anything");

        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..(i + 1)];
        }

        throw new InvalidOperationException($"Unbalanced braces after {signature}.");
    }
}
