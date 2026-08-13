extern alias ProductionWpf;

using System.Reflection;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WpfRendererTestAccessOwnershipTests
{
    private static readonly string[] MainWindowTestMembers =
    [
        "RaiseFormulaReferenceGripDragForTest",
        "FormulaBoxTextForTest",
        "BeginFormulaPointModeEditForTest",
        "RaiseFormulaBoxKeyDownForTest",
        "RouteFormulaPointSelectionForTest",
        "SelectRangeForTest",
        "FindRenderedRibbonCommandControlForTest",
        "PopulateTableDesignStyleGalleryMenuForTest"
    ];

    private static readonly string[] MainWindowTestFields =
    [
        "_reservationPasswordPromptOverrideForTest",
        "_wheelScrollLinesTestOverride"
    ];

    [Fact]
    public void ShippingRenderer_DoesNotOwnTestAccessMembers()
    {
        var assembly = typeof(ProductionWpf::FreeX.App.Host.MainWindow).Assembly;
        var mainWindow = assembly.GetType("FreeX.App.Host.MainWindow")!;
        var prewarmer = assembly.GetType("FreeX.App.Host.StartupPipelinePrewarmer")!;

        GetMemberNames(mainWindow).Should().NotContain(MainWindowTestMembers);
        GetMemberNames(mainWindow).Should().NotContain(MainWindowTestFields);
        GetMemberNames(prewarmer).Should().NotContain("RunPrewarmForTests");

    }

    [Fact]
    public void ToolRenderer_OwnsTestAccessMembers()
    {
        GetMemberNames(typeof(MainWindow)).Should().Contain(MainWindowTestMembers);
        GetMemberNames(typeof(MainWindow)).Should().Contain(MainWindowTestFields);
        GetMemberNames(typeof(StartupPipelinePrewarmer)).Should().Contain("RunPrewarmForTests");

    }

    private static string[] GetMemberNames(Type type) =>
        type.GetMembers(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic)
            .Select(member => member.Name)
            .ToArray();
}
