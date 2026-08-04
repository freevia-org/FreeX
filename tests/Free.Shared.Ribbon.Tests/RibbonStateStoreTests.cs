using System.Collections.Generic;

namespace Free.Shared.Ribbon.Tests;

public class RibbonStateStoreTests
{
    [Fact]
    public void GetState_Unset_ReturnsDefault()
    {
        var store = new RibbonStateStore();

        store.GetState("Bold").Should().Be(RibbonCommandState.Default);
        store.TryGetState("Bold", out _).Should().BeFalse();
    }

    [Fact]
    public void SetChecked_UpdatesStateAndRaisesEvent()
    {
        var store = new RibbonStateStore();
        var events = new List<RibbonStateChangedEventArgs>();
        store.StateChanged += (_, e) => events.Add(e);

        store.SetChecked("Bold", true);

        store.GetState("Bold").IsChecked.Should().BeTrue();
        store.TryGetState("Bold", out var state).Should().BeTrue();
        state.IsChecked.Should().BeTrue();
        events.Should().ContainSingle();
        events[0].Id.Should().Be(new RibbonCommandId("Bold"));
        events[0].State.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void Setters_MergeIndependentFacets()
    {
        var store = new RibbonStateStore();

        store.SetChecked("Italic", true);
        store.SetEnabled("Italic", false);
        store.SetValue("Italic", "x");

        var state = store.GetState("Italic");
        state.IsChecked.Should().BeTrue();
        state.IsEnabled.Should().BeFalse();
        state.Value.Should().Be("x");
    }

    [Fact]
    public void SetValue_UpdatesComboValue()
    {
        var store = new RibbonStateStore();

        store.SetValue("Font", "Calibri");

        store.GetState("Font").Value.Should().Be("Calibri");
    }

    [Fact]
    public void NoOpWrite_DoesNotRaiseEvent()
    {
        var store = new RibbonStateStore();
        store.SetChecked("Bold", true);

        var raised = 0;
        store.StateChanged += (_, _) => raised++;
        store.SetChecked("Bold", true);

        raised.Should().Be(0);
    }

    [Fact]
    public void SetState_ReplacesWholeState()
    {
        var store = new RibbonStateStore();
        store.SetChecked("Bold", true);

        store.SetState("Bold", new RibbonCommandState(IsEnabled: false));

        var state = store.GetState("Bold");
        state.IsChecked.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void DistinctCommands_AreIndependent()
    {
        var store = new RibbonStateStore();

        store.SetChecked("Bold", true);
        store.SetChecked("Italic", false);

        store.GetState("Bold").IsChecked.Should().BeTrue();
        store.GetState("Italic").IsChecked.Should().BeFalse();
    }
}
