using Free.Shared.Ribbon;

namespace FreeP.Ribbon.Definitions.Tests;

public sealed class FreePRibbonDefinitionProfileTests
{
    private static readonly string[] WpfOnlyTabIds =
    [
        "design",
        "transitions",
        "animations",
    ];

    private static readonly string[] AvaloniaOnlyShellCommands =
    [
        "freep.undo",
        "freep.redo",
        "freep.slideshow.from-current",
    ];

    [Fact]
    public void Shared_factory_builds_valid_wpf_and_avalonia_profiles()
    {
        var wpf = FreePRibbon.Build(FreePRibbonCapabilities.Wpf);
        var avalonia = FreePRibbon.Build(FreePRibbonCapabilities.Avalonia);

        wpf.Tabs.Select(tab => tab.Id)
            .Should()
            .Equal("home", "insert", "design", "transitions", "animations");
        avalonia.Tabs.Select(tab => tab.Id)
            .Should()
            .Equal("home", "insert");

        RibbonDefinitionValidator.Validate(wpf).HasErrors.Should().BeFalse();
        RibbonDefinitionValidator.Validate(avalonia).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Profile_tab_ids_match_except_named_capability_deltas()
    {
        var wpfTabIds = FreePRibbon.Build(FreePRibbonCapabilities.Wpf).Tabs.Select(tab => tab.Id).ToArray();
        var avaloniaTabIds = FreePRibbon.Build(FreePRibbonCapabilities.Avalonia).Tabs.Select(tab => tab.Id).ToArray();

        wpfTabIds.Except(avaloniaTabIds, StringComparer.Ordinal)
            .Should()
            .BeEquivalentTo(WpfOnlyTabIds);
        avaloniaTabIds.Except(wpfTabIds, StringComparer.Ordinal)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Avalonia_backed_content_commands_are_wpf_commands_except_named_shell_aliases()
    {
        var wpfIds = CommandIds(FreePRibbon.Build(FreePRibbonCapabilities.Wpf))
            .ToHashSet(StringComparer.Ordinal);
        var unexpectedAvaloniaOnly = CommandIds(FreePRibbon.Build(FreePRibbonCapabilities.Avalonia))
            .Where(commandId => !wpfIds.Contains(commandId))
            .Where(commandId => !IsAllowedAvaloniaShellCommand(commandId))
            .ToArray();

        unexpectedAvaloniaOnly.Should().BeEmpty(
            "cross-platform content commands should come from the shared FreeP command surface");
    }

    [Fact]
    public void Definition_project_stays_platform_neutral()
    {
        var project = File.ReadAllText(RepoFile("freep", "FreeP.Ribbon.Definitions", "FreeP.Ribbon.Definitions.csproj"));
        project.Should().Contain(@"..\..\shared\Free.Shared.Ribbon\Free.Shared.Ribbon.csproj");
        project.Should().NotContain("UseWPF");
        project.Should().NotContain("Free.Shared.Ribbon.Wpf");
        project.Should().NotContain("Free.Shared.Ribbon.Avalonia");
        project.Should().NotContain("PackageReference Include=\"Avalonia");

        var sourceFiles = Directory.GetFiles(
            RepoPath("freep", "FreeP.Ribbon.Definitions"),
            "*.cs",
            SearchOption.AllDirectories);
        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            source.Should().NotContain("using System.Windows");
            source.Should().NotContain("using Avalonia");
            source.Should().NotContain("PresentationFramework");
        }
    }

    [Fact]
    public void App_adapters_delegate_to_shared_definition_without_local_builders()
    {
        var host = File.ReadAllText(RepoFile("freep", "FreeP.App.Host", "FreePRibbon.cs"));
        host.Should().Contain("FreeP.Ribbon.Definitions.FreePRibbon.Build(FreePRibbonCapabilities.Wpf)");
        host.Should().NotContain("new RibbonDefinitionBuilder");

        var avalonia = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "FreePRibbonAvalonia.cs"));
        avalonia.Should().Contain("FreeP.Ribbon.Definitions.FreePRibbon.Build(FreePRibbonCapabilities.Avalonia)");
        avalonia.Should().NotContain("new RibbonDefinitionBuilder");
    }

    [Fact]
    public void App_projects_reference_shared_definition_project()
    {
        File.ReadAllText(RepoFile("freep", "FreeP.App.Host", "FreeP.App.Host.csproj"))
            .Should()
            .Contain(@"..\FreeP.Ribbon.Definitions\FreeP.Ribbon.Definitions.csproj");
        File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj"))
            .Should()
            .Contain(@"..\FreeP.Ribbon.Definitions\FreeP.Ribbon.Definitions.csproj");
    }

    private static bool IsAllowedAvaloniaShellCommand(string commandId) =>
        commandId.StartsWith("freep.file.", StringComparison.Ordinal) ||
        AvaloniaOnlyShellCommands.Contains(commandId, StringComparer.Ordinal);

    private static IEnumerable<string> CommandIds(RibbonDefinition definition)
    {
        foreach (var tab in definition.Tabs)
        {
            foreach (var group in tab.Groups)
            {
                foreach (var control in group.Controls)
                {
                    if (!string.IsNullOrEmpty(control.CommandId.Value))
                        yield return control.CommandId.Value;

                    foreach (var menuCommandId in MenuCommandIds(control))
                        yield return menuCommandId;
                }
            }
        }
    }

    private static IEnumerable<string> MenuCommandIds(RibbonControl control)
    {
        var menu = control switch
        {
            RibbonSplitButton splitButton => splitButton.Menu,
            RibbonDropdown dropdown => dropdown.Menu,
            _ => null,
        };

        return menu is null
            ? Array.Empty<string>()
            : MenuCommandIds(menu.Items);
    }

    private static IEnumerable<string> MenuCommandIds(IEnumerable<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } commandId)
                yield return commandId.Value;

            foreach (var childCommandId in MenuCommandIds(item.Children))
                yield return childCommandId;
        }
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine(RepoRoot(), Path.Combine(parts));

    private static string RepoPath(params string[] parts) =>
        Path.Combine(RepoRoot(), Path.Combine(parts));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "freep")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the FreeX repo root.");
    }
}
