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

    [Fact]
    public void Flags_DuplicateKeytips_CaseInsensitive()
    {
        // Two buttons with keytips that differ only by case ('F' vs 'f') collide at runtime
        // because keytip matching is OrdinalIgnoreCase.  The validator must flag RBN004.
        var group = new RibbonGroup(
            "g", "G", "G", 1,
            new RibbonControl[]
            {
                new RibbonButton("cmd1", "Format") { KeyTip = "F" },
                new RibbonButton("cmd2", "Fill")   { KeyTip = "f" },
            },
            new RibbonGroupSizing(new[] { RibbonAdaptiveGroupState.Full }));

        var def = new RibbonDefinition(new[]
        {
            new RibbonTab("home", "Home", "H", null, new[] { group })
        });

        var diagnostics = RibbonDefinitionValidator.Validate(def);

        diagnostics.Items.Should().Contain(d => d.Code == "RBN004",
            because: "keytips 'F' and 'f' are case-insensitively identical and collide at runtime");
    }

    [Fact]
    public void Does_Not_Flag_Keytips_WhenAllDistinctCaseInsensitively()
    {
        // Two buttons with entirely distinct keytips (different letters) must NOT trigger RBN004.
        var def = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", t => t
                .Group("g", "G", "G", 1, g => g
                    .Button("cmd1", "Format", b => b with { KeyTip = "F" })
                    .Button("cmd2", "Bold",   b => b with { KeyTip = "B" })))
            .Build();

        var diagnostics = RibbonDefinitionValidator.Validate(def);

        diagnostics.Items.Should().NotContain(d => d.Code == "RBN004");
    }
}
