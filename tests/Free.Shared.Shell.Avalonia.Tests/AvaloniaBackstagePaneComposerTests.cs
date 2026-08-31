using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Free.Shared.Ribbon;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class AvaloniaBackstagePaneComposerTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    [Fact]
    public async Task Composer_ProjectsPortableInfoAndActionSpecs()
    {
        await Session.Dispatch(() =>
        {
            var invoked = false;
            var composer = new AvaloniaBackstagePaneComposer(AvaloniaBackstageChromeStyle.FromContract());
            var info = Assert.IsType<StackPanel>(composer.BuildInfoPane(new BackstageInfoPaneSpec(
                "Presentation",
                "Deck.fxp",
                IsDirty: true,
                Location: null,
                Properties: [new BackstageFieldRow("Title", "Deck")],
                Statistics: [new BackstageFieldRow("Slides", "3")])));
            var actions = Assert.IsType<StackPanel>(composer.BuildActionPane(new BackstageActionPaneSpec(
                "Export",
                "Create a copy.",
                [new BackstageActionGroup(
                    "PDF",
                    [new BackstageActionRow("Localized export", "Publish.", () => invoked = true)
                    {
                        AutomationId = "ExportPdfAction",
                    }])]),
                "Export"));

            info.Children.OfType<TextBlock>().First().Text.Should().Be("Info");
            info.GetVisualDescendants().OfType<TextBlock>()
                .Should().Contain(text => text.Text == "Deck.fxp  (unsaved changes)");
            info.GetVisualDescendants().OfType<TextBlock>()
                .Should().Contain(text => text.Text == BackstageInfoPaneText.NotSavedYet);

            var button = actions.GetVisualDescendants().OfType<Button>().Single();
            AutomationProperties.GetAutomationId(button).Should().Be("ExportPdfAction");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            invoked.Should().BeTrue();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Composer_UsesPortableRecentOptionsAndAccountRows()
    {
        await Session.Dispatch(() =>
        {
            var opened = "";
            var composer = new AvaloniaBackstagePaneComposer(AvaloniaBackstageChromeStyle.FromContract());
            var recent = composer.BuildRecentPane(new BackstageRecentPaneSpec(
                [@"C:\Decks\Roadmap.fxp"],
                "No recent presentations.",
                path => opened = path));
            var options = composer.BuildOptionsPane(new BackstageOptionsPaneSpec(
                "Settings",
                [new BackstageFieldRow("UI language", "en-US")]));
            var account = composer.BuildAccountPane(new BackstageAccountPaneSpec(
                "Account",
                "Local account",
                [new SisterBackstageAccountFieldGroup("Product", [new BackstageFieldRow("Version", "1.0")])]));

            recent.GetVisualDescendants().OfType<Button>().Single()
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            opened.Should().Be(@"C:\Decks\Roadmap.fxp");
            options.GetVisualDescendants().OfType<TextBlock>().Should().Contain(text => text.Text == "en-US");
            account.GetVisualDescendants().OfType<TextBlock>().Should().Contain(text => text.Text == "1.0");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Composer_ContainsAndReportsActionCallbackFailure()
    {
        await Session.Dispatch(() =>
        {
            var failure = new InvalidOperationException("export failed");
            (Exception Exception, string CommandId)? reported = null;
            var previousHandler = RibbonCommandFaultReporter.Handler;
            RibbonCommandFaultReporter.Handler = (exception, commandId) =>
                reported = (exception, commandId);
            try
            {
                var composer = new AvaloniaBackstagePaneComposer(AvaloniaBackstageChromeStyle.FromContract());
                var spec = new BackstageActionPaneSpec(
                    "Export",
                    "Create a copy.",
                    [new BackstageActionGroup(
                        "PDF",
                        [new BackstageActionRow("Export PDF", "Publish.", () => throw failure)
                        {
                            AutomationId = "ExportPdfAction",
                        }])]);
                var pane = composer.BuildActionPane(spec, "Export");
                var button = pane.GetVisualDescendants().OfType<Button>().Single();

                var click = () => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                click.Should().NotThrow("an action fault must not escape the Avalonia click boundary");
                reported.Should().NotBeNull();
                reported!.Value.Exception.Should().BeSameAs(failure);
                reported.Value.CommandId.Should().Be("ExportPdfAction");
            }
            finally
            {
                RibbonCommandFaultReporter.Handler = previousHandler;
            }
        }, CancellationToken.None);
    }
}
