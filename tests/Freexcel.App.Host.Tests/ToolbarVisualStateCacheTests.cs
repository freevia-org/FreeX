using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ToolbarVisualStateCacheTests
{
    [Fact]
    public void GetOrCreate_ReusesStateWhenStyleAndUndoRedoSourceAreUnchanged()
    {
        var cache = new ToolbarVisualStateCache();
        var styleId = new StyleId(4);
        var calls = 0;

        cache.GetOrCreate(styleId, canUndo: true, canRedo: false, CreateState);
        var second = cache.GetOrCreate(styleId, canUndo: true, canRedo: false, CreateState);

        calls.Should().Be(1);
        second.Should().Be(new ToolbarVisualState(
            CanUndo: true,
            CanRedo: false,
            Bold: true,
            Italic: false,
            Underline: false,
            Strikethrough: false,
            VerticalAlignment: VerticalAlignment.Bottom,
            HorizontalAlignment: HorizontalAlignment.General,
            WrapText: false,
            FontName: "Calibri",
            FontSizeText: "11"));
        return;

        ToolbarVisualState CreateState()
        {
            calls++;
            return ToolbarVisualState.From(new CellStyle { Bold = true }, canUndo: true, canRedo: false);
        }
    }

    [Fact]
    public void GetOrCreate_RebuildsStateWhenUndoAvailabilityChanges()
    {
        var cache = new ToolbarVisualStateCache();
        var styleId = new StyleId(4);
        var calls = 0;

        cache.GetOrCreate(styleId, canUndo: false, canRedo: false, () => CreateState(false));
        var second = cache.GetOrCreate(styleId, canUndo: true, canRedo: false, () => CreateState(true));

        calls.Should().Be(2);
        second.CanUndo.Should().BeTrue();
        return;

        ToolbarVisualState CreateState(bool canUndo)
        {
            calls++;
            return ToolbarVisualState.From(CellStyle.Default, canUndo, canRedo: false);
        }
    }
}
