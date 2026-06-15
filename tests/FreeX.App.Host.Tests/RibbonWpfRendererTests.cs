using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Ribbon;

namespace FreeX.App.Host.Tests;

public class RibbonWpfRendererTests
{
    private sealed class RecordingCommand : IRibbonCommand
    {
        public int Invocations { get; private set; }
        public void Execute(RibbonCommandContext context) => Invocations++;
    }

    [Fact]
    public void HomeDefinition_IsValid_AndHasAllSevenGroups()
    {
        var definition = HomeRibbonDefinition.Build();

        RibbonDefinitionValidator.Validate(definition).HasErrors.Should().BeFalse();
        definition.FindTab("HomeTab")!.Groups.Select(g => g.Header).Should().Equal(
            "Clipboard", "Font", "Alignment", "Number", "Styles", "Cells", "Editing");
    }

    [Fact]
    public void RenderedButton_InvokesRegisteredCommand_OnClick()
    {
        var registry = new RibbonCommandRegistry();
        var paste = new RecordingCommand();
        registry.Register("Paste", paste);

        StaTestRunner.Run(() =>
        {
            var host = BuildHost();
            var tab = HomeRibbonDefinition.Build().FindTab("HomeTab")!;
            host.Child = RibbonWpfRenderer.BuildTabContent(tab, host, registry);
            host.Measure(new Size(1880, 130));
            host.Arrange(new Rect(0, 0, 1880, 130));
            host.UpdateLayout();

            var pasteButton = FindByCommandName(host, "Paste");
            pasteButton.Should().NotBeNull();
            pasteButton!.IsEnabled.Should().BeTrue();
            ((ButtonBase)pasteButton).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            // "Cut" has no registered command -> rendered disabled, never throws.
            FindByCommandName(host, "Cut")!.IsEnabled.Should().BeFalse();
        });

        paste.Invocations.Should().Be(1);
    }

    private static Border BuildHost()
    {
        if (Application.Current is null)
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var host = new Border { Width = 1880 };
        host.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/FreeX.App.Host;component/Resources/MainWindowResources.xaml")
        });
        return host;
    }

    private static Control? FindByCommandName(DependencyObject root, string commandName)
    {
        foreach (var child in Descendants(root))
        {
            if (child is Control control &&
                string.Equals(RibbonMetadata.GetCommandName(control), commandName, StringComparison.Ordinal))
            {
                return control;
            }
        }

        return null;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var grandChild in Descendants(child))
                yield return grandChild;
        }
    }
}
