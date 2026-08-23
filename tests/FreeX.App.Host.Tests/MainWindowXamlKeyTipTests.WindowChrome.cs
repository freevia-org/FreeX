using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void TitleBarWindowChrome_ExposesMinimizeMaximizeRestoreAndCloseButtons()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ViewCommands.cs");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var systemButtons = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click")?.Value is "MinimizeBtn_Click" or "MaxRestoreBtn_Click" or "CloseSysBtn_Click")
            .Select(button => new
            {
                Click = button.Attribute("Click")?.Value,
                AutomationName = LocalizedAttribute(button, "AutomationProperties.Name"),
                IconKind = button.Element(local + "RibbonIcon")?.Attribute("Kind")?.Value
            })
            .ToList();

        systemButtons.Should().BeEquivalentTo(
        [
            new { Click = "MinimizeBtn_Click", AutomationName = "Minimize", IconKind = "WindowMinimize" },
            new { Click = "MaxRestoreBtn_Click", AutomationName = "Maximize or Restore", IconKind = "WindowMaximize" },
            new { Click = "CloseSysBtn_Click", AutomationName = "Close", IconKind = "WindowClose" }
        ]);

        source.Should().Contain("SystemCommands.MinimizeWindow(this)");
        source.Should().Contain("SystemCommands.RestoreWindow(this)");
        source.Should().Contain("SystemCommands.MaximizeWindow(this)");
        source.Should().Contain("SystemCommands.CloseWindow(this)");
    }

    [Fact]
    public void QuickAccessToolbar_BuildsPersistedCommandsWithKeyTipsAndSharedCommandRoutes()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var catalogSource = DialogSourceTestSupport.ReadAppServicesRibbonSource("QuickAccessToolbarCatalog.cs");
        var qatSource = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAccessToolbar.cs");
        var applicationRoutingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs");
        var keyTipSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyTips.cs");
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var lifecycleSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookLifecycle.cs");
        var commandSource = DialogSourceTestSupport.ReadHostSources("MainWindow.CommandExecution.cs");

        xaml.Should().Contain("x:Name=\"TitleBarQatPanel\"");
        xaml.Should().Contain("x:Name=\"BelowRibbonQatPanel\"");
        catalogSource.Should().Contain("DefaultCommandIds");
        catalogSource.Should().Contain("QuickAccessToolbarCommandIds.Save");
        catalogSource.Should().Contain("QuickAccessToolbarCommandIds.Undo");
        catalogSource.Should().Contain("QuickAccessToolbarCommandIds.Redo");
        catalogSource.Should().Contain("QuickAccessToolbarCommandIds.Print");
        catalogSource.Should().Contain("QuickAccessToolbarCommandIds.InsertFunction");
        catalogSource.Should().Contain("QuickAccessToolbarCommandIds.NameManager");
        qatSource.Should().Contain("RebuildQuickAccessToolbar()");
        qatSource.Should().Contain("RibbonTooltip.SetKeyTip(button, QuickAccessToolbarCatalog.FormatKeyTip(visibleIndex));");
        // The QAT button (style, glyph, hit-test, automation id/name) is built through the shared
        // Free.Shared.Ribbon.Wpf QAT renderer from a neutral descriptor carrying the catalog automation id;
        // FreeX keeps its RibbonTooltip / RibbonMetadata / context-menu / click decorations on top.
        qatSource.Should().Contain("SharedQat.BuildButton(");
        qatSource.Should().Contain("AutomationId = command.AutomationId");
        qatSource.Should().Contain("RegisterQuickAccessToolbarName(command.AutomationId, button);");
        qatSource.Should().Contain("RegisterQuickAccessToolbarName(historyButton.Name, historyButton);");
        qatSource.Should().Contain("ExecuteQuickAccessToolbarCommand(command.Id, button, args)");

        keyTipSource.Should().Contain("private bool TryInvokeTopLevelQatKeyTip(string keyTip)");
        keyTipSource.Should().Contain("GetVisibleKeyTipElements(RibbonKeyTipScope.TopLevel)");
        keyTipSource.Should().Contain("private IEnumerable<FrameworkElement> EnumerateKeyTipCandidateElements");
        keyTipSource.Should().Contain("RibbonTabs.Items.OfType<TabItem>()");
        keyTipSource.Should().Contain("EnumerateQuickAccessToolbarButtons()");
        keyTipSource.Should().Contain("selectedTab.Content as DependencyObject ?? selectedTab");
        keyTipSource.Should().Contain("if (!match.IsEnabled)");
        keyTipSource.Should().Contain("match.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, match));");

        backstageSource.Should().Contain("private async void SaveButton_Click(object sender, RoutedEventArgs e)");
        // Save now delegates the existing-path-vs-dialog DECISION to the shared SaveResolvedAsync helper
        // (MainWindow.WorkbookLifecycle.cs), the same resolution the dirty-gate's "Save then proceed" takes.
        backstageSource.Should().Contain("await SaveResolvedAsync()");
        lifecycleSource.Should().Contain("private async Task<bool> SaveResolvedAsync()");
        lifecycleSource.Should().Contain("_fileWorkflow.SaveResolvedAsync(");
        lifecycleSource.Should().Contain("_fileAdapters");
        lifecycleSource.Should().Contain("SaveWorkbookToTargetAsync");
        lifecycleSource.Should().Contain("SaveWorkbookWithDialogAsync");
        backstageSource.Should().Contain("MarkWorkbookSaved()");
        backstageSource.Should().Contain("UpdateTitleBar()");

        qatSource.Should().Contain("WorkbookApplicationCommandRouter.TryRouteQuickAccess(commandId, out var route)");
        applicationRoutingSource.Should().Contain("Undo = Handled(");
        applicationRoutingSource.Should().Contain("ExecuteUndo()");
        applicationRoutingSource.Should().Contain("Redo = Handled(");
        applicationRoutingSource.Should().Contain("ExecuteRedo()");
        commandSource.Should().Contain("_session.UndoLastEdit()");
        commandSource.Should().Contain("_session.RedoLastEdit()");
        commandSource.Should().Contain("RefreshToolbar()");
    }

    [Fact]
    public void GreenSurfaceButtons_UseCustomHoverChromeInsteadOfNativeBlueHover()
    {
        var mainWindow = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        var resources = DialogSourceTestSupport.LoadHostXamlDocument("Resources", "MainWindowResources.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var buttonName in new[] { "StatusZoomOutButton", "StatusZoomInButton" })
        {
            var button = mainWindow
                .Descendants(presentation + "Button")
                .Single(element => element.Attribute(x + "Name")?.Value == buttonName);

            button.Attribute("Style")?.Value.Should().Be("{StaticResource StatusBarZoomButtonStyle}");
        }

        static XElement ResourceStyle(XDocument document, XNamespace presentation, XNamespace x, string key) =>
            document
                .Descendants(presentation + "Style")
                .Single(style => style.Attribute(x + "Key")?.Value == key);

        foreach (var styleKey in new[] { "StatusBarZoomButtonStyle", "SysBtnStyle", "TitleBarQatButton" })
        {
            var style = ResourceStyle(resources, presentation, x, styleKey);

            style
                .Descendants(presentation + "ControlTemplate")
                .Should()
                .NotBeEmpty($"{styleKey} should not fall back to the native WPF button template");

            var styleText = style.ToString(SaveOptions.DisableFormatting);
            styleText.Should().Contain(
                styleKey == "StatusBarZoomButtonStyle" ? "FreeXRibbonButtonHoverBrush" : "FreeXTitleBarHoverBrush",
                $"{styleKey} should use its contrast-safe chrome hover color");
        }

        var closeStyle = ResourceStyle(resources, presentation, x, "CloseSysBtnStyle");
        closeStyle.Attribute("BasedOn")?.Value.Should().Be("{StaticResource SysBtnStyle}");
        closeStyle
            .Descendants(presentation + "Trigger")
            .Where(trigger => trigger.Attribute("Property")?.Value == "IsMouseOver")
            .Should()
            .BeEmpty("the close button should share the same title-bar hover chrome as the other green-surface buttons");

        var chromeStyleText = string.Concat(
            new[] { "StatusBarZoomButtonStyle", "SysBtnStyle", "TitleBarQatButton", "CloseSysBtnStyle" }
                .Select(styleKey => ResourceStyle(resources, presentation, x, styleKey).ToString(SaveOptions.DisableFormatting)));

        chromeStyleText.Should().NotContain("#0078", "chrome hover should not use Windows blue accent colors");
        chromeStyleText.Should().NotContain("SystemColors.Highlight", "chrome hover should not use native highlight brushes");
    }
}
