using System.IO;
using System.Windows.Input;

namespace Free.Shared.Shell.Wpf.Tests;

public sealed class WpfKeyModifiersMapperTests
{
    [Fact]
    public void FromWpf_MapsEveryModifierAndKeepsWindowsAsMetaByDefault()
    {
        WpfKeyModifiersMapper
            .FromWpf(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows)
            .Should()
            .Be(FreeKeyModifiers.Control | FreeKeyModifiers.Shift | FreeKeyModifiers.Alt | FreeKeyModifiers.Meta);
        WpfKeyModifiersMapper.FromWpf(ModifierKeys.None).Should().Be(FreeKeyModifiers.None);
    }

    [Fact]
    public void FromWpf_DropPolicyRemovesOnlyTheWindowsKey()
    {
        WpfKeyModifiersMapper
            .FromWpf(ModifierKeys.Control | ModifierKeys.Windows, KeyModifierMetaPolicy.Drop)
            .Should()
            .Be(FreeKeyModifiers.Control);
    }

    [Fact]
    public void FromWpf_AsControlPolicyFoldsWindowsIntoControl()
    {
        WpfKeyModifiersMapper
            .FromWpf(ModifierKeys.Windows, KeyModifierMetaPolicy.AsControl)
            .Should()
            .Be(FreeKeyModifiers.Control);
    }

    [Theory]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Shift | ModifierKeys.Alt)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows)]
    public void ToWpf_RoundTripsKeepPolicy(ModifierKeys modifiers) =>
        WpfKeyModifiersMapper.ToWpf(WpfKeyModifiersMapper.FromWpf(modifiers)).Should().Be(modifiers);

    [Fact]
    public void FreeXWpfHostUsesTheSharedMapperInsteadOfLocalModifierSwitches()
    {
        foreach (var file in new[]
                 {
                     "FormulaBarWpfInputAdapter.cs",
                     "KeyboardShortcutMatcher.cs",
                     "ExcelWorksheetNavigationPlanner.cs",
                     "MainWindow.Drawing.cs",
                 })
        {
            var source = File.ReadAllText(TestWorkspaceFileLocator.Find("src", "FreeX.App.Host", file));
            source.Should().Contain("WpfKeyModifiersMapper.FromWpf(", file);
            source.Should().NotContain("modifiers.HasFlag(ModifierKeys.", file);
            source.Should().NotContain("(modifiers & ModifierKeys.", file);
        }
    }
}
