using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class SharedCompactDialogComboBoxSourceGuardTests
{
    [Fact]
    public void SharedAvaloniaComboBoxStylesTheNativeTemplateWithoutReplacingItsInteractionContract()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaCompactDialogChrome.cs"));

        source.Should().Contain("Keep Avalonia's native popup, editing, keyboard, focus, and automation behavior");
        source.Should().Contain(".Name(\"PART_LayoutRoot\")");
        source.Should().Contain(".Name(\"PART_ContentPresenter\")");
        source.Should().Contain(".Name(\"DropDownGlyph\")");
        source.Should().Contain("Dispatcher.UIThread.Post(ApplyWpfComboGlyph");
        source.Should().Contain("Where(border => border.Name is \"PART_LayoutRoot\" or \"Background\")");
        source.Should().NotContain("comboBox.Template =");
        source.Should().NotContain("CreateCompactComboBoxTemplate");
        source.Should().NotContain("Name = \"PART_Popup\"");
    }
}
