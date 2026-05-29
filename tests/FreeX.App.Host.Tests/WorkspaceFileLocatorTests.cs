using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WorkspaceFileLocatorTests
{
    [Fact]
    public void Find_UsesFreeXRepoRootOverride()
    {
        var previous = Environment.GetEnvironmentVariable("FREEX_REPO_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("FREEX_REPO_ROOT", Environment.CurrentDirectory);

            WorkspaceFileLocator.Find("tests", "FreeX.App.Host.Tests", "WorkspaceFileLocatorTests.cs")
                .Should()
                .EndWith(Path.Combine("tests", "FreeX.App.Host.Tests", "WorkspaceFileLocatorTests.cs"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FREEX_REPO_ROOT", previous);
        }
    }
}
