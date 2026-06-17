using System.Collections.Generic;

using FreeX.App.Avalonia.Ribbon;
using Free.Shared.Ribbon;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Verifies the Avalonia ribbon registry binds the shell's host callbacks to the right command ids, so the
/// declarative ribbon invokes the same handlers as the native menus (charts/CF/table/quick-analysis/etc.).
/// Pure registry assertions — no running shell or UI thread required.
/// </summary>
public sealed class AvaloniaRibbonHostCallbackTests
{
    private static readonly RibbonCommandContext EmptyContext =
        new(new Dictionary<string, object?>());

    [Theory]
    [InlineData("data.textToColumns")]
    [InlineData("data.consolidate")]
    [InlineData("insert.table")]
    [InlineData("home.formatAsTable")]
    [InlineData("home.conditional")]
    [InlineData("data.quickAnalysis")]
    [InlineData("data.sortAsc")]
    [InlineData("data.sortDesc")]
    [InlineData("data.filter")]
    [InlineData("data.validation")]
    [InlineData("data.validationDialog")]
    [InlineData("home.cut")]
    [InlineData("home.copy")]
    [InlineData("home.paste")]
    [InlineData("home.alignLeft")]
    [InlineData("home.alignCenter")]
    [InlineData("home.alignRight")]
    [InlineData("home.wrapText")]
    [InlineData("home.merge")]
    [InlineData("home.mergeCenter")]
    [InlineData("home.currency")]
    [InlineData("home.percent")]
    [InlineData("home.comma")]
    public void BuildRegistry_WithCallbacks_BindsRealCommand(string commandId)
    {
        var registry = SampleRibbon.BuildRegistry(() => null, _ => { }, AllWired());

        Assert.True(registry.TryGet(new RibbonCommandId(commandId), out var command));
        Assert.IsType<RelayRibbonCommand>(command);
    }

    [Theory]
    [InlineData("insert.table")]
    [InlineData("home.conditional")]
    [InlineData("data.quickAnalysis")]
    [InlineData("data.sortAsc")]
    public void BuildRegistry_WithoutCallbacks_LeavesNoOp(string commandId)
    {
        var registry = SampleRibbon.BuildRegistry(() => null, _ => { });

        Assert.True(registry.TryGet(new RibbonCommandId(commandId), out var command));
        Assert.IsType<NoOpRibbonCommand>(command);
    }

    [Fact]
    public void WiredCommands_Execute_InvokeTheHostCallback()
    {
        var fired = new HashSet<string>();
        var callbacks = new AvaloniaRibbonHostCallbacks
        {
            OpenTextToColumns = () => fired.Add("textToColumns"),
            OpenConsolidate = () => fired.Add("consolidate"),
            InsertTable = () => fired.Add("table"),
            ConditionalFormatting = () => fired.Add("conditional"),
            QuickAnalysis = () => fired.Add("quickAnalysis"),
            SortAscending = () => fired.Add("sortAsc"),
            SortDescending = () => fired.Add("sortDesc"),
            DataValidation = () => fired.Add("validation"),
            Copy = () => fired.Add("copy"),
            AlignCenter = () => fired.Add("alignCenter"),
            PercentFormat = () => fired.Add("percent"),
        };
        var registry = SampleRibbon.BuildRegistry(() => null, _ => { }, callbacks);

        Execute(registry, "data.quickAnalysis");
        Execute(registry, "insert.table");
        Execute(registry, "home.conditional");
        Execute(registry, "data.sortDesc");
        Execute(registry, "home.copy");
        Execute(registry, "home.alignCenter");
        Execute(registry, "home.percent");

        Assert.Contains("quickAnalysis", fired);
        Assert.Contains("table", fired);
        Assert.Contains("conditional", fired);
        Assert.Contains("sortDesc", fired);
        Assert.Contains("copy", fired);
        Assert.Contains("alignCenter", fired);
        Assert.Contains("percent", fired);
    }

