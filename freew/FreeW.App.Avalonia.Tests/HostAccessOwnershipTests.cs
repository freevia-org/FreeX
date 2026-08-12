using System.IO;
using FreeW.TestSupport;

namespace FreeW.App.Avalonia.Tests;

public sealed class HostAccessOwnershipTests
{
    [Fact]
    public void TestVariant_OwnsMovedAccess_WhileShippingSourcesDoNot()
    {
        var assembly = typeof(MainWindow).Assembly;
        AssertSupportMember(assembly, "FreeW.App.Avalonia.MainWindow", "RibbonKeyTipsVisibleForTest");
        AssertSupportMember(assembly, "FreeW.App.Avalonia.Editing.DocumentView", "CaretRectForTest");
        AssertSupportMember(assembly, "FreeW.App.Avalonia.Editing.DocumentView", "HandleRectsForSelection");
        AssertSupportMember(assembly, "FreeW.App.Avalonia.AutosaveAdapter", "SnapshotNowForTests");
        AssertSupportMember(assembly, "FreeW.App.Avalonia.PasswordPromptDialog", "CreateForTest");
        AssertSupportMember(assembly, "FreeW.App.Avalonia.NavigationPane", "HeadingItemCount");
        AssertSupportMember(assembly, "FreeW.App.Avalonia.Editing.OutlineView", "VisibleRows");

        var root = HostAccessOwnershipAssertions.FindRepositoryRoot();
        var projectDirectory = Path.Combine(root, "freew", "FreeW.App.Avalonia");
        HostAccessOwnershipAssertions.ShippingSourceHookViolations(projectDirectory).Should().BeEmpty();
        File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"))
            .Should().Contain("Condition=\"'$(FreeWHostTestSupport)' == 'true'\"");
    }

    [Fact]
    public void NormalShippingAssembly_ExcludesMovedAccess()
    {
        var root = HostAccessOwnershipAssertions.FindRepositoryRoot();
        var assemblyPath = Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "bin",
            HostAccessOwnershipAssertions.CurrentConfiguration(),
            "net10.0",
            "FreeW.dll");
        File.Exists(assemblyPath).Should().BeTrue($"the normal shipping variant must be built at {assemblyPath}");

        var names = HostAccessOwnershipAssertions.AssemblyMemberNames(assemblyPath);
        names.Should().NotContain(name => name.Contains("ForTest", StringComparison.Ordinal));
        foreach (var movedMember in new[]
        {
            "FreeW.App.Avalonia.Editing.DocumentView.HandleRectsForSelection",
            "FreeW.App.Avalonia.Editing.DocumentView.BeginFloatDrag",
            "FreeW.App.Avalonia.Editing.DocumentView.CaretRectForTest",
            "FreeW.App.Avalonia.NavigationPane.HeadingItemCount",
            "FreeW.App.Avalonia.Editing.OutlineView.VisibleRows"
        })
            names.Should().NotContain(movedMember);
    }

    private static void AssertSupportMember(System.Reflection.Assembly assembly, string typeName, string memberName)
    {
        var type = assembly.GetType(typeName);
        type.Should().NotBeNull();
        HostAccessOwnershipAssertions.MemberNames(type!).Should().Contain(memberName);
    }
}
