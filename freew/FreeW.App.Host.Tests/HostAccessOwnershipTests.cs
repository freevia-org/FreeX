using System.IO;
using System.Reflection;

namespace FreeW.App.Host.Tests;

public sealed class HostAccessOwnershipTests
{
    [Fact]
    public void TestVariant_OwnsMovedAccess_WhileShippingSourcesDoNot()
    {
        MemberNames(typeof(MainWindow)).Should().Contain("IsReadModeActiveForTests");
        MemberNames(typeof(Editing.DocumentView)).Should().Contain("NativeSpellCheckEnabledForTest");

        var root = FindRepositoryRoot();
        File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "MainWindow.cs"))
            .Should().NotContain("IsReadModeActiveForTests");
        File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs"))
            .Should().NotContain("NativeSpellCheckEnabledForTest");
        File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "FreeW.App.Host.csproj"))
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
