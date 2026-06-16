namespace Free.Shared.Ribbon.Tests;

public class RibbonDefinitionValidatorTests
{
    [Fact]
    public void Flags_DuplicateTabIds()
    {
        var def = new RibbonDefinition(new[]
        {
            new RibbonTab("home", "Home", "H", null, Array.Empty<RibbonGroup>()),
            new RibbonTab("home", "Home2", "J", null, Array.Empty<RibbonGroup>())
        });

        var diagnostics = RibbonDefinitionValidator.Validate(def);

        diagnostics.HasErrors.Should().BeTrue();
        diagnostics.Items.Should().Contain(d => d.Code == "RBN001");
    }

    [Fact]
    public void Flags_GroupMissingFullVariant()
    {
        var def = new RibbonDefinition(new[]
        {
            new RibbonTab("home", "Home", "H", null, new[]
            {
                new RibbonGroup("g", "G", "G", 1, Array.Empty<RibbonControl>(),
                    new RibbonGroupSizing(new[] { RibbonAdaptiveGroupState.Collapsed }))
            })
        });

        RibbonDefinitionValidator.Validate(def).Items.Should().Contain(d => d.Code == "RBN003");
    }

    [Fact]
    public void Clean_Definition_HasNoErrors()
    {
        var def = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", t => t
                .Group("g", "G", "G", 1, g => g.Button("paste", "Paste")))
            .Build();

        RibbonDefinitionValidator.Validate(def).HasErrors.Should().BeFalse();
    }
}
