using Avalonia.Input;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class AvaloniaKeyModifiersMapperTests
{
    [Fact]
    public void FromAvalonia_MapsEveryModifierAndKeepsCommandAsMetaByDefault()
    {
        AvaloniaKeyModifiersMapper
            .FromAvalonia(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta)
            .Should()
            .Be(FreeKeyModifiers.Control | FreeKeyModifiers.Shift | FreeKeyModifiers.Alt | FreeKeyModifiers.Meta);
        AvaloniaKeyModifiersMapper.FromAvalonia(KeyModifiers.None).Should().Be(FreeKeyModifiers.None);
    }

    [Fact]
    public void FromAvalonia_DropPolicyRemovesOnlyMeta()
    {
        AvaloniaKeyModifiersMapper
            .FromAvalonia(KeyModifiers.Shift | KeyModifiers.Meta, KeyModifierMetaPolicy.Drop)
            .Should()
            .Be(FreeKeyModifiers.Shift);
    }

    [Fact]
    public void FromAvalonia_AsControlPolicyFoldsMetaIntoControlAndClearsMeta()
    {
        var mapped = AvaloniaKeyModifiersMapper.FromAvalonia(KeyModifiers.Meta, KeyModifierMetaPolicy.AsControl);

        mapped.Should().Be(FreeKeyModifiers.Control);
        mapped.HasFlag(FreeKeyModifiers.Meta).Should().BeFalse();
    }

    [Theory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Shift | KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta)]
    public void ToAvalonia_RoundTripsKeepPolicy(KeyModifiers modifiers) =>
        AvaloniaKeyModifiersMapper.ToAvalonia(AvaloniaKeyModifiersMapper.FromAvalonia(modifiers)).Should().Be(modifiers);
}
