using FluentAssertions;
using Free.Shared.Ribbon;
using FreeX.App.Host;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Host.Tests;

public sealed class FormControlInsertRibbonTests
{
    [Fact]
    public void InsertRibbon_ExposesOnlySupportedLegacyFormControlCommandsWithLiveWpfHandlers()
    {
        var controls = FreeXRibbon.Build().FindTab("InsertTab")!
            .Groups.Single(group => group.Id == "InsertControlsGroup")
            .Controls;
        var dropdown = controls.Should().ContainSingle().Subject.Should().BeOfType<RibbonDropdown>().Subject;
        var menuIds = dropdown.Menu.Items
            .Where(item => item.CommandId is not null)
            .Select(item => item.CommandId!.Value.Value)
            .ToArray();
        var expected = new[]
        {
            FreeXRibbonCommandIds.InsertFormControlCheckBox,
            FreeXRibbonCommandIds.InsertFormControlOptionButton,
            FreeXRibbonCommandIds.InsertFormControlButton,
            FreeXRibbonCommandIds.InsertFormControlDropDown,
            FreeXRibbonCommandIds.InsertFormControlListBox,
            FreeXRibbonCommandIds.InsertFormControlSpinner,
            FreeXRibbonCommandIds.InsertFormControlScrollBar,
        };

        dropdown.CommandId.Value.Should().Be(FreeXRibbonCommandIds.InsertFormControls);
        menuIds.Should().Equal(expected);
        FreeXRibbonHandlerMap.Handlers.Keys.Should().Contain(FreeXRibbonCommandIds.InsertFormControls);
        expected.All(FreeXRibbonHandlerMap.Handlers.ContainsKey).Should().BeTrue();
    }
}
