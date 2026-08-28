using Avalonia.Controls;

namespace FreeP.App.Avalonia.Tests;

public sealed class SwitchWindowsMenuParitySourceTests
{
    [Fact]
    public void Switch_windows_menu_marks_the_active_window_as_a_checkable_item()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("IsChecked = ReferenceEquals(target, this)");
        source.Should().Contain("ToggleType = MenuItemToggleType.CheckBox");
    }

    /// <summary>
    /// Round 166 sweep104 F2: the real wiring for View &gt; Switch Window is not in
    /// <c>FreeP.App.Avalonia/MainWindow.cs</c> at all -- it is the ribbon action-profile entry
    /// <c>SwitchPresentationWindow = ShowPresentationWindowPicker,</c> in the shared
    /// <c>freep/RendererShared/MainWindow.RibbonActionProfile.cs</c>, compiled into both the WPF and
    /// Avalonia hosts. The assertions above only pin strings inside
    /// <c>ShowPresentationWindowPicker</c>'s own body in a file this test never reads, so they can
    /// never detect that entry being deleted or repointed to a no-op -- the command would go dead on
    /// both shells while this test stays green. Pin the call site itself, in the file that actually
    /// contains it, scoped to the method body via brace matching (mirroring the round-165
    /// TogglePageBreakPreview remediation).
    /// </summary>
    [Fact]
    public void RibbonActionProfile_wires_the_switch_presentation_window_action_to_the_window_picker()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root, "freep", "RendererShared", "MainWindow.RibbonActionProfile.cs"));

        var profileBody = MethodBody(source, "private FreePRibbonActionPortProfile GetRibbonActionPortProfile() =>");
        profileBody.Should().Contain(
            "SwitchPresentationWindow = ShowPresentationWindowPicker,",
            "entering View > Switch Window is what opens the picker on both the WPF and Avalonia " +
            "hosts; without this wiring line ShowPresentationWindowPicker is unreachable dead code " +
            "on both shells");

        // Sibling no-regression check: the adjacent window-management actions on the same ribbon
        // action-port profile (New Window / Arrange All / Cascade) must still be wired the same
        // way -- this fix must not have disturbed them.
        profileBody.Should().Contain("NewPresentationWindow = OpenNewPresentationWindow,");
        profileBody.Should().Contain("ArrangeAllPresentationWindows = ArrangeAllPresentationWindows,");
        profileBody.Should().Contain("CascadePresentationWindows = CascadePresentationWindows,");
    }

    /// <summary>
    /// Returns the body of <paramref name="signature"/> by brace matching, so an assertion can be
    /// scoped to one method rather than to the whole file. Works for an expression-bodied method too:
    /// the first <c>{</c> after the signature is the object initializer this profile is built from.
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
