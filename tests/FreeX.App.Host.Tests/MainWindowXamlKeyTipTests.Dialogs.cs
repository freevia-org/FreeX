using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void DialogEntryPointHandlers_UseOwnedActivatedDialogs()
    {
        var appHostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var source = DialogSourceTestSupport.ReadHostSources(
            Directory.GetFiles(appHostDirectory, "MainWindow*.cs")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.GetFileName(path)!)
                .ToArray());
        var invokeButtonSource = DialogSourceTestSupport.ReadHostSources("AutomationInvokeButton.cs");

        source.Should().Contain("ShowOwnedDialog(");
        source.Should().Contain("ShowOwnedMessage(");
        source.Should().Contain("var dlg = new InsertFunctionDialog");
        source.Should().Contain("var dlg = new OptionsDialog");
        source.Should().Contain("ShowOwnedDialog(dlg)");
        source.Should().Contain("ShowOwnedMessage(");
        source.Should().Contain("var dialog = new AboutDialog();");
        source.Should().Contain("ShowOwnedDialog(dialog);");
        source.Should().Contain("var dialog = new LegalNoticesDialog();");
        source.Should().Contain("ShowOwnedDialog(dialog);");
        invokeButtonSource.Should().Contain("IInvokeProvider");
        invokeButtonSource.Should().Contain("Dispatcher.BeginInvoke");
        invokeButtonSource.Should().Contain("ButtonBase.ClickEvent");
    }

    [Fact]
    public void DeferredCommandButtons_DescribeDeferredStatusInTooltip()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants()
            .Where(element => element.Name == presentation + "Button" || element.Name == presentation + "ToggleButton")
            .Where(button => button.Attribute("Click")?.Value == "PageLayoutDeferredBtn_Click")
            .Where(button =>
                LocalizedAttribute(button, ribbonWpf + "RibbonTooltip.Description")?.Contains("Deferred:", StringComparison.OrdinalIgnoreCase) != true)
            .Select(button => LocalizedAttribute(button, ribbonWpf + "RibbonTooltip.Title") ?? LocalizedAttribute(button, "Content") ?? "Button")
            .ToList();

        missing.Should().BeEmpty("deferred visible commands should clearly say they are deferred before the user clicks");
    }
}
