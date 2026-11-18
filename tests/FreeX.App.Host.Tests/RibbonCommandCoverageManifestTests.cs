using System.IO;
using System.Text.Json;
using FluentAssertions;
using FreeX.App.Presentation.Interactions;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonCommandCoverageManifestTests
{
    [Fact]
    public void Manifest_ContainsEveryCanonicalRibbonCommandExactlyOnce()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var manifestPath = Path.Combine(root, "docs", "testing", "freex-command-surface.jsonl");
        var lines = File.ReadAllLines(manifestPath);
        lines.Should().NotBeEmpty();

        using var header = JsonDocument.Parse(lines[0]);
        header.RootElement.GetProperty("RecordType").GetString().Should().Be("manifest");
        header.RootElement.GetProperty("Scope").GetString().Should().Be("ribbon");

        var commands = lines.Skip(1).Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            commands.Should().OnlyContain(command =>
                command.RootElement.GetProperty("RecordType").GetString() == "command" &&
                command.RootElement.GetProperty("Surfaces").GetArrayLength() > 0);

            var manifestCommandIds = commands
                .Select(command => command.RootElement.GetProperty("CommandId").GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var canonicalCommandIds = FreeXRibbonCommandCatalog.All
                .Select(commandId => commandId.Value)
                .Order(StringComparer.Ordinal)
                .ToArray();

            manifestCommandIds.Should().OnlyHaveUniqueItems();
            manifestCommandIds.Should().Equal(canonicalCommandIds);
        }
        finally
        {
            foreach (var command in commands)
                command.Dispose();
        }
    }

    [Fact]
    public void InteractionSurfaceManifest_ContainsEveryCatalogSurfaceExactlyOnce()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var manifestPath = Path.Combine(root, "docs", "testing", "freex-interaction-surface-catalog.jsonl");
        var lines = File.ReadAllLines(manifestPath);
        lines.Should().NotBeEmpty();

        using var header = JsonDocument.Parse(lines[0]);
        header.RootElement.GetProperty("RecordType").GetString().Should().Be("manifest");
        header.RootElement.GetProperty("Scope").GetString().Should().Be("interaction-surfaces");

        var surfaces = lines.Skip(1).Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            surfaces.Should().OnlyContain(surface =>
                surface.RootElement.GetProperty("RecordType").GetString() == "surface" &&
                surface.RootElement.GetProperty("MouseRoute").GetString() == "pending" &&
                surface.RootElement.GetProperty("KeyboardRoute").GetString() == "pending");

            var manifestIds = surfaces
                .Select(surface => surface.RootElement.GetProperty("Surface").GetProperty("Id").GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var catalogIds = InteractionSurfaceCatalog.Rows
                .Select(surface => surface.Id)
                .Order(StringComparer.Ordinal)
                .ToArray();

            manifestIds.Should().OnlyHaveUniqueItems();
            manifestIds.Should().Equal(catalogIds);
        }
        finally
        {
            foreach (var surface in surfaces)
                surface.Dispose();
        }
    }
}
