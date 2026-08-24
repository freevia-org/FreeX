using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.ContextMenus;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class TableStylesGalleryTests
{
    [Fact]
    public void Gallery_exposes_direct_table_style_previews_and_routes_the_selected_style()
    {
        var registry = new RibbonCommandRegistry();
        var commands = new List<RecordingCommand>();
        for (var index = 0; index < DocumentTableStyle.Catalog.Count; index++)
        {
            var command = new RecordingCommand();
            commands.Add(command);
            registry.Register(new RibbonCommandId(FreeWContextMenuPlanner.TableStylesPrefix + index), command);
        }

        var gallery = TableStylesGallery.Build(registry).Should().BeOfType<StackPanel>().Subject;
        var strip = ((Border)gallery.Children[0]).Child.Should().BeOfType<StackPanel>().Subject;
        var buttons = strip.Children.OfType<Button>().ToArray();

        buttons.Select(AutomationProperties.GetName).Should().Equal(
            DocumentTableStyle.Catalog.Take(3).Select(style => style.Name));
        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        commands[1].ExecuteCount.Should().Be(1);
        AutomationProperties.GetName((Button)gallery.Children[1]).Should().Be("More Table Styles");
    }

    private sealed class RecordingCommand : IRibbonPreviewCommand
    {
        public int ExecuteCount { get; private set; }
        public void BeginPreview(RibbonCommandContext context) { }
        public void CancelPreview() { }
        public void Execute(RibbonCommandContext context) => ExecuteCount++;
    }
}
