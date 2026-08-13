using System.Collections.Generic;
using System.Linq;

using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Ribbon;
using FreeX.Ribbon.Definitions;
using Free.Shared.Ribbon;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Verifies the Avalonia ribbon registry binds the shell's host callbacks to the right command ids, so the
/// declarative ribbon invokes the same handlers as the native menus (charts/CF/table/etc.).
/// The ribbon definition is now the single-source shared <see cref="FreeXRibbon"/>; the shell registers
/// handlers directly under canonical ids obtained from <see cref="FreeXRibbonCommandCatalog"/>. Pure registry
/// assertions — no running shell or UI thread required.
/// </summary>
public sealed class AvaloniaRibbonHostCallbackTests
{
    private static readonly RibbonCommandContext EmptyContext =
        new(new Dictionary<string, object?>());

    private static RibbonCommandId Canonical(string canonicalId) =>
        FreeXRibbonCommandCatalog.GetRequired(canonicalId);

    [Theory]
    [InlineData("Text to Columns")]
    [InlineData("Consolidate")]
    [InlineData("Table")]
    [InlineData("Format as Table")]
    [InlineData("Conditional Formatting")]
    [InlineData(FreeXRibbonCommandIds.DataSortAscending)]
    [InlineData(FreeXRibbonCommandIds.DataSortDescending)]
    [InlineData(FreeXRibbonCommandIds.DataFilter)]
    [InlineData(FreeXRibbonCommandIds.DataValidation)]
    [InlineData("Cut")]
    [InlineData("Copy")]
    [InlineData("Paste")]
    [InlineData("Align Left")]
    [InlineData("Center")]
    [InlineData("Align Right")]
    [InlineData("Wrap Text")]
    [InlineData("Merge & Center")]
    [InlineData("Accounting Number Format")]
    [InlineData("Percent Style")]
    [InlineData("Comma Style")]
    public void BuildRegistry_WithCallbacks_BindsRealCommand(string commandId)
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, AllWired());

        Assert.True(registry.TryGet(Canonical(commandId), out var command));
        Assert.IsType<ActionRibbonCommand>(command);
    }

    [Theory]
    [InlineData("Table")]
    [InlineData("Conditional Formatting")]
    [InlineData(FreeXRibbonCommandIds.DataSortAscending)]
    public void BuildRegistry_WithoutCallbacks_LeavesNoOp(string commandId)
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });

        Assert.True(registry.TryGet(Canonical(commandId), out var command));
        Assert.IsType<EmptyRibbonCommand>(command);
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
            SortAscending = () => fired.Add("sortAsc"),
            SortDescending = () => fired.Add("sortDesc"),
            DataValidation = () => fired.Add("validation"),
            Copy = () => fired.Add("copy"),
            AlignCenter = () => fired.Add("alignCenter"),
            PercentFormat = () => fired.Add("percent"),
        };
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, callbacks);

        Execute(registry, "Table");
        Execute(registry, "Conditional Formatting");
        Execute(registry, FreeXRibbonCommandIds.DataSortDescending);
        Execute(registry, "Copy");
        Execute(registry, "Center");
        Execute(registry, "Percent Style");

        Assert.Contains("table", fired);
        Assert.Contains("conditional", fired);
        Assert.Contains("sortDesc", fired);
        Assert.Contains("copy", fired);
        Assert.Contains("alignCenter", fired);
        Assert.Contains("percent", fired);
    }

    [Fact]
    public void ExtraCommands_BindCanonicalMenuItems_AndExecute()
    {
        // ExtraCommands accepts only canonical ids emitted by the shared definition.
        var fired = new List<string>();
        var callbacks = new AvaloniaRibbonHostCallbacks
        {
            ExtraCommands = new Dictionary<string, Action>
            {
                ["All Borders"] = () => fired.Add("bordersAll"),
                ["Paste Values"] = () => fired.Add("pasteValues"),
                ["Clear Contents"] = () => fired.Add("clearContents"),
            },
        };
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, callbacks);

        Assert.True(registry.TryGet(Canonical("All Borders"), out var c));
        Assert.IsType<ActionRibbonCommand>(c);

        Execute(registry, "All Borders");
        Execute(registry, "Paste Values");
        Execute(registry, "Clear Contents");

        Assert.Equal(new[] { "bordersAll", "pasteValues", "clearContents" }, fired);
    }

    [Fact]
    public void BuildDefinition_IsTheSharedSingleSourceDefinition()
    {
        // The Avalonia ribbon is now built from the exact same definition the WPF app consumes.
        var headers = AvaloniaRibbonComposition.BuildDefinition().Tabs.Select(t => t.Header).ToList();
        var shared = FreeXRibbon.Build().Tabs.Select(t => t.Header).ToList();

        Assert.Equal(shared, headers);
    }

    [Fact]
    public void BuildDefinition_HasWindowsTabStructure()
    {
        var headers = AvaloniaRibbonComposition.BuildDefinition().Tabs.Select(t => t.Header).ToList();

        foreach (var expected in new[] { "Home", "Insert", "Data", "Page Layout", "Formulas", "Review", "View" })
            Assert.Contains(expected, headers);
    }

    [Fact]
    public void BuildDefinition_OutlineGroupAndUngroupAreTrueSplitButtons()
    {
        var outline = AvaloniaRibbonComposition.BuildDefinition()
            .FindTab("DataTab")!
            .FindGroup("DataOutlineGroup")!;

        Assert.IsType<RibbonSplitButton>(outline.Controls[0]);
        Assert.IsType<RibbonSplitButton>(outline.Controls[1]);
        Assert.IsType<RibbonButton>(outline.Controls[2]);
    }

    [Fact]
    public void BuildDefinition_ShapesUsesTheCompleteSharedGallery()
    {
        var draw = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(tab => tab.Id == "DrawTab");
        var shapes = Assert.IsType<RibbonSplitButton>(draw.Groups
            .SelectMany(group => group.Controls)
            .Single(control => control.CommandId.Value == "Shapes"));

        Assert.Equal(
            DrawingInsertionPlanner.ShapeGroups.Select(group => group.Label),
            shapes.Menu.Items.Select(group => group.Header));
        Assert.Equal(
            DrawingInsertionPlanner.ShapeItems.Select(item => item.Label),
            shapes.Menu.Items.SelectMany(group => group.Children).Select(item => item.Header));
    }

    [Fact]
    public void ShapeGalleryCommand_InvokesTheSelectedShapeKind()
    {
        DrawingShapeKind? inserted = null;
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => null,
            _ => { },
            new AvaloniaRibbonHostCallbacks { InsertShape = kind => inserted = kind });
        var commandId = new RibbonCommandId(
            AvaloniaRibbonComposition.GetShapeCommandId(DrawingShapeKind.Diamond));

        Assert.True(registry.TryGet(commandId, out var command));
        command!.Execute(EmptyContext);

        Assert.Equal(DrawingShapeKind.Diamond, inserted);
    }

    [Fact]
    public void BuildDefinition_FormulasTab_HasExpectedGroups()
    {
        var formulas = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(t => t.Header == "Formulas");
        var groups = formulas.Groups.Select(g => g.Header).ToList();

        Assert.Contains("Function Library", groups);
        Assert.Contains("Defined Names", groups);
        Assert.Contains("Formula Auditing", groups);
        Assert.Contains("Calculation", groups);
    }

    [Fact]
    public void BuildDefinition_ReviewTab_HasNotesAndProtectGroups()
    {
        var review = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(t => t.Header == "Review");
        var groups = review.Groups.Select(g => g.Header).ToList();

        Assert.Contains("Notes", groups);
        Assert.Contains("Protect", groups);
    }

    [Theory]
    [InlineData(FreeXRibbonCommandIds.FormulasMoreFunctions)]
    [InlineData("Name Manager")]
    [InlineData(FreeXRibbonCommandIds.FormulasAutoSum)]
    [InlineData(FreeXRibbonCommandIds.ReviewProtectSheet)]
    [InlineData("Check Accessibility")]
    [InlineData("Convert to Comments")]
    [InlineData(FreeXRibbonCommandIds.HelpCopyDiagnostics)]
    [InlineData(FreeXRibbonCommandIds.HelpLegalNotices)]
    [InlineData("Gridlines")]
    [InlineData(FreeXRibbonCommandIds.ViewFreezePanes)]
    [InlineData(FreeXRibbonCommandIds.ViewZoom100)]
    [InlineData("Margins")]
    [InlineData("Strikethrough")]
    [InlineData("Increase Font Size")]
    [InlineData("Top Align")]
    [InlineData("Increase Indent")]
    [InlineData("Increase Decimal Places")]
    [InlineData("Flash Fill")]
    [InlineData(FreeXRibbonCommandIds.DataRemoveDuplicates)]
    [InlineData("Advanced")]
    [InlineData("What-If Analysis")]
    [InlineData("Unhide")]
    [InlineData("Split")]
    [InlineData("Print Titles")]
    [InlineData("Math & Trig")]
    [InlineData("Lookup & Reference")]
    [InlineData("Format")]
    // Home ▸ Editing ▸ Fill / Clear dropdown items are wired under their raw canonical menu ids.
    [InlineData("Down")]
    [InlineData("Right")]
    [InlineData("Up")]
    [InlineData("Left")]
    [InlineData("Series")]
    [InlineData("Clear All")]
    [InlineData("Clear Formats")]
    [InlineData("Clear Contents")]
    [InlineData("Clear Comments and Notes")]
    [InlineData("Clear Hyperlinks")]
    // Home ▸ Editing ▸ AutoSum / Find & Select dropdown items (raw canonical menu ids).
    [InlineData("Sum")]
    [InlineData("Average")]
    [InlineData("Count Numbers")]
    [InlineData("Count All")]
    [InlineData("Max")]
    [InlineData("Min")]
    [InlineData("More Functions")]
    [InlineData("Find")]
    [InlineData("Replace")]
    [InlineData("Go To")]
    [InlineData("Go To Special")]
    [InlineData("Formulas")]
    [InlineData("Notes")]
    [InlineData("Constants")]
    [InlineData("Data Validation")]
    [InlineData("Select Objects")]
    [InlineData("Selection Pane")]
    // Home ▸ Font ▸ Borders / Underline / Orientation dropdown items (raw canonical menu ids).
    [InlineData("Inside Borders")]
    [InlineData("Top Border")]
    [InlineData("Bottom Border")]
    [InlineData("Left Border")]
    [InlineData("Right Border")]
    [InlineData("More Borders")]
    [InlineData("Horizontal")]
    [InlineData("Angle Counterclockwise")]
    [InlineData("Angle Clockwise")]
    [InlineData("Vertical Text")]
    [InlineData("Rotate Text Up")]
    [InlineData("Rotate Text Down")]
    // Home ▸ Cells (Insert/Delete/Format) + Conditional Formatting preset dropdown items.
    [InlineData("Insert Cells")]
    [InlineData("Insert Sheet")]
    [InlineData("Delete Cells")]
    [InlineData("Format Cells")]
    [InlineData("Protect Sheet")]
    [InlineData("Unhide Sheet")]
    [InlineData("New Rule")]
    [InlineData("Clear Rules")]
    [InlineData("Data Bars")]
    [InlineData("Color Scales")]
    [InlineData("Greater Than")]
    [InlineData("Top 10 Items")]
    public void NewTabCommands_AreRealCommandIds_AndBindViaExtraCommands(string commandId)
    {
        // The canonical id exists in the shared definition (so it seeds a NoOp default to begin with) ...
        var defaults = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });
        Assert.True(defaults.TryGet(Canonical(commandId), out var noOp));
        Assert.IsType<EmptyRibbonCommand>(noOp);

        // ... and ExtraCommands (how MainWindow wires the new tabs) overrides it with a real command.
        var wired = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
        {
            ExtraCommands = new Dictionary<string, Action> { [commandId] = () => { } },
        });
        Assert.True(wired.TryGet(Canonical(commandId), out var command));
        Assert.IsType<ActionRibbonCommand>(command);
    }

    [Fact]
    public void ConditionalFormatPopupCatalogRows_AreCanonicalCommandIds_AndBindViaExtraCommands()
    {
        foreach (var item in ConditionalFormatPresetGalleryPlanner.PopupItems)
        {
            Assert.True(FreeXRibbonCommandCatalog.TryGet(item.CommandId, out _));

            var defaults = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });
            Assert.True(defaults.TryGet(Canonical(item.CommandId), out var noOp), $"Conditional-format popup id '{item.CommandId}' is not in the shared definition.");
            Assert.IsType<EmptyRibbonCommand>(noOp);

            var wired = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
            {
                ExtraCommands = new Dictionary<string, Action> { [item.CommandId] = () => { } },
            });
            Assert.True(wired.TryGet(Canonical(item.CommandId), out var command));
            Assert.IsType<ActionRibbonCommand>(command);
        }
    }

    [Fact]
    public void EveryCellStylePreset_DisplayNameIsARealMenuId_AndBindsViaExtraCommands()
    {
        // MainWindow wires the Home ▸ Styles ▸ Cell Styles gallery items by looping CellStylePreset and using
        // each preset's display name as the canonical ribbon menu id. Verify every display name is a real id
        // the shared definition emits (seeded NoOp) and that wiring it via ExtraCommands overrides the NoOp.
        foreach (var preset in System.Enum.GetValues<FreeX.App.Services.CellStylePreset>())
        {
            var id = FreeX.App.Services.CellStyleDiffPlanner.GetCellStylePresetDisplayName(preset);

            var defaults = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });
            Assert.True(defaults.TryGet(Canonical(id), out var noOp), $"Cell-style id '{id}' is not in the shared definition.");
            Assert.IsType<EmptyRibbonCommand>(noOp);

            var wired = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
            {
                ExtraCommands = new Dictionary<string, Action> { [id] = () => { } },
            });
            Assert.True(wired.TryGet(Canonical(id), out var command));
            Assert.IsType<ActionRibbonCommand>(command);
        }
    }

    [Fact]
    public void BuildDefinition_HomeTab_MatchesWindowsGroups()
    {
        var home = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(t => t.Header == "Home");
        var groups = home.Groups.Select(g => g.Header).ToList();

        foreach (var expected in new[] { "Clipboard", "Font", "Alignment", "Number", "Styles", "Cells", "Editing" })
            Assert.Contains(expected, groups);
    }

    [Fact]
    public void BuildDefinition_DataTab_HasForecastAndOutlineGroups()
    {
        var data = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(t => t.Header == "Data");
        var groups = data.Groups.Select(g => g.Header).ToList();

        Assert.Contains("Forecast", groups);
        Assert.Contains("Outline", groups);
    }

    [Fact]
    public void BuildDefinition_InsertTab_MatchesWindowsGroups()
    {
        var insert = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(t => t.Header == "Insert");
        var groups = insert.Groups.Select(g => g.Header).ToList();

        foreach (var expected in new[] { "Tables", "Charts", "Sparklines", "Filters", "Links", "Comments", "Text", "Symbols" })
            Assert.Contains(expected, groups);
    }

    [Fact]
    public void BuildDefinition_ViewTab_HasShowZoomWindowGroups()
    {
        var view = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(t => t.Header == "View");
        var groups = view.Groups.Select(g => g.Header).ToList();

        Assert.Contains("Show", groups);
        Assert.Contains("Zoom", groups);
        Assert.Contains("Window", groups);
    }

    [Fact]
    public void SetFontSize_BindsFontSizeCombo_AndPassesSelectedValue()
    {
        string? applied = null;
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => null, _ => { }, new AvaloniaRibbonHostCallbacks { SetFontSize = v => applied = v });

        Assert.True(registry.TryGet(Canonical("Font Size"), out var command));
        Assert.IsType<ValueRibbonCommand>(command);

        command!.Execute(RibbonCommandContext.ForSelectedValue("14"));
        Assert.Equal("14", applied);
    }

    [Fact]
    public void WithoutSetFontSize_FontSizeComboStaysNoOp()
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });

        Assert.True(registry.TryGet(Canonical("Font Size"), out var command));
        Assert.IsType<EmptyRibbonCommand>(command);
    }

    [Fact]
    public void SetFontName_BindsFontNameCombo_AndPassesSelectedValue()
    {
        string? applied = null;
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => null, _ => { }, new AvaloniaRibbonHostCallbacks { SetFontName = v => applied = v });

        Assert.True(registry.TryGet(Canonical("Font"), out var command));
        Assert.IsType<ValueRibbonCommand>(command);

        command!.Execute(RibbonCommandContext.ForSelectedValue("Arial"));
        Assert.Equal("Arial", applied);
    }

    [Fact]
    public void WithoutSetFontName_FontNameComboStaysNoOp()
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });

        Assert.True(registry.TryGet(Canonical("Font"), out var command));
        Assert.IsType<EmptyRibbonCommand>(command);
    }

    [Theory]
    [InlineData("Scale Width", "2 pages")]
    [InlineData("Scale Height", "3 pages")]
    [InlineData("Scale Percent", "85%")]
    public void PageLayoutScaleCombos_PassSelectedValueToValueAwareCallbacks(
        string commandId,
        string selectedValue)
    {
        var openedPageSetup = false;
        string? applied = null;
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => null,
            _ => { },
            new AvaloniaRibbonHostCallbacks
            {
                ExtraCommands = new Dictionary<string, Action>
                {
                    [commandId] = () => openedPageSetup = true,
                },
                SetPageLayoutScaleWidth = commandId == "Scale Width" ? value => applied = value : null,
                SetPageLayoutScaleHeight = commandId == "Scale Height" ? value => applied = value : null,
                SetPageLayoutScalePercent = commandId == "Scale Percent" ? value => applied = value : null,
            });

        Assert.True(registry.TryGet(Canonical(commandId), out var command));
        Assert.IsType<ValueRibbonCommand>(command);

        command!.Execute(RibbonCommandContext.ForSelectedValue(selectedValue));

        Assert.Equal(selectedValue, applied);
        Assert.False(openedPageSetup);
    }

    [Fact]
    public void PageLayoutScaleCombos_ExposeLiveStateWhenProvided()
    {
        var states = new Dictionary<string, Func<RibbonCommandState>>
        {
            ["Scale Width"] = () => new RibbonCommandState(Value: "4 pages"),
            ["Scale Height"] = () => new RibbonCommandState(Value: "Automatic"),
            ["Scale Percent"] = () => new RibbonCommandState(Value: "125%"),
        };
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => null,
            _ => { },
            new AvaloniaRibbonHostCallbacks
            {
                SetPageLayoutScaleWidth = _ => { },
                SetPageLayoutScaleHeight = _ => { },
                SetPageLayoutScalePercent = _ => { },
                ExtraCommandStates = states,
            });

        AssertStateValue(registry, "Scale Width", "4 pages");
        AssertStateValue(registry, "Scale Height", "Automatic");
        AssertStateValue(registry, "Scale Percent", "125%");

        static void AssertStateValue(IRibbonCommandRegistry registry, string commandId, string expected)
        {
            Assert.True(registry.TryGet(Canonical(commandId), out var command));
            Assert.Equal(expected, Assert.IsAssignableFrom<IRibbonStatefulCommand>(command).GetState().Value);
        }
    }

    [Fact]
    public void DrawCommands_DefaultToEnabledAndBindThroughTheSharedExtraCommandPath()
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });

        Assert.True(registry.TryGet(new RibbonCommandId("Bring Forward"), out var bringForward));
        Assert.IsType<EmptyRibbonCommand>(bringForward);
        Assert.True(registry.TryGet(new RibbonCommandId("Shape Fill"), out var shapeFill));
        Assert.IsType<EmptyRibbonCommand>(shapeFill);

        foreach (var commandId in new[] { "Crop Picture", "Shape Gradient", "Shape Effects" })
        {
            Assert.True(registry.TryGet(new RibbonCommandId(commandId), out var defaultCommand));
            Assert.IsType<EmptyRibbonCommand>(defaultCommand);

            var wired = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
            {
                ExtraCommands = new Dictionary<string, Action> { [commandId] = () => { } },
            });
            Assert.True(wired.TryGet(new RibbonCommandId(commandId), out var wiredCommand));
            Assert.IsType<ActionRibbonCommand>(wiredCommand);
        }
    }

    [Fact]
    public void ExtraCommandStates_RegisterStatefulRelayCommand()
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
        {
            ExtraCommands = new Dictionary<string, Action>
            {
                ["Gridlines"] = () => { },
            },
            ExtraCommandStates = new Dictionary<string, Func<RibbonCommandState>>
            {
                ["Gridlines"] = () => new RibbonCommandState(IsChecked: true),
            },
        });

        Assert.True(registry.TryGet(Canonical("Gridlines"), out var command));
        var stateful = Assert.IsAssignableFrom<IRibbonStatefulCommand>(command);
        Assert.True(stateful.GetState().IsChecked);
    }

    [Fact]
    public void DrawPicturesAndShapes_BindToInsertCallbacks_WhenProvided()
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
        {
            InsertPicture = () => { },
            InsertShape = _ => { },
        });

        Assert.True(registry.TryGet(Canonical("Pictures"), out var picture));
        Assert.True(registry.TryGet(Canonical("Shapes"), out var shapes));
        Assert.IsType<ActionRibbonCommand>(picture);
        Assert.IsType<ActionRibbonCommand>(shapes);
    }

    [Fact]
    public void DrawingObjectRotateAndSize_ExecuteThroughSharedCanonicalCommands()
    {
        var fired = new List<string>();
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
        {
            ExtraCommands = new Dictionary<string, Action>
            {
                ["Rotate Object"] = () => fired.Add("rotate"),
                ["Object Size"] = () => fired.Add("size"),
            },
        });

        Execute(registry, "Rotate Object");
        Execute(registry, "Object Size");

        Assert.Equal(new[] { "rotate", "size" }, fired);

        var definition = AvaloniaRibbonComposition.BuildDefinition();
        foreach (var tabId in new[] { "DrawTab", "ShapeFormatTab" })
        {
            var ids = definition.FindTab(tabId)!.Groups
                .SelectMany(group => group.Controls)
                .Select(control => control.CommandId.Value)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("Rotate Object", ids);
            Assert.Contains("Object Size", ids);
        }
    }

    [Fact]
    public void InsertTable_BindsBothRibbonAndHomeButtons_ToTheSameAction()
    {
        var count = 0;
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => null, _ => { }, new AvaloniaRibbonHostCallbacks { InsertTable = () => count++ });

        Execute(registry, "Table");
        Execute(registry, "Format as Table");

        Assert.Equal(2, count);
    }

    [Fact]
    public void CanonicalCatalog_IsDerivedFromTheSharedDefinition()
    {
        var definitionIds = FreeXRibbonCommandCatalog
            .Enumerate(FreeXRibbon.Build())
            .Select(id => id.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var catalogIds = FreeXRibbonCommandCatalog.All
            .Select(id => id.Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(definitionIds, catalogIds);
    }

    [Fact]
    public void UnknownExtraCommandId_IsRejectedInsteadOfCreatingAnUnreachableRegistration()
    {
        var callbacks = new AvaloniaRibbonHostCallbacks
        {
            ExtraCommands = new Dictionary<string, Action>
            {
                ["legacy.unreachable"] = () => { },
            },
        };

        Assert.Throws<ArgumentException>(() =>
            AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, callbacks));
    }
    private static AvaloniaRibbonHostCallbacks AllWired() => new()
    {
        OpenTextToColumns = () => { },
        OpenConsolidate = () => { },
        InsertTable = () => { },
        ConditionalFormatting = () => { },
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
        Assert.True(registry.TryGet(Canonical(commandId), out var command));
        command!.Execute(EmptyContext);
    }
}
