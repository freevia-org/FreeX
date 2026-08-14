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

    private static string Read(string root, string fileName) =>
        File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            fileName));
}
