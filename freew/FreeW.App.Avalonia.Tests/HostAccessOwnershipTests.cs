using System.Reflection;

namespace FreeW.App.Avalonia.Tests;

public sealed class HostAccessOwnershipTests
{
    [Fact]
    public void TestVariant_OwnsMovedAccess_WhileShippingSourcesDoNot()
    {
        MemberNames(typeof(MainWindow)).Should().Contain("RibbonKeyTipsVisibleForTest");
        MemberNames(typeof(Editing.DocumentView)).Should().Contain("CaretRectForTest");

        var root = FindRepositoryRoot();
        File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"))
            .Should().NotContain("RibbonKeyTipsVisibleForTest");
        File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"))
            .Should().NotContain("CaretRectForTest");
        File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"))
            .Should().Contain("Condition=\"'$(FreeWHostTestSupport)' == 'true'\"");
    }

    private static string[] MemberNames(Type type) =>
        type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(member => member.Name)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
