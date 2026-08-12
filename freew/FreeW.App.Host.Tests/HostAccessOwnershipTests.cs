using System.IO;
using FreeW.TestSupport;

namespace FreeW.App.Host.Tests;

public sealed class HostAccessOwnershipTests
{
    [Fact]
    public void TestVariant_OwnsMovedAccess_WhileShippingSourcesDoNot()
    {
        var assembly = typeof(MainWindow).Assembly;
        AssertSupportMember(assembly, "FreeW.App.Host.MainWindow", "IsReadModeActiveForTests");
        AssertSupportMember(assembly, "FreeW.App.Host.Editing.DocumentView", "NativeSpellCheckEnabledForTest");
        AssertSupportMember(assembly, "FreeW.App.Host.Editing.DocumentView", "SimulateTypeText");
        AssertSupportMember(assembly, "FreeW.App.Host.AutosaveCoordinator", "SnapshotNowForTests");
        AssertSupportMember(assembly, "FreeW.App.Host.CompareDocumentsDialog", "CreateForTest");
        AssertSupportMember(assembly, "FreeW.App.Host.Editing.OutlineView", "VisibleRows");

        var root = HostAccessOwnershipAssertions.FindRepositoryRoot();
        var projectDirectory = Path.Combine(root, "freew", "FreeW.App.Host");
        HostAccessOwnershipAssertions.ShippingSourceHookViolations(projectDirectory).Should().BeEmpty();
        File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "FreeW.App.Host.csproj"))
            .Should().Contain("Condition=\"'$(FreeWHostTestSupport)' == 'true'\"");
    }

    [Fact]
    public void NormalShippingAssembly_ExcludesMovedAccess()
    {
        var root = HostAccessOwnershipAssertions.FindRepositoryRoot();
        var assemblyPath = Path.Combine(
            root,
            "freew",
            "FreeW.App.Host",
            "bin",
            HostAccessOwnershipAssertions.CurrentConfiguration(),
            "net10.0-windows10.0.19041.0",
            "FreeW.App.Host.dll");
        File.Exists(assemblyPath).Should().BeTrue($"the normal shipping variant must be built at {assemblyPath}");

        var names = HostAccessOwnershipAssertions.AssemblyMemberNames(assemblyPath);
        names.Should().NotContain(name => name.Contains("ForTest", StringComparison.Ordinal));
        foreach (var movedMember in new[]
        {
            "FreeW.App.Host.Editing.DocumentView.SimulateTypeText",
            "FreeW.App.Host.Editing.DocumentView.ViewDepthLayout",
            "FreeW.App.Host.Editing.OutlineView.VisibleRows"
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