    [Fact]
    public void ExtraCommands_BindParameterizedMenuItems_AndExecute()
    {
        var fired = new List<string>();
        var callbacks = new AvaloniaRibbonHostCallbacks
        {
            ExtraCommands = new Dictionary<string, Action>
            {
                ["home.fmtGeneral"] = () => fired.Add("general"),
                ["home.fmtDate"] = () => fired.Add("date"),
                ["home.fillYellow"] = () => fired.Add("yellow"),
                ["home.bordersAll"] = () => fired.Add("bordersAll"),
                ["home.pasteValues"] = () => fired.Add("pasteValues"),
            },
        };
        var registry = SampleRibbon.BuildRegistry(() => null, _ => { }, callbacks);

        Assert.True(registry.TryGet(new RibbonCommandId("home.fmtGeneral"), out var c));
        Assert.IsType<RelayRibbonCommand>(c);

        Execute(registry, "home.fmtGeneral");
        Execute(registry, "home.fmtDate");
        Execute(registry, "home.fillYellow");
        Execute(registry, "home.bordersAll");
        Execute(registry, "home.pasteValues");

        Assert.Equal(new[] { "general", "date", "yellow", "bordersAll", "pasteValues" }, fired);
    }

    [Fact]
    public void SetFontSize_BindsFontSizeCombo_AndPassesSelectedValue()
    {
        string? applied = null;
        var registry = SampleRibbon.BuildRegistry(
            () => null, _ => { }, new AvaloniaRibbonHostCallbacks { SetFontSize = v => applied = v });

        Assert.True(registry.TryGet(new RibbonCommandId("home.fontSize"), out var command));
        Assert.IsType<RelayValueRibbonCommand>(command);

        command!.Execute(RibbonCommandContext.ForSelectedValue("14"));
        Assert.Equal("14", applied);
    }

    [Fact]
    public void WithoutSetFontSize_FontSizeComboStaysNoOp()
    {
        var registry = SampleRibbon.BuildRegistry(() => null, _ => { });

        Assert.True(registry.TryGet(new RibbonCommandId("home.fontSize"), out var command));
        Assert.IsType<NoOpRibbonCommand>(command);
    }

    [Fact]
    public void SetFontName_BindsFontNameCombo_AndPassesSelectedValue()
    {
        string? applied = null;
        var registry = SampleRibbon.BuildRegistry(
            () => null, _ => { }, new AvaloniaRibbonHostCallbacks { SetFontName = v => applied = v });

        Assert.True(registry.TryGet(new RibbonCommandId("home.fontName"), out var command));
        Assert.IsType<RelayValueRibbonCommand>(command);

        command!.Execute(RibbonCommandContext.ForSelectedValue("Arial"));
        Assert.Equal("Arial", applied);
    }

    [Fact]
    public void WithoutSetFontName_FontNameComboStaysNoOp()
    {
        var registry = SampleRibbon.BuildRegistry(() => null, _ => { });

        Assert.True(registry.TryGet(new RibbonCommandId("home.fontName"), out var command));
        Assert.IsType<NoOpRibbonCommand>(command);
    }

    [Fact]
    public void InsertTable_BindsBothRibbonAndHomeButtons_ToTheSameAction()
    {
        var count = 0;
        var registry = SampleRibbon.BuildRegistry(
            () => null, _ => { }, new AvaloniaRibbonHostCallbacks { InsertTable = () => count++ });

        Execute(registry, "insert.table");
        Execute(registry, "home.formatAsTable");

        Assert.Equal(2, count);
    }

    private static AvaloniaRibbonHostCallbacks AllWired() => new()
    {
        OpenTextToColumns = () => { },
        OpenConsolidate = () => { },
        InsertTable = () => { },
        ConditionalFormatting = () => { },
        QuickAnalysis = () => { },
        SortAscending = () => { },
        SortDescending = () => { },
        ToggleFilter = () => { },
        DataValidation = () => { },
        Cut = () => { },
        Copy = () => { },
        Paste = () => { },
        AlignLeft = () => { },
        AlignCenter = () => { },
        AlignRight = () => { },
        WrapText = () => { },
        MergeAndCenter = () => { },
        CurrencyFormat = () => { },
        PercentFormat = () => { },
        CommaStyle = () => { },
    };

    private static void Execute(IRibbonCommandRegistry registry, string commandId)
    {
        Assert.True(registry.TryGet(new RibbonCommandId(commandId), out var command));
        command!.Execute(EmptyContext);
    }
}
