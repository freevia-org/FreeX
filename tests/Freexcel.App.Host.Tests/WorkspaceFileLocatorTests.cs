using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class WorkspaceFileLocatorTests
{
    [Fact]
    public void Find_UsesFreexcelRepoRootOverride()
    {
        var previous = Environment.GetEnvironmentVariable("FREEXCEL_REPO_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("FREEXCEL_REPO_ROOT", Environment.CurrentDirectory);

            WorkspaceFileLocator.Find("tests", "Freexcel.App.Host.Tests", "WorkspaceFileLocatorTests.cs")
                .Should()
                .EndWith(Path.Combine("tests", "Freexcel.App.Host.Tests", "WorkspaceFileLocatorTests.cs"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FREEXCEL_REPO_ROOT", previous);
        }
    }
}
