using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class SharedCompactDialogComboBoxSourceGuardTests
{
    [Fact]
    public void SharedAvaloniaComboBoxOwnsStableTemplateWithRequiredNativeInteractionParts()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaCompactDialogChrome.cs"));

        source.Should().Contain("Own the compact template instead of repairing private Fluent-theme parts after attach");
        source.Should().Contain("comboBox.Template = CreateCompactComboBoxTemplate(");
        source.Should().Contain("private static FuncControlTemplate<ComboBox> CreateCompactComboBoxTemplate(");
        source.Should().Contain("Name = \"PART_ContentPresenter\"");
        source.Should().Contain("Name = \"PART_EditableTextBox\"");
        source.Should().Contain("Name = \"PART_ItemsPresenter\"");
        source.Should().Contain("Name = \"PART_Popup\"");
        source.Should().Contain("Mode = BindingMode.TwoWay");
        source.Should().Contain("Name = \"DropDownGlyph\"");
        source.Should().NotContain("Dispatcher.UIThread.Post(ApplyWpfComboGlyph");
    }
}
