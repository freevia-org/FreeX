using System.IO;

namespace Free.Shared.Theme.Tests;

public sealed class SharedAvaloniaDialogChromeOwnershipTests
{
    [Fact]
    public void SharedMessageDialogsInheritTheSingleAvaloniaChromeAuthority()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        foreach (var fileName in new[]
                 {
                     "AvaloniaSaveChangesDialog.cs",
                     "AvaloniaSynchronousUserMessageDialog.cs",
                     "AvaloniaUserMessageDialog.cs",
                 })
        {
            var source = Read(root, fileName);
            var className = Path.GetFileNameWithoutExtension(fileName);

            source.Should().Contain($"class {className} : AvaloniaDialogWindow", fileName)
                .And.NotContain($"class {className} : Window", fileName)
                .And.NotContain("AvaloniaCompactDialogChrome.ApplyWindow(this)", fileName);
        }
    }

    [Fact]
    public void LegalNoticesUsesTheSharedWindowsTypographyForItsDefaultButton()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = Read(root, "AvaloniaLegalNoticesDialog.cs");

        source.Should().Contain("AvaloniaCompactDialogChrome.WindowsStyle")
            .And.NotContain("new AvaloniaCompactDialogChromeStyle(FontFamily.Default)");
    }

    [Fact]
    public void CompactComboBoxOwnsStableRequiredPartsInsteadOfPatchingFluentInternals()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = Read(root, "AvaloniaCompactDialogChrome.cs");

        source.Should().Contain("comboBox.Template = CreateCompactComboBoxTemplate(")
            .And.Contain("Name = \"PART_EditableTextBox\"")
            .And.Contain("Name = \"PART_Popup\"")
            .And.Contain("Name = \"PART_ItemsPresenter\"")
            .And.Contain("Mode = BindingMode.TwoWay")
            .And.NotContain("void ApplyWpfComboGlyph()")
            .And.NotContain("selector.OfType<Border>().Name(\"PART_LayoutRoot\")");
    }

    [Fact]
    public void AppDialogsDoNotRepairSharedComboBoxTemplateParts()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var consumers = new[]
        {
            Path.Combine(root, "freew", "FreeW.App.Avalonia", "FontParagraphDialogChrome.cs"),
            Path.Combine(root, "freep", "FreeP.App.Avalonia", "HyperlinkDialog.cs"),
        };

        foreach (var path in consumers)
        {
            var source = File.ReadAllText(path);
            source.Should().NotContain("PART_LayoutRoot", path)
                .And.NotContain("GetVisualDescendants().OfType<ContentPresenter>()", path);
        }

        File.ReadAllText(consumers[1]).Should()
            .Contain("AvaloniaCompactDialogChrome.ApplyWpfDisabledComboSurface(_slideCombo)");
    }

    private static string Read(string root, string fileName) =>
        File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            fileName));
}
