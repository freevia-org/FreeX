namespace Free.Shared.Ribbon.Tests;

public class RibbonCommandContextTests
{
    [Fact]
    public void ForSelectedValue_RoundTripsThroughSelectedValue()
    {
        var context = RibbonCommandContext.ForSelectedValue("14");

        Assert.Equal("14", context.SelectedValue);
        Assert.Equal("14", context.Parameters[RibbonCommandContext.SelectedValueKey]);
    }

    [Fact]
    public void ForSelectedValue_Null_YieldsNullSelectedValue()
    {
        var context = RibbonCommandContext.ForSelectedValue(null);

        Assert.Null(context.SelectedValue);
    }

    [Fact]
    public void Empty_HasNoSelectedValue()
    {
        Assert.Null(RibbonCommandContext.Empty.SelectedValue);
    }
}
