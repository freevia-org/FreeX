using FluentAssertions;
using FreeX.App.Presentation.Tests;

namespace FreeX.App.Presentation.Tests.Editing;

public sealed class ClipboardCsvTextRendererSourceGuardTests
{
    [Fact]
    public void WpfAndAvaloniaClipboardAdapters_UseTheSharedCsvRenderer()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        var rendererPath = Path.Combine(presentationRoot, "Editing", "ClipboardCsvTextRenderer.cs");

        File.Exists(rendererPath).Should().BeTrue("CSV text rendering should have one portable Presentation owner");

        var adapterPaths = new[]
        {
            Path.Combine(repoRoot, "src", "FreeX.App.Host", "MainWindow.ClipboardCommands.cs"),
            Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.ClipboardHtml.cs")
        };

        foreach (var adapterPath in adapterPaths)
        {
            var source = File.ReadAllText(adapterPath);

            source.Should().Contain("ClipboardCsvTextRenderer.Render(text)");
            source.Should().NotContain("BuildCsvClipboardText");
            source.Should().NotContain("AppendCsvField");
        }
    }
}
