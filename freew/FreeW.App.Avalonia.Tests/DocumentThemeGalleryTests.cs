using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentThemeGalleryTests
{
    [Fact]
    public void Theme_gallery_uses_a_compact_chooser()
    {
        var registry = new RibbonCommandRegistry();
        var commands = new Dictionary<string, RecordingPreviewCommand>();
        foreach (var theme in DocumentTheme.Catalog)
        {
            var command = new RecordingPreviewCommand();
            commands.Add(theme.Name, command);
            registry.Register(
                new RibbonCommandId($"freew.theme.{theme.Name.ToLowerInvariant()}"),
                command);
        }

        var gallery = DocumentThemeGallery.Build(registry).Should().BeOfType<Button>().Subject;
        AutomationProperties.GetName(gallery).Should().Be("Themes");
    }

    [Fact]
    public void Document_formatting_exposes_style_sets_as_the_visible_gallery()
    {
        var gallery = DocumentThemeGallery.BuildDocumentFormatting(new RibbonCommandRegistry())
            .Should().BeOfType<StackPanel>().Subject;

        gallery.Children.OfType<Button>().Select(AutomationProperties.GetName).Should().Contain([
            "Office", "Simple", "Elegant", "Formal", "Lines (Simple)", "Minimalist", "Shadow", "Shaded",
            "More Style Sets", "Colors", "Fonts", "Paragraph\nSpacing", "Effects"]);
    }

    private sealed class RecordingPreviewCommand : IRibbonPreviewCommand
    {
        public int ExecuteCount { get; private set; }

        public void BeginPreview(RibbonCommandContext context) { }

        public void CancelPreview() { }

        public void Execute(RibbonCommandContext context) => ExecuteCount++;
    }
}
