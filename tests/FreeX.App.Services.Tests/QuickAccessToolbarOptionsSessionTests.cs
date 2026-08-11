using FluentAssertions;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Services.Tests;

public sealed class QuickAccessToolbarOptionsSessionTests
{
    [Fact]
    public void MutationsAndReset_UseOneNormalizedSessionState()
    {
        var session = new QuickAccessToolbarOptionsSession(
            ["Save", "Undo"],
            quickAccessToolbarBelowRibbon: false);

        session.Apply("Redo", QuickAccessToolbarCustomizationAction.Add);
        session.Move("Redo", -1);
        session.Apply("Save", QuickAccessToolbarCustomizationAction.Remove);

        session.CommandIds.Should().Equal("Redo", "Undo");
        session.SetPlacement(true);
        session.QuickAccessToolbarBelowRibbon.Should().BeTrue();

        session.Reset();
        session.CommandIds.Should().Equal(QuickAccessToolbarCatalog.DefaultCommandIds);
    }

    [Fact]
    public void ImportAndExport_AdoptAndWriteTheCurrentSessionState()
    {
        using var temp = new TestTemporaryDirectory();
        var importPath = Path.Combine(temp.Path, "import.freex-qat.json");
        var exportPath = Path.Combine(temp.Path, "export.freex-qat.json");
        File.WriteAllText(
            importPath,
            QuickAccessToolbarCustomizationFile.Serialize(
                ["Save", "Redo"],
                quickAccessToolbarBelowRibbon: true));
        var session = new QuickAccessToolbarOptionsSession(["Save"], false);

        var import = session.TryImport(importPath);
        var exported = session.TryExport(exportPath, out var errorMessage);

        import.Success.Should().BeTrue();
        session.CommandIds.Should().Equal("Save", "Redo");
        session.QuickAccessToolbarBelowRibbon.Should().BeTrue();
        exported.Should().BeTrue();
        errorMessage.Should().BeNull();
        var roundTrip = QuickAccessToolbarCustomizationFile.TryLoad(exportPath);
        roundTrip.Success.Should().BeTrue();
        roundTrip.Customization!.CommandIds.Should().Equal("Save", "Redo");
        roundTrip.Customization.QuickAccessToolbarBelowRibbon.Should().BeTrue();
    }
}
