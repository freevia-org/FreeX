using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentThemeGalleryTests
{
    [Fact]
    public void Theme_gallery_exposes_every_builtin_thumbnail_and_routes_its_command()
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

        var gallery = DocumentThemeGallery.Build(registry).Should().BeOfType<StackPanel>().Subject;
        var buttons = gallery.Children.OfType<Button>().ToArray();
        buttons.Should().HaveCount(DocumentTheme.Catalog.Count);
        buttons.Select(AutomationProperties.GetName).Should().Equal(DocumentTheme.Catalog.Select(theme => theme.Name));

        var berlin = buttons.Single(button => AutomationProperties.GetName(button) == "Berlin");
        berlin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        commands["Berlin"].ExecuteCount.Should().Be(1);
        commands["Office"].ExecuteCount.Should().Be(0);
    }

    private sealed class RecordingPreviewCommand : IRibbonPreviewCommand
    {
        public int ExecuteCount { get; private set; }

        public void BeginPreview(RibbonCommandContext context) { }

        public void CancelPreview() { }

        public void Execute(RibbonCommandContext context) => ExecuteCount++;
    }
}
