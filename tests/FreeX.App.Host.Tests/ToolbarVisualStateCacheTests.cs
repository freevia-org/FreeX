using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ToolbarVisualStateCacheTests
{
    [Fact]
    public void GetOrCreate_ReusesStateWhenStyleSourceIsUnchanged()
    {
        var cache = new ToolbarVisualStateCache();
        var workbookId = WorkbookId.New();
        var styleId = new StyleId(4);
        var calls = 0;

        cache.GetOrCreate(workbookId, styleId, CreateState);
        var second = cache.GetOrCreate(workbookId, styleId, CreateState);

        calls.Should().Be(1);
        second.Should().Be(new ToolbarVisualState(
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
            return ToolbarVisualState.From(new CellStyle { Bold = true });
        }
    }

    [Fact]
    public void GetOrCreate_DoesNotRebuildFormattingStateWhenUndoAvailabilityChanges()
    {
        var cache = new ToolbarVisualStateCache();
        var workbookId = WorkbookId.New();
        var styleId = new StyleId(4);
        var calls = 0;
        var canUndo = false;

        cache.GetOrCreate(workbookId, styleId, CreateState);
        canUndo = true;
        var second = cache.GetOrCreate(workbookId, styleId, CreateState);

        calls.Should().Be(1);
        second.Bold.Should().BeFalse();
        return;

        ToolbarVisualState CreateState()
        {
            calls++;
            return ToolbarVisualState.From(new CellStyle { Bold = canUndo });
        }
    }

    [Fact]
    public void GetOrCreate_RebuildsStateWhenStyleChanges()
    {
        var cache = new ToolbarVisualStateCache();
        var workbookId = WorkbookId.New();
        var calls = 0;

        cache.GetOrCreate(workbookId, new StyleId(4), CreateState);
        cache.GetOrCreate(workbookId, new StyleId(5), CreateState);

        calls.Should().Be(2);
        return;

        ToolbarVisualState CreateState()
        {
            calls++;
            return ToolbarVisualState.From(CellStyle.Default);
        }
    }

    [Fact]
    public void GetOrCreate_RebuildsStateWhenWorkbookChanges()
    {
        var cache = new ToolbarVisualStateCache();
        var styleId = new StyleId(4);
        var calls = 0;

        cache.GetOrCreate(WorkbookId.New(), styleId, CreateState);
        cache.GetOrCreate(WorkbookId.New(), styleId, CreateState);

        calls.Should().Be(2);
        return;

        ToolbarVisualState CreateState()
        {
            calls++;
            return ToolbarVisualState.From(CellStyle.Default);
        }
    }
}
