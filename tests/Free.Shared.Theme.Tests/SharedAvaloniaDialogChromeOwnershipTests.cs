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
            .And.Contain("nameScope.Register(\"PART_Popup\", popup)")
            .And.Contain("nameScope.Register(\"PART_EditableTextBox\", editableText)")
            .And.Contain("nameScope.Register(\"PART_ItemsPresenter\", items)")
            .And.Contain("InheritsTransform = true")
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

    [Fact]
    public void CompactTextBoxOwnsFluentBorderRealizationAndApplicationsDoNotRepairIt()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var shared = Read(root, "AvaloniaCompactDialogChrome.cs");
        var tokens = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell",
            "CompactDialogVisualTokens.cs"));
        var freeWHelper = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "FontParagraphDialogChrome.cs"));
        var fontDialog = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "FontDialog.cs"));
        var paragraphDialog = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "ParagraphDialog.cs"));

        tokens.Should().Contain("DisabledFieldBorderHex = \"#D0D1D4\"");
        shared.Should().Contain("CompactTextBoxClass = \"free-compact-dialog-textbox\"")
            .And.Contain("CompactDialogVisualTokens.DisabledFieldBorderHex")
            .And.Contain("Name(\"PART_BorderElement\")")
            .And.Contain("QueueRenderedTextBoxChrome(textBox, style, fixedHeight)")
            .And.Contain("if (fixedHeight)")
            .And.Contain("style.DisabledTextBoxBackgroundBrush ?? textBoxBackground");

        freeWHelper.Should().NotContain("PART_BorderElement")
            .And.NotContain("GetVisualDescendants")
            .And.NotContain("Dispatcher")
            .And.NotContain("ApplyTextBox")
            .And.NotContain("ApplyComboBox")
            .And.NotContain("WpfDisabledInputBorderBrush");
        fontDialog.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);")
            .And.Contain("AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);")
            .And.NotContain("FontParagraphDialogChrome.ApplyTextBox")
            .And.NotContain("FontParagraphDialogChrome.ApplyComboBox");
        paragraphDialog.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);")
            .And.Contain("AvaloniaCompactDialogChrome.ApplyComboBox(_special, DialogChromeStyle);")
            .And.NotContain("FontParagraphDialogChrome.ApplyTextBox")
            .And.NotContain("FontParagraphDialogChrome.ApplyComboBox");
    }

    private static string Read(string root, string fileName) =>
        File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            fileName));
}
