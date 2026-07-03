using System;
using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ProofingLanguageCatalogSourceGuardTests
{
    [Fact]
    public void FreeWRibbonCommands_UsesSharedProofingLanguageCatalog()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs"));

        source.Should().Contain("ProofingLanguageCatalog.CommonLanguages");
        source.Should().NotContain("private static readonly (string Tag, string Label)[] Languages");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FreeW.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
