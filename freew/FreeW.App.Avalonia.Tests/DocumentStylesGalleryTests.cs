using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentStylesGalleryTests
{
    [Fact]
    public void Styles_gallery_exposes_word_quick_styles_and_routes_the_selected_style()
    {
        var registry = new RibbonCommandRegistry();
        var commands = new Dictionary<string, RecordingCommand>();
        foreach (var style in BuiltInStyles.Gallery.Where(style => style.Type == StyleType.Paragraph))
        {
            var command = new RecordingCommand();
            commands.Add(style.Id, command);
            registry.Register(new RibbonCommandId(FormattingGalleryRibbonWorkflow.StyleCommandId(style.Id)), command);
        }
        registry.Register(new RibbonCommandId("freew.style-clear"), new RecordingCommand());
        registry.Register(new RibbonCommandId("freew.new-style"), new RecordingCommand());
        registry.Register(new RibbonCommandId("freew.manage-styles"), new RecordingCommand());

        var gallery = DocumentStylesGallery.Build(new DocumentView(), registry)
            .Should().BeOfType<StackPanel>().Subject;
        var strip = ((ScrollViewer)((Border)gallery.Children[0]).Child!).Content
            .Should().BeOfType<StackPanel>().Subject;
        var buttons = strip.Children.OfType<Button>().ToArray();

        buttons.Select(AutomationProperties.GetName).Should().Equal(
            "Normal", "No Spacing", "Heading 1", "Heading 2", "Heading 3", "Title", "Subtitle", "Quote");
        buttons.Single(button => AutomationProperties.GetName(button) == "Title")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        commands["Title"].ExecuteCount.Should().Be(1);
    }

    private sealed class RecordingCommand : IRibbonPreviewCommand
    {
        public int ExecuteCount { get; private set; }
        public void BeginPreview(RibbonCommandContext context) { }
        public void CancelPreview() { }
        public void Execute(RibbonCommandContext context) => ExecuteCount++;
    }
}
