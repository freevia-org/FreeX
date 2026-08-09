namespace Free.Shared.Theme.Tests;

public sealed class ThemeResourceResolutionOwnershipTests
{
    [Fact]
    public void PortableLookupCore_DoesNotOwnNativeUiTypes()
    {
        var source = Read("shared", "Free.Shared.Theme", "ThemeResourceLookup.cs");

        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia");
        source.Should().NotContain("ResourceDictionary");
        source.Should().NotContain("SolidColorBrush");
    }

    [Fact]
    public void SisterRenderers_DelegateApplicationThemeLookupToSharedAdapters()
    {
        var wpfSources = new[]
        {
            Read("freew", "FreeW.App.Host", "MainWindow.cs"),
            Read("freep", "FreeP.App.Host", "MainWindow.cs"),
        };
        var avaloniaSources = new[]
        {
            Read("src", "FreeX.App.Avalonia", "MainWindow.cs"),
            Read("src", "FreeX.App.Avalonia", "DialogControlStyles.cs"),
            Read("freew", "FreeW.App.Avalonia", "MainWindow.cs"),
            Read("freep", "FreeP.App.Avalonia", "MainWindow.cs"),
        };

        foreach (var source in wpfSources)
        {
            source.Should().Contain("WpfThemeResourceResolver");
            source.Should().NotContain("Application.Current?.Resources[key]");
        }

        foreach (var source in avaloniaSources)
        {
            source.Should().Contain("AvaloniaThemeResourceResolver");
            source.Should().NotContain("TryGetResource(key");
        }
    }

    [Fact]
    public void FreeWAndFreePRenderers_DoNotReintroduceLocalThemeResolverMethodsOrKeyLiterals()
    {
        var sources = new Dictionary<string, string>
        {
            ["FreeW WPF"] = Read("freew", "FreeW.App.Host", "MainWindow.cs"),
            ["FreeW Avalonia"] = Read("freew", "FreeW.App.Avalonia", "MainWindow.cs"),
            ["FreeP WPF"] = Read("freep", "FreeP.App.Host", "MainWindow.cs"),
            ["FreeP Avalonia"] = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs"),
        };

        foreach (var (renderer, source) in sources)
        {
            source.Should().NotContain("ResolveThemeBrush(", renderer);
            source.Should().NotContain("ResolveTokenBrush(", renderer);
            source.Should().NotContain("ResolveTokenColor(", renderer);
            source.Should().Contain("ProductThemeResourceProfiles.", renderer);
        }

        sources["FreeW WPF"].Should().NotContain("\"FreeWTitleBarBrush\"");
        sources["FreeW Avalonia"].Should().NotContain("\"FreeWStatusSurfaceBrush\"");
        sources["FreeP WPF"].Should().NotContain("\"FreePTitleBarBrush\"");
        sources["FreeP Avalonia"].Should().NotContain("\"FreePStatusSurfaceBrush\"");
    }

    private static string Read(params string[] pathSegments)
    {
        var fullPathSegments = new string[pathSegments.Length + 1];
        fullPathSegments[0] = FindRepositoryRoot();
        pathSegments.CopyTo(fullPathSegments, 1);
        return File.ReadAllText(Path.Combine(fullPathSegments));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing FreeX.slnx.");
    }
}
