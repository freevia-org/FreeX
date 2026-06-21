using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;
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
            FindRepositoryRoot(),
            appFolder,
            projectFolder,
            "MainWindow.cs"));

        source.Should().Contain("RibbonFileTabStyle.Build(");
        source.Should().Contain("AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback(");
        source.Should().NotContain("new FrameworkElementFactory(typeof(Border), \"FileTabBorder\")");
        source.Should().NotContain("$\"%LOCALAPPDATA%\\\\{AppProduct.Current.ProductDirectoryName}\"");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
