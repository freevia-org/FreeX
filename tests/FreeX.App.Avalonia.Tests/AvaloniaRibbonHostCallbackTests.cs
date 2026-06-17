using System.Collections.Generic;
using System.Linq;

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
    public void BuildDefinition_HasWindowsTabStructure()
    {
        var headers = SampleRibbon.BuildDefinition().Tabs.Select(t => t.Header).ToList();

        foreach (var expected in new[] { "Home", "Insert", "Data", "Page Layout", "Formulas", "Review", "View" })
            Assert.Contains(expected, headers);
    }

    [Fact]
    public void BuildDefinition_FormulasTab_HasExpectedGroups()
    {
        var formulas = SampleRibbon.BuildDefinition().Tabs.Single(t => t.Header == "Formulas");
        var groups = formulas.Groups.Select(g => g.Header).ToList();

        Assert.Contains("Function Library", groups);
        Assert.Contains("Defined Names", groups);
        Assert.Contains("Formula Auditing", groups);
        Assert.Contains("Calculation", groups);
    }

    [Fact]
    public void BuildDefinition_ReviewTab_HasNotesAndProtectGroups()
    {
        var review = SampleRibbon.BuildDefinition().Tabs.Single(t => t.Header == "Review");
        var groups = review.Groups.Select(g => g.Header).ToList();

        Assert.Contains("Notes", groups);
        Assert.Contains("Protect", groups);
    }

    [Theory]
    [InlineData("formulas.insertFunction")]
    [InlineData("formulas.nameManager")]
    [InlineData("formulas.autoSum")]
    [InlineData("review.protectSheet")]
    [InlineData("review.checkAccessibility")]
    [InlineData("view.gridlines")]
    [InlineData("view.freezePanes")]
    [InlineData("view.zoom100")]
    [InlineData("pageLayout.margins")]
    [InlineData("home.strikethrough")]
    [InlineData("home.increaseFont")]
    [InlineData("home.fontColorRed")]
    [InlineData("home.alignTop")]
    [InlineData("home.increaseIndent")]
    [InlineData("home.increaseDecimal")]
    [InlineData("data.flashFill")]
    [InlineData("data.removeDuplicates")]
    [InlineData("data.advancedFilter")]
    [InlineData("data.whatIf")]
    [InlineData("view.unhide")]
    [InlineData("view.split")]
    [InlineData("pageLayout.printTitles")]
    [InlineData("formulas.mathTrig")]
    [InlineData("formulas.lookupReference")]
    [InlineData("insert.pivotChart")]
    [InlineData("review.thesaurus")]
    [InlineData("review.translate")]
    [InlineData("pageLayout.themeColors")]
    public void NewTabCommands_AreRealCommandIds_AndBindViaExtraCommands(string commandId)
    {
        // The id exists in the ribbon definition (else it is not a NoOp default to begin with) ...
        var defaults = SampleRibbon.BuildRegistry(() => null, _ => { });
        Assert.True(defaults.TryGet(new RibbonCommandId(commandId), out var noOp));
        Assert.IsType<NoOpRibbonCommand>(noOp);

        // ... and ExtraCommands (how MainWindow wires the new tabs) overrides it with a real command.
        var wired = SampleRibbon.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
        {
            ExtraCommands = new Dictionary<string, Action> { [commandId] = () => { } },
        });
        Assert.True(wired.TryGet(new RibbonCommandId(commandId), out var command));
        Assert.IsType<RelayRibbonCommand>(command);
    }

    [Fact]
    public void BuildDefinition_HomeTab_MatchesWindowsGroups()
    {
        var home = SampleRibbon.BuildDefinition().Tabs.Single(t => t.Header == "Home");
        var groups = home.Groups.Select(g => g.Header).ToList();

        foreach (var expected in new[] { "Clipboard", "Font", "Alignment", "Number", "Styles", "Cells", "Editing" })
            Assert.Contains(expected, groups);
    }

    [Fact]
    public void BuildDefinition_DataTab_HasForecastAndOutlineGroups()
    {
        var data = SampleRibbon.BuildDefinition().Tabs.Single(t => t.Header == "Data");
        var groups = data.Groups.Select(g => g.Header).ToList();

        Assert.Contains("Forecast", groups);
        Assert.Contains("Outline", groups);
    }

    [Fact]
    public void BuildDefinition_InsertTab_MatchesWindowsGroups()
    {
        var insert = SampleRibbon.BuildDefinition().Tabs.Single(t => t.Header == "Insert");
        var groups = insert.Groups.Select(g => g.Header).ToList();

        foreach (var expected in new[] { "Tables", "Charts", "Sparklines", "Filters", "Links", "Comments", "Text", "Symbols" })
            Assert.Contains(expected, groups);
    }

    [Fact]
    public void BuildDefinition_ViewTab_HasShowZoomWindowGroups()
    {
        var view = SampleRibbon.BuildDefinition().Tabs.Single(t => t.Header == "View");
        var groups = view.Groups.Select(g => g.Header).ToList();

        Assert.Contains("Show", groups);
        Assert.Contains("Zoom", groups);
        Assert.Contains("Window", groups);
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
