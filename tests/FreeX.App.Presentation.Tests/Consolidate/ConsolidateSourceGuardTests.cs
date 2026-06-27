using FluentAssertions;
using FreeX.App.Presentation.Tests;

namespace FreeX.App.Presentation.Tests.Consolidate;

public sealed class ConsolidateSourceGuardTests
{
    [Fact]
    public void ConsolidatePresentationPlanners_DoNotReferencePlatformUiAssemblies()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "Consolidate");

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            var source = File.ReadAllText(file);

            source.Should().NotContain("System.Windows");
            source.Should().NotContain("Avalonia");
            source.Should().NotContain("FreeX.App.Host");
            source.Should().NotContain("FreeX.App.Avalonia");
        }
    }
}
