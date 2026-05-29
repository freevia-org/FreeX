using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class NativeXmlPreserveBagTests
{
    [Fact]
    public void Get_UnknownKey_ReturnsNull()
    {
        var bag = new NativeXmlPreserveBag();

        bag.Get("missing").Should().BeNull();
    }

    [Fact]
    public void SetGet_RoundTrip_ReturnsStoredValue()
    {
        var bag = new NativeXmlPreserveBag();

        bag.Set("pageSetup", "<e orientation=\"landscape\" />");
        bag.Get("pageSetup").Should().Be("<e orientation=\"landscape\" />");
    }

    [Fact]
    public void Set_NullValue_RemovesExistingKey()
    {
        var bag = new NativeXmlPreserveBag();
        bag.Set("pageSetup", "<e orientation=\"landscape\" />");

        bag.Set("pageSetup", null);

        bag.Get("pageSetup").Should().BeNull();
        bag.Contains("pageSetup").Should().BeFalse();
    }

    [Fact]
    public void Set_NullValue_OnAbsentKey_DoesNotThrow()
    {
        var bag = new NativeXmlPreserveBag();

        var act = () => bag.Set("missing", null);

        act.Should().NotThrow();
        bag.Contains("missing").Should().BeFalse();
    }

    [Fact]
    public void Contains_ExistingKey_ReturnsTrue()
    {
        var bag = new NativeXmlPreserveBag();
        bag.Set("sheetPr", "<e />");

        bag.Contains("sheetPr").Should().BeTrue();
    }

    [Fact]
    public void Contains_AbsentKey_ReturnsFalse()
    {
        var bag = new NativeXmlPreserveBag();

        bag.Contains("anything").Should().BeFalse();
    }

    [Fact]
    public void All_ReflectsCurrentEntries()
    {
        var bag = new NativeXmlPreserveBag();
        bag.Set("a", "valA");
        bag.Set("b", "valB");

        bag.All.Should().HaveCount(2);
        bag.All["a"].Should().Be("valA");
        bag.All["b"].Should().Be("valB");
    }

    [Fact]
    public void Clone_ProducesIndependentCopy()
    {
        var original = new NativeXmlPreserveBag();
        original.Set("pageSetup", "<e orientation=\"portrait\" />");

        var clone = original.Clone();
        clone.Set("pageSetup", "<e orientation=\"landscape\" />");

        original.Get("pageSetup").Should().Be("<e orientation=\"portrait\" />",
            "mutating the clone should not affect the original");
    }

    [Fact]
    public void Clone_IncludesAllKeys()
    {
        var original = new NativeXmlPreserveBag();
        original.Set("a", "1");
        original.Set("b", "2");

        var clone = original.Clone();

        clone.Get("a").Should().Be("1");
        clone.Get("b").Should().Be("2");
    }

    [Fact]
    public void LookupIsCaseInsensitive()
    {
        var bag = new NativeXmlPreserveBag();
        bag.Set("PageSetup", "upper");

        bag.Get("pageSetup").Should().Be("upper");
        bag.Get("PAGESETUP").Should().Be("upper");
        bag.Contains("pagesetup").Should().BeTrue();
    }
}
