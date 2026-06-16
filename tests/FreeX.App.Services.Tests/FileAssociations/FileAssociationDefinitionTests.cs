using FluentAssertions;
using FreeX.App.Services.FileAssociations;
using Xunit;

namespace FreeX.App.Services.Tests.FileAssociations;

public class FileAssociationDefinitionTests
{
    [Fact]
    public void Catalog_OwnsOnlyFxl()
    {
        var owned = FileAssociationDefinition.All
            .Where(d => d.Ownership == AssociationOwnership.Default)
            .Select(d => d.Extension)
            .ToArray();

        owned.Should().BeEquivalentTo(new[] { ".fxl" });
    }

    [Fact]
    public void Catalog_OffersNeutralTypesWithoutStealingDefault()
    {
        foreach (var ext in new[] { ".csv", ".tsv", ".tab", ".txt", ".xml", ".xlsx", ".xls" })
        {
            var def = FileAssociationDefinition.All.Single(d => d.Extension == ext);
            def.Ownership.Should().Be(AssociationOwnership.OpenWith,
                $"{ext} must be offered via Open With, never made the default handler");
        }
    }

    [Fact]
    public void EveryDefinition_HasProgIdAndFriendlyName()
    {
        foreach (var def in FileAssociationDefinition.All)
        {
            def.ProgId.Should().StartWith("FreeX.");
            def.FriendlyName.Should().NotBeNullOrWhiteSpace();
            def.Extension.Should().StartWith(".");
        }
    }
}
