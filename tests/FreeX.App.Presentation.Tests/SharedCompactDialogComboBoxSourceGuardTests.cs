using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class SharedCompactDialogComboBoxSourceGuardTests
{
    [Fact]
    public void SharedAvaloniaComboBoxOwnsStableTemplateAndPreservesInteractionParts()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaCompactDialogChrome.cs"));

        source.Should().Contain("comboBox.Template = CreateCompactComboBoxTemplate(style, foreground, arrowBackground)");
        source.Should().Contain("Name = \"PART_EditableTextBox\"");
        source.Should().Contain("nameof(ComboBox.Text)");
        source.Should().Contain("Mode = BindingMode.TwoWay");
        source.Should().Contain("Name = \"PART_Popup\"");
        source.Should().Contain("nameof(ComboBox.IsDropDownOpen)");
        source.Should().Contain("Name = \"PART_ItemsPresenter\"");
        source.Should().Contain("nameof(ComboBox.SelectionBoxItem)");
        source.Should().Contain("nameof(ComboBox.SelectionBoxItemTemplate)");
        source.Should().NotContain(".Name(\"PART_LayoutRoot\")");
        source.Should().NotContain("Dispatcher.UIThread.Post(ApplyWpfComboGlyph");
        source.Should().NotContain("Where(border => border.Name is \"PART_LayoutRoot\"");
    }
}
