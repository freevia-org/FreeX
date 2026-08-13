using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell.Wpf;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class SisterAppFrameHelperTests
{
    [StaFact]
    public void RibbonFileTabStyle_BuildsSharedAccentFileTabTemplate()
    {
        var style = RibbonFileTabStyle.Build(
            Color.FromRgb(0x0F, 0x6D, 0x8C),
            Color.FromRgb(0x0B, 0x55, 0x6E));

        style.TargetType.Should().Be(typeof(TabItem));
        style.Setters.OfType<Setter>()
            .Should()
            .Contain(setter => setter.Property == Control.TemplateProperty);
        style.Setters.OfType<Setter>()
            .Should()
            .Contain(setter => setter.Property == Control.ForegroundProperty && Equals(setter.Value, Brushes.White));
        style.Setters.OfType<Setter>()
            .Should()
            .Contain(setter => setter.Property == UIElement.FocusableProperty && Equals(setter.Value, true));
    }

    [Theory]
    [InlineData("freew", "FreeW.App.Host")]
    [InlineData("freep", "FreeP.App.Host")]
    public void SisterAppMainWindows_UseSharedFrameHelpers(string appFolder, string projectFolder)
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            appFolder,
            projectFolder,
            "MainWindow.cs"));

        source.Should().Contain("RibbonShellBuilder.Build(");
        source.Should().Contain("using Free.Shared.Shell.Wpf;");
        source.Should().Contain("SisterAppClientFrameBuilder.Build(");
        source.Should().Contain("WorkArea:");
        source.Should().Contain("StatusBar:");
        source.Should().Contain("SisterAppWindowFrameBuilder.Build(");
        source.Should().Contain("_titleBinder = new SisterWpfWindowTitleBinder(this, titleBar.TitleText);");
        source.Should().Contain("new SisterAppWindowFrameSpec(_titleBar, root, _backstage)");
        source.Should().Contain("Content = frame.Root;");
        source.Should().Contain("SisterAppStatusBarChrome.Build(");
        source.Should().Contain("SisterQuickAccessToolbarBuilder.Render(");
        if (appFolder == "freew")
            source.Should().Contain("AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback(");
        AssertBefore(source, "_titleBinder = new SisterWpfWindowTitleBinder(this, titleBar.TitleText);", "SisterAppClientFrameBuilder.Build(");
        AssertBefore(source, "SisterAppClientFrameBuilder.Build(", "SisterAppWindowFrameBuilder.Build(");
        AssertBefore(source, "SisterAppWindowFrameBuilder.Build(", "Content = frame.Root;");
        source.Should().NotContain("WindowTitlePlanner.Compose(");
        source.Should().NotContain("_titleText.Text = title;");
        source.Should().NotContain("RibbonTabControlFactory.Create(");
        source.Should().NotContain("RibbonFileTabRouter.Attach(");
        source.Should().NotContain("RibbonWpfRenderer.BuildTabContent(");
        source.Should().NotContain("new QuickAccessToolbarItem(\"Save\"");
        source.Should().NotContain("new QuickAccessToolbarItem(\"Undo\"");
        source.Should().NotContain("new QuickAccessToolbarItem(\"Redo\"");
        source.Should().NotContain("private void OnQuickAccessCommand");
        source.Should().NotContain("new FrameworkElementFactory(typeof(Border), \"FileTabBorder\")");
        source.Should().NotContain("$\"%LOCALAPPDATA%\\\\{AppProduct.Current.ProductDirectoryName}\"");
        source.Should().NotContain("belowTitle.Children.Add(root)");
        source.Should().NotContain("Content = outer;");
    }

    [Fact]
    public void SisterAppFrameHelpers_LiveInSharedShellWpfInsteadOfRibbonWpf()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var shellProject = Path.Combine(root, "shared", "Free.Shared.Shell.Wpf");
        var ribbonProject = Path.Combine(root, "shared", "Free.Shared.Ribbon.Wpf");

        foreach (var fileName in new[]
        {
            "SisterAppClientFrameBuilder.cs",
            "SisterAppWindowFrameBuilder.cs",
            "SisterWpfWindowTitleBinder.cs",
            "SisterAppStatusBarChrome.cs",
            "BackstageFrame.cs",
            "BackstageFrameComposer.cs",
            "BackstageViewShell.cs",
            "SisterBackstageEntryBuilder.cs",
            "SisterBackstageHostController.cs",
            "SisterBackstageTheme.cs"
        })
        {
            var source = File.ReadAllText(Path.Combine(shellProject, fileName));
            source.Should().Contain("namespace Free.Shared.Shell.Wpf;");
            File.Exists(Path.Combine(ribbonProject, fileName)).Should().BeFalse();
        }

        File.ReadAllText(Path.Combine(ribbonProject, "Free.Shared.Ribbon.Wpf.csproj"))
            .Should().NotContain("Free.Shared.AppServices");
    }


    private static void AssertBefore(string source, string first, string second)
    {
        source.IndexOf(first, StringComparison.Ordinal)
            .Should()
            .BeLessThan(source.IndexOf(second, StringComparison.Ordinal), $"{first} should appear before {second}");
    }
}
