namespace FreeW.App.Presentation.Tests;

public sealed class DialogTextRasterizationPolicyTests
{
    [Fact]
    public void SharedDialogBasesUseEquivalentLayoutRoundingContracts()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Wpf",
            "DialogWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaCompactDialogChrome.cs"));

        wpf.Should().Contain("UseLayoutRounding = true");
        avalonia.Should().Contain("window.UseLayoutRounding = true");
    }

    [Fact]
    public void SharedDialogBasesUseEquivalentSubpixelTextContracts()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Wpf",
            "DialogWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaCompactDialogChrome.cs"));

        wpf.Should().Contain("TextRenderingMode.ClearType");
        avalonia.Should().Contain("TextRenderingMode.SubpixelAntialias");
        avalonia.Should().NotContain("TextOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias)");
    }

    [Fact]
    public void FreeWParagraphDialogInheritsTheSharedRasterizationPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "ParagraphDialog.cs"));

        source.Should().NotContain("TextOptions.SetTextRenderingMode(this");
    }

    [Fact]
    public void FreeWFontDialogUsesItsWpfAuthorityRasterizationPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "FontDialog.cs"));

        source.Should().Contain("TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias)");
    }
}
