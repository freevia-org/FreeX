using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class DialogVisualHarnessSemanticTextTests
{
    [Fact]
    public void Shared_extractor_normalizes_access_key_markers()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var helper = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness", "DialogSemanticText.cs"));

        helper.Should().Contain("var resolved = string.IsNullOrWhiteSpace(automationName)");
        helper.Should().Contain("return RemoveAccessKeyMarkers(resolved);");
        helper.Should().Contain("normalized.Append('_');");
    }

    [Fact]
    public void Shared_extractor_uses_blank_automation_name_fallback_without_masking_nonblank_names()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var helper = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness", "DialogSemanticText.cs"));

        helper.Should().Contain("string.IsNullOrWhiteSpace(automationName)");
        helper.Should().Contain("? content ?? fallback");
        helper.Should().Contain("RemoveAccessKeyMarkers(resolved)");
    }

    [Fact]
    public void Both_visual_harnesses_use_the_shared_button_text_normalization()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness.Wpf", "Program.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "Program.cs"));

        wpf.Should().Contain("DialogSemanticText.ResolveButtonText(");
        avalonia.Should().Contain("DialogSemanticText.ResolveButtonText(");
        avalonia.Should().NotContain("AutomationProperties.GetName(d) ?? d.Content?.ToString()");
        avalonia.Should().NotContain("AutomationProperties.GetName(c) ?? c.Content?.ToString()");
    }

    [Fact]
    public void Both_visual_harnesses_preserve_the_symbol_picker_authority_focus_state()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness.Wpf", "Program.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "Program.cs"));

        wpf.Should().Contain("if (scenario.RouteId == \"symbol-picker\")");
        avalonia.Should().Contain("if (scenario.RouteId == \"symbol-picker\")");
    }
}
